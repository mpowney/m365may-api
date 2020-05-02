using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using com.m365may.entities;

namespace com.m365may.v1
{
    public static partial class QueueNames {

        public const string ProcessFileExports = "ProcessFileExports";
    }
    public static partial class FileExportTypes {

        public const string AllVideos = "AllVideos";
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

            processFileExportsQueue.Add(new FileExportEntity(
                FileExportTypes.AllVideos,
                "AzureStaticSiteStorage", 
                "$web",
                $"data/videos.json"));

            processFileExportsQueue.Add(new FileExportEntity(
                FileExportTypes.AllVideos, 
                "AzureStaticSiteStorage", 
                "$web",
                $"data/videos_{now.ToString("u").Replace(" ", "_")}.json"));


        }

        [FunctionName("ProcessFileExports")]
        public static async void ProcessFileExports(
            [QueueTrigger(QueueNames.ProcessFileExports)] string processFileExportsQueue,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context)
        {

            FileExportEntity queueFileExport = JsonConvert.DeserializeObject<FileExportEntity>(processFileExportsQueue);

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            Stream stream = new MemoryStream();
            if (queueFileExport.SourceUrl.StartsWith("https://") || queueFileExport.SourceUrl.StartsWith("http://")) {
                var client = new HttpClient();
                stream = await client.GetStreamAsync(queueFileExport.SourceUrl);
            }
            else {
                if (queueFileExport.SourceUrl == FileExportTypes.AllVideos) {
                    dynamic[] data = await GetVideo.All(redirectTable);
                    stream = new MemoryStream();
                    var writer = new StreamWriter(stream);
                    writer.Write(JsonConvert.SerializeObject(data));
                    writer.Flush();
                    stream.Position = 0;
                }
            }

            // Create a BlobServiceClient object which will be used to create a container client
            BlobServiceClient blobServiceClient = new BlobServiceClient(config.GetValue<string>(queueFileExport.DestinationStorage));

            // Create the container and return a container client object
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(queueFileExport.DestinationContainer);

            BlobClient blobClient = containerClient.GetBlobClient(queueFileExport.DestinationLocation);

            await blobClient.DeleteIfExistsAsync();
            await blobClient.UploadAsync(stream);

            BlobHttpHeaders headers = new BlobHttpHeaders();
            bool addHeaders = false;
            
            if (queueFileExport.DestinationLocation.ToLower().EndsWith(".json")) {
                headers.ContentType = "application/json";
                addHeaders = true;
            }
            if (config.GetValue<string>("FILE_EXPORT_CACHE_CONTROL") != null) {
                headers.CacheControl = config.GetValue<string>("FILE_EXPORT_CACHE_CONTROL");
                addHeaders = true;
            }
            if (addHeaders) await blobClient.SetHttpHeadersAsync(headers);

            // log.LogError($"URL lookup {queueFileExport.SourceUrl} failed with status code {getResponse.StatusCode}");
            // throw new System.Exception($"URL lookup {queueFileExport.SourceUrl} failed with status code {getResponse.StatusCode}");


        }



    }

}
