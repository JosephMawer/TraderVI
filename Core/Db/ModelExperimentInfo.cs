using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace Core.Db;

public class ModelExperimentRepository : SQLBase
{
    public ModelExperimentRepository() : base("[dbo].[ModelExperiment]",
        "[ExperimentId],[TaskType],[ExperimentName],[LabelDefinition],[FeatureSet],[FeatureCount]," +
        "[TrainWindows],[TestWindows],[AUC],[Accuracy],[F1AtDefault],[F1AtOptimal],[OptimalThreshold]," +
        "[PrecisionAtOpt],[RecallAtOpt],[RMSE],[MAE],[RSquared],[Spearman],[Hypothesis],[Outcome]," +
        "[Decision],[CreatedUtc],[Notes]")
    { }

    public async Task<Guid> InsertExperiment(
        string taskType,
        string experimentName,
        string? labelDefinition = null,
        string? featureSet = null,
        int? featureCount = null,
        int? trainWindows = null,
        int? testWindows = null,
        double? auc = null,
        double? accuracy = null,
        double? f1AtDefault = null,
        double? f1AtOptimal = null,
        double? optimalThreshold = null,
        double? precisionAtOpt = null,
        double? recallAtOpt = null,
        double? rmse = null,
        double? mae = null,
        double? rSquared = null,
        double? spearman = null,
        string? hypothesis = null,
        string? outcome = null,
        string? decision = null,
        string? notes = null)
    {
        var experimentId = Guid.NewGuid();

        var query = $@"
INSERT INTO {DbName}
([ExperimentId],[TaskType],[ExperimentName],[LabelDefinition],[FeatureSet],[FeatureCount],
 [TrainWindows],[TestWindows],[AUC],[Accuracy],[F1AtDefault],[F1AtOptimal],[OptimalThreshold],
 [PrecisionAtOpt],[RecallAtOpt],[RMSE],[MAE],[RSquared],[Spearman],[Hypothesis],[Outcome],
 [Decision],[Notes])
VALUES
(@ExperimentId,@TaskType,@ExperimentName,@LabelDefinition,@FeatureSet,@FeatureCount,
 @TrainWindows,@TestWindows,@AUC,@Accuracy,@F1AtDefault,@F1AtOptimal,@OptimalThreshold,
 @PrecisionAtOpt,@RecallAtOpt,@RMSE,@MAE,@RSquared,@Spearman,@Hypothesis,@Outcome,
 @Decision,@Notes);";

        await Insert(query,
        [
            new SqlParameter("@ExperimentId", SqlDbType.UniqueIdentifier) { Value = experimentId },
            new SqlParameter("@TaskType", SqlDbType.NVarChar, 64) { Value = taskType },
            new SqlParameter("@ExperimentName", SqlDbType.NVarChar, 128) { Value = experimentName },
            new SqlParameter("@LabelDefinition", SqlDbType.NVarChar, 256) { Value = (object?)labelDefinition ?? DBNull.Value },
            new SqlParameter("@FeatureSet", SqlDbType.NVarChar, 64) { Value = (object?)featureSet ?? DBNull.Value },
            new SqlParameter("@FeatureCount", SqlDbType.Int) { Value = (object?)featureCount ?? DBNull.Value },
            new SqlParameter("@TrainWindows", SqlDbType.Int) { Value = (object?)trainWindows ?? DBNull.Value },
            new SqlParameter("@TestWindows", SqlDbType.Int) { Value = (object?)testWindows ?? DBNull.Value },
            new SqlParameter("@AUC", SqlDbType.Float) { Value = (object?)auc ?? DBNull.Value },
            new SqlParameter("@Accuracy", SqlDbType.Float) { Value = (object?)accuracy ?? DBNull.Value },
            new SqlParameter("@F1AtDefault", SqlDbType.Float) { Value = (object?)f1AtDefault ?? DBNull.Value },
            new SqlParameter("@F1AtOptimal", SqlDbType.Float) { Value = (object?)f1AtOptimal ?? DBNull.Value },
            new SqlParameter("@OptimalThreshold", SqlDbType.Float) { Value = (object?)optimalThreshold ?? DBNull.Value },
            new SqlParameter("@PrecisionAtOpt", SqlDbType.Float) { Value = (object?)precisionAtOpt ?? DBNull.Value },
            new SqlParameter("@RecallAtOpt", SqlDbType.Float) { Value = (object?)recallAtOpt ?? DBNull.Value },
            new SqlParameter("@RMSE", SqlDbType.Float) { Value = (object?)rmse ?? DBNull.Value },
            new SqlParameter("@MAE", SqlDbType.Float) { Value = (object?)mae ?? DBNull.Value },
            new SqlParameter("@RSquared", SqlDbType.Float) { Value = (object?)rSquared ?? DBNull.Value },
            new SqlParameter("@Spearman", SqlDbType.Float) { Value = (object?)spearman ?? DBNull.Value },
            new SqlParameter("@Hypothesis", SqlDbType.NVarChar, 512) { Value = (object?)hypothesis ?? DBNull.Value },
            new SqlParameter("@Outcome", SqlDbType.NVarChar, 512) { Value = (object?)outcome ?? DBNull.Value },
            new SqlParameter("@Decision", SqlDbType.NVarChar, 64) { Value = (object?)decision ?? DBNull.Value },
            new SqlParameter("@Notes", SqlDbType.NVarChar, -1) { Value = (object?)notes ?? DBNull.Value }
        ]);

        return experimentId;
    }

    public async Task<List<ModelExperimentInfo>> GetExperimentsByTaskType(string taskType)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [TaskType] = @TaskType ORDER BY [CreatedUtc] DESC";

        return await ExecuteReaderAsync(query,
            [new SqlParameter("@TaskType", SqlDbType.NVarChar, 64) { Value = taskType }],
            MapExperiment);
    }

    public async Task<List<ModelExperimentInfo>> GetRecentExperiments(int count = 50)
    {
        var query = $"SELECT TOP {count} {Fields} FROM {DbName} ORDER BY [CreatedUtc] DESC";
        return await ExecuteReaderAsync(query, MapExperiment);
    }

    private static ModelExperimentInfo MapExperiment(SqlDataReader reader) => new()
    {
        ExperimentId = reader.GetGuid(0),
        TaskType = reader.GetString(1),
        ExperimentName = reader.GetString(2),
        LabelDefinition = reader.IsDBNull(3) ? null : reader.GetString(3),
        FeatureSet = reader.IsDBNull(4) ? null : reader.GetString(4),
        FeatureCount = reader.IsDBNull(5) ? null : reader.GetInt32(5),
        TrainWindows = reader.IsDBNull(6) ? null : reader.GetInt32(6),
        TestWindows = reader.IsDBNull(7) ? null : reader.GetInt32(7),
        AUC = reader.IsDBNull(8) ? null : reader.GetDouble(8),
        Accuracy = reader.IsDBNull(9) ? null : reader.GetDouble(9),
        F1AtDefault = reader.IsDBNull(10) ? null : reader.GetDouble(10),
        F1AtOptimal = reader.IsDBNull(11) ? null : reader.GetDouble(11),
        OptimalThreshold = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        PrecisionAtOpt = reader.IsDBNull(13) ? null : reader.GetDouble(13),
        RecallAtOpt = reader.IsDBNull(14) ? null : reader.GetDouble(14),
        RMSE = reader.IsDBNull(15) ? null : reader.GetDouble(15),
        MAE = reader.IsDBNull(16) ? null : reader.GetDouble(16),
        RSquared = reader.IsDBNull(17) ? null : reader.GetDouble(17),
        Spearman = reader.IsDBNull(18) ? null : reader.GetDouble(18),
        Hypothesis = reader.IsDBNull(19) ? null : reader.GetString(19),
        Outcome = reader.IsDBNull(20) ? null : reader.GetString(20),
        Decision = reader.IsDBNull(21) ? null : reader.GetString(21),
        CreatedUtc = reader.GetDateTime(22),
        Notes = reader.IsDBNull(23) ? null : reader.GetString(23)
    };
}

public class TradeLogRepository : SQLBase
{
    public TradeLogRepository() : base("[dbo].[TradeLog]",
        "[TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission]," +
        "[NetAmount],[PositionId],[Reason],[RealizedPnL],[RealizedPnLPct],[HoldingDays]," +
        "[EntryComposite],[ExitComposite],[StrategyVersionId],[CreatedUtc],[Notes]")
    { }

    public async Task<Guid> InsertTrade(
        string symbol,
        string tradeType,
        DateTime tradeDate,
        int shares,
        decimal price,
        decimal amount,
        decimal? commission = null,
        decimal netAmount = 0,
        Guid? positionId = null,
        string? reason = null,
        decimal? realizedPnL = null,
        double? realizedPnLPct = null,
        int? holdingDays = null,
        double? entryComposite = null,
        double? exitComposite = null,
        Guid? strategyVersionId = null,
        string? notes = null)
    {
        var tradeId = Guid.NewGuid();

        var query = $@"
INSERT INTO {DbName}
([TradeId],[Symbol],[TradeType],[TradeDate],[Shares],[Price],[Amount],[Commission],
 [NetAmount],[PositionId],[Reason],[RealizedPnL],[RealizedPnLPct],[HoldingDays],
 [EntryComposite],[ExitComposite],[StrategyVersionId],[Notes])
VALUES
(@TradeId,@Symbol,@TradeType,@TradeDate,@Shares,@Price,@Amount,@Commission,
 @NetAmount,@PositionId,@Reason,@RealizedPnL,@RealizedPnLPct,@HoldingDays,
 @EntryComposite,@ExitComposite,@StrategyVersionId,@Notes);";

        await Insert(query,
        [
            new SqlParameter("@TradeId", SqlDbType.UniqueIdentifier) { Value = tradeId },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol },
            new SqlParameter("@TradeType", SqlDbType.NVarChar, 8) { Value = tradeType },
            new SqlParameter("@TradeDate", SqlDbType.DateTime2) { Value = tradeDate },
            new SqlParameter("@Shares", SqlDbType.Int) { Value = shares },
            new SqlParameter("@Price", SqlDbType.Decimal) { Value = price },
            new SqlParameter("@Amount", SqlDbType.Decimal) { Value = amount },
            new SqlParameter("@Commission", SqlDbType.Decimal) { Value = (object?)commission ?? DBNull.Value },
            new SqlParameter("@NetAmount", SqlDbType.Decimal) { Value = netAmount },
            new SqlParameter("@PositionId", SqlDbType.UniqueIdentifier) { Value = (object?)positionId ?? DBNull.Value },
            new SqlParameter("@Reason", SqlDbType.NVarChar, 64) { Value = (object?)reason ?? DBNull.Value },
            new SqlParameter("@RealizedPnL", SqlDbType.Decimal) { Value = (object?)realizedPnL ?? DBNull.Value },
            new SqlParameter("@RealizedPnLPct", SqlDbType.Float) { Value = (object?)realizedPnLPct ?? DBNull.Value },
            new SqlParameter("@HoldingDays", SqlDbType.Int) { Value = (object?)holdingDays ?? DBNull.Value },
            new SqlParameter("@EntryComposite", SqlDbType.Float) { Value = (object?)entryComposite ?? DBNull.Value },
            new SqlParameter("@ExitComposite", SqlDbType.Float) { Value = (object?)exitComposite ?? DBNull.Value },
            new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier) { Value = (object?)strategyVersionId ?? DBNull.Value },
            new SqlParameter("@Notes", SqlDbType.NVarChar, 512) { Value = (object?)notes ?? DBNull.Value }
        ]);

        return tradeId;
    }

    public async Task<List<TradeLogInfo>> GetTradesBySymbol(string symbol)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [Symbol] = @Symbol ORDER BY [TradeDate] DESC";
        return await ExecuteReaderAsync(query,
            [new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol }],
            MapTrade);
    }

    public async Task<List<TradeLogInfo>> GetRecentTrades(int count = 50)
    {
        var query = $"SELECT TOP {count} {Fields} FROM {DbName} ORDER BY [TradeDate] DESC";
        return await ExecuteReaderAsync(query, MapTrade);
    }

    public async Task<List<TradeLogInfo>> GetTradesByDateRange(DateTime fromDate, DateTime toDate)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [TradeDate] >= @FromDate AND [TradeDate] <= @ToDate ORDER BY [TradeDate] DESC";
        return await ExecuteReaderAsync(query,
        [
            new SqlParameter("@FromDate", SqlDbType.DateTime2) { Value = fromDate },
            new SqlParameter("@ToDate", SqlDbType.DateTime2) { Value = toDate }
        ], MapTrade);
    }

    public async Task<(decimal TotalPnL, int WinCount, int LossCount)> GetPnLSummary()
    {
        var query = @"
SELECT 
    ISNULL(SUM([RealizedPnL]), 0) AS TotalPnL,
    SUM(CASE WHEN [RealizedPnL] > 0 THEN 1 ELSE 0 END) AS WinCount,
    SUM(CASE WHEN [RealizedPnL] < 0 THEN 1 ELSE 0 END) AS LossCount
FROM [dbo].[TradeLog]
WHERE [TradeType] = 'SELL' AND [RealizedPnL] IS NOT NULL;";

        using var con = new SqlConnection(ConnectionString);
        await con.OpenAsync();
        using var cmd = new SqlCommand(query, con);
        using var reader = await cmd.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            return (
                reader.GetDecimal(0),
                reader.GetInt32(1),
                reader.GetInt32(2)
            );
        }

        return (0, 0, 0);
    }

    private static TradeLogInfo MapTrade(SqlDataReader reader) => new()
    {
        TradeId = reader.GetGuid(0),
        Symbol = reader.GetString(1),
        TradeType = reader.GetString(2),
        TradeDate = reader.GetDateTime(3),
        Shares = reader.GetInt32(4),
        Price = reader.GetDecimal(5),
        Amount = reader.GetDecimal(6),
        Commission = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        NetAmount = reader.GetDecimal(8),
        PositionId = reader.IsDBNull(9) ? null : reader.GetGuid(9),
        Reason = reader.IsDBNull(10) ? null : reader.GetString(10),
        RealizedPnL = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
        RealizedPnLPct = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        HoldingDays = reader.IsDBNull(13) ? null : reader.GetInt32(13),
        EntryComposite = reader.IsDBNull(14) ? null : reader.GetDouble(14),
        ExitComposite = reader.IsDBNull(15) ? null : reader.GetDouble(15),
        StrategyVersionId = reader.IsDBNull(16) ? null : reader.GetGuid(16),
        CreatedUtc = reader.GetDateTime(17),
        Notes = reader.IsDBNull(18) ? null : reader.GetString(18)
    };
}

public class ActivePositionRepository : SQLBase
{
    public ActivePositionRepository() : base("[dbo].[ActivePosition]",
        "[PositionId],[Symbol],[EntryDate],[EntryPrice],[Shares],[CostBasis],[CurrentPrice]," +
        "[CurrentValue],[UnrealizedPnL],[UnrealizedPnLPct],[HighWaterMark],[DrawdownFromHigh]," +
        "[DaysHeld],[OriginalPickId],[StopLossPrice],[WarningPrice],[IsActive],[LastUpdatedUtc],[Notes]")
    { }

    public async Task<Guid> InsertPosition(
        string symbol,
        DateTime entryDate,
        decimal entryPrice,
        int shares,
        decimal costBasis,
        Guid? originalPickId = null,
        decimal? stopLossPrice = null,
        decimal? warningPrice = null,
        string? notes = null)
    {
        var positionId = Guid.NewGuid();

        var query = $@"
INSERT INTO {DbName}
([PositionId],[Symbol],[EntryDate],[EntryPrice],[Shares],[CostBasis],[OriginalPickId],
 [StopLossPrice],[WarningPrice],[HighWaterMark],[Notes])
VALUES
(@PositionId,@Symbol,@EntryDate,@EntryPrice,@Shares,@CostBasis,@OriginalPickId,
 @StopLossPrice,@WarningPrice,@HighWaterMark,@Notes);";

        await Insert(query,
        [
            new SqlParameter("@PositionId", SqlDbType.UniqueIdentifier) { Value = positionId },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol },
            new SqlParameter("@EntryDate", SqlDbType.Date) { Value = entryDate },
            new SqlParameter("@EntryPrice", SqlDbType.Decimal) { Value = entryPrice },
            new SqlParameter("@Shares", SqlDbType.Int) { Value = shares },
            new SqlParameter("@CostBasis", SqlDbType.Decimal) { Value = costBasis },
            new SqlParameter("@OriginalPickId", SqlDbType.UniqueIdentifier) { Value = (object?)originalPickId ?? DBNull.Value },
            new SqlParameter("@StopLossPrice", SqlDbType.Decimal) { Value = (object?)stopLossPrice ?? DBNull.Value },
            new SqlParameter("@WarningPrice", SqlDbType.Decimal) { Value = (object?)warningPrice ?? DBNull.Value },
            new SqlParameter("@HighWaterMark", SqlDbType.Decimal) { Value = entryPrice }, // Start with entry price
            new SqlParameter("@Notes", SqlDbType.NVarChar, 512) { Value = (object?)notes ?? DBNull.Value }
        ]);

        return positionId;
    }

    public async Task<List<ActivePositionInfo>> GetActivePositions()
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [IsActive] = 1 ORDER BY [EntryDate]";
        return await ExecuteReaderAsync(query, MapPosition);
    }

    public async Task<ActivePositionInfo?> GetPositionBySymbol(string symbol)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [Symbol] = @Symbol AND [IsActive] = 1";
        var results = await ExecuteReaderAsync(query,
            [new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol }],
            MapPosition);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task UpdatePositionPrices(
        Guid positionId,
        decimal currentPrice,
        decimal currentValue,
        decimal unrealizedPnL,
        double unrealizedPnLPct,
        decimal highWaterMark,
        double drawdownFromHigh,
        int daysHeld)
    {
        var query = $@"
UPDATE {DbName}
SET [CurrentPrice] = @CurrentPrice,
    [CurrentValue] = @CurrentValue,
    [UnrealizedPnL] = @UnrealizedPnL,
    [UnrealizedPnLPct] = @UnrealizedPnLPct,
    [HighWaterMark] = @HighWaterMark,
    [DrawdownFromHigh] = @DrawdownFromHigh,
    [DaysHeld] = @DaysHeld,
    [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId;";

        await Update(query,
        [
            new SqlParameter("@PositionId", SqlDbType.UniqueIdentifier) { Value = positionId },
            new SqlParameter("@CurrentPrice", SqlDbType.Decimal) { Value = currentPrice },
            new SqlParameter("@CurrentValue", SqlDbType.Decimal) { Value = currentValue },
            new SqlParameter("@UnrealizedPnL", SqlDbType.Decimal) { Value = unrealizedPnL },
            new SqlParameter("@UnrealizedPnLPct", SqlDbType.Float) { Value = unrealizedPnLPct },
            new SqlParameter("@HighWaterMark", SqlDbType.Decimal) { Value = highWaterMark },
            new SqlParameter("@DrawdownFromHigh", SqlDbType.Float) { Value = drawdownFromHigh },
            new SqlParameter("@DaysHeld", SqlDbType.Int) { Value = daysHeld }
        ]);
    }

    public async Task ClosePosition(Guid positionId)
    {
        var query = $@"
UPDATE {DbName}
SET [IsActive] = 0, [LastUpdatedUtc] = SYSUTCDATETIME()
WHERE [PositionId] = @PositionId;";

        await Update(query,
            [new SqlParameter("@PositionId", SqlDbType.UniqueIdentifier) { Value = positionId }]);
    }

    private static ActivePositionInfo MapPosition(SqlDataReader reader) => new()
    {
        PositionId = reader.GetGuid(0),
        Symbol = reader.GetString(1),
        EntryDate = reader.GetDateTime(2),
        EntryPrice = reader.GetDecimal(3),
        Shares = reader.GetInt32(4),
        CostBasis = reader.GetDecimal(5),
        CurrentPrice = reader.IsDBNull(6) ? null : reader.GetDecimal(6),
        CurrentValue = reader.IsDBNull(7) ? null : reader.GetDecimal(7),
        UnrealizedPnL = reader.IsDBNull(8) ? null : reader.GetDecimal(8),
        UnrealizedPnLPct = reader.IsDBNull(9) ? null : reader.GetDouble(9),
        HighWaterMark = reader.IsDBNull(10) ? null : reader.GetDecimal(10),
        DrawdownFromHigh = reader.IsDBNull(11) ? null : reader.GetDouble(11),
        DaysHeld = reader.IsDBNull(12) ? null : reader.GetInt32(12),
        OriginalPickId = reader.IsDBNull(13) ? null : reader.GetGuid(13),
        StopLossPrice = reader.IsDBNull(14) ? null : reader.GetDecimal(14),
        WarningPrice = reader.IsDBNull(15) ? null : reader.GetDecimal(15),
        IsActive = reader.GetBoolean(16),
        LastUpdatedUtc = reader.GetDateTime(17),
        Notes = reader.IsDBNull(18) ? null : reader.GetString(18)
    };
}

public class DailyPickRepository : SQLBase
{
    public DailyPickRepository() : base("[dbo].[DailyPick]",
        "[PickId],[PickDate],[Symbol],[Rank],[Direction],[CompositeScore],[BreakoutProb]," +
        "[DirectionProb],[VolExpansionProb],[RelStrengthProb],[ExpectedReturn],[SuggestedSize]," +
        "[AllocationPercent],[StrategyVersionId],[CreatedUtc],[Notes]")
    { }

    public async Task<Guid> InsertPick(
        DateTime pickDate,
        string symbol,
        int rank,
        string direction,
        double compositeScore,
        double? breakoutProb = null,
        double? directionProb = null,
        double? volExpansionProb = null,
        double? relStrengthProb = null,
        double? expectedReturn = null,
        decimal? suggestedSize = null,
        double? allocationPercent = null,
        Guid? strategyVersionId = null,
        string? notes = null)
    {
        var pickId = Guid.NewGuid();

        var query = $@"
INSERT INTO {DbName}
([PickId],[PickDate],[Symbol],[Rank],[Direction],[CompositeScore],[BreakoutProb],
 [DirectionProb],[VolExpansionProb],[RelStrengthProb],[ExpectedReturn],[SuggestedSize],
 [AllocationPercent],[StrategyVersionId],[Notes])
VALUES
(@PickId,@PickDate,@Symbol,@Rank,@Direction,@CompositeScore,@BreakoutProb,
 @DirectionProb,@VolExpansionProb,@RelStrengthProb,@ExpectedReturn,@SuggestedSize,
 @AllocationPercent,@StrategyVersionId,@Notes);";

        await Insert(query,
        [
            new SqlParameter("@PickId", SqlDbType.UniqueIdentifier) { Value = pickId },
            new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol },
            new SqlParameter("@Rank", SqlDbType.Int) { Value = rank },
            new SqlParameter("@Direction", SqlDbType.NVarChar, 8) { Value = direction },
            new SqlParameter("@CompositeScore", SqlDbType.Float) { Value = compositeScore },
            new SqlParameter("@BreakoutProb", SqlDbType.Float) { Value = (object?)breakoutProb ?? DBNull.Value },
            new SqlParameter("@DirectionProb", SqlDbType.Float) { Value = (object?)directionProb ?? DBNull.Value },
            new SqlParameter("@VolExpansionProb", SqlDbType.Float) { Value = (object?)volExpansionProb ?? DBNull.Value },
            new SqlParameter("@RelStrengthProb", SqlDbType.Float) { Value = (object?)relStrengthProb ?? DBNull.Value },
            new SqlParameter("@ExpectedReturn", SqlDbType.Float) { Value = (object?)expectedReturn ?? DBNull.Value },
            new SqlParameter("@SuggestedSize", SqlDbType.Decimal) { Value = (object?)suggestedSize ?? DBNull.Value },
            new SqlParameter("@AllocationPercent", SqlDbType.Float) { Value = (object?)allocationPercent ?? DBNull.Value },
            new SqlParameter("@StrategyVersionId", SqlDbType.UniqueIdentifier) { Value = (object?)strategyVersionId ?? DBNull.Value },
            new SqlParameter("@Notes", SqlDbType.NVarChar, 512) { Value = (object?)notes ?? DBNull.Value }
        ]);

        return pickId;
    }

    public async Task<List<DailyPickInfo>> GetPicksByDate(DateTime pickDate)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [PickDate] = @PickDate ORDER BY [Rank]";
        return await ExecuteReaderAsync(query,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate }],
            MapPick);
    }

    public async Task<List<DailyPickInfo>> GetTopPicksByDate(DateTime pickDate, int topN = 10)
    {
        var query = $"SELECT TOP {topN} {Fields} FROM {DbName} WHERE [PickDate] = @PickDate ORDER BY [Rank]";
        return await ExecuteReaderAsync(query,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate }],
            MapPick);
    }

    public async Task<DailyPickInfo?> GetPickByDateAndSymbol(DateTime pickDate, string symbol)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [PickDate] = @PickDate AND [Symbol] = @Symbol";
        var results = await ExecuteReaderAsync(query,
        [
            new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate },
            new SqlParameter("@Symbol", SqlDbType.NVarChar, 16) { Value = symbol }
        ], MapPick);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task DeletePicksByDate(DateTime pickDate)
    {
        var query = $"DELETE FROM {DbName} WHERE [PickDate] = @PickDate";
        await Delete(query,
            [new SqlParameter("@PickDate", SqlDbType.Date) { Value = pickDate }]);
    }

    private static DailyPickInfo MapPick(SqlDataReader reader) => new()
    {
        PickId = reader.GetGuid(0),
        PickDate = reader.GetDateTime(1),
        Symbol = reader.GetString(2),
        Rank = reader.GetInt32(3),
        Direction = reader.GetString(4),
        CompositeScore = reader.GetDouble(5),
        BreakoutProb = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        DirectionProb = reader.IsDBNull(7) ? null : reader.GetDouble(7),
        VolExpansionProb = reader.IsDBNull(8) ? null : reader.GetDouble(8),
        RelStrengthProb = reader.IsDBNull(9) ? null : reader.GetDouble(9),
        ExpectedReturn = reader.IsDBNull(10) ? null : reader.GetDouble(10),
        SuggestedSize = reader.IsDBNull(11) ? null : reader.GetDecimal(11),
        AllocationPercent = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        StrategyVersionId = reader.IsDBNull(13) ? null : reader.GetGuid(13),
        CreatedUtc = reader.GetDateTime(14),
        Notes = reader.IsDBNull(15) ? null : reader.GetString(15)
    };
}

public class StrategyVersionRepository : SQLBase
{
    public StrategyVersionRepository() : base("[dbo].[StrategyVersion]",
        "[VersionId],[VersionName],[Description],[IsActive],[MinCompositeScore],[MinDirectionProb]," +
        "[RegressionVeto],[StopLossPercent],[WarningPercent],[MaxPositions],[CreatedUtc],[Notes]")
    { }

    public async Task<StrategyVersionInfo?> GetActiveVersion()
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [IsActive] = 1";
        var results = await ExecuteReaderAsync(query, MapVersion);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<StrategyVersionInfo?> GetVersionByName(string versionName)
    {
        var query = $"SELECT {Fields} FROM {DbName} WHERE [VersionName] = @VersionName";
        var results = await ExecuteReaderAsync(query,
            [new SqlParameter("@VersionName", SqlDbType.NVarChar, 32) { Value = versionName }],
            MapVersion);
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<List<StrategyVersionInfo>> GetAllVersions()
    {
        var query = $"SELECT {Fields} FROM {DbName} ORDER BY [CreatedUtc] DESC";
        return await ExecuteReaderAsync(query, MapVersion);
    }

    public async Task SetActiveVersion(string versionName)
    {
        // Deactivate all, then activate the specified one
        var query = @"
UPDATE [dbo].[StrategyVersion] SET [IsActive] = 0;
UPDATE [dbo].[StrategyVersion] SET [IsActive] = 1 WHERE [VersionName] = @VersionName;";

        await Insert(query,
            [new SqlParameter("@VersionName", SqlDbType.NVarChar, 32) { Value = versionName }]);
    }

    public async Task<Guid> InsertVersion(
        string versionName,
        string? description = null,
        bool isActive = false,
        double? minCompositeScore = 0.35,
        double? minDirectionProb = 0.25,
        double? regressionVeto = -0.03,
        double? stopLossPercent = -0.10,
        double? warningPercent = -0.05,
        int? maxPositions = 1,
        string? notes = null)
    {
        var versionId = Guid.NewGuid();

        var query = $@"
INSERT INTO {DbName}
([VersionId],[VersionName],[Description],[IsActive],[MinCompositeScore],[MinDirectionProb],
 [RegressionVeto],[StopLossPercent],[WarningPercent],[MaxPositions],[Notes])
VALUES
(@VersionId,@VersionName,@Description,@IsActive,@MinCompositeScore,@MinDirectionProb,
 @RegressionVeto,@StopLossPercent,@WarningPercent,@MaxPositions,@Notes);";

        await Insert(query,
        [
            new SqlParameter("@VersionId", SqlDbType.UniqueIdentifier) { Value = versionId },
            new SqlParameter("@VersionName", SqlDbType.NVarChar, 32) { Value = versionName },
            new SqlParameter("@Description", SqlDbType.NVarChar, 256) { Value = (object?)description ?? DBNull.Value },
            new SqlParameter("@IsActive", SqlDbType.Bit) { Value = isActive },
            new SqlParameter("@MinCompositeScore", SqlDbType.Float) { Value = (object?)minCompositeScore ?? DBNull.Value },
            new SqlParameter("@MinDirectionProb", SqlDbType.Float) { Value = (object?)minDirectionProb ?? DBNull.Value },
            new SqlParameter("@RegressionVeto", SqlDbType.Float) { Value = (object?)regressionVeto ?? DBNull.Value },
            new SqlParameter("@StopLossPercent", SqlDbType.Float) { Value = (object?)stopLossPercent ?? DBNull.Value },
            new SqlParameter("@WarningPercent", SqlDbType.Float) { Value = (object?)warningPercent ?? DBNull.Value },
            new SqlParameter("@MaxPositions", SqlDbType.Int) { Value = (object?)maxPositions ?? DBNull.Value },
            new SqlParameter("@Notes", SqlDbType.NVarChar, -1) { Value = (object?)notes ?? DBNull.Value }
        ]);

        return versionId;
    }

    private static StrategyVersionInfo MapVersion(SqlDataReader reader) => new()
    {
        VersionId = reader.GetGuid(0),
        VersionName = reader.GetString(1),
        Description = reader.IsDBNull(2) ? null : reader.GetString(2),
        IsActive = reader.GetBoolean(3),
        MinCompositeScore = reader.IsDBNull(4) ? null : reader.GetDouble(4),
        MinDirectionProb = reader.IsDBNull(5) ? null : reader.GetDouble(5),
        RegressionVeto = reader.IsDBNull(6) ? null : reader.GetDouble(6),
        StopLossPercent = reader.IsDBNull(7) ? null : reader.GetDouble(7),
        WarningPercent = reader.IsDBNull(8) ? null : reader.GetDouble(8),
        MaxPositions = reader.IsDBNull(9) ? null : reader.GetInt32(9),
        CreatedUtc = reader.GetDateTime(10),
        Notes = reader.IsDBNull(11) ? null : reader.GetString(11)
    };
}

public sealed class ModelExperimentInfo
{
    public Guid ExperimentId { get; init; }
    public string TaskType { get; init; } = string.Empty;
    public string ExperimentName { get; init; } = string.Empty;
    public string? LabelDefinition { get; init; }
    public string? FeatureSet { get; init; }
    public int? FeatureCount { get; init; }
    public int? TrainWindows { get; init; }
    public int? TestWindows { get; init; }
    public double? AUC { get; init; }
    public double? Accuracy { get; init; }
    public double? F1AtDefault { get; init; }
    public double? F1AtOptimal { get; init; }
    public double? OptimalThreshold { get; init; }
    public double? PrecisionAtOpt { get; init; }
    public double? RecallAtOpt { get; init; }
    public double? RMSE { get; init; }
    public double? MAE { get; init; }
    public double? RSquared { get; init; }
    public double? Spearman { get; init; }
    public string? Hypothesis { get; init; }
    public string? Outcome { get; init; }
    public string? Decision { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string? Notes { get; init; }
}


public sealed class StrategyVersionInfo
{
    public Guid VersionId { get; init; }
    public string VersionName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public double? MinCompositeScore { get; init; }
    public double? MinDirectionProb { get; init; }
    public double? RegressionVeto { get; init; }
    public double? StopLossPercent { get; init; }
    public double? WarningPercent { get; init; }
    public int? MaxPositions { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string? Notes { get; init; }
}

public sealed class DailyPickInfo
{
    public Guid PickId { get; init; }
    public DateTime PickDate { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public int Rank { get; init; }
    public string Direction { get; init; } = string.Empty;
    public double CompositeScore { get; init; }
    public double? BreakoutProb { get; init; }
    public double? DirectionProb { get; init; }
    public double? VolExpansionProb { get; init; }
    public double? RelStrengthProb { get; init; }
    public double? ExpectedReturn { get; init; }
    public decimal? SuggestedSize { get; init; }
    public double? AllocationPercent { get; init; }
    public Guid? StrategyVersionId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string? Notes { get; init; }
}

public sealed class ActivePositionInfo
{
    public Guid PositionId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public DateTime EntryDate { get; init; }
    public decimal EntryPrice { get; init; }
    public int Shares { get; init; }
    public decimal CostBasis { get; init; }
    public decimal? CurrentPrice { get; set; }
    public decimal? CurrentValue { get; set; }
    public decimal? UnrealizedPnL { get; set; }
    public double? UnrealizedPnLPct { get; set; }
    public decimal? HighWaterMark { get; set; }
    public double? DrawdownFromHigh { get; set; }
    public int? DaysHeld { get; set; }
    public Guid? OriginalPickId { get; init; }
    public decimal? StopLossPrice { get; init; }
    public decimal? WarningPrice { get; init; }
    public bool IsActive { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public string? Notes { get; init; }
}

public sealed class TradeLogInfo
{
    public Guid TradeId { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public string TradeType { get; init; } = string.Empty;
    public DateTime TradeDate { get; init; }
    public int Shares { get; init; }
    public decimal Price { get; init; }
    public decimal Amount { get; init; }
    public decimal? Commission { get; init; }
    public decimal NetAmount { get; init; }
    public Guid? PositionId { get; init; }
    public string? Reason { get; init; }
    public decimal? RealizedPnL { get; init; }
    public double? RealizedPnLPct { get; init; }
    public int? HoldingDays { get; init; }
    public double? EntryComposite { get; init; }
    public double? ExitComposite { get; init; }
    public Guid? StrategyVersionId { get; init; }
    public DateTime CreatedUtc { get; init; }
    public string? Notes { get; init; }
}