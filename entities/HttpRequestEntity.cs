using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using Microsoft.Extensions.Primitives;

namespace com.m365may.entities {

    public class HttpRequestEntity {
        public HttpRequestEntity() { }
        public HttpRequestEntity(HttpRequest req) { 
            this.ContentLength = req.ContentLength;
            this.ContentType = req.ContentType;
            this.Headers = (IDictionary<string, StringValues>)req.Headers;
            this.Host = req.Host;
            this.IsHttps = req.IsHttps;
            this.Method = req.Method;
            this.Path = req.Path;
            this.PathBase = req.PathBase;
            this.QueryString = req.QueryString;
            this.RemoteIpAddress = req.HttpContext.Connection.RemoteIpAddress.ToString();
            this.Scheme = req.Scheme;
        }

        public long? ContentLength { get; set; }
        public string ContentType { get; set; }
        public IDictionary<string, StringValues> Headers { get; set; }
        public HostString Host { get; set; }
        public bool IsHttps { get; set; }
        public string Method { get; set; }
        public PathString Path { get; set; }
        public PathString PathBase { get; set; }
        public QueryString QueryString { get; set; }
        public string RemoteIpAddress {get; set; }
        public string Scheme { get; set; }
    }
    
}