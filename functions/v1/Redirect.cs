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
using com.m365may.utils;

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

        const string EMBED_JS = "";

        [FunctionName("RedirectSession")]
        public static async Task<IActionResult> RunRedirectSession(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "redirect/session/{id}/")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            [Table(TableNames.RedirectSessions)] CloudTable sessionRedirectTable,
            [Queue(QueueNames.ProcessRedirectClicks), StorageAccount("AzureWebJobsStorage")] ICollector<HttpRequestEntity> processRedirectQueue,
            string id,
            ILogger log,
            ExecutionContext context)
        {

            id = id ?? req.Query["id"];

            int numericId = 0;
            int.TryParse(id, out numericId);
            if (numericId > 0) id = numericId.ToString();

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            RedirectEntity redirect = await RedirectEntity.get(sessionRedirectTable, id);
            Session foundSession = GetSession.ById(await GetSession.GetAllSessions(cacheTable, log, context), id, req, true);
            
            if (redirect != null && redirect.RedirectTo != null) {

                int startRedirectingMinutes = (redirect.StartRedirectingMinutes == null) ? -5 : redirect.StartRedirectingMinutes.Value;

                if (foundSession != null && foundSession.startsAt != null) {
                    
                    DateTime startRedirecting = foundSession.startsAt.Value.ToUniversalTime().AddMinutes(startRedirectingMinutes);
                    DateTime now = DateTime.Now;
                    
                    if (DateTime.Compare(now, startRedirecting) >= 0) {
                        log.LogInformation($"Start redirecting condition met for {req.Path} - redirect time was {startRedirecting} (current time {now})");

                        if (req.QueryString.ToString().IndexOf("check") < 0) {
                            processRedirectQueue.Add(new HttpRequestEntity(req));

                            if (redirect.RedirectTo.StartsWith("/") && redirect.RedirectTo.Substring(1, 1) != "/")
                                return new RedirectResult($"{config["REDIRECT_DESTINATION_HOST"]}{redirect.RedirectTo}", false);
                            
                            return new RedirectResult($"{redirect.RedirectTo}", false);

                        }

                        return new AcceptedResult();


                    }
                    else {
                        log.LogInformation($"Start redirecting condition not met for {req.Path} - waiting until {startRedirecting} (current time {now})");

                        if (req.QueryString.ToString().IndexOf("check") >= 0) {
                            return new OkObjectResult($"Session found, waiting until {startRedirecting} before redirecting (current time {now})");
                        }

                    }
                } else {

                    log.LogInformation($"Start redirecting condition not met for {req.Path} - session has no start time)");

                    if (req.QueryString.ToString().IndexOf("check") >= 0) {
                        return new OkObjectResult($"Session found, but has no start time in sessionize");
                    }


                }

            }

            if (foundSession != null) {

                if (req.QueryString.ToString().IndexOf("check") >= 0) {
                    return new OkObjectResult($"Session found, wait for redirect");
                }

                string holdingPageUrl = $"{config["HOLDPAGE_SESSION"]}";
                holdingPageUrl = holdingPageUrl.Replace("{id}", foundSession.id ??= string.Empty);

                int holdpageCacheMinutes = 0;
                bool foundConfig = int.TryParse(config["HOLDPAGE_CACHE_MINUTES"], out holdpageCacheMinutes);
                if (!foundConfig) holdpageCacheMinutes = 10;

                CacheEntity cachedSessionPage = await CacheEntity.get(cacheTable, CacheType.Session, $"session-{id}", new TimeSpan(0, holdpageCacheMinutes, 0));

                if (cachedSessionPage != null) {
                    string value = cachedSessionPage.GetValue();

                    return new ContentResult {
                        ContentType = "text/html; charset=UTF-8",
                        Content = value
                    };
                }

                log.LogInformation($"Looking up holding page content: {holdingPageUrl}.");

                var client = new HttpClient();
                var getResponse = await client.GetAsync(holdingPageUrl);
                if (getResponse.IsSuccessStatusCode) {
                    
                    string value = await getResponse.Content.ReadAsStringAsync();

                    int redirectDelay = 10;
                    bool foundRedirectDelay = int.TryParse(config["REDIRECT_DELAY"], out redirectDelay);
                    if (!foundRedirectDelay) redirectDelay = 10;

                    value = value.Replace("{title}", foundSession.title ??= string.Empty);
                    value = value.Replace("{description}", foundSession.description ??= string.Empty );
                    value = value.Replace("{id}", foundSession.id ??= string.Empty);
                    value = value.Replace("{url}", foundSession.url ??= string.Empty);
                    value = value.Replace("{ical}", foundSession.ical ??= string.Empty);
                    value = value.Replace("{speakers}", string.Join(", ", foundSession.speakers.Select(speaker => speaker.name)));
                    value = value.Replace("{redirect-js}", Constants.REDIRECT_JS.Replace("{url}", $"{req.Path}?check")).Replace("{redirect-delay}", (redirectDelay * 1000).ToString());
                    value = value.Replace("{embed-js}", EMBED_JS);

                    if (value.IndexOf("{speaker-profiles}") >= 0) {
                        string speakerData = await GetSpeaker.GetAllSpeakers(cacheTable, log, context);
                        value = value.Replace("{speaker-profiles}", string.Join("", foundSession.speakers.Select(speaker => { 
                            SpeakerInformation foundSpeaker = GetSpeaker.ById(speakerData, speaker.id, req, false);
                            return SpeakerInformation.ProcessSpeakerTokens(Constants.SPEAKER_HTML, foundSpeaker);
                        })));
                    }

                    await CacheEntity.put(cacheTable, CacheType.Session, $"session-{id}", value, 600);

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

            MatchCollection matches = Regex.Matches(queuedHttpRequest.Path, "/redirect/session/(\\d+)/", RegexOptions.IgnoreCase);
            if (matches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, matches[0].Groups[1].Value);
                redirectEntity.ClickCount++;
                await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.GeoCount ??= "{}"), JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.CalendarGeoCount ??= "{}"));
                processRedirectQueueForGeo.Add(queuedHttpRequest);
    
                log.LogInformation($"Successfully processed click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                return;

            }

            MatchCollection sessionMatches = Regex.Matches(queuedHttpRequest.Path, "/calendar/session/(\\d+)", RegexOptions.IgnoreCase);
            if (sessionMatches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, sessionMatches[0].Groups[1].Value);
                redirectEntity.CalendarClickCount++;
                await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.GeoCount ??= "{}"), JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.CalendarGeoCount ??= "{}"));
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

            string ipLookupUrl = $"{config["FREEGEOIP_HOST"] ??= "https://freegeoip.app"}/json/{queuedHttpRequest.RemoteIpAddress}";

            log.LogInformation($"Looking up freegeoip: {ipLookupUrl}.");

            var client = new HttpClient();
            var getResponse = await client.GetAsync(ipLookupUrl);
            if (getResponse.StatusCode == HttpStatusCode.OK) {

                MatchCollection matches = Regex.Matches(queuedHttpRequest.Path, "/redirect/session/(\\d+)/", RegexOptions.IgnoreCase);
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
                    
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, _geoCount, JsonConvert.DeserializeObject<IDictionary<string, int>>(redirectEntity.CalendarGeoCount));
        
                    log.LogInformation($"Successfully processed geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                    return;

                }

                MatchCollection calendarMatches = Regex.Matches(queuedHttpRequest.Path, "/calendar/session/(\\d+)", RegexOptions.IgnoreCase);
                if (calendarMatches.Count > 0) {

                    string ipResponseString = await getResponse.Content.ReadAsStringAsync();
                    dynamic ipResponse = JsonConvert.DeserializeObject(ipResponseString);
                    GeoEntity geoEntity = new GeoEntity(ipResponse);
                    string geoEntityString = JsonConvert.SerializeObject(geoEntity);

                    RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, calendarMatches[0].Groups[1].Value);
                    if (redirectEntity.GeoCount == null) {
                        log.LogInformation($"Adding CalendarGeoCount property to redirect entity {queuedHttpRequest.Path}");
                        // redirectEntity.GeoCount = new Dictionary<string, int>();
                    }
                    
                    Dictionary<string, int> _calendarGeoCount = JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.CalendarGeoCount);
                    if (_calendarGeoCount.ContainsKey(geoEntityString)) {
                        log.LogInformation($"Incrementing CalendarGeoCount for redirect entity {queuedHttpRequest.Path}");
                        
                        _calendarGeoCount[geoEntityString] = _calendarGeoCount[geoEntityString] + 1;
                    }
                    else {
                        log.LogInformation($"Creating CalendarGeoCount for redirect entity {queuedHttpRequest.Path}");
                        _calendarGeoCount.Add(geoEntityString, 1);
                    }

                    log.LogInformation($" CalendarGeoCount property value: {JsonConvert.SerializeObject(redirectEntity.GeoCount)}");
                    
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, JsonConvert.DeserializeObject<IDictionary<string, int>>(redirectEntity.GeoCount), _calendarGeoCount);
        
                    log.LogInformation($"Successfully processed calendar geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

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
