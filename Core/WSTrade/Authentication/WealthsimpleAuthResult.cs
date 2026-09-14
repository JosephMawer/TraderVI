using System;

namespace Core.WSTrade.Authentication;

public enum WealthsimpleAuthStatus
{
    Authenticated,
    OtpRequired,
    InvalidOtp,
    BrowserInteractionRequired,
    Rejected,
    RateLimited,
    ServiceUnavailable,
    InvalidResponse,
    TransportFailure,
    NotAuthenticated
}

/// <summary>Only non-sensitive, locally classified information may leave the auth boundary.</summary>
public sealed record WealthsimpleAuthResult(
    WealthsimpleAuthStatus Status,
    int? HttpStatus = null,
    string OtpMethod = null,
    DateTimeOffset? ExpiresAt = null,
    bool CanRefresh = false);
