#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Core.Runtime;

internal enum DelphiLiveEvaluationSaveStage
{ Opening, PolicyRead, ObservedClock, PolicyAssignment, BatchStart, ApplicationLock, Lease, Policy, Epoch, Evidence, Checkpoint, Insert, Commit }

/// <summary>Private bounded failure metadata; never provider bodies, messages, SQL or account data.</summary>
internal static class DelphiLiveFailureDiagnostics
{
    private static readonly object Gate = new();
    private static readonly Queue<(string File, string Entry)> Pending = new();
    private static bool writing;
    private const int MaximumPending = 64;
    private const int MaximumEntryCharacters = 16 * 1024;
    private const long MaximumFileBytes = 512 * 1024;

    internal static string Describe(Exception exception) => JsonSerializer.Serialize(new
    {
        CheckedUtc = DateTime.UtcNow,
        ExceptionType = exception.GetType().FullName,
        exception.HResult,
        SqlErrorNumbers = exception is SqlException sql ? sql.Errors.Cast<SqlError>().Select(e => e.Number).ToArray() : Array.Empty<int>(),
        CoreFrames = new StackTrace(exception, true).GetFrames()
            .Where(frame => frame.GetMethod()?.DeclaringType?.Assembly == typeof(DelphiLiveFailureDiagnostics).Assembly)
            .Take(8).Select(frame => new
            {
                Method = frame.GetMethod()?.DeclaringType?.FullName + "." + frame.GetMethod()?.Name,
                Line = frame.GetFileLineNumber()
            }).ToArray()
    });

    internal static Task RecordAsync(Exception exception)
    {
        try { Enqueue("delphi-live-failures.jsonl", Describe(exception)); }
        catch { /* Diagnosis must never replace the original failure or relax recovery. */ }
        return Task.CompletedTask;
    }

    internal static DelphiLiveEvaluationSaveStage? ReadEvaluationStage(string message) => message switch
    {
        "EvaluationSave:ApplicationLock" => DelphiLiveEvaluationSaveStage.ApplicationLock,
        "EvaluationSave:Lease" => DelphiLiveEvaluationSaveStage.Lease,
        "EvaluationSave:Policy" => DelphiLiveEvaluationSaveStage.Policy,
        "EvaluationSave:Epoch" => DelphiLiveEvaluationSaveStage.Epoch,
        "EvaluationSave:Evidence" => DelphiLiveEvaluationSaveStage.Evidence,
        "EvaluationSave:Checkpoint" => DelphiLiveEvaluationSaveStage.Checkpoint,
        "EvaluationSave:Insert" => DelphiLiveEvaluationSaveStage.Insert,
        "EvaluationSave:Commit" => DelphiLiveEvaluationSaveStage.Commit,
        _ => null
    };

    internal static string DescribeEvaluationSave(int attempt, DelphiLiveEvaluationSaveStage stage,
        TimeSpan stageElapsed, TimeSpan attemptElapsed, Exception error, bool retry) => JsonSerializer.Serialize(new
        {
            CheckedUtc = DateTime.UtcNow, Attempt = attempt, Stage = Enum.IsDefined(stage) ? stage.ToString() : "Unknown",
            StageElapsedMs = stageElapsed.TotalMilliseconds, AttemptElapsedMs = attemptElapsed.TotalMilliseconds,
            ExceptionType = error.GetType().FullName,
            SqlErrorNumbers = error is SqlException sql ? sql.Errors.Cast<SqlError>().Select(e => e.Number).ToArray() : [],
            WillRetrySameEvaluation = retry
        });

    internal static Task RecordEvaluationSaveAsync(int attempt, DelphiLiveEvaluationSaveStage stage,
        TimeSpan stageElapsed, TimeSpan attemptElapsed, Exception error, bool retry)
    {
        try { Enqueue("delphi-live-evaluation-saves.jsonl", DescribeEvaluationSave(attempt, stage, stageElapsed, attemptElapsed, error, retry)); }
        catch { /* Never replace the original save outcome. */ }
        return Task.CompletedTask;
    }

    // Bounded, best-effort logging cannot hold the evaluation or its retry behind disk I/O.
    private static void Enqueue(string file, string entry)
    {
        if (entry.Length > MaximumEntryCharacters) return;
        lock (Gate)
        {
            if (Pending.Count == MaximumPending) Pending.Dequeue();
            Pending.Enqueue((file, entry));
            if (writing) return;
            writing = true; _ = Task.Run(DrainAsync);
        }
    }
    private static async Task DrainAsync()
    {
        while (true)
        {
            (string File, string Entry) item;
            lock (Gate)
            {
                if (!Pending.TryDequeue(out item)) { writing = false; return; }
            }
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Diagnostics");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, item.File);
                if (File.Exists(path) && new FileInfo(path).Length >= MaximumFileBytes)
                    File.Move(path, path + ".previous", overwrite: true);
                await File.AppendAllTextAsync(path, item.Entry + Environment.NewLine);
            }
            catch { /* Diagnostics are optional; later entries may still succeed. */ }
        }
    }
}
