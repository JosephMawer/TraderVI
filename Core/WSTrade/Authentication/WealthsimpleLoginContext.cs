using System;
using System.Linq;

namespace Core.WSTrade.Authentication;

/// <summary>Optional values from a user-completed browser challenge. Never logs or serializes them.</summary>
public sealed class WealthsimpleLoginContext
{
    public WealthsimpleLoginContext(string turnstileToken, string deviceId = null, string sessionId = null)
    {
        TurnstileToken = Validate(turnstileToken);
        DeviceId = Validate(deviceId);
        SessionId = Validate(sessionId);
    }
    internal string TurnstileToken { get; }
    internal string DeviceId { get; }
    internal string SessionId { get; }
    private static string Validate(string value)
    {
        if (value != null && (value.Length == 0 || value.Length > 32 * 1024 || value.Any(c => c < '!' || c > '~')))
            throw new ArgumentException("Invalid authentication context; values are not included in diagnostics.");
        return value;
    }
    public override string ToString() => "Wealthsimple login context (values hidden)";
}
