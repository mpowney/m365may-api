using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System.Collections.Generic;
using com.m365may.entities;

namespace com.m365may.v1
{

    public static partial class TableNames {
        public const string Cache = "Cache";
        public const string RedirectSessions = "RedirectSessions";
    }

    public static partial class QueueNames {
        public const string ProcessRedirectClicks = "ProcessRedirectClicks";
        public const string ProcessRedirectClicksForGeo = "ProcessRedirectClicksForGeo";
    }

    public static class Redirect
    {
        [FunctionName("RedirectSession")]
        public static async Task<IActionResult> RunRedirectSession(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_redirect/session/{id}")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            [Table(TableNames.RedirectSessions)] CloudTable sessionRedirectTable,
            [Queue(QueueNames.ProcessRedirectClicks), StorageAccount("AzureWebJobsStorage")] ICollector<HttpRequestEntity> processRedirectQueue,
            string id,
            ILogger log,
            ExecutionContext context)
        {

            id = id ?? req.Query["id"];

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            RedirectEntity redirect = await RedirectEntity.get(sessionRedirectTable, id);

            if (redirect != null) {

                processRedirectQueue.Add(new HttpRequestEntity(req));

                if (redirect.RedirectTo == null) {
                    return new NotFoundObjectResult($"Redirect for key {redirect.RowKey} not specified.");
                }

                if (redirect.RedirectTo.StartsWith("/") && redirect.RedirectTo.Substring(1, 1) != "/")
                    return new RedirectResult($"{config["REDIRECT_DESTINATION_HOST"]}{redirect.RedirectTo}");
                
                return new RedirectResult($"{redirect.RedirectTo}");

            }

            Session foundSession = GetSession.ById(await GetSession.GetAllSessions(cacheTable, log, context), id, req, false);

            if (foundSession != null) {

                string holdingPageUrl = $"{config["HOLDPAGE_SESSION"]}";

                log.LogInformation($"Looking up holding page content: {holdingPageUrl}.");

                var client = new HttpClient();
                var getResponse = await client.GetAsync(holdingPageUrl);
                if (getResponse.IsSuccessStatusCode) {
                    
                    string value = await getResponse.Content.ReadAsStringAsync();

                    value = value.Replace("{title}", foundSession.title ??= string.Empty);
                    value = value.Replace("{description}", foundSession.description ??= string.Empty );
                    value = value.Replace("{id}", foundSession.id ??= string.Empty);
                    value = value.Replace("{url}", foundSession.url ??= string.Empty);
                    value = value.Replace("{speakers}", string.Join(", ", foundSession.speakers.Select(speaker => speaker.name)));

                    return new ContentResult {
                        ContentType = "text/html; charset=UTF-8",
                        Content = value
                    };

                }
                else {

                    return new NotFoundObjectResult($"{config["HOLDPAGE_SESSION"]} template page not found");

                }

            }

            return new NotFoundResult();

        }

        [FunctionName("ProcessRedirectClicks")]
        public static async void ProcessRedirectClicks(
            [QueueTrigger(QueueNames.ProcessRedirectClicks)] string queuedHttpRequestString,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            [Queue(QueueNames.ProcessRedirectClicksForGeo), StorageAccount("AzureWebJobsStorage")] ICollector<HttpRequestEntity> processRedirectQueueForGeo,
            ILogger log,
            ExecutionContext context)
        {

            HttpRequestEntity queuedHttpRequest = JsonConvert.DeserializeObject<HttpRequestEntity>(queuedHttpRequestString);

            MatchCollection matches = Regex.Matches(queuedHttpRequest.Path, "/_redirect/session/(\\d+)", RegexOptions.IgnoreCase);
            if (matches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, matches[0].Groups[1].Value);
                redirectEntity.ClickCount++;
                await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.GeoCount ??= "{}"));
                processRedirectQueueForGeo.Add(queuedHttpRequest);
    
                log.LogInformation($"Successfully processed click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                return;

            }

            log.LogError($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");
            throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");

        }

        [FunctionName("ProcessRedirectClicksForGeo")]
        public static async void ProcessRedirectClicksForGeo(
            [QueueTrigger(QueueNames.ProcessRedirectClicksForGeo)] string queuedHttpRequestString,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context)
        {

            HttpRequestEntity queuedHttpRequest = JsonConvert.DeserializeObject<HttpRequestEntity>(queuedHttpRequestString);

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string ipLookupUrl = $"{config["FREEGEOIP_HOST"]}/json/{queuedHttpRequest.RemoteIpAddress}";

            log.LogInformation($"Looking up freegeoip: {ipLookupUrl}.");

            var client = new HttpClient();
            var getResponse = await client.GetAsync(ipLookupUrl);
            if (getResponse.StatusCode == HttpStatusCode.OK) {

                MatchCollection matches = Regex.Matches(queuedHttpRequest.Path, "/_redirect/session/(\\d+)", RegexOptions.IgnoreCase);
                if (matches.Count > 0) {

                    string ipResponseString = await getResponse.Content.ReadAsStringAsync();
                    dynamic ipResponse = JsonConvert.DeserializeObject(ipResponseString);
                    GeoEntity geoEntity = new GeoEntity(ipResponse);
                    string geoEntityString = JsonConvert.SerializeObject(geoEntity);

                    RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, matches[0].Groups[1].Value);
                    if (redirectEntity.GeoCount == null) {
                        log.LogInformation($"Adding GeoCount property to redirect entity {queuedHttpRequest.Path}");
                        // redirectEntity.GeoCount = new Dictionary<string, int>();
                    }
                    
                    Dictionary<string, int> _geoCount = JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.GeoCount);
                    if (_geoCount.ContainsKey(geoEntityString)) {
                        log.LogInformation($"Incrementing GeoCount for redirect entity {queuedHttpRequest.Path}");
                        
                        _geoCount[geoEntityString] = _geoCount[geoEntityString] + 1;
                    }
                    else {
                        log.LogInformation($"Creating GeoCount for redirect entity {queuedHttpRequest.Path}");
                        _geoCount.Add(geoEntityString, 1);
                    }

                    log.LogInformation($" GeoCount property value: {JsonConvert.SerializeObject(redirectEntity.GeoCount)}");
                    
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, _geoCount);
        
                    log.LogInformation($"Successfully processed geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                    return;

                }

                log.LogError($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");
                throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");
            
            }

            log.LogError($"Free geo ip lookup for IP {queuedHttpRequest.RemoteIpAddress} failed with status code {getResponse.StatusCode}");
            throw new System.Exception($"Free geo ip lookup for IP {queuedHttpRequest.RemoteIpAddress} failed with status code {getResponse.StatusCode}");

        }    

    }

}
