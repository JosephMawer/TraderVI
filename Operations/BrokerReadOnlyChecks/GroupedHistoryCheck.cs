#if GROUPED_HISTORY_CHECK
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;

namespace BrokerReadOnlyChecks;

internal sealed record GroupedHistoryCheck(bool Success, string Result, DateTimeOffset CheckedUtc, int IndividualReadRequests,
    int GroupedReadRequests, int TotalRequests, int[] GroupSizes, int IndividualOrders, int GroupedOrders,
    int? DifferingOrderIds, bool? ListingEvidenceMatches, bool? SameActivityCheckpoint)
{
    public int HelperVersion => 3;
    public int OrdersSent => 0;
    public int TokenRefreshes => 0;
    public bool SavedCredentialsChanged => false;
    public bool QualifiedForAutomation => false;

    internal static async Task<GroupedHistoryCheck> RunAsync(WealthsimpleAuthClient client, WealthsimpleTfsaAccount account, CancellationToken ct)
    {
        var start = client.RequestCount;
        var individual = await client.ReadTfsaOrderHistoryAsync(account, ct);
        int singles = client.RequestCount - start;
        var first = individual.Data;
        var result = new GroupedHistoryCheck(false, "IndividualRead/" + individual.Status, DateTimeOffset.UtcNow, singles,
            0, client.RequestCount, [], first?.Orders.Count ?? 0, 0, null, null, null);
        if (individual.Status != WealthsimpleReadStatus.Success || first == null) return result;
        var grouped = await client.ReadRegularTradingHistoryAsync(account, ct);
        var second = grouped.Data;
        result = result with { CheckedUtc = DateTimeOffset.UtcNow, Result = $"GroupedRead/{grouped.Status}/{grouped.Stage}",
            GroupSizes = client.GroupSizes.ToArray(), GroupedReadRequests = client.RequestCount - start - singles,
            TotalRequests = client.RequestCount, GroupedOrders = second?.Orders.Count ?? 0 };
        if (grouped.Status != WealthsimpleReadStatus.Success || second == null) return result;
        var checkpoint = await client.ReadTfsaActivityCheckpointAsync(account, ct);
        var end = checkpoint.Data;
        bool stable = checkpoint.Status == WealthsimpleReadStatus.Success && end != null &&
            first.AccountId == account.Id && second.AccountId == first.AccountId && end.AccountId == first.AccountId &&
            first.IdentityId == second.IdentityId && end.IdentityId == first.IdentityId && first.ActivityFingerprint != null &&
            first.ActivityFingerprint == second.ActivityFingerprint && end.Fingerprint == first.ActivityFingerprint;
        var left = first.Orders.ToDictionary(o => o.ExternalId, StringComparer.Ordinal);
        var right = second.Orders.ToDictionary(o => o.ExternalId, StringComparer.Ordinal);
        int differences = left.Keys.Union(right.Keys).Count(id => left.GetValueOrDefault(id) != right.GetValueOrDefault(id));
        bool listings = (first.Instruments ?? []).OrderBy(i => i.SecurityId, StringComparer.Ordinal).SequenceEqual(
            (second.Instruments ?? []).OrderBy(i => i.SecurityId, StringComparer.Ordinal));
        bool match = differences == 0 && listings && first.Limitations.Order(StringComparer.Ordinal).SequenceEqual(second.Limitations.Order(StringComparer.Ordinal));
        string outcome = !stable ? "ActivityNotConfirmedStable" : result.GroupSizes.Length == 0 ? "NoGroupedRequestExercised"
            : match ? "Matched" : "DifferentOrderHistory";
        return result with { Result = outcome, Success = outcome == "Matched", CheckedUtc = DateTimeOffset.UtcNow,
            TotalRequests = client.RequestCount, DifferingOrderIds = differences, ListingEvidenceMatches = listings, SameActivityCheckpoint = stable };
    }
}
#endif
