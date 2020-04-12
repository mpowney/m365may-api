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

namespace com.m365may
{
    public static class GetSession
    {
        [FunctionName("GetSession")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "_api/v1/session/{id}")] HttpRequest req,
            [Table("Cache")] CloudTable cacheTable,
            string id,
            ILogger log,
            ExecutionContext context)
        {

            id = id ?? req.Query["id"];

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            id = id ?? data?.id;

            string cacheKey = $"sessions";
            CacheEntity entity = await CacheEntity.get(cacheTable, CacheType.Sessions, cacheKey);

            if (entity != null) {
                log.LogInformation("Returning sessions info from cache entity");

                string jsonData = entity.GetValue();

                #nullable enable
                Session? foundSession = GetSession.ById(jsonData, id);
                #nullable disable
                if (foundSession != null) {
                    return new OkObjectResult(GetSession.ById(jsonData, id));
                }

                return new NotFoundResult();


            }

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string url = config.GetValue<string>("SESSIONIZE_SESSIONS_URL");
            log.LogInformation($"Looking up SESSIONIZE_SESSIONS_URL {url}.");

            var client = new HttpClient();
            var postResponse = await client.GetAsync(url);

            if (postResponse.StatusCode == HttpStatusCode.OK) {

                log.LogTrace($"SESSIONIZE_SESSIONS_URL {url} returned OK");
                var jsonData = await postResponse.Content.ReadAsStringAsync();

                bool success = await CacheEntity.put(cacheTable, CacheType.Sessions, cacheKey, jsonData);
                log.LogInformation($"Populated sessions cache success: {success}");

                #nullable enable
                Session? foundSession = GetSession.ById(jsonData, id);
                #nullable disable
                if (foundSession != null) {
                    return new OkObjectResult(GetSession.ById(jsonData, id));
                }

                return new NotFoundResult();

            }

            return new BadRequestObjectResult($"Bad result: {postResponse.StatusCode}");

        }

        #nullable enable
        private static Session? ById(string jsonData, string? id) {
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
                return foundSessions.First();
            }

            return null;

        }

    }

}
