//using System;
//using System.Collections.Generic;

//namespace Core.TMX.Models
//{
//    // ---------- Models ----------
//    public class TimeSeriesResponse
//    {
//        public List<TimeSeriesPointItem> getTimeSeriesData { get; set; } = new();
//    }

//    public class TimeSeriesPointItem
//    {
//        public string dateTime { get; set; } = "";  // TMX sends strings like "2025-10-24 3:55:00 PM"
//        public decimal open { get; set; }
//        public decimal high { get; set; }
//        public decimal low { get; set; }
//        public decimal close { get; set; }
//        public long volume { get; set; }

//        public DateTimeOffset ParsedDateTime =>
//            DateTimeOffset.TryParse(dateTime, System.Globalization.CultureInfo.InvariantCulture,
//                                    System.Globalization.DateTimeStyles.AssumeLocal, out var dto)
//            ? dto : DateTimeOffset.MinValue;
//        //public long dateTime { get; set; }
//        //public decimal? open { get; set; }
//        //public decimal? high { get; set; }
//        //public decimal? low { get; set; }
//        //public decimal? close { get; set; }
//        //public long? volume { get; set; }

//        //public DateTime DateTimeUtc =>
//        //    DateTimeOffset.FromUnixTimeSeconds(dateTime).UtcDateTime;

//        //public override string ToString()
//        //{
//        //    var dt = DateTimeOffset.FromUnixTimeSeconds(dateTime).DateTime;
//        //    return $"{dt:yyyy-MM-dd HH:mm}  O:{open}  H:{high}  L:{low}  C:{close}  V:{volume:N0}";
//        //}
//    }
//}
