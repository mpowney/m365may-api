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
    public static class GetCache
    {
        [FunctionName("GetCacheById")]
        public static async Task<IActionResult> RunGetCacheById (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "cache/{key}")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            string key,
            ILogger log,
            ExecutionContext context)
        {

            key = key ?? req.Query["key"];
            bool link = req.Query.ContainsKey("link");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);
            key = key ?? data?.key;

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            CacheEntity entity = await CacheEntity.get(cacheTable, key, key, null);

            if (entity != null) {

                if (entity != null) {
                    return new OkObjectResult(entity.GetValue());
                }

                log.LogError($"Cache item {key} not found, returning 404");
                return new NotFoundResult();

            }

            log.LogCritical($"Cache data can't be found");
            return new BadRequestObjectResult($"Cache data can't be found");

        }

    }

}
