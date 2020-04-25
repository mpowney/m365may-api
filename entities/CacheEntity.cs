using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System;

namespace com.m365may.entities
{

    public static class CacheType {
        public static string Sessions { get { return "sessions"; } }
        public static string Speakers { get { return "speakers"; } }
    }

    public class CacheEntity : TableEntity
    {
        public CacheEntity() {}
        public CacheEntity(string cacheType, string key, string value, int storeForSeconds = 60) {
            
            this.PartitionKey = cacheType;
            this.RowKey = key;
            this.StoreForSeconds = storeForSeconds;

            int index = 0;
            // Hacky way of getting around Table Storage properties restricted to 32768 butes
            for (int x = 0; x < value.Length; x = x + 32000) {
                int remaining = value.Length - x;
                if (remaining > 32000) {
                    remaining = 32000;
                }
                this.GetType().GetProperty($"Value{index}").SetValue(this, value.Substring(x, remaining));
                index++;
            }

            
        }
        public int StoreForSeconds { get; set; }
        public string Value0 { get; set; }
        public string Value1 { get; set; }
        public string Value2 { get; set; }
        public string Value3 { get; set; }
        public string Value4 { get; set; }
        public string Value5 { get; set; }
        public string Value6 { get; set; }
        public string Value7 { get; set; }
        public string Value8 { get; set; }
        public string Value9 { get; set; }

        public string GetValue() {
            return (
                string.Concat(this.Value0,
                                this.Value1,
                                this.Value2,
                                this.Value3,
                                this.Value4,
                                this.Value5,
                                this.Value6,
                                this.Value7,
                                this.Value8,
                                this.Value9)
            );
        }

        public static async Task<CacheEntity> get(CloudTable cacheTable, string cacheType, string key, TimeSpan cacheExpiry) {

            await cacheTable.CreateIfNotExistsAsync();

            TimeSpan expiry = cacheExpiry == null ? new TimeSpan(365, 0, 0, 0) : cacheExpiry;
            
            TableQuery<CacheEntity> rangeQuery = new TableQuery<CacheEntity>().Where(
                TableQuery.CombineFilters(
                    TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{cacheType}"),
                        TableOperators.And,
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, 
                            key)
                    ),
                    TableOperators.And, 
                    TableQuery.GenerateFilterConditionForDate("Timestamp", QueryComparisons.GreaterThan,
                        DateTime.Now.Subtract(expiry))
                    ));

            var cacheFound = await cacheTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (cacheFound.Results.Count > 0) {

                CacheEntity entity = cacheFound.Results.ToArray()[0];
                return entity;

            }
            else {

                return null;

            }

        }

        public static async Task<bool> put(CloudTable cacheTable, string cacheType, string key, string value, int storeForSeconds = 60) {
     
            await cacheTable.CreateIfNotExistsAsync();
            
            try {

                CacheEntity newEntity = new CacheEntity(cacheType, key, value, storeForSeconds);
                TableOperation insertCacheOperation = TableOperation.InsertOrReplace(newEntity);
                await cacheTable.ExecuteAsync(insertCacheOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

        public static async Task<bool> clear(CloudTable cacheTable, string cacheType, string key) {
     
            try {

                int length = key.Length - 1;
                var lastChar = key[length];
        
                var nextLastChar = (char) (lastChar + 1);
        
                var startsWithEndPattern = key.Substring(0, length) + nextLastChar;

                TableQuery<CacheEntity> rangeQuery = new TableQuery<CacheEntity>().Where(
                    TableQuery.CombineFilters(
                        TableQuery.GenerateFilterCondition("PartitionKey", QueryComparisons.Equal, 
                            $"{cacheType}"),
                        TableOperators.And,
                        TableQuery.CombineFilters(
                            TableQuery.GenerateFilterCondition("RowKey",
                                QueryComparisons.GreaterThanOrEqual,
                                key),
                            TableOperators.And,
                            TableQuery.GenerateFilterCondition("RowKey",
                                QueryComparisons.LessThan,
                                startsWithEndPattern)
                            )));

                var cacheFound = await cacheTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
                if (cacheFound.Results.Count > 0) {

                    CacheEntity[] cacheEntities = cacheFound.Results.ToArray();
                    for (int x = 0; x< cacheEntities.Length; x++) {

                        CacheEntity entity = cacheEntities[x];
                        TableOperation deleteCacheOperation = TableOperation.Delete(entity);
                        await cacheTable.ExecuteAsync(deleteCacheOperation);

                    }
                    return true;

                }
                else {

                    return false;

                }

            }
            catch {

                return false;
                
            }

        }

    }

}
