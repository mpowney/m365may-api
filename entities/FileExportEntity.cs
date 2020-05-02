using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace com.m365may.entities {

    public class FileExportEntity {
        public FileExportEntity() { }
        public FileExportEntity(string sourceUrl, string destinationStorage, string destinationContainer, string destinationLocation) { 
            this.SourceUrl = sourceUrl;
            this.DestinationStorage = destinationStorage;
            this.DestinationContainer = destinationContainer;
            this.DestinationLocation = destinationLocation;
        }

        public string SourceUrl {get; set; }
        public string DestinationStorage { get; set; }
        public string DestinationContainer { get; set; }
        public string DestinationLocation { get; set; }
    }
    
}