#nullable enable
using Core.Runtime;
using Core.Trader;
using Core.Trader.Promotion;
using Dapper;
using System;
using System.Collections.Immutable;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Core.Db.TradingRecommendationStorage;

namespace Core.Db;

public sealed record TradingAccountView(bool Installed, string AccountLabel, string LedgerFingerprint,
    ImmutableArray<ReportedTradingHolding> Holdings, ReportedTradingAccount? Report, TradingStrategyAssignment? Assignment, TradingRecommendationBatch? Latest);
public sealed record TradingAccountReportRequest(Guid RequestId, string AccountLabel, string ExpectedLedgerFingerprint,
    decimal Cash, decimal AccountValue, decimal SessionOpeningValue, decimal HighestClosingValue, int PendingOrders, string ReportedBy)
{
    public string? CapitalReviewReason { get; init; }
}
public sealed record TradingReportedBuyRequest(Guid RequestId, Guid BatchId, string AccountLabel, string Symbol, string Kind,
    int Quantity, decimal Price, DateTime FillUtc, string ReportedBy, string Note);

/// <summary>Human-reported Real financial state. No simulated account is a source for this repository.</summary>
public sealed class TradingAccountRepository
{
    public async Task<TradingAccountView> ReadAsync(string accountLabel, CancellationToken token = default)
    {
        string account = Account(accountLabel); await using var c = await Open(token);
        if (!await Installed(c)) return new(false, account, "", [], null, null, null);
        using var t = c.BeginTransaction(IsolationLevel.Serializable); await Fence(c, t, account, false);
        var financial = await Financial(c, account, t);
        var view = new TradingAccountView(true, account, financial.Fingerprint, financial.Holdings, await Report(c, account, t), await Assignment(c, account, t), await Batch(c, account, t));
        t.Commit(); return view;
    }
    public async Task<ReportedTradingAccount> ReportAsync(TradingAccountReportRequest request, CancellationToken token = default)
    {
        string account = Account(request.AccountLabel);
        if (request.RequestId == Guid.Empty || request.Cash < 0m || request.AccountValue <= 0m || request.SessionOpeningValue <= 0m ||
            request.HighestClosingValue <= 0m || request.PendingOrders < 0 || string.IsNullOrWhiteSpace(request.ReportedBy))
            throw new ArgumentException("Report non-negative cash/order count and positive account values with your identity.");
        string requestHash = EngineStrategySettings.Hash(Json(request));
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, account, true); await RequireInstalled(c, t);
        var saved = await c.QuerySingleOrDefaultAsync<SavedReport>("SELECT RequestHash,ReportJson FROM dbo.TradingAccountReport WHERE ReportId=@Id", new { Id = request.RequestId }, t);
        if (saved is not null)
        {
            if (saved.RequestHash != requestHash) throw new InvalidOperationException("This account report request already has different saved content.");
            t.Commit(); return Read<ReportedTradingAccount>(saved.ReportJson);
        }
        var facts = await Financial(c, account, t);
        if (facts.Fingerprint != request.ExpectedLedgerFingerprint) throw new InvalidOperationException("Real holdings/fills changed. Reload the account before confirming its state.");
        var prior = await Report(c, account, t); var latest = await Batch(c, account, t);
        var now = DateTime.UtcNow; var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(now));
        var protection = TradingRecommendationMemory.Reconcile(latest, prior);
        decimal high = System.Math.Max(request.HighestClosingValue, protection.HighestClosingValue ?? 0m);
        bool daily = protection.TradingDate == date && protection.DailyLossHold;
        bool capital = protection.CapitalReviewRequired;
        if (request.CapitalReviewReason is not null)
        {
            if (string.IsNullOrWhiteSpace(request.CapitalReviewReason) || !capital || request.PendingOrders != 0)
                throw new InvalidOperationException("An active capital hold, resolved outstanding orders and a review reason are required.");
            capital = false; high = request.AccountValue;
        }
        var report = new ReportedTradingAccount(request.RequestId, account, "CAD", date, now, request.ReportedBy, facts.Fingerprint,
            request.Cash, request.AccountValue, request.SessionOpeningValue, high, request.PendingOrders, daily, capital, facts.Holdings)
        { CapitalReviewReason = request.CapitalReviewReason };
        report.Validate();
        await c.ExecuteAsync("""
            INSERT dbo.TradingAccountReport(ReportId,AccountLabel,TradingDate,ReportedUtc,ReportedBy,LedgerFingerprint,RequestHash,ReportJson)
            VALUES(@Id,@Account,@Date,@Now,@By,@Fingerprint,@Hash,@Json)
            """, new
        {
            Id = report.SnapshotId,
            Account = account,
            Date = date.ToDateTime(TimeOnly.MinValue),
            Now = now,
            By = request.ReportedBy,
            Fingerprint = facts.Fingerprint,
            Hash = requestHash,
            Json = Json(report)
        }, t);
        t.Commit(); return report;
    }

    public async Task<TrackedPositionOpenResult> RecordBuyAsync(TradingReportedBuyRequest request, CancellationToken token = default)
    {
        string account = Account(request.AccountLabel); string symbol = request.Symbol.Trim().ToUpperInvariant();
        if (request.RequestId == Guid.Empty || request.Quantity < 1 || request.Price <= 0m || decimal.Round(request.Price, 4) != request.Price || request.FillUtc.Kind != DateTimeKind.Utc ||
            request.FillUtc > DateTime.UtcNow || string.IsNullOrWhiteSpace(request.ReportedBy) || string.IsNullOrWhiteSpace(request.Note))
            throw new ArgumentException("Report the actual completed zero-commission purchase, its time, quantity, price and your confirmation.");
        string requestHash = EngineStrategySettings.Hash(Json(request));
        await using var c = await Open(token); using var t = c.BeginTransaction(IsolationLevel.Serializable);
        await Fence(c, t, account, true); await RequireInstalled(c, t);
        var existing = await c.QuerySingleOrDefaultAsync<SavedFill>("SELECT RequestHash,ResultJson FROM dbo.TradingReportedFill WHERE RequestId=@Id", new { Id = request.RequestId }, t);
        if (existing is not null)
        {
            if (existing.RequestHash != requestHash) throw new InvalidOperationException("This fill request was already used with different financial facts.");
            t.Commit(); return Read<TrackedPositionOpenResult>(existing.ResultJson);
        }
        string json = await c.QuerySingleOrDefaultAsync<string>("SELECT BatchJson FROM dbo.TradingRecommendationBatch WHERE BatchId=@Id AND AccountLabel=@Account", new { Id = request.BatchId, Account = account }, t)
            ?? throw new InvalidOperationException("The recommendation does not belong to this Real account.");
        var batch = Read<TradingRecommendationBatch>(json);
        var recommendation = batch.Result.Recommendations.SingleOrDefault(r => string.Equals(r.Symbol, symbol, StringComparison.OrdinalIgnoreCase) && r.Kind == request.Kind && r.Action is "Buy" or "Add")
            ?? throw new InvalidOperationException("Select the purchase recommendation being reported.");
        if (request.FillUtc < batch.EvaluatedUtc || request.FillUtc < recommendation.EvidenceUtc)
            throw new InvalidOperationException("An attributed fill cannot precede the recommendation evidence.");
        var prior = await c.QuerySingleOrDefaultAsync<PositionFact>("SELECT PositionId,Symbol,EntryDate,EntryPrice,Shares,CostBasis,IsActive FROM dbo.ActivePosition WITH(UPDLOCK,HOLDLOCK) WHERE Symbol=@Symbol AND IsActive=1", new { Symbol = symbol }, t);
        Guid position = Guid.NewGuid(); decimal amount = decimal.Round(request.Quantity * request.Price, 2, MidpointRounding.AwayFromZero);
        DateTime local = PaperTradingMonitor.ToToronto(request.FillUtc);
        if (request.Kind == "AddOn")
        {
            if (prior is null || prior.PositionId != recommendation.PositionId || await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ActivePosition WHERE PositionId=@Id AND ExecutionMode=N'Real' AND AccountLabel=@Account", new { Id = prior.PositionId, Account = account }, t) != 1)
                throw new InvalidOperationException("The actual Real holding for this add-on is no longer available.");
            position = prior.PositionId;
            await c.ExecuteAsync("""
                UPDATE dbo.ActivePosition SET Shares=Shares+@Quantity,CostBasis=CostBasis+@Amount,
                    EntryPrice=(CostBasis+@Amount)/(Shares+@Quantity),LastUpdatedUtc=SYSUTCDATETIME()
                WHERE PositionId=@Position AND IsActive=1;
                """, new { Quantity = request.Quantity, Amount = amount, Position = position }, t);
        }
        else
        {
            if (prior is not null) throw new InvalidOperationException("An active tracked holding already exists for this symbol. Reconcile it before recording another initial purchase.");
            await c.ExecuteAsync("""
                INSERT dbo.ActivePosition(PositionId,Symbol,EntryDate,EntryPrice,Shares,CostBasis,CurrentPrice,CurrentValue,UnrealizedPnL,
                    HighWaterMark,ExecutionMode,AccountLabel,IsActive,Notes)
                VALUES(@Id,@Symbol,@Date,@Price,@Quantity,@Amount,@Price,@Amount,0,@Price,N'Real',@Account,1,@Note)
                """, new
            {
                Id = position,
                Symbol = symbol,
                Date = local.Date,
                Price = request.Price,
                Quantity = request.Quantity,
                Amount = amount,
                Account = account,
                Note = $"Actual reported entry; promotion {batch.PromotionId:D}; recommendation {batch.BatchId:D}."
            }, t);
        }
        Guid trade = Guid.NewGuid();
        await c.ExecuteAsync("""
            INSERT dbo.TradeLog(TradeId,Symbol,TradeType,TradeDate,Shares,Price,Amount,Commission,NetAmount,PositionId,ExecutionMode,AccountLabel,Reason,Notes)
            VALUES(@Id,@Symbol,N'BUY',@Date,@Quantity,@Price,@Amount,0,@Amount,@Position,N'Real',@Account,N'OperatorReportedRecommendationFill',@Note)
            """, new
        {
            Id = trade,
            Symbol = symbol,
            Date = local,
            Quantity = request.Quantity,
            Price = request.Price,
            Amount = amount,
            Position = position,
            Account = account,
            Note = $"Actual reported {request.Kind}; recommendation {batch.BatchId:D}."
        }, t);
        var result = new TrackedPositionOpenResult(trade, position, symbol, request.Quantity, request.Price, amount, TrackedExecutionMode.Real, account);
        await c.ExecuteAsync("""
            INSERT dbo.TradingReportedFill(RequestId,TradeId,BatchId,FillUtc,RecommendationKind,PositionTarget,RequestHash,ReportJson,ResultJson)
            VALUES(@Id,@Trade,@Batch,@Fill,@Kind,@Target,@Hash,@Report,@Result)
            """, new
        {
            Id = request.RequestId,
            Trade = trade,
            Batch = batch.BatchId,
            Fill = request.FillUtc,
            Kind = request.Kind,
            Target = recommendation.PositionTarget,
            Hash = requestHash,
            Report = Json(request),
            Result = Json(result)
        }, t);
        t.Commit(); return result;
    }
    private sealed record SavedReport(string RequestHash, string ReportJson);
    private sealed record SavedFill(string RequestHash, string ResultJson);
}
