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

    public class SpeakerInformation {
        public SpeakerInformation() { }
        public string id { get; set; }
        public string firstName { get; set; }
        public string lastName { get; set; }
        public string fullName { get; set; }
        public string bio { get; set; }
        public string tagLine { get; set; }
        public string profilePicture { get; set; }

        public static string ProcessSpeakerTokens(string value, SpeakerInformation speaker) {
            
            if (value == null) value = string.Empty;
            value = value.Replace("{firstName}", speaker.firstName ??= string.Empty);
            value = value.Replace("{lastName}", speaker.lastName ??= string.Empty );
            value = value.Replace("{fullName}", speaker.fullName ??= string.Empty);
            value = value.Replace("{bio}", speaker.bio ??= string.Empty);
            value = value.Replace("{tagLine}", speaker.tagLine ??= string.Empty);
            value = value.Replace("{profilePicture}", speaker.profilePicture != null ? $"<img src=\"{speaker.profilePicture}\" />" : string.Empty);

            return value;
        }

    }

}