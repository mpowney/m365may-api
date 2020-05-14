using System;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using com.m365may.entities;

namespace com.m365may.v1
{
    public static class Synchronise
    {

        [FunctionName("TriggerRedirectsSync")]
        public static async void TriggerRedirectsSync(
            [TimerTrigger("%NODE_SYNC_SCHEDULE%")]TimerInfo myTimer, 
            [Table(TableNames.Nodes)] CloudTable nodeTable,
            [Queue(QueueNames.SynchroniseRedirects)] ICollector<string> syncRedirectsQueue)
        {

            List<NodeEntity> nodes = await NodeEntity.get(nodeTable);
            if (nodes != null) {
                foreach (NodeEntity node in nodes) {
                    syncRedirectsQueue.Add(node.RowKey);
                }
            }

        }


        [FunctionName("ProcessRedirectsSync")]
        public static async void ProcessRedirectsSync(
            [QueueTrigger(QueueNames.SynchroniseRedirects)] string node,
            [Table(TableNames.RedirectSessions)] CloudTable redirectTable,
            ILogger log,
            ExecutionContext context)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(context.FunctionAppDirectory)
                .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            string connectionString = config[$"NODE_SYNC_CONNECTION_{node}"];

            if (connectionString == null) {
                throw new Exception($"No connection string found for node [{node}]. Aborting.");
            }

            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(connectionString);
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
            CloudTable destinationRedirectTable = tableClient.GetTableReference(TableNames.RedirectSessions);

            await destinationRedirectTable.CreateIfNotExistsAsync();


            RedirectEntity[] redirects = await RedirectEntity.getAll(redirectTable);

            foreach (RedirectEntity redirect in redirects) {
                await RedirectEntity.put(destinationRedirectTable, redirect);
            }

            RedirectEntity[] destinationRedirects = await RedirectEntity.getAll(destinationRedirectTable);
            foreach(RedirectEntity destinationRedirect in destinationRedirects) {

                if (redirects.Where(checkRedirect => checkRedirect.RowKey == destinationRedirect.RowKey).Count() == 0) {
                    await RedirectEntity.delete(destinationRedirectTable, destinationRedirect);
                }

            }

        }

    }
}

