#nullable enable

using System;
using System.IO;
using System.Threading;

namespace Core.Runtime;

/// <summary>
/// File-local Console alias target for the extracted legacy Delphi orchestration.
/// It preserves existing diagnostic text while letting each host choose a writer.
/// </summary>
internal static class DelphiWorkflowLog
{
    private static readonly AsyncLocal<TextWriter?> Current = new();

    public static IDisposable Use(TextWriter writer)
    {
        TextWriter? previous = Current.Value;
        Current.Value = writer ?? throw new ArgumentNullException(nameof(writer));
        return new Scope(previous);
    }

    public static void WriteLine() => Writer.WriteLine();

    public static void WriteLine(string? value) => Writer.WriteLine(value);

    private static TextWriter Writer => Current.Value ?? TextWriter.Null;

    private sealed class Scope(TextWriter? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
                return;
            Current.Value = previous;
            disposed = true;
        }
    }
}
