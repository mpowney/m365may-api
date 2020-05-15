using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;
using System.Collections.Generic;
using com.m365may.entities;
using com.m365may.utils;

namespace com.m365may.v1
{

    public static partial class TableNames {
        public const string Cache = "Cache";
        public const string RedirectSessions = "RedirectSessions";
        public const string Nodes = "Nodes";
    }

    public static partial class QueueNames {

        public const string ProcessRedirectClicks = "processredirectclicks";
        public const string ProcessRedirectClicksForGeo = "processredirectclicksforgeo";
        public const string SynchroniseRedirects = "synchroniseredirects";
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

        [FunctionName("RedirectVideo")]
        public static async Task<IActionResult> RunRedirectVideo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "redirect/video/{id}/")] HttpRequest req,
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
            
            if (redirect != null && redirect.VideoLink != null) {

                if (req.QueryString.ToString().IndexOf("check") < 0) {
                    processRedirectQueue.Add(new HttpRequestEntity(req));

                    if (redirect.VideoLink.StartsWith("/") && redirect.VideoLink.Substring(1, 1) != "/")
                        return new RedirectResult($"{config["REDIRECT_DESTINATION_HOST"]}{redirect.VideoLink}", false);
                    
                    return new RedirectResult($"{redirect.VideoLink}", false);

                }

                return new AcceptedResult();

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

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string nodeMaster = config["NODE_SYNC_MASTER_CONN"];

            if (nodeMaster != null) {

                try {

                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(nodeMaster);
                    CloudQueueClient queueClient = storageAccount.CreateCloudQueueClient();
                    CloudQueue destinationProcessClicksQueue = queueClient.GetQueueReference(QueueNames.ProcessRedirectClicks);

                    await destinationProcessClicksQueue.AddMessageAsync(new CloudQueueMessage(queuedHttpRequestString));
                    
                }
                catch (Exception ex) {
                    log.LogError(ex.Message);
                    throw ex;
                }

                return;

            }

            MatchCollection matches = Regex.Matches(queuedHttpRequest.Path, "/redirect/session/(\\d+)/", RegexOptions.IgnoreCase);
            if (matches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, matches[0].Groups[1].Value);
                if (redirectEntity != null) {

                    redirectEntity.ClickCount++;
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, redirectEntity.GeoCount, redirectEntity.CalendarGeoCount, redirectEntity.VideoGeoCount);
                    processRedirectQueueForGeo.Add(queuedHttpRequest);
        
                    log.LogInformation($"Successfully processed click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                    return;

                }
                else {

                    log.LogError($"Http request {queuedHttpRequest.Path} for click handling failed to match a redirect session");
                    throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");

                }

            }

            MatchCollection sessionMatches = Regex.Matches(queuedHttpRequest.Path, "/calendar/session/(\\d+)", RegexOptions.IgnoreCase);
            if (sessionMatches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, sessionMatches[0].Groups[1].Value);

                if (redirectEntity != null) {

                    redirectEntity.CalendarClickCount++;
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, redirectEntity.GeoCount, redirectEntity.CalendarGeoCount, redirectEntity.VideoGeoCount);
                    processRedirectQueueForGeo.Add(queuedHttpRequest);
        
                    log.LogInformation($"Successfully processed click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                    return;

                }
                else {

                    log.LogError($"Http request {queuedHttpRequest.Path} for click handling failed to match a redirect session");
                    throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling failed to match a redirect session");

                }

            }

            MatchCollection videoMatches = Regex.Matches(queuedHttpRequest.Path, "/redirect/video/(\\d+)/", RegexOptions.IgnoreCase);
            if (videoMatches.Count > 0) {

                RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, videoMatches[0].Groups[1].Value);

                if (redirectEntity != null) {

                    redirectEntity.VideoClickCount++;
                    await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, redirectEntity.GeoCount, redirectEntity.CalendarGeoCount, redirectEntity.VideoGeoCount);
                    processRedirectQueueForGeo.Add(queuedHttpRequest);
        
                    log.LogInformation($"Successfully processed click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                    return;

                }
                else {

                    log.LogError($"Http request {queuedHttpRequest.Path} for click handling failed to match a redirect session");
                    throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling failed to match a redirect session");

                }

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
                    if (redirectEntity != null) {

                        if (redirectEntity.GeoCount == null) {
                            log.LogInformation($"Adding GeoCount property to redirect entity {queuedHttpRequest.Path}");
                            // redirectEntity.GeoCount = new Dictionary<string, int>();
                        }
                        
                        Dictionary<string, int> _geoCount = JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.GeoCount ??= "{}");
                        if (_geoCount.ContainsKey(geoEntityString)) {
                            log.LogInformation($"Incrementing GeoCount for redirect entity {queuedHttpRequest.Path}");
                            
                            _geoCount[geoEntityString] = _geoCount[geoEntityString] + 1;
                        }
                        else {
                            log.LogInformation($"Creating GeoCount for redirect entity {queuedHttpRequest.Path}");
                            _geoCount.Add(geoEntityString, 1);
                        }

                        log.LogInformation($" GeoCount property value: {JsonConvert.SerializeObject(redirectEntity.GeoCount)}");
                        
                        await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, JsonConvert.SerializeObject(_geoCount), redirectEntity.CalendarGeoCount, redirectEntity.VideoGeoCount);
            
                        log.LogInformation($"Successfully processed geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                        return;
                    }
                    else {

                        log.LogError($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");
                        throw new System.Exception($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");

                    }
                }

                MatchCollection calendarMatches = Regex.Matches(queuedHttpRequest.Path, "/calendar/session/(\\d+)", RegexOptions.IgnoreCase);
                if (calendarMatches.Count > 0) {

                    string ipResponseString = await getResponse.Content.ReadAsStringAsync();
                    dynamic ipResponse = JsonConvert.DeserializeObject(ipResponseString);
                    GeoEntity geoEntity = new GeoEntity(ipResponse);
                    string geoEntityString = JsonConvert.SerializeObject(geoEntity);

                    RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, calendarMatches[0].Groups[1].Value);

                    if (redirectEntity != null) {

                        Dictionary<string, int> _calendarGeoCount = JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.CalendarGeoCount ??= "{}");
                        if (_calendarGeoCount.ContainsKey(geoEntityString)) {
                            log.LogInformation($"Incrementing CalendarGeoCount for redirect entity {queuedHttpRequest.Path}");
                            
                            _calendarGeoCount[geoEntityString] = _calendarGeoCount[geoEntityString] + 1;
                        }
                        else {
                            log.LogInformation($"Creating CalendarGeoCount for redirect entity {queuedHttpRequest.Path}");
                            _calendarGeoCount.Add(geoEntityString, 1);
                        }

                        log.LogInformation($" CalendarGeoCount property value: {JsonConvert.SerializeObject(redirectEntity.GeoCount)}");
                        
                        await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, redirectEntity.GeoCount, JsonConvert.SerializeObject(_calendarGeoCount), redirectEntity.VideoGeoCount);
            
                        log.LogInformation($"Successfully processed calendar geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                        return;

                    }
                    else {

                        log.LogError($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");
                        throw new System.Exception($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");

                    }

                }

                MatchCollection videoMatches = Regex.Matches(queuedHttpRequest.Path, "/redirect/video/(\\d+)/", RegexOptions.IgnoreCase);
                if (videoMatches.Count > 0) {

                    string ipResponseString = await getResponse.Content.ReadAsStringAsync();
                    dynamic ipResponse = JsonConvert.DeserializeObject(ipResponseString);
                    GeoEntity geoEntity = new GeoEntity(ipResponse);
                    string geoEntityString = JsonConvert.SerializeObject(geoEntity);

                    RedirectEntity redirectEntity = await RedirectEntity.get(redirectTable, videoMatches[0].Groups[1].Value);

                    if (redirectEntity != null) {

                        Dictionary<string, int> _videoGeoCount = JsonConvert.DeserializeObject<Dictionary<string, int>>(redirectEntity.VideoGeoCount ??= "{}");
                        if (_videoGeoCount.ContainsKey(geoEntityString)) {
                            log.LogInformation($"Incrementing VideoGeoCount for redirect entity {queuedHttpRequest.Path}");
                            
                            _videoGeoCount[geoEntityString] = _videoGeoCount[geoEntityString] + 1;
                        }
                        else {
                            log.LogInformation($"Creating VideoGeoCount for redirect entity {queuedHttpRequest.Path}");
                            _videoGeoCount.Add(geoEntityString, 1);
                        }

                        log.LogInformation($" VideoGeoCount property value: {JsonConvert.SerializeObject(redirectEntity.GeoCount)}");
                        
                        await RedirectEntity.put(redirectTable, redirectEntity.RowKey, redirectEntity.RedirectTo, redirectEntity.VideoLink, redirectEntity.StartRedirectingMinutes ??= 0, redirectEntity.ClickCount, redirectEntity.CalendarClickCount, redirectEntity.VideoClickCount, redirectEntity.GeoCount, redirectEntity.CalendarGeoCount, JsonConvert.SerializeObject(_videoGeoCount));
            
                        log.LogInformation($"Successfully processed video geo ip click for redirect query {queuedHttpRequest.Path} from {queuedHttpRequest.RemoteIpAddress}");

                        return;

                    }
                    else {

                        log.LogError($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");
                        throw new System.Exception($"Http request {queuedHttpRequest.Path} for click geo handling failed to match a redirect session");

                    }
                }
                
                log.LogError($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");
                throw new System.Exception($"Http request {queuedHttpRequest.Path} for click handling doesn't match handled paths");
            
            }

            log.LogError($"Free geo ip lookup for IP {queuedHttpRequest.RemoteIpAddress} failed with status code {getResponse.StatusCode}");
            throw new System.Exception($"Free geo ip lookup for IP {queuedHttpRequest.RemoteIpAddress} failed with status code {getResponse.StatusCode}");

        }    

        [FunctionName("RedirectsGet")]
        public static async Task<IActionResult> RedirectsGet (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_api/v1/redirects")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            RedirectEntity[] entities = await RedirectEntity.getAll(redirectTable);
            if (entities == null) {
                return new NotFoundResult();
            }

            return new OkObjectResult(entities);

        }

        [FunctionName("RedirectGet")]
        public static async Task<IActionResult> RedirectGet (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_api/v1/redirect/{key}")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            RedirectEntity entity = await RedirectEntity.get(redirectTable, key);
            if (entity == null) {
                return new NotFoundResult();
            }

            return new OkObjectResult(entity);

        }

        [FunctionName("RedirectDelete")]
        public static async Task<IActionResult> RedirectDelete (
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "_api/v1/redirect/{key}")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            RedirectEntity entity = await RedirectEntity.get(redirectTable, key);
            if (entity == null) {
                return new NotFoundResult();
            }

            bool deleteSuccess = await RedirectEntity.delete(redirectTable, entity);
            return deleteSuccess ? (IActionResult)new OkObjectResult(entity) : new BadRequestResult();

        }

        [FunctionName("RedirectPost")]
        public static async Task<IActionResult> RedirectPost (
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "_api/v1/redirect")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            RedirectEntity entity = JsonConvert.DeserializeObject<RedirectEntity>(requestBody);

            if (entity.RowKey == null || entity.RedirectTo == null) {
                return new BadRequestObjectResult($"Please specify the key and redirectTo parameters in the request body");
            }

            log.LogInformation($"Getting Redirect row for values {claimsPrincipal.Identity.Name} and {entity.RowKey}");
            RedirectEntity existingEntity = await RedirectEntity.get(redirectTable, entity.RowKey);
            if (existingEntity != null) {
                return new BadRequestObjectResult($"Redirect with {entity.RowKey} already exists for {claimsPrincipal.Identity.Name}");
            }

            bool success = await RedirectEntity.put(redirectTable, entity.RowKey, entity.RedirectTo, entity.VideoLink, -10, 0, 0, 0, "{}", "{}", "{}");
            if (!success) {
                return new BadRequestObjectResult($"Error occurred creating {entity.RowKey} already exists for {claimsPrincipal.Identity.Name}");
            }

            return new OkResult();
            
        }

        [FunctionName("RedirectPatch")]
        public static async Task<IActionResult> RedirectPatch (
            [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "_api/v1/redirect/{key}")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            string key,
            ILogger log,
            ExecutionContext context,
            ClaimsPrincipal claimsPrincipal)
        {

            if (!claimsPrincipal.Identity.IsAuthenticated) {
                return new UnauthorizedResult();
            }

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic entity = JsonConvert.DeserializeObject<dynamic>(requestBody);

            log.LogInformation($"Getting Redirect row for values {claimsPrincipal.Identity.Name} and {entity.RowKey}");
            RedirectEntity existingEntity = await RedirectEntity.get(redirectTable, key);
            if (existingEntity == null) {
                return new BadRequestObjectResult($"Redirect with {key} doesn't exist for {claimsPrincipal.Identity.Name}");
            }

            existingEntity.RedirectTo = entity.redirectTo ??= existingEntity.RedirectTo;
            existingEntity.StartRedirectingMinutes = entity.startRedirectingMinutes ??= existingEntity.StartRedirectingMinutes;
            existingEntity.VideoLink = entity.videoLink ??= existingEntity.VideoLink;

            bool success = await RedirectEntity.put(redirectTable, existingEntity);
            if (!success) {
                return new BadRequestObjectResult($"Error occurred updating {key} for {claimsPrincipal.Identity.Name}");
            }

            return new OkResult();
            
        }

    }

}
