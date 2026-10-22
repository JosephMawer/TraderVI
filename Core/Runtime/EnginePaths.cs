using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Core.Runtime;

public static class EnginePaths
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Engine");
    public static string OwnershipPath => Path.Combine(DirectoryPath, "owner.lock");
    public static string PipeName => "TraderVI.Engine.v1." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];
}
