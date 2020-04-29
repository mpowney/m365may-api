using Microsoft.WindowsAzure.Storage.Table;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace com.m365may.entities
{
    public class GeoEntity {
        public GeoEntity(dynamic rawObject) {
            this.CountryCode = rawObject.country_code;
            this.CountryName = rawObject.country_name;
            this.RegionCode = rawObject.region_code;
            this.City = rawObject.city;
            this.TimeZone = rawObject.time_zone;
            this.Latitude = rawObject.latitude;
            this.Longitude = rawObject.longitude;
        }
        public string CountryCode { get; set; }
        public string CountryName { get; set; }
        public string RegionCode { get; set; }
        public string City { get; set; }
        public string ZipCode { get; set; }
        public string TimeZone {get; set; }
        public double Latitude {get; set; }
        public double Longitude {get; set; }

    }

    public class RedirectEntity : TableEntity
    {
        public RedirectEntity() {}
        public RedirectEntity(string key, string redirectTo, int clickCount, int calendarClickCount, IDictionary<string, int> geoCount, IDictionary<string, int> calendarGeoCount) {
            
            string _geoCount = JsonConvert.SerializeObject(geoCount);
            string _calendarGeoCount = JsonConvert.SerializeObject(calendarGeoCount);

            this.PartitionKey = "";
            this.RowKey = key;
            this.RedirectTo = redirectTo;
            this.ClickCount = clickCount;
            this.CalendarClickCount = calendarClickCount;
            this.GeoCount = _geoCount;
            this.CalendarGeoCount = _calendarGeoCount;
            
        }
        public string RedirectTo { get; set; }
        public int ClickCount { get; set; }
        public int CalendarClickCount { get; set; }
        public string GeoCount { get; set; }
        public string CalendarGeoCount { get; set; }
        public int? StartRedirectingMinutes { get; set; }
        public static async Task<RedirectEntity> get(CloudTable redirectTable, string key) {

            await redirectTable.CreateIfNotExistsAsync();

            TableQuery<RedirectEntity> rangeQuery = new TableQuery<RedirectEntity>().Where(
                        TableQuery.GenerateFilterCondition("RowKey", QueryComparisons.Equal, 
                            $"{key}"));

            var sessionRedirectFound = await redirectTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                RedirectEntity entity = sessionRedirectFound.Results.ToArray()[0];
                return entity;

            }
            else {

                return null;

            }

        }

        public static async Task<bool> put(CloudTable redirectTable, string key, string redirectTo, int clickCount, int calendarClickCount, IDictionary<string, int> geoCount, IDictionary<string, int> calendarGeoCount) {
     
            await redirectTable.CreateIfNotExistsAsync();
            
            try {

                RedirectEntity newEntity = new RedirectEntity(key, redirectTo, clickCount, calendarClickCount, geoCount, calendarGeoCount);
                TableOperation insertCacheOperation = TableOperation.InsertOrMerge(newEntity);
                await redirectTable.ExecuteAsync(insertCacheOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

    }

}
