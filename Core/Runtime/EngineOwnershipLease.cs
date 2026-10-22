#nullable enable
using System;
using System.IO;

namespace Core.Runtime;

/// <summary>The OS releases this exclusive ownership handle on orderly exit or process failure.</summary>
public sealed class EngineOwnershipLease : IDisposable
{
    private readonly FileStream handle;
    public EngineOwnershipLease(string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute engine ownership path is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        handle = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public void Dispose() => handle.Dispose();
}
