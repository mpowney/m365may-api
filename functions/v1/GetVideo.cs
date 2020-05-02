using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Table;
using com.m365may.entities;

namespace com.m365may.v1
{
    public static class GetVideo
    {
        [FunctionName("GetAllVideos")]
        public static async Task<IActionResult> RunGetAllVideos (
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = "data/videos")] HttpRequest req,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context)
        {

            dynamic[]? videoData = await GetVideo.All(redirectTable);
            if (videoData != null) {
                return new OkObjectResult(videoData);
            }
            else {
                return new NotFoundResult();
            }

        }

        public static async Task<dynamic[]?> All (CloudTable redirectTable) {

            RedirectEntity[] redirectData = await RedirectEntity.getAll(redirectTable);
            if (redirectData != null) {
                return redirectData
                            .Where(redirect => redirect.VideoLink != null && redirect.VideoLink != "")
                            .Select((redirectData, index) => new { RowKey = redirectData.RowKey, VideoLink = redirectData.VideoLink}).ToArray();
            }

            return null;
        }


    }

}
