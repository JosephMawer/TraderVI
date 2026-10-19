using Core.WSTrade.Accounts;
using Core.WSTrade.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrokerReadOnlyChecks;

// No caller-controlled URL, query, account identifier, credential path or output path.
const string outputRoot = @"C:\src\TraderVI\artifacts\read-only-broker";
byte[]? plaintext = null;
try
{
    if (args.Length == 1 && args[0] == "--self-test") return await SelfTests.RunAsync();
    if (args.Length != 0) throw new CheckFailure("ArgumentsRejected");
    CheckPath(outputRoot);
    using var lease = new FileStream(Path.Combine(outputRoot, "running.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var ct = timeout.Token;
    var sessionPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TraderVI", "Wealthsimple", "session.dat");
    byte[] protectedSession = await ReadProtectedAsync(sessionPath, ct);
    var originalHash = SHA256.HashData(protectedSession);
    plaintext = ProtectedData.Unprotect(protectedSession, Encoding.UTF8.GetBytes("TraderVI.Wealthsimple.Session.v1"), DataProtectionScope.CurrentUser);
    if (plaintext.Length > 128 * 1024) throw new CheckFailure("SavedSessionInvalid");
    using var client = WealthsimpleAuthClient.FromReadOnlySession(plaintext,
        new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
    CryptographicOperations.ZeroMemory(plaintext); plaintext = null;
    if (!await client.VerifyReadOnlyAsync(ct)) throw new CheckFailure("SignInRequiredOrVerificationUnavailable");
    var catalog = await client.ReadTfsaAccountsAsync(ct);
    if (catalog.Status != WealthsimpleReadStatus.Success || catalog.Data?.Count != 1)
        throw new CheckFailure("UniqueOwnedTfsaUnavailable");
    var account = catalog.Data[0];
    client.SelectReadOnlyAccount(account);
    var current = await client.ReadTfsaSnapshotAsync(account, ct);
    if (current.Status != WealthsimpleReadStatus.Success || current.Data is null)
        throw new CheckFailure("AccountSnapshot" + current.Status);
    var pending = await client.ReadTfsaPendingOrdersAsync(account, ct);
    if (pending.Status != WealthsimpleReadStatus.Success || pending.Data is null)
        throw new CheckFailure("PendingOrders" + pending.Status);
    var daily = await client.ReadTfsaDailyPerformanceAsync(account, ct);
    if (daily.Status == WealthsimpleReadStatus.NotAuthenticated) throw new CheckFailure("SignInRequired");
    var currentProtectedSession = await ReadProtectedAsync(sessionPath, ct);
    if (!CryptographicOperations.FixedTimeEquals(originalHash, SHA256.HashData(currentProtectedSession)))
        throw new CheckFailure("SavedSessionChanged");
    var capture = Guid.NewGuid();
    var checkedUtc = DateTimeOffset.UtcNow;
    byte[] privateBytes = JsonSerializer.SerializeToUtf8Bytes(new
    {
        SchemaVersion = 1, CaptureId = capture, CheckedUtc = checkedUtc,
        AccountId = account.Id, Snapshot = current.Data,
        // Preserve identities privately for separately authorized consumers; never tokens/headers.
        HoldingIdentities = current.Data.Holdings.Select(h => new { h.Symbol, h.SecurityId }),
        Pending = pending.Data, OrderIdentities = pending.Data.Orders.Select(o => new { o.OrderId, o.SecurityId }),
        DailyStatus = daily.Status.ToString(), Daily = daily.Data
    });
    byte[] encrypted;
    try
    {
        if (privateBytes.Length > 2 * 1024 * 1024) throw new CheckFailure("SnapshotLimitExceeded");
        encrypted = ProtectedData.Protect(privateBytes, Encoding.UTF8.GetBytes("TraderVI.BrokerReadOnlyChecks.Snapshot.v1"), DataProtectionScope.CurrentUser);
    }
    finally { CryptographicOperations.ZeroMemory(privateBytes); }
    CheckPath(outputRoot);
    string snapshotFile = Path.Combine(outputRoot, $"snapshot-{capture:N}.dat");
    using (var output = new FileStream(snapshotFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await output.WriteAsync(encrypted, ct);
    var holdings = current.Data.Holdings.Where(h => h.Quantity > 0).ToArray();
    var report = new
    {
        Success = true, CaptureId = capture, CheckedUtc = checkedUtc, SnapshotRetrievedUtc = current.Data.RetrievedAt,
        PendingRetrievedUtc = pending.Data.RetrievedAt, EligibleAccountCount = 1, HoldingCount = holdings.Length,
        AllPositiveWholeShares = holdings.All(h => h.Quantity == decimal.Truncate(h.Quantity)),
        AllCad = holdings.All(h => h.Value?.Currency == "CAD" && h.BookValue?.Currency == "CAD"),
        PendingOrderCount = pending.Data.Orders.Count, DailyStatus = daily.Status.ToString(),
        DailyPerformanceAvailable = daily.Data?.Amount is not null && daily.Data.Rate.HasValue,
        DailyAsOfUtc = daily.Data?.AsOf, ReadRequests = client.RequestCount, EncryptedSnapshotFile = snapshotFile,
        OrdersSent = 0, TokenRefreshes = 0, SavedCredentialsChanged = false, QualifiedForAutomation = false
    };
    var json = JsonSerializer.Serialize(report);
    using (var output = new FileStream(Path.Combine(outputRoot, $"report-{capture:N}.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await output.WriteAsync(Encoding.UTF8.GetBytes(json), ct);
    Console.WriteLine(json);
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new
    {
        Success = false, CheckedUtc = DateTimeOffset.UtcNow,
        Reason = error is CheckFailure failure ? failure.Reason : error is OperationCanceledException ? "TimedOut"
            : error is CryptographicException ? "SavedSessionUnavailable" : error.GetType().Name,
        OrdersSent = 0, TokenRefreshes = 0, SavedCredentialsChanged = false, QualifiedForAutomation = false
    }));
    return 1;
}
finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }

static async Task<byte[]> ReadProtectedAsync(string path, CancellationToken ct)
{
    CheckPath(path);
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    if (file.Length is <= 0 or > 131072) throw new CheckFailure("SavedSessionInvalid");
    var bytes = new byte[(int)file.Length];
    await file.ReadExactlyAsync(bytes, ct);
    return bytes;
}
static void CheckPath(string path)
{
    for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new CheckFailure("ReparsePathRejected");
}
sealed class CheckFailure(string reason) : Exception("Broker check stopped.") { internal string Reason { get; } = reason; }
