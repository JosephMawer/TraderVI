#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Runtime;

/// <summary>Private bounded failure metadata; never provider bodies, messages, SQL or account data.</summary>
internal static class DelphiLiveFailureDiagnostics
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

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

    internal static async Task RecordAsync(Exception exception)
    {
        try
        {
            string entry = Describe(exception);
            await Gate.WaitAsync();
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TraderVI", "Diagnostics");
                Directory.CreateDirectory(directory);
                await File.AppendAllTextAsync(Path.Combine(directory, "delphi-live-failures.jsonl"), entry + Environment.NewLine);
            }
            finally { Gate.Release(); }
        }
        catch { /* Diagnosis must never replace the original failure or relax recovery. */ }
    }
}
