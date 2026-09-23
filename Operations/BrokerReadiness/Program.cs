using Core.Db;
using Core.Trader.Promotion;
using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Deliberately contains no order, journal writer, token refresh, model or operational host.
// No mode enables trading or modifies SQL. Output omits identities, balances and raw broker responses.
if (args.Length == 2 && args[0] == "strategy-review" && Guid.TryParse(args[1], out var paper))
{
    try
    {
    var review = await new TradingPromotionRepository().ReviewBrokerAsync(paper, TradingStrategyFamily.DailyShadow);
    var storage = await BrokerManagedSessionRepository.ReadStorageAsync();
    Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow,
        review.Strategy.PaperAccountId, review.Strategy.VersionId, review.Strategy.Fingerprint, GateProfile = review.Strategy.GateProfile.ToString(),
        review.Strategy.Strategy.Selection, review.Strategy.Strategy.MaximumHoldings, review.Strategy.Strategy.PolicyVersionId,
        DailyVersion = review.Strategy.Strategy.Daily.StrategyVersionId,
        review.Strategy.Strategy.ImplementationIdentity, review.Blocks, Storage = storage,
        EvidenceLimitations = review.Evidence.Limitations,
        MissingOperationalQualifications = BrokerActivationReview.RequiredChecks,
        QualifiedForAutomation = false, OrdersSubmitted = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
    }
    catch
    {
        Console.Error.WriteLine("The selected strategy or database could not be verified. No approval, migration or order was performed.");
        return 3;
    }
}
if (args.Length != 1 || args[0] != "verify-broker-readonly")
{
    Console.Error.WriteLine("BrokerReadiness strategy-review <paper-id> | verify-broker-readonly. No writes, token refresh or orders.");
    return 2;
}
var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Wealthsimple");
try
{
    // Atomic saved bearer snapshot only. The native app retains sole refresh/write ownership.
    // A changed saved session invalidates this report; this process has no refresh or persistence path.
    var sessionPath = Path.Combine(directory, "session.dat");
    byte[] savedSession = File.ReadAllBytes(sessionPath);
    using var client = WealthsimpleAuthClient.FromProtectedSession(savedSession, new ReadOnlyProtector());
    var auth = await client.VerifyAsync();
    if (auth.Status != WealthsimpleAuthStatus.Authenticated)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow, Authentication = auth.Status.ToString(),
            Block = "A current operator login is required. This verifier does not refresh credentials.", OrdersSubmitted = 0 }));
        return 3;
    }
    var accounts = await client.ReadTfsaAccountsAsync();
    if (accounts.Status != WealthsimpleReadStatus.Success || accounts.Data?.Count != 1)
    {
        Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow, AccountRead = accounts.Status.ToString(),
            Block = "One explicitly identified self-directed TFSA is required; no account is chosen automatically.", OrdersSubmitted = 0 }));
        return 3;
    }
    var account = accounts.Data[0];
    var history = await client.ReadTfsaOrderHistoryAsync(account);
    var snapshot = await client.ReadTfsaSnapshotAsync(account);
    var pending = await client.ReadTfsaPendingOrdersAsync(account);
    var checkpoint = await client.ReadTfsaActivityCheckpointAsync(account);
    var graph = await client.ReadTfsaDayValuesAsync(account);
    if (!CryptographicOperations.FixedTimeEquals(savedSession, File.ReadAllBytes(sessionPath)))
    {
        Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow,
            Block = "The native app replaced the saved login during verification. Repeat the read-only check.", OrdersSubmitted = 0 }));
        return 3;
    }
    bool stable = history.Data?.ActivityFingerprint is { } first && checkpoint.Data?.Fingerprint == first;
    var now = DateTimeOffset.UtcNow;
    var reconciled = stable && history.Data is not null && snapshot.Data is not null && pending.Data is not null
        ? WealthsimpleReconciliation.Compare(history.Data, snapshot.Data, pending.Data, now, unresolvedLocalOrders: false) : null;
    Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = now, History = history.Status.ToString(),
        Holdings = snapshot.Status.ToString(), PendingOrders = pending.Status.ToString(), DayGraph = graph.Status.ToString(),
        StableActivityCheckpoint = stable, ObservedHoldingsMatch = reconciled?.HoldingsMatch,
        DayPointCount = graph.Data?.Points.Length, FirstPointAt = graph.Data?.Points.FirstOrDefault()?.At,
        LastPointAt = graph.Data?.Points.LastOrDefault()?.At,
        LocalOrderJournalVerified = false, CashEffectiveTimingQualified = false, OpeningMethodQualified = false,
        QualifiedForAutomation = false, OrdersSubmitted = 0 }, new JsonSerializerOptions { WriteIndented = true }));
    return reconciled?.HoldingsMatch == true && graph.Status == WealthsimpleReadStatus.Success ? 0 : 3;
}
catch (Exception error) when (error is IOException or System.IO.InvalidDataException)
{
    Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow,
        Block = "A current saved session is unavailable. Sign in with Remember enabled or use the running app's account read controls.",
        BrokerVerificationCompleted = false, OrdersSubmitted = 0 }));
    return 3;
}
catch (Exception error) when (error is UnauthorizedAccessException or CryptographicException or InvalidOperationException or ArgumentException)
{
    Console.WriteLine(JsonSerializer.Serialize(new { CheckedAt = DateTimeOffset.UtcNow,
        Block = "The saved session could not be verified. Reauthenticate through the native app; preserve existing session data.",
        BrokerVerificationCompleted = false, OrdersSubmitted = 0 }));
    return 3;
}

sealed class ReadOnlyProtector : IWealthsimpleSessionProtector
{
    public byte[] Protect(byte[] plaintext) => throw new InvalidOperationException("Read-only verifier cannot save a session.");
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext,
        Encoding.UTF8.GetBytes("TraderVI.Wealthsimple.Session.v1"), DataProtectionScope.CurrentUser);
}
