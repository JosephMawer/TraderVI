#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text.RegularExpressions;

namespace Core.Db;

/// <summary>Called only after SQL checksum verification and a hash-verified copy succeed.</summary>
public static class TraderDbBackupRetention
{
    public const int BackupsToKeep = 2;
    private static readonly Regex Name = new(@"\ATraderDB_FULL_(\d{8}_\d{6}_\d{3})\.bak\z", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Apply(string verifiedStagingFile, string verifiedDestinationFile,
        long verifiedSize, IReadOnlySet<string> completedFileNames, Action<string>? report = null)
    {
        var plans = Preview(verifiedStagingFile, verifiedDestinationFile, verifiedSize, completedFileNames);
        var removed = new List<string>();
        foreach (var file in plans)
        {
            RequireVerifiedFile(Path.GetFullPath(verifiedStagingFile), verifiedSize);
            RequireVerifiedFile(Path.GetFullPath(verifiedDestinationFile), verifiedSize);
            RequireOrdinaryPath(file);
            File.Delete(file);
            removed.Add(file);
            report?.Invoke($"Backup retention removed: {file}");
        }
        return removed;
    }

    public static IReadOnlyList<string> Preview(string verifiedStagingFile, string verifiedDestinationFile,
        long verifiedSize, IReadOnlySet<string> completedFileNames)
    {
        var staging = Path.GetFullPath(verifiedStagingFile);
        var destination = Path.GetFullPath(verifiedDestinationFile);
        if (string.Equals(Path.GetDirectoryName(staging), Path.GetDirectoryName(destination), StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(staging) != Path.GetFileName(destination) || verifiedSize <= 0)
            throw new InvalidOperationException("Retention requires distinct directories and matching verified backup names.");
        if (!completedFileNames.Contains(Path.GetFileName(staging)))
            throw new IOException("SQL backup history does not identify the newly verified backup as completed.");
        // Inspect both locations before any deletion. Never follow directory junctions or file symlinks.
        return Plan(staging, verifiedSize, completedFileNames).Concat(Plan(destination, verifiedSize, completedFileNames)).ToArray();
    }

    private static string[] Plan(string verifiedFile, long size, IReadOnlySet<string> completedFileNames)
    {
        RequireVerifiedFile(verifiedFile, size);
        if (!TryTimestamp(Path.GetFileName(verifiedFile), out DateTime cutoff))
            throw new InvalidOperationException("The verified backup name is not a recognized full backup.");
        string directory = Path.GetDirectoryName(verifiedFile)!;
        var candidates = new List<(string Path, DateTime Timestamp)>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.bak", SearchOption.TopDirectoryOnly))
        {
            if (!TryTimestamp(Path.GetFileName(file), out var timestamp)) continue;
            if (!completedFileNames.Contains(Path.GetFileName(file))) continue;
            RequireOrdinaryPath(file);
            // A newer backup might still be running. Neither delete nor count it as a completed backup.
            if (timestamp <= cutoff && new FileInfo(file).Length > 0) candidates.Add((Path.GetFullPath(file), timestamp));
        }
        var ordered = candidates.OrderByDescending(x => x.Timestamp).ToArray();
        if (ordered.Length == 0 || !string.Equals(ordered[0].Path, verifiedFile, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The just-verified backup must head the retention set.");
        return ordered.Skip(BackupsToKeep).Select(x => x.Path).ToArray();
    }

    private static bool TryTimestamp(string name, out DateTime timestamp)
    {
        var match = Name.Match(name);
        timestamp = default;
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd_HHmmss_fff",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }

    private static void RequireVerifiedFile(string path, long size)
    {
        RequireOrdinaryPath(path);
        if (new FileInfo(path).Length != size) throw new IOException("A verified backup changed before retention.");
    }

    private static void RequireOrdinaryPath(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Retention candidate is missing.", path);
        RequireNoRedirect(path, File.GetAttributes(path));
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory != null; directory = directory.Parent)
            RequireNoRedirect(directory.FullName, directory.Attributes);
    }

    // OneDrive marks ordinary cloud files/directories as reparse points too. Allow only cloud tags;
    // symbolic links, junctions and unknown redirectors remain forbidden in the entire path.
    internal static bool IsCloudTag(uint tag) => (tag & 0xffff0fff) == 0x9000001a;
    private static void RequireNoRedirect(string path, FileAttributes attributes)
    {
        if ((attributes & FileAttributes.ReparsePoint) == 0) return;
        if (!OperatingSystem.IsWindows()) throw new IOException("Backup retention will not follow symbolic links.");
        using var handle = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        var buffer = new byte[16384];
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900a8, IntPtr.Zero, 0, buffer, buffer.Length, out int bytes, IntPtr.Zero))
            throw new IOException("Cannot inspect backup reparse point.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (bytes < 8 || !IsCloudTag(BitConverter.ToUInt32(buffer, 0)))
            throw new IOException("Backup retention will not follow a filesystem redirector.");
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputSize,
        byte[] output, int outputSize, out int bytes, IntPtr overlapped);
}
