using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace com.m365may.entities
{
    public class UserEntity : TableEntity
    {

        public string[] Permissions { get; set; }
        public UserEntity() {}
        public UserEntity(string key, string[] permissions) {
            
            this.PartitionKey = string.Empty;
            this.RowKey = key;
            this.Permissions = permissions;
            
        }
        public bool HasPermission(string permission) {
            foreach (string perm in this.Permissions) {
                if (perm == permission) return true;
            }
            return false;
        }

        public static async Task<List<UserEntity>> get(CloudTable userTable) {

            await userTable.CreateIfNotExistsAsync();

            TableQuery<UserEntity> rangeQuery = new TableQuery<UserEntity>().Where(
                TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{string.Empty}"));

            var sessionRedirectFound = await userTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                List<UserEntity> entities = sessionRedirectFound.Results;
                return entities;

            }
            else {

                return null;

            }

        }

        public static async Task<UserEntity> get(CloudTable userTable, string key) {

            await userTable.CreateIfNotExistsAsync();

            TableQuery<UserEntity> rangeQuery = new TableQuery<UserEntity>().Where(
                TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{string.Empty}"),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, 
                            $"{key}")));

            var sessionRedirectFound = await userTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                UserEntity entity = sessionRedirectFound.Results.ToArray()[0];
                return entity;

            }
            else {

                return new UserEntity("invalid", new string[0]);

            }

        }

        public static async Task<bool> put(CloudTable userTable, string key, string[] permissions) {
     
            await userTable.CreateIfNotExistsAsync();
            
            try {

                UserEntity newEntity = new UserEntity(key, permissions);
                TableOperation insertEntityOperation = TableOperation.InsertOrMerge(newEntity);
                await userTable.ExecuteAsync(insertEntityOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

        public static async Task<bool> put(CloudTable userTable, UserEntity entity) {
     
            await userTable.CreateIfNotExistsAsync();
            
            try {

                TableOperation insertCacheOperation = TableOperation.InsertOrMerge(entity);
                await userTable.ExecuteAsync(insertCacheOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

        public static async Task<bool> delete(CloudTable userTable, UserEntity entity) {
     
            await userTable.CreateIfNotExistsAsync();
            
            try {

                TableOperation deleteOperation = TableOperation.Delete(entity);
                await userTable.ExecuteAsync(deleteOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

    }

}
