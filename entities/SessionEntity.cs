using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using Ical.Net;

namespace com.m365may.entities {

    public class Session {
        public Session() { }
        public string id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string url { get; set; }
        public string ical { get; set; }
        public DateTime? startsAt { get; set; }
        public DateTime? endsAt { get; set; }
        public Speaker[] speakers { get; set; }

        public string ToIcalString(string SummaryFormat = "{title}", string DescriptionFormat = "{description}", string UidFormat = "{id}") 
        {

            if (SummaryFormat == null) SummaryFormat = "{title}";
            if (UidFormat == null) UidFormat = "{id}";
            if (DescriptionFormat == null) DescriptionFormat = "{description}";

            var calendar = new Calendar();
            calendar.Events.Add(new CalendarEvent {
                Start = new CalDateTime(this.startsAt ??= DateTime.Now.ToUniversalTime()),
                End = new CalDateTime(this.endsAt ??= DateTime.Now.ToUniversalTime().AddMinutes(30)),
                Summary = ProcessSessionTokens(SummaryFormat, this),
                Description = ProcessSessionTokens(DescriptionFormat, this),
                Url = this.url == null ? null : new Uri(this.url),
                Uid = ProcessSessionTokens(UidFormat, this)
            });

            calendar.Events[0].Start.HasTime = true;
            calendar.Events[0].End.HasTime = true;

            var serializer = new CalendarSerializer(calendar);

            return serializer.SerializeToString();

        }

        private static string ProcessSessionTokens(string value, Session session) {
            
            if (value == null) value = string.Empty;
            value = value.Replace("{title}", session.title ??= string.Empty);
            value = value.Replace("{description}", session.description ??= string.Empty );
            value = value.Replace("{id}", session.id ??= string.Empty);
            value = value.Replace("{url}", session.url ??= string.Empty);
            value = value.Replace("{speakers}", string.Join(", ", session.speakers.Select(speaker => speaker.name)));

            return value;
        }


    }

    public class Speaker {
        public string id { get; set; }
        public string name { get; set; }
    }


    public class PersonConverter : JsonCreationConverter<Session>
    {
        protected override Session Create(Type objectType, JObject jObject)
        {
            return new Session();
        }

        private bool FieldExists(string fieldName, JObject jObject)
        {
            return jObject[fieldName] != null;
        }
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JObject jo = new JObject();
            Type type = value.GetType();
            jo.Add("type", type.Name);

            foreach (PropertyInfo prop in type.GetProperties())
            {
                if (prop.CanRead)
                {
                    object propVal = prop.GetValue(value, null);
                    if (propVal != null)
                    {
                        jo.Add(prop.Name, JToken.FromObject(propVal, serializer));
                    }
                }
            }
            jo.WriteTo(writer);
        }
    }

    public abstract class JsonCreationConverter<T> : JsonConverter
    {
        /// <summary>
        /// Create an instance of objectType, based properties in the JSON object
        /// </summary>
        /// <param name="objectType">type of object expected</param>
        /// <param name="jObject">
        /// contents of JSON object that will be deserialized
        /// </param>
        /// <returns></returns>
        protected abstract T Create(Type objectType, JObject jObject);

        public override bool CanConvert(Type objectType)
        {
            return typeof(T).IsAssignableFrom(objectType);
        }

        public override bool CanWrite
        {
            get { return false; }
        }

        public override object ReadJson(JsonReader reader, 
                                        Type objectType, 
                                        object existingValue, 
                                        JsonSerializer serializer)
        {
            // Load JObject from stream
            JObject jObject = JObject.Load(reader);

            // Create target object based on JObject
            T target = Create(objectType, jObject);

            // Populate the object properties
            serializer.Populate(jObject.CreateReader(), target);

            return target;
        }
    }
}