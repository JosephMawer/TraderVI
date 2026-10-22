#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Core.Trader.DelphiLive;

/// <summary>Stable ordinal leaves. A change reads/writes only its authenticated binary path.</summary>
public static class DelphiLiveArchiveTree
{
    public static readonly string Empty = new('0', 64);
    public const int MaximumCount = 2_000_000;
    private sealed record Branch(int Version, int Height, string Left, string Right);
    public sealed record Change(int Ordinal, string? Expected, string ContentHash);
    public sealed record Result(int Count, string Root, IReadOnlyDictionary<string, string> Nodes);

    // These records never change after append. A chain needs no extra stored nodes per quote/fill/mark.
    public static bool UsesAppendChain(string collection) => collection is "fills" or "quotes" or "marks";
    public static string AppendChain(string root, int count, string leaf)
    {
        RequireRoot(count, root); RequireCount(checked(count + 1)); RequireHash(leaf);
        return DelphiLiveSnapshotCodec.Hash("TraderVI.ArchiveChain.1\n" + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + root + "\n" + leaf);
    }
    public static Result BuildCollection(string collection, IReadOnlyList<string> leaves)
    {
        if (!UsesAppendChain(collection)) return Build(leaves);
        string root = Empty;
        for (int i = 0; i < leaves.Count; i++) root = AppendChain(root, i, leaves[i]);
        return new(leaves.Count, root, new Dictionary<string, string>());
    }

    public static Result Build(IReadOnlyList<string> leaves)
    {
        RequireCount(leaves.Count);
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        string Walk(int start, int height)
        {
            if (start >= leaves.Count) return Empty;
            if (height == 0) { RequireHash(leaves[start]); return leaves[start]; }
            return Save(height, Walk(start, height - 1), Walk(start + (1 << (height - 1)), height - 1), nodes);
        }
        return new(leaves.Count, leaves.Count == 0 ? Empty : Walk(0, Height(leaves.Count)), nodes);
    }

    public static async Task<Result> ApplyAsync(int count, string root, IReadOnlyList<Change> changes,
        Func<string, CancellationToken, Task<string?>> read, CancellationToken ct = default)
    {
        RequireRoot(count, root);
        var nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        var touched = new HashSet<int>();
        async Task<string> Replace(string hash, int height, int ordinal, Change change)
        {
            ct.ThrowIfCancellationRequested();
            if (height == 0)
            {
                if (hash != (change.Expected ?? Empty)) throw new InvalidDataException("Archive leaf differs from the verified working state.");
                return change.ContentHash;
            }
            string left = Empty, right = Empty;
            if (hash != Empty)
            {
                string? json = nodes.GetValueOrDefault(hash) ?? await read(hash, ct);
                var branch = Read(hash, height, json); left = branch.Left; right = branch.Right;
            }
            int half = 1 << (height - 1);
            if (ordinal < half) left = await Replace(left, height - 1, ordinal, change);
            else right = await Replace(right, height - 1, ordinal - half, change);
            return Save(height, left, right, nodes);
        }
        foreach (var change in changes)
        {
            RequireHash(change.ContentHash);
            if (!touched.Add(change.Ordinal) || change.Ordinal < 0 || change.Ordinal > count ||
                (change.Ordinal == count) != (change.Expected is null))
                throw new InvalidDataException("Archive changes must have distinct existing ordinals or consecutive new ordinals.");
            if (change.Expected is not null) RequireHash(change.Expected);
            if (change.Ordinal == count)
            {
                RequireCount(checked(count + 1));
                if (count > 0 && Height(count + 1) > Height(count)) root = Save(Height(count + 1), root, Empty, nodes);
                count++;
            }
            root = await Replace(root, Height(count), change.Ordinal, change);
        }
        return new(count, root, nodes);
    }

    public static async Task VerifyLeafAsync(int count, string root, int ordinal, string expected,
        Func<string, CancellationToken, Task<string?>> read, CancellationToken ct = default)
    {
        RequireRoot(count, root); RequireHash(expected);
        if (ordinal < 0 || ordinal >= count) throw new InvalidDataException("Archive ordinal is outside its committed count.");
        for (int height = Height(count); height > 0; height--)
        {
            var branch = Read(root, height, await read(root, ct));
            int half = 1 << (height - 1);
            if (ordinal < half) root = branch.Left;
            else { root = branch.Right; ordinal -= half; }
        }
        if (root != expected) throw new InvalidDataException("Current archive item does not match its committed root.");
    }

    private static Branch Read(string hash, int height, string? json)
    {
        if (json is null || DelphiLiveSnapshotCodec.Hash(json) != hash) throw new InvalidDataException("Archive path is missing or damaged.");
        var branch = JsonSerializer.Deserialize<Branch>(json, DelphiLiveLedgerJson.Options);
        if (branch is null || branch.Version != 1 || branch.Height != height || branch.Left == Empty)
            throw new InvalidDataException("Archive path has an invalid shape.");
        RequireHash(branch.Left); RequireHash(branch.Right, allowEmpty: true);
        return branch;
    }
    private static string Save(int height, string left, string right, Dictionary<string, string> nodes)
    {
        if (left == Empty && right == Empty) return Empty;
        string json = JsonSerializer.Serialize(new Branch(1, height, left, right), DelphiLiveLedgerJson.Options);
        string hash = DelphiLiveSnapshotCodec.Hash(json); nodes[hash] = json; return hash;
    }
    public static void RequireRoot(int count, string root)
    {
        RequireCount(count); RequireHash(root, count == 0);
        if ((count == 0) != (root == Empty)) throw new InvalidDataException("Archive root and count disagree.");
    }
    private static void RequireCount(int count)
    { if (count < 0 || count > MaximumCount) throw new InvalidDataException("Archive count exceeds its supported range."); }
    private static void RequireHash(string hash, bool allowEmpty = false)
    { if (hash.Length != 64 || !hash.All(char.IsAsciiHexDigitUpper) || (!allowEmpty && hash == Empty)) throw new InvalidDataException("Invalid archive fingerprint."); }
    private static int Height(int count) => count <= 1 ? 0 : System.Numerics.BitOperations.Log2((uint)(count - 1)) + 1;
}
