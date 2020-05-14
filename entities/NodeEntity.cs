using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System;

namespace com.m365may.entities
{
    public class NodeEntity : TableEntity
    {
        public NodeEntity() {}
        public NodeEntity(string key) {
            
            this.PartitionKey = string.Empty;
            this.RowKey = key;
            
        }
        public static async Task<List<NodeEntity>> get(CloudTable nodeTable) {

            await nodeTable.CreateIfNotExistsAsync();

            TableQuery<NodeEntity> rangeQuery = new TableQuery<NodeEntity>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{string.Empty}"));

            var sessionRedirectFound = await nodeTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                List<NodeEntity> entities = sessionRedirectFound.Results;
                return entities;

            }
            else {

                return null;

            }

        }

        public static async Task<NodeEntity> get(CloudTable nodeTable, string key) {

            await nodeTable.CreateIfNotExistsAsync();

            TableQuery<NodeEntity> rangeQuery = new TableQuery<NodeEntity>().Where(
                TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{string.Empty}"),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, 
                            $"{key}")));

            var sessionRedirectFound = await nodeTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                NodeEntity entity = sessionRedirectFound.Results.ToArray()[0];
                return entity;

            }
            else {

                return null;

            }

        }

        public static async Task<bool> put(CloudTable nodeTable, string key) {
     
            await nodeTable.CreateIfNotExistsAsync();
            
            try {

                NodeEntity newEntity = new NodeEntity(key);
                TableOperation insertEntityOperation = TableOperation.InsertOrMerge(newEntity);
                await nodeTable.ExecuteAsync(insertEntityOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

        public static async Task<bool> put(CloudTable nodeTable, NodeEntity entity) {
     
            await nodeTable.CreateIfNotExistsAsync();
            
            try {

                TableOperation insertCacheOperation = TableOperation.InsertOrMerge(entity);
                await nodeTable.ExecuteAsync(insertCacheOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

    }

}
