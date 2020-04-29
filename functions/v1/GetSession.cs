using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Collections.Generic;
using System.Linq;
using com.m365may.entities;

namespace com.m365may.v1
{
    public static class GetSession
    {
        [FunctionName("GetSessionById")]
        public static async Task<IActionResult> RunGetSessionById (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "calendar/session/{id}")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            [Queue(QueueNames.ProcessRedirectClicks), StorageAccount("AzureWebJobsStorage")] ICollector<HttpRequestEntity> processRedirectQueue,
            string id,
            ILogger log,
            ExecutionContext context)
        {

            id = id ?? req.Query["id"];
            bool ical = req.Query.ContainsKey("ical");
            bool link = req.Query.ContainsKey("link");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            id = id ?? data?.id;

            int numericId = 0;
            int.TryParse(id, out numericId);
            if (numericId > 0) id = numericId.ToString();

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string jsonData = await GetSession.GetAllSessions(cacheTable, log, context);

            if (jsonData != null) {

                #nullable enable
                Session? foundSession = GetSession.ById(jsonData, id, req, true);
                #nullable disable
                if (foundSession != null) {

                    processRedirectQueue.Add(new HttpRequestEntity(req));

                    return ical ? 
                            (IActionResult)new ContentResult {
                                ContentType = "text/calendar",
                                Content = foundSession.ToIcalString(config["ICAL_FORMAT_TITLE"], config["ICAL_FORMAT_DESCRIPTION"], config["ICAL_FORMAT_UID"])
                            }
                        :
                            new OkObjectResult(foundSession);
                }

                log.LogError($"Session {id} not found, returning 404");
                return new NotFoundResult();

            }

            log.LogCritical($"Sessions data can't be found");
            return new BadRequestObjectResult($"Sessions data can't be found");

        }

        public static async Task<string?> GetAllSessions(CloudTable cacheTable, ILogger log, ExecutionContext context) {

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            int sessionizeCacheMinutes = 0;
            bool foundConfig = int.TryParse(config["SESSIONIZE_CACHE_MINUTES"], out sessionizeCacheMinutes);
            if (!foundConfig) sessionizeCacheMinutes = 1;

            string cacheKey = $"sessions";
            CacheEntity entity = await CacheEntity.get(cacheTable, CacheType.Sessions, cacheKey, new TimeSpan(0, sessionizeCacheMinutes, 0));

            if (entity != null) {
                log.LogInformation("Returning sessions info from cache entity");

                return entity.GetValue();

            }

            string url = config.GetValue<string>("SESSIONIZE_SESSIONS_URL");
            log.LogInformation($"Looking up SESSIONIZE_SESSIONS_URL {url}.");

            var client = new HttpClient();
            var postResponse = await client.GetAsync(url);

            if (postResponse.IsSuccessStatusCode) {

                string sessionizeContent = await postResponse.Content.ReadAsStringAsync();

                await CacheEntity.put(cacheTable, CacheType.Sessions, cacheKey, sessionizeContent, 60);
                log.LogTrace($"SESSIONIZE_SESSIONS_URL {url} returned OK");
                return sessionizeContent;

            }
            else {
                log.LogCritical($"SESSIONIZE_SESSIONS_URL {url} returned status {postResponse.StatusCode}");
            }

            return null;

        }


        #nullable enable
        public static Session? ById(string jsonData, string? id, HttpRequest req, bool addUrl) {
        #nullable disable

            if (id == null) {
                return null;
            }

            dynamic data = JsonConvert.DeserializeObject(jsonData);
            JArray sessionsData = data?[0].sessions;
            List<Session> sessions = JsonConvert.DeserializeObject<List<Session>>(JsonConvert.SerializeObject(sessionsData));
            IEnumerable<Session> foundSessions = sessions.Where<Session>(session => session.id == id);

            if (foundSessions.Count() > 0)
            {
                Session session = foundSessions.First();

                if (addUrl) session.url = $"{(req.IsHttps ? "https:" : "http:")}//{req.Host}/redirect/session/{id}/";
                if (addUrl) session.ical = $"{(req.IsHttps ? "https:" : "http:")}//{req.Host}/calendar/session/{id}?ical";

                return session;
            }

            return null;

        }

    }

}
