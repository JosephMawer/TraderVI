using Core.WSTrade.Orders;
using System.Security.Cryptography;
using System.Text;

namespace TraderVI.WPF.Services;

public sealed class WindowsManualBuyProtector : IManualBuyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TraderVI.Wealthsimple.ManualOrders.v1");
    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
