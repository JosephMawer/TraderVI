namespace Core.WSTrade.Authentication;

/// <summary>The desktop supplies Windows user-scoped encryption. Never persist the input bytes.</summary>
public interface IWealthsimpleSessionProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
