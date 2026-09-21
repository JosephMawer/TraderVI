#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.WSTrade.Accounts;

public sealed record BrokerImportLegacyPosition(Guid PositionId, string Symbol, DateOnly EntryDate,
    int Quantity, decimal CostBasis, bool IsActive);
public sealed record BrokerImportLegacyTrade(Guid TradeId, Guid? PositionId, string Symbol, string Side,
    DateOnly TradingDate, DateTimeOffset? FillAt, int Quantity, decimal Gross, decimal? Fees, decimal Net);
public sealed record BrokerImportLegacyLedger(string AccountLabel, string Fingerprint,
    IReadOnlyList<BrokerImportLegacyPosition> Positions, IReadOnlyList<BrokerImportLegacyTrade> Trades)
{
    public override string ToString() => "Legacy account ledger (financial data omitted)";
}
public sealed record BrokerImportBinding(Guid BindingId, string AccountLabel, string BrokerKey,
    string LegacyFingerprint, DateTimeOffset CreatedAt, string ReviewedBy);
public sealed record BrokerImportState(bool Installed, BrokerImportLegacyLedger Legacy,
    BrokerImportBinding? Binding, BrokerImportReceipt? Latest);
public sealed record BrokerImportRow(string ExternalId, string Symbol, string Side, decimal Quantity,
    decimal? Gross, decimal? Fees, decimal? Tax, decimal? Net, Guid? LegacyTradeId, Guid? LegacyPositionId,
    string Action)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordedSymbol { get; init; }
    public override string ToString() => "Broker import row (financial data omitted)";
}
public sealed record BrokerImportDifference(BrokerImportLegacyTrade Recorded, BrokerOrderFact Broker,
    IReadOnlyList<string> Fields)
{
    public override string ToString() => "Broker import difference (financial data omitted)";
}
public sealed record BrokerImportPlan(string AccountLabel, string BrokerKey, string LegacyFingerprint,
    Guid? PreviousRequestId, BrokerAccountReconciliation Observation,
    IReadOnlyList<BrokerImportRow> Rows, IReadOnlyList<string> Blocks)
{
    public bool CanImport => Blocks.Count == 0;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BrokerImportDifference>? Differences { get; init; }
    // Read times establish freshness, but a second read with identical facts need not invalidate human review.
    public string Fingerprint
    {
        get
        {
            var original = BrokerAccountImport.Hash(new { Version = 1, AccountLabel, BrokerKey, LegacyFingerprint,
                PreviousRequestId, Facts = BrokerAccountImport.FinancialFingerprint(Observation), Rows, Blocks });
            return Differences is { Count: > 0 } ? BrokerAccountImport.Hash(new { Version = 2, Original = original, Differences }) : original;
        }
    }
    public override string ToString() => "Broker account import plan (financial data omitted)";
}
public sealed record BrokerImportRequest(Guid RequestId, string ReviewedFingerprint, string ReviewedBy, BrokerImportPlan Plan)
{
    public string RequestHash => BrokerAccountImport.Hash(new { RequestId, ReviewedFingerprint, ReviewedBy,
        Plan.AccountLabel, Plan.BrokerKey });
    public override string ToString() => "Broker account import request (financial data omitted)";
}
public sealed record BrokerImportReceipt(Guid RequestId, Guid BindingId, string RequestHash,
    DateTimeOffset SavedAt, BrokerImportPlan Plan)
{
    public override string ToString() => "Saved broker account history (financial data omitted)";
}

/// <summary>
/// Plans an append-only broker history import. Matches are reviewed references to legacy evidence;
/// cumulative orders never masquerade as individual executions or mutate ActivePosition/TradeLog.
/// </summary>
public static class BrokerAccountImport
{
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
    public static string Key(BrokerAccountReconciliation observation) => Hash(new[] { observation.IdentityId, observation.AccountId });
    public static string FinancialFingerprint(BrokerAccountReconciliation observation)
    {
        var original = Hash(new {
        observation.IdentityId, observation.AccountId, observation.Cash, observation.BuyingPower, observation.AccountValue,
        observation.ActivityFingerprint,
        Orders = observation.Orders.OrderBy(o => o.ExternalId, StringComparer.Ordinal).ToArray(),
        Holdings = observation.Holdings.OrderBy(h => h.SecurityId, StringComparer.Ordinal).ToArray(),
        Blocks = observation.Blocks.Order(StringComparer.Ordinal).ToArray(),
        Conflicts = observation.HistoryConflicts.Order(StringComparer.Ordinal).ToArray()
        });
        return observation.Instruments is { Count: > 0 } ? Hash(new { Version = 2, Original = original,
            Instruments = observation.Instruments.OrderBy(i => i.SecurityId, StringComparer.Ordinal).ToArray() }) : original;
    }

    public static BrokerImportPlan Prepare(BrokerImportRequest request, BrokerImportState current, DateTimeOffset now)
    {
        BrokerImportWorkflow.Validate(request.RequestId, request.ReviewedBy);
        if (request.Plan.Fingerprint != request.ReviewedFingerprint)
            throw new InvalidOperationException("The submitted import differs from the reviewed plan.");
        var plan = Preview(request.Plan.Observation, current, now);
        if (!plan.CanImport || plan.Fingerprint != request.ReviewedFingerprint)
            throw new InvalidOperationException("The broker observation, binding or recorded ledger changed. Refresh the import preview.");
        return plan;
    }

    public static BrokerImportPlan Preview(BrokerAccountReconciliation observation, BrokerImportState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(observation); ArgumentNullException.ThrowIfNull(state);
        var legacy = state.Legacy;
        if (legacy.AccountLabel != "TFSA" || !IsHash(legacy.Fingerprint))
            throw new InvalidOperationException("The initial broker import supports the permanent TFSA account only.");
        var blocks = new List<string>();
        if (!state.Installed) blocks.Add("Install reviewed migration 034 before saving broker history. This preview changes no database records.");
        if (!observation.HoldingsMatch) blocks.Add("Resolve the broker reconciliation issues before importing account history.");
        blocks.AddRange(observation.Blocks); blocks.AddRange(observation.HistoryConflicts);
        if (string.IsNullOrWhiteSpace(observation.IdentityId) || string.IsNullOrWhiteSpace(observation.AccountId) ||
            !IsHash(observation.ActivityFingerprint) || observation.Cash is null or < 0 ||
            observation.BuyingPower is null or < 0 || observation.AccountValue is null or < 0 ||
            observation.CheckedAt < observation.SnapshotAt || observation.SnapshotAt == default ||
            observation.CheckedAt > now || now - observation.SnapshotAt > TimeSpan.FromMinutes(2))
            blocks.Add("Refresh a complete, current account reconciliation before saving history.");
        string key = Key(observation);
        if (state.Binding is { } binding && (binding.BrokerKey != key || binding.AccountLabel != legacy.AccountLabel))
            blocks.Add("The saved TFSA is linked to another broker account. Account relinking needs a separate review.");
        if (state.Binding is { } bound && bound.LegacyFingerprint != legacy.Fingerprint)
            blocks.Add("Manually recorded account history changed after linking. Resolve those changes before updating the broker import.");
        if ((state.Binding is null) != (state.Latest is null))
            blocks.Add("The saved account binding and import history are incomplete. Review the database before continuing.");
        if (state.Latest is { } latest)
        {
            if (latest.BindingId != state.Binding?.BindingId || latest.Plan.BrokerKey != key ||
                latest.Plan.LegacyFingerprint != legacy.Fingerprint || observation.SnapshotAt < latest.Plan.Observation.SnapshotAt)
                blocks.Add("The saved import belongs to different account facts or is newer than this observation.");
            foreach (var old in latest.Plan.Observation.Orders)
                if (observation.Orders.SingleOrDefault(o => o.ExternalId == old.ExternalId) is not { } current || !current.IsSameOrPostedSuccessor(old))
                    blocks.Add("An imported order disappeared or changed. Broker corrections require explicit resolution; prior facts stay saved.");
        }
        if (observation.Orders.Any(o => string.IsNullOrWhiteSpace(o.ExternalId) || string.IsNullOrWhiteSpace(o.SecurityId)) ||
            observation.Orders.Select(o => o.ExternalId).Distinct().Count() != observation.Orders.Count)
            throw new InvalidOperationException("Broker order identities are missing or repeated.");
        if (observation.Orders.Any(o => !o.IsTerminal || !WealthsimpleReconciliation.Valid(o, observation.CheckedAt)))
            blocks.Add("Only resolved orders with complete supported cumulative fill facts can be imported.");
        ValidateReconstruction(observation, blocks);
        var byOrder = new Dictionary<string, BrokerImportLegacyTrade>(StringComparer.Ordinal);
        var differences = new List<BrokerImportDifference>();
        if (legacy.Trades.Select(t => t.TradeId).Distinct().Count() != legacy.Trades.Count ||
            legacy.Positions.Select(p => p.PositionId).Distinct().Count() != legacy.Positions.Count)
            throw new InvalidOperationException("Legacy record identities are repeated.");
        foreach (var trade in legacy.Trades.OrderBy(t => t.TradeId))
        {
            var previous = state.Latest?.Plan.Rows.SingleOrDefault(r => r.LegacyTradeId == trade.TradeId);
            var candidates = observation.Orders.Where(o => Matches(trade, o, observation) && (previous is null || previous.ExternalId == o.ExternalId)).ToArray();
            if (candidates.Length != 1 || byOrder.ContainsKey(candidates[0].ExternalId))
            {
                var comparable = observation.Orders.Where(o => SameExecution(trade, o, observation) &&
                    (previous is null || previous.ExternalId == o.ExternalId)).ToArray();
                if (candidates.Length == 0 && comparable.Length == 1 &&
                    FinancialDifferences(trade, comparable[0]) is { Count: > 0 } fields)
                {
                    differences.Add(new(trade, comparable[0], fields));
                    blocks.Add($"{trade.Symbol} {trade.Side} {trade.TradingDate:yyyy-MM-dd}: recorded {string.Join(", ", fields)} differ from Wealthsimple. Review the comparison below; neither record has been changed.");
                }
                else blocks.Add($"{trade.Symbol}: a recorded trade has no unique broker match. Split fills, missing history and ambiguous matches need review.");
                continue;
            }
            byOrder.Add(candidates[0].ExternalId, trade);
        }
        ValidateLegacy(legacy, byOrder, observation, blocks);
        var oldIds = state.Latest?.Plan.Observation.Orders.Select(o => o.ExternalId).ToHashSet(StringComparer.Ordinal) ?? [];
        var rows = observation.Orders.OrderBy(o => o.LastFilledAt ?? o.CreatedAt).ThenBy(o => o.ExternalId, StringComparer.Ordinal).Select(o =>
        {
            byOrder.TryGetValue(o.ExternalId, out var match);
            return new BrokerImportRow(o.ExternalId, o.Symbol, o.Side, o.FilledQuantity, o.GrossValue, o.Fees, o.Tax, o.NetValue,
                match?.TradeId, match?.PositionId, oldIds.Contains(o.ExternalId) ? "Already saved" : match is not null ? "Match recorded trade" : "Add broker order")
                { RecordedSymbol = match is not null && !string.Equals(match.Symbol, o.Symbol, StringComparison.OrdinalIgnoreCase) ? match.Symbol : null };
        }).ToArray();
        return new(legacy.AccountLabel, key, legacy.Fingerprint, state.Latest?.RequestId, observation, rows,
            blocks.Distinct().Order(StringComparer.Ordinal).ToArray()) { Differences = differences.Count == 0 ? null : differences.ToArray() };
    }

    private static bool Matches(BrokerImportLegacyTrade trade, BrokerOrderFact order, BrokerAccountReconciliation observation) =>
        SameExecution(trade, order, observation) && FinancialDifferences(trade, order).Count == 0;

    private static bool SameExecution(BrokerImportLegacyTrade trade, BrokerOrderFact order, BrokerAccountReconciliation observation)
    {
        if (!order.IsTerminal || order.FilledQuantity <= 0 || order.LastFilledAt is null || trade.Quantity <= 0 ||
            !SymbolMatches(trade.Symbol, order, observation) || trade.Side != order.Side ||
            trade.Quantity != order.FilledQuantity || order.Currency != "CAD" ||
            !WealthsimpleReconciliation.Valid(order, observation.CheckedAt)) return false;
        // Similarity is only a proposed match for explicit review, never an inferred broker execution ID.
        return trade.TradingDate == TorontoDate(order.LastFilledAt.Value) &&
            (trade.FillAt is { } at ? at >= order.CreatedAt && at <= order.LastFilledAt : TorontoDate(order.CreatedAt) == trade.TradingDate);
    }
    private static IReadOnlyList<string> FinancialDifferences(BrokerImportLegacyTrade trade, BrokerOrderFact order)
    {
        var fields = new List<string>();
        if (trade.Gross != order.GrossValue) fields.Add("gross");
        if (trade.Fees != order.Fees) fields.Add("fees");
        if (trade.Net != order.NetValue) fields.Add("net");
        if (order.Tax != 0) fields.Add("tax");
        return fields;
    }
    private static bool SymbolMatches(string recorded, BrokerOrderFact order, BrokerAccountReconciliation observation)
    {
        if (string.Equals(recorded, order.Symbol, StringComparison.OrdinalIgnoreCase)) return true;
        // Alias evidence must come from the same broker security, currency and supported listing.
        // Reject collisions across security IDs instead of choosing a Canadian listing by name alone.
        var evidence = observation.Instruments ?? [];
        var candidates = evidence.Where(i => i.Currency == "CAD" && i.SecurityType == order.SecurityType &&
            i.SecurityType is "EQUITY" or "EXCHANGE_TRADED_FUND" &&
            CanadianTickerPair(recorded, i.Symbol, i.PrimaryMic)).ToArray();
        return candidates.Length == 1 && candidates[0].SecurityId == order.SecurityId &&
            string.Equals(candidates[0].Symbol, order.Symbol, StringComparison.OrdinalIgnoreCase);
    }
    private static bool CanadianTickerPair(string a, string b, string mic)
    {
        if (mic is not ("XTSE" or "XTSX") || new[] { a, b }.Any(s => string.IsNullOrWhiteSpace(s) ||
            s.Length > 32 || s.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')))) return false;
        string suffix = mic == "XTSE" ? ".TO" : ".V";
        return string.Equals(a + suffix, b, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(b + suffix, a, StringComparison.OrdinalIgnoreCase);
    }
    private static void ValidateLegacy(BrokerImportLegacyLedger legacy, Dictionary<string, BrokerImportLegacyTrade> matches,
        BrokerAccountReconciliation observation, List<string> blocks)
    {
        foreach (var trade in legacy.Trades)
            if (trade.PositionId is null || !legacy.Positions.Any(p => p.PositionId == trade.PositionId))
                blocks.Add($"{trade.Symbol}: a recorded trade has no corresponding tracked position.");
        foreach (var position in legacy.Positions)
        {
            DateTimeOffset? RecordedTime(BrokerImportLegacyTrade t) => t.FillAt ?? matches.Where(m => m.Value.TradeId == t.TradeId)
                .Select(m => observation.Orders.Single(o => o.ExternalId == m.Key).LastFilledAt).SingleOrDefault();
            var recorded = legacy.Trades.Where(t => t.PositionId == position.PositionId).OrderBy(t => t.TradingDate).ThenBy(RecordedTime).ToArray();
            bool uncertainSequence = recorded.GroupBy(t => t.TradingDate).Any(g => g.Select(t => t.Side).Distinct().Count() > 1 && g.Any(t => RecordedTime(t) is null));
            if (recorded.Length == 0 || recorded.Any(t => !string.Equals(t.Symbol, position.Symbol, StringComparison.OrdinalIgnoreCase)) || (!uncertainSequence && recorded[0].Side != "BUY") ||
                recorded[0].TradingDate != position.EntryDate)
            { blocks.Add($"{position.Symbol}: tracked position identity or purchase history needs review."); continue; }
            decimal recordedQuantity = 0, recordedCost = 0;
            bool recordedValid = true;
            foreach (var trade in recorded)
            {
                if (trade.Quantity <= 0 || trade.Net < 0 || trade.Side is not ("BUY" or "SELL")) { recordedValid = false; break; }
                if (trade.Side == "BUY") { recordedQuantity += trade.Quantity; recordedCost += trade.Net; }
                else if (trade.Quantity > recordedQuantity) { recordedValid = false; break; }
                else { recordedCost -= recordedCost * trade.Quantity / recordedQuantity; recordedQuantity -= trade.Quantity; }
            }
            if (!uncertainSequence && (!recordedValid || recordedQuantity != (position.IsActive ? position.Quantity : 0) ||
                (position.IsActive && decimal.Abs(recordedCost - position.CostBasis) > .01m)))
                blocks.Add($"{position.Symbol}: tracked shares or book value disagree with its recorded trades.");
            var trades = matches.Where(x => x.Value.PositionId == position.PositionId).Select(x => observation.Orders.Single(o => o.ExternalId == x.Key))
                .OrderBy(o => o.LastFilledAt).ToArray();
            // Unmatched trades already have specific blocks/comparisons. A matched-only subset
            // cannot prove a position discrepancy; validate the complete recorded ledger above.
            if (trades.Length != recorded.Length) continue;
            if (trades.Length == 0 || trades.Select(o => o.SecurityId).Distinct().Count() != 1 ||
                trades.Any(o => !SymbolMatches(position.Symbol, o, observation)) ||
                trades[0].Side != "BUY" || TorontoDate(trades[0].LastFilledAt!.Value) != position.EntryDate)
            { blocks.Add($"{position.Symbol}: tracked position identity or purchase history needs review."); continue; }
            decimal quantity = 0, cost = 0;
            bool bought = false;
            foreach (var trade in trades)
            {
                if (trade.Side == "BUY")
                {
                    if (bought && quantity == 0) blocks.Add($"{position.Symbol}: one tracked position spans multiple closed-and-reopened holdings.");
                    quantity += trade.FilledQuantity; cost += trade.NetValue!.Value; bought = true;
                }
                else if (trade.FilledQuantity > quantity) { blocks.Add($"{position.Symbol}: recorded sales exceed recorded purchases."); break; }
                else { cost -= cost * trade.FilledQuantity / quantity; quantity -= trade.FilledQuantity; }
            }
            if (quantity != (position.IsActive ? position.Quantity : 0) ||
                (position.IsActive && decimal.Abs(cost - position.CostBasis) > .01m))
                blocks.Add($"{position.Symbol}: tracked shares or book value disagree with its recorded trades.");
        }
    }
    private static void ValidateReconstruction(BrokerAccountReconciliation observation, List<string> blocks)
    {
        if (observation.Holdings.Select(h => h.SecurityId).Distinct().Count() != observation.Holdings.Count)
        { blocks.Add("Reconciliation contains repeated holdings."); return; }
        var totals = new Dictionary<string, (decimal Quantity, decimal Cost)>();
        var lastFills = new Dictionary<string, DateTimeOffset>();
        foreach (var order in observation.Orders.OrderBy(o => o.LastFilledAt ?? o.CreatedAt))
        {
            if (!WealthsimpleReconciliation.Valid(order, observation.CheckedAt) || order.FilledQuantity == 0) continue;
            if (lastFills.TryGetValue(order.SecurityId, out var last) && order.CreatedAt <= last)
                blocks.Add("Overlapping broker orders need individual fill sequencing before import.");
            lastFills[order.SecurityId] = order.LastFilledAt!.Value;
            var p = totals.GetValueOrDefault(order.SecurityId);
            if (order.Side == "BUY") p = (p.Quantity + order.FilledQuantity, p.Cost + order.NetValue!.Value);
            else if (p.Quantity < order.FilledQuantity) { blocks.Add("Broker sales have missing purchase history."); continue; }
            else p = (p.Quantity - order.FilledQuantity, p.Cost - p.Cost * order.FilledQuantity / p.Quantity);
            totals[order.SecurityId] = p;
        }
        foreach (string id in totals.Keys.Union(observation.Holdings.Select(h => h.SecurityId)))
        {
            var h = observation.Holdings.SingleOrDefault(h => h.SecurityId == id); var p = totals.GetValueOrDefault(id);
            if (h is null || !h.Matches || string.IsNullOrWhiteSpace(h.Symbol) || h.BrokerQuantity != p.Quantity ||
                h.BrokerQuantity < 0 || h.BrokerQuantity > int.MaxValue || decimal.Truncate(h.BrokerQuantity) != h.BrokerQuantity ||
                decimal.Abs(h.ReconstructedBookValue - p.Cost) > .01m)
                blocks.Add("The reconciliation totals do not reproduce the cumulative order history.");
        }
    }
    internal static DateOnly TorontoDate(DateTimeOffset at) => DateOnly.FromDateTime(Core.Trader.PaperTradingMonitor.ToToronto(at.UtcDateTime));
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
