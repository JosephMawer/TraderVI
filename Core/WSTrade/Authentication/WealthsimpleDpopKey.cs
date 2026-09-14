using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Core.WSTrade.Authentication;

/// <summary>An in-memory P-256 signing key. Private material is never exported or persisted.</summary>
internal sealed class WealthsimpleDpopKey : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    internal string CreateProof(string method, Uri uri, DateTimeOffset now,
        string accessToken = null, string nonce = null)
    {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("DPoP requires an HTTPS destination without user information.");

        var publicKey = key.ExportParameters(false);
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            typ = "dpop+jwt", alg = "ES256",
            jwk = new { kty = "EC", crv = "P-256", x = Encode(publicKey.Q.X), y = Encode(publicKey.Q.Y) }
        }));
        var claims = new Dictionary<string, object>
        {
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["htm"] = method.ToUpperInvariant(),
            ["htu"] = uri.GetLeftPart(UriPartial.Path),
            ["iat"] = now.ToUnixTimeSeconds()
        };
        if (accessToken != null)
            claims["ath"] = Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        if (nonce != null) claims["nonce"] = nonce;
        var input = header + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return input + "." + Encode(signature);
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose() => key.Dispose();
    public override string ToString() => "Wealthsimple DPoP key (private material hidden)";
}
