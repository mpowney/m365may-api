using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;

namespace com.m365may.v1
{
    public static class GetCache
    {
        [FunctionName("GetAllSessions")]
        public static async Task<IActionResult> RunGetCacheById (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "data/sessions")] HttpRequest req,
            [Table(TableNames.Cache)] CloudTable cacheTable,
            ILogger log,
            ExecutionContext context)
        {

            string sessionData = await GetSession.GetAllSessions(cacheTable, log, context);
            if (sessionData != null) {
                return new ContentResult {
                    ContentType = "application/json",
                    Content = sessionData
                };
            }
            else {
                return new NotFoundResult();
            }

        }

    }

}
