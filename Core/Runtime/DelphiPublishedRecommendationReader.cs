#nullable enable

using Core.Db;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>
/// Read-only query used by the Delphi desktop tab. Loading published picks never
/// launches Delphi or changes operational state.
/// </summary>
public sealed class DelphiPublishedRecommendationReader
{
    public async Task<DelphiPublishedRecommendations?> LoadLatestAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var repository = new DailyPickRepository();
        DateTime? pickDate = await repository.GetLatestPickDate();
        if (!pickDate.HasValue)
            return null;

        cancellationToken.ThrowIfCancellationRequested();
        List<DailyPickInfo> continuation = await repository.GetPicksByDate(
            pickDate.Value,
            "Continuation");
        cancellationToken.ThrowIfCancellationRequested();
        List<DailyPickInfo> breakout = await repository.GetPicksByDate(
            pickDate.Value,
            "Breakout");
        DateTime latestCreatedUtc = continuation
            .Concat(breakout)
            .Select(pick => pick.CreatedUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max();
        DelphiPresentationSnapshot? presentation =
            await new DelphiPersistedPresentationReader().LoadAsync(
                pickDate.Value,
                latestCreatedUtc,
                continuation,
                cancellationToken);

        return new DelphiPublishedRecommendations(
            pickDate.Value.Date,
            latestCreatedUtc,
            continuation.AsReadOnly(),
            breakout.AsReadOnly(),
            presentation);
    }
}
