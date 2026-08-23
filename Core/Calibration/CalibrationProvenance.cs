using Core.Db;
using Core.ML.Engine.Profit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Core.Calibration;

public static class CalibrationProvenance
{
    public static CodeProvenance ResolveCode(string? startDirectory = null)
    {
        string? explicitVersion = Environment.GetEnvironmentVariable("TRADERVI_CODE_VERSION");
        string? explicitState = Environment.GetEnvironmentVariable("TRADERVI_WORKING_TREE_STATE");
        string directory = startDirectory ?? AppContext.BaseDirectory;

        string? root = FindRepositoryRoot(directory) ?? FindRepositoryRoot(Environment.CurrentDirectory);
        string commit = explicitVersion ?? RunGit(root, "rev-parse HEAD") ?? "unavailable";
        string state = explicitState ?? ResolveWorkingTreeState(root);
        string source = explicitVersion is null ? (root is null ? "Unavailable" : "Git") : "Environment";
        return new CodeProvenance(commit.Trim(), source, NormalizeState(state));
    }

    public static async Task<IReadOnlyList<ModelArtifactProvenance>> ResolveLoadedModelsAsync()
    {
        var enabled = await new ModelRegistryRepository().GetEnabledModels();
        var allowed = ProfitModelRegistry.All.Select(x => x.TaskType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ModelArtifactProvenance>();

        foreach (var model in enabled)
        {
            if (!allowed.Contains(model.TaskType) || !seen.Add(model.TaskType) || !File.Exists(model.ZipPath))
                continue;

            await using var stream = File.OpenRead(model.ZipPath);
            string hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            result.Add(new ModelArtifactProvenance(
                model.ModelId,
                model.TaskType,
                model.ModelKind,
                model.InputSchema,
                model.FeatureSet,
                model.TrainedFromUtc,
                model.TrainedToUtc,
                hash));
        }

        return result;
    }

    private static string? FindRepositoryRoot(string? start)
    {
        if (string.IsNullOrWhiteSpace(start)) return null;
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    private static string ResolveWorkingTreeState(string? root)
    {
        if (root is null) return "Unknown";
        string? status = RunGit(root, "status --porcelain");
        return status is null ? "Unknown" : (status.Length == 0 ? "Clean" : "Dirty");
    }

    private static string? RunGit(string? root, string arguments)
    {
        if (root is null) return null;
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return null;
            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeState(string state) => state.Trim().ToLowerInvariant() switch
    {
        "clean" => "Clean",
        "dirty" => "Dirty",
        _ => "Unknown"
    };
}
