#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Core.Trader;
using Core.Trader.DelphiLive;

namespace Core.Practice;

/// <summary>Read-only recorded playback; deliberately has no workflow, market client or database dependency.</summary>
public static class PracticePlayback
{
    public static IReadOnlyList<PracticeFrame> Load(string path)
    {
        if (new FileInfo(path).Length > 512L * 1024 * 1024) throw new InvalidDataException("Playback exceeds the 512 MB viewer limit.");
        var frames = new List<PracticeFrame>();
        var codes = SystemShadowPortfolioDefinition.Version1.Select(x => x.Code).ToHashSet();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var frame = JsonSerializer.Deserialize<PracticeFrame>(line, PracticeDataset.Json) ?? throw new InvalidDataException("Empty practice frame.");
            if (frame.Label != "Practice / Exploratory" || string.IsNullOrWhiteSpace(frame.DatasetIdentity) ||
                frame.SimulatedUtc.Kind != DateTimeKind.Utc || frame.Accounts.Count != 4 ||
                !codes.SetEquals(frame.Accounts.Select(a => a.Code)) || frame.Accounts.Any(a => a.Cash < 0 || a.PortfolioId == Guid.Empty) ||
                frame.Candidates.Any(c => !codes.Contains(c.Account)) ||
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(frame.SimulatedUtc, ReviewedTsxSessionCalendar.Toronto)) != frame.Session ||
                frames.Count > 0 && (frame.DatasetIdentity != frames[0].DatasetIdentity || frame.SimulatedUtc <= frames[^1].SimulatedUtc))
                throw new InvalidDataException("The file is not one chronological Practice / Exploratory playback.");
            frames.Add(frame);
        }
        if (frames.Count == 0) throw new InvalidDataException("No completed practice frames have been recorded.");
        return frames.AsReadOnly();
    }
}
