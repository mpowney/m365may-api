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
    public static class GetSpeaker
    {
        [FunctionName("GetSpeakerById")]
        public static async Task<IActionResult> RunGetSpeakerById (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "speaker/{id}")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            string id,
            ILogger log,
            ExecutionContext context)
        {

            id = id ?? req.Query["id"];
            bool link = req.Query.ContainsKey("link");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            id = id ?? data?.id;

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string jsonData = await GetSpeaker.GetAllSpeakers(cacheTable, log, context);

            if (jsonData != null) {

                #nullable enable
                SpeakerInformation? foundSpeaker = GetSpeaker.ById(jsonData, id, req, true);
                #nullable disable
                if (foundSpeaker != null) {
                    return new OkObjectResult(foundSpeaker);
                }

                log.LogError($"Speaker {id} not found, returning 404");
                return new NotFoundResult();

            }

            log.LogCritical($"Speakers data can't be found");
            return new BadRequestObjectResult($"Speakers data can't be found");

        }

        public static async Task<string?> GetAllSpeakers(CloudTable cacheTable, ILogger log, ExecutionContext context) {

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            int sessionizeCacheMinutes = 0;
            bool foundConfig = int.TryParse(config["SESSIONIZE_CACHE_MINUTES"], out sessionizeCacheMinutes);
            if (!foundConfig) sessionizeCacheMinutes = 1;

            string cacheKey = CacheType.Speakers;
            CacheEntity entity = await CacheEntity.get(cacheTable, CacheType.Speakers, cacheKey, new TimeSpan(0, sessionizeCacheMinutes, 0));

            if (entity != null) {
                log.LogInformation("Returning speakers info from cache entity");

                return entity.GetValue();

            }

            string url = config.GetValue<string>("SESSIONIZE_SPEAKERS_URL");
            log.LogInformation($"Looking up SESSIONIZE_SPEAKERS_URL {url}.");

            var client = new HttpClient();
            var postResponse = await client.GetAsync(url);

            if (postResponse.IsSuccessStatusCode) {

                string sessionizeContent = await postResponse.Content.ReadAsStringAsync();

                await CacheEntity.put(cacheTable, CacheType.Speakers, cacheKey, sessionizeContent, 60);
                log.LogTrace($"SESSIONIZE_SPEAKERS_URL {url} returned OK");
                return sessionizeContent;

            }
            else {
                log.LogCritical($"SESSIONIZE_SPEAKERS_URL {url} returned status {postResponse.StatusCode}");
            }

            return null;

        }


        #nullable enable
        public static SpeakerInformation? ById(string jsonData, string? id, HttpRequest req, bool addUrl) {
        #nullable disable

            if (id == null) {
                return null;
            }

            dynamic data = JsonConvert.DeserializeObject(jsonData);
            JArray speakersData = data;
            List<SpeakerInformation> speakers = JsonConvert.DeserializeObject<List<SpeakerInformation>>(JsonConvert.SerializeObject(speakersData));
            IEnumerable<SpeakerInformation> foundSpeakers = speakers.Where<SpeakerInformation>(session => session.id == id);

            if (foundSpeakers.Count() > 0)
            {
                SpeakerInformation speaker = foundSpeakers.First();

                // if (addUrl) session.url = $"{(req.IsHttps ? "https:" : "http:")}//{req.Host}/redirect/session/{id}";

                return speaker;
            }

            return null;

        }

    }

}
