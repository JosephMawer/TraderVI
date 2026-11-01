#nullable enable
using Core.Runtime;
using Core.Trader.Promotion;
using Core.Trader.Storage;
using Core.WSTrade.Orders;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

// Called under exclusive engine ownership. A later startup must never recapture a damaged checkpoint.
internal sealed class CompactAccountPreparation(ITradingStorageDatabase database, IEngineContinuityStore continuity,
    IEngineContinuityFile witness, IBrokerManagedSessionStore sessions, BrokerExecutionHostGrantFileStore grants,
    ManualBuyFileJournal journal, IManualBuyProtector protector, string directory)
{
    internal async Task PrepareAsync(string build, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        using var fence = grants.AcquireFence();
        var grant = grants.Read() ?? throw new InvalidDataException("No approved trading permission.");
        await using var session = await sessions.AcquireAsync(grant.BindingId, ct);
        var saved = session.Current ?? throw new InvalidDataException("No approved trading assignment.");
        var observed = await continuity.ReadAsync(ct);
        if (observed.Stamp == null || observed.Stamp.Database != observed.Database || observed.Stamp != witness.Read())
            throw new InvalidDataException("Local and database restart records disagree.");
        var now = DateTimeOffset.UtcNow;
        BrokerExecutionHost.RequireMatchingAssignment(grant, saved, build, now);
        if (!saved.HasReviewedCompactRestart || !grant.Enabled || !grant.RestoreAfterRestart || grant.ImplementationIdentity != build)
            throw new InvalidOperationException("Review the scoped restart terms and enable this exact trading build first.");
        if (journal.HasUnresolvedOrders())
            throw new InvalidOperationException("Resolve the original journal orders before the initial transfer.");
        await using var tx = await database.BeginAsync(grant.BindingId, true, ct);
        if (await tx.ReadCurrentAsync(ct) != null)
            throw new InvalidOperationException("A compact account already exists. Verify its existing checkpoint.");
        string sourceHash = Core.WSTrade.Accounts.BrokerAccountImport.Hash(saved);
        // Weekend review observes the current account without advancing the market's daily risk
        // reference. Preserve the actual risk date and controls. Only the regular worker may advance them.
        var frozen = RestartAccountRehearsal.ProjectSettled(saved, grant, build, sourceHash, now, transferReviewedAccount: true);
        var initial = frozen with
        {
            Settings = frozen.Settings with { BuyAllowed = true, SellAllowed = true },
            Checkpoints = [frozen.Checkpoints.Single() with { OperationKind = "ReviewedLiveCutover" }]
        };
        TradingCurrentStore.ValidateCurrent(initial, grant.BindingId);
        var assignment = CompactDelphiAssignment.Capture(saved, grant, initial, build, now);
        journal.PrepareRestartSellScopes(assignment.Identity, assignment.Account, initial.Positions.Select(p =>
            new ManualBuyFileJournal.RestartHolding(p.SecurityId, p.PositionId, p.FirstEntryUtc)).ToArray());
        await RestartAccountRehearsal.InsertInitialAsync(tx, initial, ct);
        var written = await tx.ReadCurrentAsync(ct) ?? throw new InvalidDataException("The new compact account cannot be read.");
        TradingCurrentStore.ValidateCurrent(written, grant.BindingId);
        // SQL decimal(19,6) can return 10.000000 for a source value of 10. Compare typed
        // values first, then seal the SQL representation that subsequent restarts will read.
        if (written.Account != initial.Account || written.Settings != initial.Settings ||
            !written.Positions.OrderBy(p => p.PositionId).SequenceEqual(initial.Positions.OrderBy(p => p.PositionId)) ||
            !written.Symbols.OrderBy(s => s.Symbol, StringComparer.Ordinal).SequenceEqual(initial.Symbols.OrderBy(s => s.Symbol, StringComparer.Ordinal)) ||
            !written.Orders.IsEmpty || !written.Restrictions.IsEmpty || !written.Checkpoints.SequenceEqual(initial.Checkpoints))
            throw new InvalidDataException("Stored account facts differ from the reviewed assignment.");
        var checkpoint = CompactDelphiCheckpoint.From(grant.BindingId, observed.Database, assignment, written)
            with { ManagedRevision = saved.RevisionId };
        checkpoint.Write(Slot(directory, written.Account.Version), protector);
        await tx.CommitAsync(ct);
    }

    internal static string Slot(string directory, long version) => Path.Combine(directory, $"compact-{version % 2}.dat");
}
