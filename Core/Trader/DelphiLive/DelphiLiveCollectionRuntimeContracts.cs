#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

public interface IDelphiLiveCollectionRuntimeStore : IDelphiLiveCycleStore, IDelphiLiveLeaseStore
{
    Task<DelphiLiveCollectionRecovery> RecoverSessionAsync(
        Guid sessionId, DelphiLiveLease lease, CancellationToken cancellationToken = default,
        bool wasArmedAtSessionOpen = false);

    Task<IReadOnlyList<DelphiLiveFiveMinuteBar>> GetSessionBarsAsync(
        Guid sessionId, DateTime throughBarEndUtc, CancellationToken cancellationToken = default);

    Task FinishSessionAsync(Guid sessionId, DelphiLiveLease lease, bool hostStopping,
        CancellationToken cancellationToken = default);
}

public sealed record DelphiLiveCollectionRecovery(
    Guid ContinuityEpochId, int EpochNumber, bool HostGapObserved,
    int AddedMissedCycles, DateTime RecoveredUtc);
