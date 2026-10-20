#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Accounts;
using Core.WSTrade.Orders;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.Promotion;

/// <summary>Produced by native app reads, not operator checkboxes. No orders or financial authority.</summary>
public sealed record BrokerNativeTrialObservation(Guid ObservationId, int ProcessId, DateTimeOffset ProcessStartedAt,
    DateTimeOffset CapturedAt, BrokerStrategyReview Strategy, DateTimeOffset NormalUpdatedAt,
    BrokerAccountReconciliation Account, BrokerAccountRiskBaseline Baseline, BrokerRiskBaselineReceipt FoundBaseline,
    BrokerAccountRiskState Risk, bool BaselineInstalled, bool ContinuationInstalled, bool SessionInstalled, bool ImmutableStorageVerified,
    ImmutableArray<ManualBuyRecord> Buys, ImmutableArray<ManualSellRecord> Sales)
{
    public override string ToString() => "Native limited-trial observation (financial data omitted)";
}

public static class BrokerNativeTrialQualification
{
    public static bool MatchesAccount(WealthsimpleTfsaAccount account, BrokerAccountReconciliation observation) => account.Id == observation.AccountId;
    public static bool RequiresOrderReview(IReadOnlyList<ManualBuyRecord> buys, IReadOnlyList<ManualSellRecord> sales,
        ImmutableArray<BrokerStrategyOrder> owned)
    {
        foreach (var order in owned)
        {
            if (order.Side == "BUY")
            {
                var saved = buys.SingleOrDefault(b => b.Intent.RequestId == order.RequestId);
                if (saved is null || saved.ConflictingObservation || saved.Submission != WealthsimpleSubmissionStatus.Acknowledged ||
                    saved.Intent.Quantity != order.Quantity || saved.Intent.SecurityId != order.SecurityId ||
                    saved.Observations.Any(o => UnsupportedOutcome(o.Status, o.FilledQuantity, order.Quantity)) ||
                    saved.Latest?.IsFilled == true && !saved.HasCompleteFillFacts) return true;
            }
            else
            {
                var saved = sales.SingleOrDefault(s => s.Intent.RequestId == order.RequestId);
                if (saved is null || saved.ConflictingObservation || saved.Submission != WealthsimpleSubmissionStatus.Acknowledged ||
                    saved.Intent.Quantity != order.Quantity || saved.Intent.SecurityId != order.SecurityId ||
                    saved.Observations.Any(o => UnsupportedOutcome(o.Status, o.FilledQuantity, order.Quantity)) ||
                    saved.Latest?.IsFilled == true && !saved.HasCompleteFillFacts) return true;
            }
        }
        return false;
    }
    private static bool UnsupportedOutcome(string? status, decimal? filled, int quantity) =>
        status is "PARTIALLY_FILLED" or "CANCELLED" or "CANCELED" or "EXPIRED" or "REJECTED" ||
        filled > 0 && filled < quantity || status == "FILLED" && filled != quantity;
    public const string Contract = "LimitedFirstDayNativeFullFillReviewV1";
    public const string Limits = "First-session engineering trial accepts disclosed performance gaps. Native full-fill/manual normal-restart observations only; partial/cancel/uncertain-send/power-loss and populated engine recovery are not claimed qualified. Those outcomes stop automatic order progression for operator review.";
    public static void ValidateObservation(BrokerNativeTrialObservation o, DateTimeOffset now)
    {
        o.Strategy.Strategy.Validate();
        var account = o.Account; var baseline = o.Baseline;
        var date = DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(o.CapturedAt.UtcDateTime));
        var carry = o.Risk.TrialCarry;
        bool carriedSession = carry is not null && carry.Session == BrokerTrialCarryForward.ExecutionDate &&
            baseline.TradingDate == BrokerTrialCarryForward.VerificationDate && carry.Session == date &&
            carry.NoOutsideChangesConfirmed && carry.ReviewedAt <= o.CapturedAt &&
            BrokerAccountImport.Hash(carry.Baseline) == BrokerAccountImport.Hash(baseline) &&
            o.Risk.RevisionId == BrokerTrialCarryForward.RequestId(baseline.BaselineId) &&
            o.Risk.Through == carry.ReviewedAccount.SnapshotAt;
        if (carry is not null)
        {
            if (!carriedSession) throw new InvalidOperationException("Recover the exact reviewed next-session carry and original baseline; no replacement reference is permitted.");
            BrokerTrialCarryForward.RequireUnchangedBook(carry.OriginalAnchor, account);
        }
        if (o.ObservationId == Guid.Empty || o.ProcessId <= 0 || o.ProcessStartedAt == default || o.ProcessStartedAt > o.CapturedAt ||
            o.CapturedAt > now || now - o.CapturedAt > TimeSpan.FromSeconds(30) ||
            account.CheckedAt > o.CapturedAt || account.SnapshotAt > account.CheckedAt ||
            o.CapturedAt - account.SnapshotAt > TimeSpan.FromSeconds(30) ||
            !account.HoldingsMatch || account.Blocks.Count != 0 || account.HistoryConflicts.Count != 0 ||
            account.Holdings.Any(h => h.BrokerQuantity != 0) || account.Cash is not > 0 || account.AccountValue != account.Cash ||
            account.BuyingPower is not > 0 || account.Orders.Any(f => !f.IsTerminal) ||
            account.Orders.Select(f => f.ExternalId).Distinct().Count() != account.Orders.Count)
            throw new InvalidOperationException("Capture a fresh, reconciled cash-only native book after all manual verification trades, with no pending/conflicting order.");
        if (!o.BaselineInstalled || !o.ContinuationInstalled || !o.SessionInstalled || !o.ImmutableStorageVerified ||
            baseline.BaselineId == Guid.Empty || baseline.BindingId == Guid.Empty ||
            BrokerAccountImport.Hash(baseline) != BrokerAccountImport.Hash(o.FoundBaseline.Baseline) ||
            !CompleteTradingStrategy.IsHash(o.FoundBaseline.RequestHash) ||
            o.Risk.BaselineId != baseline.BaselineId || o.Risk.BindingId != baseline.BindingId ||
            o.Risk.TradingDate != date || (carry is null && o.Risk.Through != baseline.ObservationAt) ||
            o.Risk.SessionReference != baseline.SessionReference || o.Risk.DrawdownReference != baseline.DrawdownReference ||
            o.Risk.DailyLossHold || o.Risk.CapitalReviewRequired ||
            (carry is null && baseline.TradingDate != date))
            throw new InvalidOperationException("Installed native storage and matching saved baseline/Find/readback references are required; no known reference or hold may be reset.");
        var strategy = o.Strategy.Strategy;
        if (strategy.Strategy.Family != TradingStrategyFamily.DelphiLive || strategy.GateProfile != Core.Trader.PaperGateProfile.NormalV1 ||
            strategy.Comparison is null || !o.Strategy.Blocks.IsEmpty ||
            o.Strategy.Evidence.PaperAccountId != strategy.PaperAccountId || string.IsNullOrWhiteSpace(o.Strategy.Evidence.FactsJson) ||
            o.Strategy.PreparedUtc.Kind != DateTimeKind.Utc || o.Strategy.PreparedUtc > o.CapturedAt.UtcDateTime ||
            o.CapturedAt.UtcDateTime - o.Strategy.PreparedUtc > TimeSpan.FromMinutes(2) ||
            o.NormalUpdatedAt > o.CapturedAt || o.CapturedAt - o.NormalUpdatedAt > TimeSpan.FromMinutes(2))
            throw new InvalidOperationException("Review the exact native Normal profile/policy/daily/build, fresh source and disclosed paper evidence before the trial checkpoint.");
        var buys = o.Buys.Where(b => SameAccount(b.Intent.IdentityId, b.Intent.AccountId, account) && Today(b.Intent.CreatedAt, baseline)).ToArray();
        var sales = o.Sales.Where(s => SameAccount(s.Intent.IdentityId, s.Intent.AccountId, account) && Today(s.Intent.CreatedAt, baseline)).ToArray();
        if (carry is not null && (buys.Any(b => !FreshOriginalRead(b.LastCheckedAt, o)) || sales.Any(s => !FreshOriginalRead(s.LastCheckedAt, o))))
            throw new InvalidOperationException("Read back the same four original order IDs in this running process before the next-session checkpoint.");
        if (buys.Length != 1 || buys[0].Intent.Quantity != 1 || !VerifiedBuy(buys[0], account) || sales.Length != 3 ||
            sales.Select(s => s.Intent.RequestId).Distinct().Count() != 3 ||
            sales.Any(s => !BrokerNoTransferSessionScope.IsVerifiedOpeningSale(s, account)) ||
            buys.Any(b => b.Observations.Any(o => UnsupportedOutcome(o.Status, o.FilledQuantity, b.Intent.Quantity))) ||
            sales.Any(s => s.Observations.Any(o => UnsupportedOutcome(o.Status, o.FilledQuantity, s.Intent.Quantity))) ||
            buys[0].Submission != WealthsimpleSubmissionStatus.Acknowledged || sales.Any(s => s.Submission != WealthsimpleSubmissionStatus.Acknowledged))
            throw new InvalidOperationException("Native full-fill qualification needs exactly one completed one-share verification buy and three full verified manual sales today, all matched to native history.");
    }
    private static bool Today(DateTimeOffset at, BrokerAccountRiskBaseline baseline) =>
        DateOnly.FromDateTime(PaperTradingMonitor.ToToronto(at.UtcDateTime)) == baseline.TradingDate;
    private static bool FreshOriginalRead(DateTimeOffset? at, BrokerNativeTrialObservation observation) => at is { } read &&
        read >= observation.ProcessStartedAt && read <= observation.CapturedAt;
    private static bool SameAccount(string identity, string account, BrokerAccountReconciliation book) => identity == book.IdentityId && account == book.AccountId;
    private static bool VerifiedBuy(ManualBuyRecord b, BrokerAccountReconciliation account) => b.IsResolved && b.Latest?.IsFilled == true &&
        b.Latest.FilledQuantity == b.Intent.Quantity && account.Orders.Any(f =>
            f.ExternalId == "order-" + b.Intent.RequestId.ToString("D") && f.Side == "BUY" && f.IsTerminal && f.Currency == "CAD" &&
            f.SecurityId == b.Intent.SecurityId && f.Symbol == b.Intent.Symbol && f.SubmittedQuantity == b.Intent.Quantity &&
            f.FilledQuantity == b.Intent.Quantity && f.NetValue == b.Latest.FilledNetValue?.Amount &&
            f.GrossValue == b.Latest.FilledValue?.Amount && f.Fees == b.Latest.Fees?.Amount && f.Tax == b.Latest.TaxWithholding?.Amount);

    public static void ValidateRecovery(BrokerNativeTrialObservation before, BrokerNativeTrialObservation after,
        BrokerNoTransferSessionScope scope, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        ValidateObservation(before, before.CapturedAt); ValidateObservation(after, now); scope.Validate(calendar);
        if (scope.TrialCarry?.Fingerprint != before.Risk.TrialCarry?.Fingerprint ||
            scope.TrialCarry?.Fingerprint != after.Risk.TrialCarry?.Fingerprint)
            throw new InvalidOperationException("Recover the same original saved trial carry on both sides of the native restart.");
        if (!scope.LimitedFirstSessionTrial || !scope.FullFillOnlyNativeTrial || !scope.OpeningSalesVerified ||
            scope.PurchaseExpiresAt <= now || scope.BindingId != after.Baseline.BindingId || scope.BaselineId != after.Baseline.BaselineId ||
            before.ProcessId == after.ProcessId || after.ProcessStartedAt <= before.CapturedAt ||
            before.Strategy.Strategy.Fingerprint != after.Strategy.Strategy.Fingerprint ||
            BrokerAccountImport.Hash(before.Baseline) != BrokerAccountImport.Hash(after.Baseline) ||
            before.FoundBaseline.RequestHash != after.FoundBaseline.RequestHash ||
            BrokerAccountImport.Hash(before.Risk) != BrokerAccountImport.Hash(after.Risk) ||
            !SameAccount(before.Account.IdentityId, before.Account.AccountId, after.Account) ||
            scope.CashOnlyAnchor.AccountId != after.Account.AccountId || scope.CashOnlyAnchor.IdentityId != after.Account.IdentityId ||
            scope.CashOnlyAnchor.SnapshotAt != after.Baseline.ObservationAt ||
            before.Account.Cash != after.Account.Cash || after.Account.Cash != scope.CashOnlyAnchor.Cash ||
            scope.CashOnlyAnchor.Orders.Count != before.Account.Orders.Count ||
            scope.CashOnlyAnchor.Orders.Any(f => !before.Account.Orders.Any(n => n.ExternalId == f.ExternalId && n.IsSameOrPostedSuccessor(f))) ||
            before.Account.Orders.Count != after.Account.Orders.Count ||
            before.Account.Orders.Any(f => !after.Account.Orders.Any(n => n.ExternalId == f.ExternalId && n.IsSameOrPostedSuccessor(f))))
            throw new InvalidOperationException("Recover the same account, exact build/strategy, original orders, baseline and risk references after a normal process restart. Changed financial anchors cannot be replaced.");
        var buy = after.Buys.Single(b => SameAccount(b.Intent.IdentityId, b.Intent.AccountId, after.Account) && Today(b.Intent.CreatedAt, after.Baseline));
        var oldBuy = before.Buys.SingleOrDefault(b => b.Intent.RequestId == buy.Intent.RequestId);
        var originalIds = scope.OpeningSales.Select(s => s.Intent.RequestId).ToArray();
        var sales = after.Sales.Where(s => SameAccount(s.Intent.IdentityId, s.Intent.AccountId, after.Account) && Today(s.Intent.CreatedAt, after.Baseline)).ToArray();
        var closing = sales.Where(s => !originalIds.Contains(s.Intent.RequestId)).ToArray();
        if (oldBuy is null || BrokerAccountImport.Hash(oldBuy.Intent) != BrokerAccountImport.Hash(buy.Intent) ||
            !FreshOriginalRead(buy.LastCheckedAt, after) || buy.Latest!.LastFilledAt is null ||
            scope.OpeningSales.Any(s => !sales.Any(n => n.Intent.RequestId == s.Intent.RequestId) ||
                s.Latest?.LastFilledAt is null || s.Latest.LastFilledAt > buy.Intent.CreatedAt) ||
            closing.Length != 1 || closing[0].Intent.SecurityId != buy.Intent.SecurityId || closing[0].Intent.Quantity != 1 ||
            closing[0].Intent.CreatedAt < buy.Latest.LastFilledAt || sales.Any(s => !FreshOriginalRead(s.LastCheckedAt, after) ||
                !before.Sales.Any(old => old.Intent.RequestId == s.Intent.RequestId && BrokerAccountImport.Hash(old.Intent) == BrokerAccountImport.Hash(s.Intent))))
            throw new InvalidOperationException("Read back the same original full buy and all three sale IDs after restart. Original sales must precede the test buy, followed by its separate liquidation before baseline setup.");
    }
}

/// <summary>Protected native observations and scoped publication. Never saves financial references/authority or sends orders.</summary>
public sealed class BrokerNativeTrialEvidenceFileStore(string packetPath, IManualBuyProtector protector)
{
    private string CheckpointPath(DateOnly date) => Path.Combine(Path.GetDirectoryName(packetPath)!, $"native-trial-checkpoint-{date:yyyyMMdd}.dat");
    private T Read<T>(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Native trial evidence exceeds the supported size.");
        var plain = protector.Unprotect(File.ReadAllBytes(path));
        try { return JsonSerializer.Deserialize<T>(plain) ?? throw new InvalidDataException("Native trial evidence needs recovery."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public BrokerNativeTrialObservation? ReadCheckpoint(DateOnly date) => File.Exists(CheckpointPath(date)) ? Read<BrokerNativeTrialObservation>(CheckpointPath(date)) : null;
    public BrokerNativeTrialObservation Capture(BrokerNativeTrialObservation observation)
    {
        if (!Path.IsPathFullyQualified(packetPath)) throw new InvalidOperationException("Native evidence requires its absolute protected-store path.");
        BrokerNativeTrialQualification.ValidateObservation(observation, observation.CapturedAt);
        Directory.CreateDirectory(Path.GetDirectoryName(packetPath)!);
        using var lease = new FileStream(packetPath + ".prepare.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var prior = ReadCheckpoint(observation.Risk.TradingDate);
        if (prior is not null)
        {
            if (prior.ProcessId != observation.ProcessId || prior.Strategy.Strategy.Fingerprint != observation.Strategy.Strategy.Fingerprint ||
                BrokerAccountImport.Hash(prior.Baseline) != BrokerAccountImport.Hash(observation.Baseline) ||
                BrokerAccountImport.Hash(prior.Risk) != BrokerAccountImport.Hash(observation.Risk))
                throw new InvalidOperationException("Preserve the original recovery checkpoint; prepare the first review after restart instead of replacing it.");
            return prior;
        }
        Write(CheckpointPath(observation.Risk.TradingDate), observation, false); return ReadCheckpoint(observation.Risk.TradingDate)!;
    }
    public BrokerExecutionQualificationBundle Complete(BrokerNativeTrialObservation after, BrokerNoTransferSessionScope scope,
        ITsxSessionCalendar calendar, string reviewedBy, DateTimeOffset now)
    {
        if (!Path.IsPathFullyQualified(packetPath)) throw new InvalidOperationException("Native evidence requires its absolute protected-store path.");
        using var lease = new FileStream(packetPath + ".prepare.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var before = ReadCheckpoint(scope.Session) ?? throw new InvalidOperationException("Capture the native qualification checkpoint after the manual trades and saved cash-only baseline, then restart this exact copy.");
        BrokerNativeTrialQualification.ValidateRecovery(before, after, scope, calendar, now);
        if (string.IsNullOrWhiteSpace(reviewedBy)) throw new InvalidOperationException("An identified native evidence reviewer is required.");
        var reader = new BrokerExecutionQualificationFileStore(packetPath, protector); var prior = reader.Read(now);
        if (prior is not null)
        {
            if (prior.BindingId != scope.BindingId || prior.StrategyFingerprint != after.Strategy.Strategy.Fingerprint ||
                prior.ImplementationIdentity != after.Strategy.Strategy.Strategy.ImplementationIdentity || prior.Session != scope.Session ||
                prior.NoTransferScope is null || BrokerAccountImport.Hash(prior.NoTransferScope) != BrokerAccountImport.Hash(scope) ||
                BrokerActivationReview.ChecksFor(scope).Any(c => !prior.Qualifications.Any(q => q.Check == c && q.FirstSessionTrialOnly)))
                throw new InvalidOperationException("Existing qualification needs independent recovery; native review cannot replace its conclusions or financial scope.");
            return prior;
        }
        var proofPath = Path.Combine(Path.GetDirectoryName(packetPath)!, "native-trial-review-" + Guid.NewGuid().ToString("N") + ".dat");
        Write(proofPath, new { Contract = BrokerNativeTrialQualification.Contract, Before = before, After = after, Scope = scope,
            ReviewedBy = reviewedBy, ReviewedAt = now, Limitations = BrokerNativeTrialQualification.Limits }, false);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(proofPath)));
        var qualifications = BrokerActivationReview.ChecksFor(scope).Select(c => new BrokerQualification(c, scope.BindingId,
            after.Strategy.Strategy.Fingerprint, now, hash, proofPath) { FirstSessionTrialOnly = true }).ToImmutableArray();
        var packet = new BrokerExecutionQualificationBundle(scope.BindingId, after.Strategy.Strategy.Fingerprint,
            after.Strategy.Strategy.Strategy.ImplementationIdentity, scope.Session, now, scope.PurchaseExpiresAt!.Value, reviewedBy, qualifications, scope);
        packet.Validate(now); Write(packetPath, packet, false);
        return reader.Read(now) ?? throw new InvalidDataException("Published native review could not be recovered.");
    }
    private void Write<T>(string path, T value, bool overwrite)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(value); byte[] encrypted;
        try { encrypted = protector.Protect(plain); } finally { CryptographicOperations.ZeroMemory(plain); }
        if (encrypted.Length > 8 * 1024 * 1024) throw new InvalidDataException("Native review is too large; no partial packet is installed.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(encrypted); stream.Flush(true); }
        File.Move(temporary, path, overwrite);
    }
}
