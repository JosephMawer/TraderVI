using System;
using System.Globalization;

namespace Core.TMX.Models.Dto
{
    /// <summary>
    /// TMX GraphQL DTO for getTimeSeriesData response points.
    /// Property names match TMX API exactly (camelCase).
    /// </summary>
    public class TmxTimeSeriesPointDto
    {
        public string dateTime { get; set; } = "";  // TMX format: "2025-10-24 3:55:00 PM"
        public decimal open { get; set; }
        public decimal high { get; set; }
        public decimal low { get; set; }
        public decimal close { get; set; }
        public long volume { get; set; }

        /// <summary>
        /// Attempts to parse the TMX dateTime string (local Eastern Time).
        /// For mapping use TmxMapper.ToOhlcvBar instead.
        /// </summary>
        public DateTimeOffset ParsedDateTime =>
            DateTimeOffset.TryParse(dateTime, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dto)
            ? dto : DateTimeOffset.MinValue;
    }
}


