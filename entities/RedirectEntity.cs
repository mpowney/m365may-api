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
        public RedirectEntity(string key, string redirectTo, string videoLink, int clickCount, int calendarClickCount, int videoClickCount, string geoCount, string calendarGeoCount, string videoGeoCount) {
            
            this.PartitionKey = "";
            this.RowKey = key;
            this.RedirectTo = redirectTo;
            this.VideoLink = videoLink;
            this.ClickCount = clickCount;
            this.CalendarClickCount = calendarClickCount;
            this.VideoClickCount = videoClickCount;
            this.GeoCount = geoCount;
            this.CalendarGeoCount = calendarGeoCount;
            this.VideoGeoCount = videoGeoCount;
            
        }
        public RedirectEntity(string key, string redirectTo, string videoLink, int clickCount, int calendarClickCount, int videoClickCount, IDictionary<string, int> geoCount, IDictionary<string, int> calendarGeoCount, IDictionary<string, int> videoGeoCount) {
            
            string _geoCount = JsonConvert.SerializeObject(geoCount);
            string _calendarGeoCount = JsonConvert.SerializeObject(calendarGeoCount);
            string _videoGeoCount = JsonConvert.SerializeObject(videoGeoCount);

            this.PartitionKey = "";
            this.RowKey = key;
            this.RedirectTo = redirectTo;
            this.VideoLink = videoLink;
            this.ClickCount = clickCount;
            this.CalendarClickCount = calendarClickCount;
            this.VideoClickCount = videoClickCount;
            this.GeoCount = _geoCount;
            this.CalendarGeoCount = _calendarGeoCount;
            this.VideoGeoCount = _videoGeoCount;
            
        }
        public string RedirectTo { get; set; }
        public string VideoLink { get; set; }
        public int ClickCount { get; set; }
        public int CalendarClickCount { get; set; }
        public int VideoClickCount { get; set; }
        public string GeoCount { get; set; }
        public string CalendarGeoCount { get; set; }
        public string VideoGeoCount { get; set; }
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

        public static async Task<RedirectEntity[]> getAll(CloudTable redirectTable) {

            await redirectTable.CreateIfNotExistsAsync();

            TableQuery<RedirectEntity> rangeQuery = new TableQuery<RedirectEntity>();

            var sessionRedirectFound = await redirectTable.ExecuteQuerySegmentedAsync(rangeQuery, null);
            if (sessionRedirectFound.Results.Count > 0) {

                RedirectEntity[] entities = sessionRedirectFound.Results.ToArray();
                return entities;

            }
            else {

                return null;

            }

        }

        public static async Task<bool> put(CloudTable redirectTable, string key, string redirectTo, string videoLink, int clickCount, int calendarClickCount, int videoClickCount, string geoCount, string calendarGeoCount, string videoGeoCount) {
     
            await redirectTable.CreateIfNotExistsAsync();
            
            try {

                RedirectEntity newEntity = new RedirectEntity(key, redirectTo, videoLink, clickCount, calendarClickCount, videoClickCount, geoCount, calendarGeoCount, videoGeoCount);
                TableOperation insertCacheOperation = TableOperation.InsertOrMerge(newEntity);
                await redirectTable.ExecuteAsync(insertCacheOperation);

            }
            catch {

                return false;
                
            }
            return true;

        }

        public static async Task<bool> put(CloudTable redirectTable, string key, string redirectTo, string videoLink, int clickCount, int calendarClickCount, int videoClickCount, IDictionary<string, int> geoCount, IDictionary<string, int> calendarGeoCount, IDictionary<string, int> videoGeoCount) {
     
            await redirectTable.CreateIfNotExistsAsync();
            
            try {

                RedirectEntity newEntity = new RedirectEntity(key, redirectTo, videoLink, clickCount, calendarClickCount, videoClickCount, geoCount, calendarGeoCount, videoGeoCount);
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
