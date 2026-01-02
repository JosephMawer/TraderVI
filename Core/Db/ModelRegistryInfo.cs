using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db
{
    public sealed class ModelRegistryInfo
    {
        public Guid ModelId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string TaskType { get; init; } = string.Empty;
        public string ModelKind { get; init; } = string.Empty;
        public string? Family { get; init; }
        public string TimeFrame { get; init; } = string.Empty;
        public int LookbackBars { get; init; }
        public int HorizonBars { get; init; }
        public string InputSchema { get; init; } = string.Empty;
        public string? FeatureSet { get; init; }
        public string ZipPath { get; init; } = string.Empty;
        public double ThresholdBuy { get; init; }
        public double ThresholdSell { get; init; }
        public bool IsEnabled { get; init; }
        public DateTime CreatedUtc { get; init; }
        public DateTime? TrainedFromUtc { get; init; }
        public DateTime? TrainedToUtc { get; init; }
        public string? Notes { get; init; }
    }

    public class ModelRegistryRepository : SQLBase
    {
        public ModelRegistryRepository() : base("[dbo].[ModelRegistry]",
            "[ModelId],[Name],[TaskType],[ModelKind],[Family],[TimeFrame],[LookbackBars],[HorizonBars],[InputSchema],[FeatureSet],[ZipPath],[ThresholdBuy],[ThresholdSell],[IsEnabled],[TrainedFromUtc],[TrainedToUtc],[CreatedUtc],[Notes]")
        { }

        public async Task<List<ModelRegistryInfo>> GetEnabledModels()
        {
            string query = $"SELECT {Fields} FROM {DbName} WHERE [IsEnabled] = @enabled ORDER BY [CreatedUtc] DESC";

            return await ExecuteReaderAsync(query,
                [
                    new SqlParameter("@enabled", SqlDbType.Bit) { Value = true }
                ],
                reader => new ModelRegistryInfo
                {
                    ModelId = reader.GetGuid(0),
                    Name = reader.GetString(1),
                    TaskType = reader.GetString(2),
                    ModelKind = reader.GetString(3),
                    Family = reader.IsDBNull(4) ? null : reader.GetString(4),
                    TimeFrame = reader.GetString(5),
                    LookbackBars = reader.GetInt32(6),
                    HorizonBars = reader.GetInt32(7),
                    InputSchema = reader.GetString(8),
                    FeatureSet = reader.IsDBNull(9) ? null : reader.GetString(9),
                    ZipPath = reader.GetString(10),
                    ThresholdBuy = reader.IsDBNull(11) ? 0.60 : reader.GetDouble(11),
                    ThresholdSell = reader.IsDBNull(12) ? 0.40 : reader.GetDouble(12),
                    IsEnabled = reader.GetBoolean(13),
                    TrainedFromUtc = reader.IsDBNull(14) ? null : reader.GetDateTime(14),
                    TrainedToUtc = reader.IsDBNull(15) ? null : reader.GetDateTime(15),
                    CreatedUtc = reader.GetDateTime(16),
                    Notes = reader.IsDBNull(17) ? null : reader.GetString(17)
                });
        }
    }
}