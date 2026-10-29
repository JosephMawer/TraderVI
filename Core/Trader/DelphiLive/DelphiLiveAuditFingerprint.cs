#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Core.Trader.DelphiLive;

public static partial class DelphiLiveSnapshotCodec
{
    // Only used by one guarded audit decoder. Nodes come from its private, immutable decoded
    // content; header bytes are compared exactly. No digest state crosses a pass or restart.
    private sealed class AuditFingerprint(int maximumKeys = 65_536) : IDisposable
    {
        private readonly int keyLimit = maximumKeys is >= 0 and <= 65_536 ? maximumKeys
            : throw new ArgumentOutOfRangeException(nameof(maximumKeys));
        private const int MaximumRawBytes = 1024 * 1024;
        private const int CheckpointInterval = 512;
        private const int MaximumCheckpoints = 128;
        private Key[] previous = [];
        private SortedDictionary<int, IncrementalHash> checkpoints = new();
        internal long LastBytesAppended { get; private set; }

        internal readonly record struct Key(byte[]? Raw, JsonNode? Node)
        {
            internal bool Matches(Key other) => Raw is not null && other.Raw is not null
                ? Raw.AsSpan().SequenceEqual(other.Raw)
                : Raw is null && other.Raw is null && ReferenceEquals(Node, other.Node);

            internal void Write(Stream stream, CanonicalStrings strings)
            {
                if (Raw is not null) { stream.Write(Raw); return; }
                using var writer = new Utf8JsonWriter(stream);
                WriteCanonical(writer, Node, strings);
            }
        }

        internal void Verify(IEnumerable<Key> keys, string expected, CanonicalStrings strings)
        {
            var nextKeys = new List<Key>();
            var nextCheckpoints = new SortedDictionary<int, IncrementalHash>();
            IncrementalHash? hash = null;
            HashStream? stream = null;
            int index = 0, rawBytes = 0;
            bool committed = false;
            try
            {
                foreach (var key in keys)
                {
                    bool samePrefix = hash is null && index < previous.Length && key.Matches(previous[index]);
                    if (nextKeys.Count == index && index < keyLimit &&
                        (key.Raw?.Length ?? 0) <= MaximumRawBytes - rawBytes)
                    {
                        nextKeys.Add(key);
                        rawBytes += key.Raw?.Length ?? 0;
                    }
                    if (samePrefix)
                    {
                        index++;
                        if (checkpoints.TryGetValue(index, out var saved) && nextKeys.Count == index &&
                            nextCheckpoints.Count < MaximumCheckpoints)
                            nextCheckpoints.Add(index, saved.Clone());
                        continue;
                    }
                    EnsureHash();
                    key.Write(stream!, strings);
                    index++;
                    if (index == nextKeys.Count && index % CheckpointInterval == 0 &&
                        nextCheckpoints.Count < MaximumCheckpoints)
                        nextCheckpoints.Add(index, hash!.Clone());
                }
                EnsureHash();
                // Save the exact ending state before GetHashAndReset. It is published only after
                // comparison with this revision's original fingerprint, never on a failed read.
                if (index == nextKeys.Count && !nextCheckpoints.ContainsKey(index) &&
                    nextCheckpoints.Count < MaximumCheckpoints)
                    nextCheckpoints.Add(index, hash!.Clone());
                LastBytesAppended = stream!.BytesWritten;
                if (Convert.ToHexString(hash!.GetHashAndReset()) != expected)
                    throw new InvalidDataException("Audit snapshot fingerprint differs from the original.");
                DisposeCheckpoints(checkpoints);
                previous = nextKeys.ToArray();
                checkpoints = nextCheckpoints;
                committed = true;
            }
            finally
            {
                stream?.Dispose(); hash?.Dispose();
                if (!committed) DisposeCheckpoints(nextCheckpoints);
            }

            void EnsureHash()
            {
                if (hash is not null) return;
                var saved = checkpoints.LastOrDefault(p => p.Key <= index);
                hash = saved.Value?.Clone() ?? IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                stream = new HashStream(hash);
                // A checkpoint may precede the first changed token. Replay that exact remainder;
                // a changed earlier field necessarily discards all later prefix reuse.
                for (int n = saved.Key; n < index; n++) previous[n].Write(stream, strings);
            }
        }

        internal static IEnumerable<Key> Parts(JsonObject root, IReadOnlyDictionary<string, JsonObject[]> collections,
            CanonicalStrings strings)
        {
            bool firstField = true;
            foreach (var field in root.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                string name = Encoding.UTF8.GetString(JsonEncodedText.Encode(field.Key).EncodedUtf8Bytes);
                byte[] prefix = Encoding.UTF8.GetBytes((firstField ? "{" : ",") + "\"" + name + "\":");
                firstField = false;
                if (collections.TryGetValue(field.Key, out var items))
                {
                    yield return new(prefix, null);
                    yield return new("["u8.ToArray(), null);
                    for (int n = 0; n < items.Length; n++)
                    {
                        if (n != 0) yield return new(","u8.ToArray(), null);
                        yield return new(null, items[n]);
                    }
                    yield return new("]"u8.ToArray(), null);
                }
                else
                {
                    using var bytes = new MemoryStream();
                    bytes.Write(prefix);
                    using (var writer = new Utf8JsonWriter(bytes)) WriteCanonical(writer, field.Value, strings);
                    yield return new(bytes.ToArray(), null);
                }
            }
            yield return new(firstField ? "{}"u8.ToArray() : "}"u8.ToArray(), null);
        }

        public void Dispose()
        {
            DisposeCheckpoints(checkpoints); checkpoints.Clear(); previous = [];
        }
        private static void DisposeCheckpoints(SortedDictionary<int, IncrementalHash> values)
        { foreach (var value in values.Values) value.Dispose(); }

        private sealed class HashStream(IncrementalHash hash) : Stream
        {
            internal long BytesWritten { get; private set; }
            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> buffer) { hash.AppendData(buffer); BytesWritten += buffer.Length; }
            public override void Flush() { }
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
