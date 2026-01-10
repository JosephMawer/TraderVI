using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db
{
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

        public async Task InsertModel(
            string name,
            string taskType,
            string modelKind,
            string? family,
            string timeFrame,
            int lookbackBars,
            int horizonBars,
            string inputSchema,
            string? featureSet,
            string zipPath,
            double thresholdBuy,
            double thresholdSell,
            bool isEnabled,
            DateTime? trainedFromUtc,
            DateTime? trainedToUtc,
            string? notes)
        {
            var query = $@"
INSERT INTO {DbName}
(
    [Name],
    [TaskType],
    [ModelKind],
    [Family],
    [TimeFrame],
    [LookbackBars],
    [HorizonBars],
    [InputSchema],
    [FeatureSet],
    [ZipPath],
    [ThresholdBuy],
    [ThresholdSell],
    [IsEnabled],
    [TrainedFromUtc],
    [TrainedToUtc],
    [Notes]
)
VALUES
(
    @Name,
    @TaskType,
    @ModelKind,
    @Family,
    @TimeFrame,
    @LookbackBars,
    @HorizonBars,
    @InputSchema,
    @FeatureSet,
    @ZipPath,
    @ThresholdBuy,
    @ThresholdSell,
    @IsEnabled,
    @TrainedFromUtc,
    @TrainedToUtc,
    @Notes
);";

            await Insert(query,
            [
                new SqlParameter("@Name", SqlDbType.NVarChar, 128) { Value = name },
                new SqlParameter("@TaskType", SqlDbType.NVarChar, 64) { Value = taskType },
                new SqlParameter("@ModelKind", SqlDbType.NVarChar, 32) { Value = modelKind },
                new SqlParameter("@Family", SqlDbType.NVarChar, 32) { Value = (object?)family ?? DBNull.Value },
                new SqlParameter("@TimeFrame", SqlDbType.NVarChar, 16) { Value = timeFrame },
                new SqlParameter("@LookbackBars", SqlDbType.Int) { Value = lookbackBars },
                new SqlParameter("@HorizonBars", SqlDbType.Int) { Value = horizonBars },
                new SqlParameter("@InputSchema", SqlDbType.NVarChar, 64) { Value = inputSchema },
                new SqlParameter("@FeatureSet", SqlDbType.NVarChar, 64) { Value = (object?)featureSet ?? DBNull.Value },
                new SqlParameter("@ZipPath", SqlDbType.NVarChar, 260) { Value = zipPath },
                new SqlParameter("@ThresholdBuy", SqlDbType.Float) { Value = thresholdBuy },
                new SqlParameter("@ThresholdSell", SqlDbType.Float) { Value = thresholdSell },
                new SqlParameter("@IsEnabled", SqlDbType.Bit) { Value = isEnabled },
                new SqlParameter("@TrainedFromUtc", SqlDbType.DateTime2) { Value = (object?)trainedFromUtc ?? DBNull.Value },
                new SqlParameter("@TrainedToUtc", SqlDbType.DateTime2) { Value = (object?)trainedToUtc ?? DBNull.Value },
                new SqlParameter("@Notes", SqlDbType.NVarChar, 4000) { Value = (object?)notes ?? DBNull.Value }
            ]);
        }
    }
}