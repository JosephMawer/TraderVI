#nullable enable
using Core.Trader.Promotion;
using Core.Runtime;
using Dapper;
using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Db;

/// <summary>Independent execution must never silently fall back to paper-derived input freshness.</summary>
public static class IndependentExecutionReadiness
{
    public static async Task VerifyAsync(BrokerManagedSession session, CancellationToken ct)
    {
        session.Strategy.Validate();
        await using var c = new SqlConnection(new SQLBase().ConnectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        var installed = await c.QuerySingleAsync<int>(new CommandDefinition("""
SELECT CASE WHEN OBJECT_ID(N'dbo.EngineInputPublication', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineEvidenceContent', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineMarketCycle', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineCurrentState', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EnginePaperRecordIdentity', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineIntegrityIncident', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineIntegrityResolution', N'U') IS NOT NULL
 AND OBJECT_ID(N'dbo.EngineAuditRun', N'U') IS NOT NULL THEN 1 ELSE 0 END;
""", cancellationToken: ct));
        if (installed != 1) throw new InvalidOperationException("The independent input/state migration is not installed.");
        if (!await EngineIntegrityRepository.RequiredGuardsAsync(c, null, ct))
            throw new InvalidOperationException("Independent immutable-history guards are unavailable.");
        var access = EngineIntegrityAccess.Evaluate(await EngineIntegrityRepository.ReadOpenAsync(c, null, ct), EngineIntegrityScope.Broker, session.BindingId);
        if (!access.ProtectionAllowed)
            throw new InvalidOperationException("An integrity hold makes affected automatic protection unavailable. Verify recovery before resuming.");
        try { await new EngineRealObservationStore().RequireCurrentAsync(session, ct).ConfigureAwait(false); }
        catch (Exception error) when (error is InvalidDataException or JsonException)
        {
            await new EngineIntegrityRepository().RaiseAsync(EngineIntegrityScope.Broker, session.BindingId,
                "RealObservation/" + session.RevisionId.ToString("D"), "CurrentSubscriptionInvalid", true, ct).ConfigureAwait(false);
            throw;
        }
        // The caller verifies the paired continuity witness first. Original exact-build permission remains
        // in BrokerExecutionHost; the direct shared-input reader and order transport enforce fresh inputs,
        // scoped holds, account/order reconciliation and seven-minute freshness before each new action.
        // No paper progress or historical-audit completion receipt is a normal-cycle prerequisite.
    }
}
