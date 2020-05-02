using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Net.Http;
using com.m365may.entities;

namespace com.m365may.v1
{
    public static partial class QueueNames {

        public const string ProcessFileExports = "ProcessFileExports";
    }
    public static class ExportFile {

        [FunctionName("ScheduleFileExports")]
        public static void Run(
            [TimerTrigger("%FILE_EXPORT_SCHEDULE%")]TimerInfo myTimer, 
            [Queue(QueueNames.ProcessFileExports), StorageAccount("AzureWebJobsStorage")] ICollector<FileExportEntity> processFileExportsQueue,
            ILogger log,
            ExecutionContext context)
        {
            log.LogInformation($"ScheduleFileExports trigger function executed at: {DateTime.Now}");
            DateTime now = DateTime.UtcNow;

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            processFileExportsQueue.Add(new FileExportEntity(
                config.GetValue<string>("SESSIONIZE_SESSIONS_URL"), 
                "AzureStaticSiteStorage", 
                "$web",
                "data/sessions.json"));

            processFileExportsQueue.Add(new FileExportEntity(
                config.GetValue<string>("SESSIONIZE_SESSIONS_URL"), 
                "AzureStaticSiteStorage", 
                "$web",
                $"data/sessions_{now.ToString("u").Replace(" ", "_")}.json"));

            processFileExportsQueue.Add(new FileExportEntity(
                config.GetValue<string>("SESSIONIZE_SPEAKERS_URL"), 
                "AzureStaticSiteStorage", 
                "$web",
                "data/speakers.json"));

            processFileExportsQueue.Add(new FileExportEntity(
                config.GetValue<string>("SESSIONIZE_SPEAKERS_URL"), 
                "AzureStaticSiteStorage", 
                "$web",
                $"data/speakers_{now.ToString("u").Replace(" ", "_")}.json"));


        }

        [FunctionName("ProcessFileExports")]
        public static async void ProcessRedirectClicks(
            [QueueTrigger(QueueNames.ProcessFileExports)] string processFileExportsQueue,
            ILogger log,
            ExecutionContext context)
        {

            FileExportEntity queueFileExport = JsonConvert.DeserializeObject<FileExportEntity>(processFileExportsQueue);

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            var client = new HttpClient();
            var getResponse = await client.GetStreamAsync(queueFileExport.SourceUrl);

            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(config.GetValue<string>(queueFileExport.DestinationStorage));

            // Create the container and return a container client object
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(queueFileExport.DestinationContainer);

            BlobClient blobClient = containerClient.GetBlobClient(queueFileExport.DestinationLocation);

            await blobClient.DeleteIfExistsAsync();
            await blobClient.UploadAsync(getResponse);

            if (queueFileExport.DestinationLocation.ToLower().EndsWith(".json")) {
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "application/json" });
            }
            if (config.GetValue<string>("FILE_EXPORT_CACHE_CONTROL") != null) {
                await blobClient.SetHttpHeadersAsync(new BlobHttpHeaders { CacheControl = config.GetValue<string>("FILE_EXPORT_CACHE_CONTROL") });
            }

            // log.LogError($"URL lookup {queueFileExport.SourceUrl} failed with status code {getResponse.StatusCode}");
            // throw new System.Exception($"URL lookup {queueFileExport.SourceUrl} failed with status code {getResponse.StatusCode}");


        }



    }

}
