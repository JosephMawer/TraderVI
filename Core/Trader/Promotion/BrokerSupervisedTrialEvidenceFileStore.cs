#nullable enable
using Core.Trader.DelphiLive;
using Core.WSTrade.Orders;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Core.Trader.Promotion;

/// <summary>Explicit preparation of scoped proof from verified opening sales/references.
/// Independently reviewed production qualifications are retained unchanged, never issued here.</summary>
public sealed class BrokerSupervisedTrialEvidenceFileStore(string path, IManualBuyProtector protector)
{
    public BrokerExecutionQualificationBundle Prepare(BrokerNoTransferSessionScope scope,
        BrokerManagedStrategy strategy, string implementation, ITsxSessionCalendar calendar, DateTimeOffset now)
    {
        strategy.Validate(); scope.Validate(calendar);
        if (!Path.IsPathFullyQualified(path) || !scope.LimitedFirstSessionTrial || !scope.OpeningSalesVerified ||
            scope.ApprovedAt > now || scope.PurchaseExpiresAt <= now ||
            strategy.Strategy.Family != TradingStrategyFamily.DelphiLive || strategy.GateProfile != PaperGateProfile.NormalV1 ||
            strategy.Comparison is null || strategy.Strategy.ImplementationIdentity != implementation)
            throw new InvalidOperationException("Prepare only the explicitly approved current Normal limited trial from its verified native sales and saved references.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var lease = new FileStream(path + ".prepare.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var reader = new BrokerExecutionQualificationFileStore(path, protector);
        var prior = reader.Read(now);
        if (prior is not null && (prior.BindingId != scope.BindingId || prior.StrategyFingerprint != strategy.Fingerprint ||
            prior.ImplementationIdentity != implementation || prior.Session != scope.Session))
            throw new InvalidOperationException("Preserve the existing qualification packet; it needs independent review for this account, strategy, build and session.");
        if (prior?.NoTransferScope is { } saved)
        {
            if (!saved.LimitedFirstSessionTrial || saved.ScopeId != scope.ScopeId ||
                saved.BaselineId != scope.BaselineId || saved.CashOnlyAnchor != scope.CashOnlyAnchor &&
                Core.WSTrade.Accounts.BrokerAccountImport.Hash(saved.CashOnlyAnchor) != Core.WSTrade.Accounts.BrokerAccountImport.Hash(scope.CashOnlyAnchor))
                throw new InvalidOperationException("Recover the original scoped trial; preparation cannot replace its financial anchor.");
            return prior;
        }
        var proofBytes = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new {
            Contract = "LimitedFirstSessionTrialV1", Scope = scope, StrategyFingerprint = strategy.Fingerprint,
            ImplementationIdentity = implementation,
            IndependentRestartEvidence = prior?.Qualifications.SingleOrDefault(q => q.Check == "restart-recovery"),
            Limitation = "Conditional no-transfer reconciliation and saved setup references; not a native cash-event feed or historical opening book." }));
        if (proofBytes.Length > 8 * 1024 * 1024) throw new InvalidDataException("Trial proof exceeds the supported size.");
        var proofPath = Path.Combine(Path.GetDirectoryName(path)!, "trial-evidence-" + Guid.NewGuid().ToString("N") + ".dat");
        using (var stream = new FileStream(proofPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(proofBytes); stream.Flush(true); }
        var hash = Convert.ToHexString(SHA256.HashData(proofBytes));
        var native = (prior?.Qualifications ?? []).Where(q => BrokerActivationReview.RequiredChecks.Contains(q.Check) &&
            q.Check is not ("cash-events" or "opening-values")).ToImmutableArray();
        var scoped = BrokerActivationReview.AdditionalScopedChecks.Where(c => c != "scoped-durable-recovery" ||
            native.Any(q => q.Check == "restart-recovery")).Select(c =>
                new BrokerQualification(c, scope.BindingId, strategy.Fingerprint, now, hash, proofPath));
        var packet = new BrokerExecutionQualificationBundle(scope.BindingId, strategy.Fingerprint, implementation,
            scope.Session, now, prior is null || prior.ExpiresAt > scope.PurchaseExpiresAt!.Value
                ? scope.PurchaseExpiresAt!.Value : prior.ExpiresAt, scope.ApprovedBy, native.AddRange(scoped), scope);
        packet.Validate(now);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(packet))); stream.Flush(true); }
        File.Move(temporary, path, overwrite: true);
        return reader.Read(now) ?? throw new InvalidDataException("The prepared trial evidence could not be recovered.");
    }
}
