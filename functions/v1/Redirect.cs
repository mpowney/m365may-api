using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace com.m365may
{
    public static class Redirect
    {
        [FunctionName("RedirectSession")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "_redirect/session/{id}")] HttpRequest req,
            [Table("Cache")] CloudTable cacheTable,
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

            return new RedirectResult($"{config["REDIRECT_SESSIONS_DESTINATION"]}{id}");

        }

    }

}
