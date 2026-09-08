#nullable enable
using Core.Db;
using Core.Calibration;
using Core.Indicators;
using Core.Indicators.Models;
using Core.ML;
using Core.ML.Engine.Profit;
using Core.Practice;
using Core.Runtime;
using Core.Trader;
using Shouldly;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public class PracticeDatasetTests
{
    private static DelphiHistoricalInputs Inputs(string modelPath)
    {
        var source = PracticeSessionTests.Fixture().Document.Daily;
        return source with { Models = source.Models with { Models = ProfitModelRegistry.All.Select(model => new ProfitModelSnapshot(new ModelRegistryInfo
        {
            ModelId = Guid.NewGuid(), TaskType = model.TaskType, ModelKind = model.ModelKind.ToString(),
            InputSchema = model.TaskType + "_profit", FeatureSet = model.FeatureBuilder.Name + ".XiuDatedV1",
            LookbackBars = model.Lookback, HorizonBars = model.HorizonBars, ThresholdBuy = .6, ZipPath = modelPath
        }, PracticeDataset.Hash(System.Text.Encoding.UTF8.GetBytes("synthetic bytes; not an ML model")))).ToImmutableArray() } };
    }

    [Fact]
    public async Task FullDailyWorkflowUsesBoundedInputsAndBothListsWithoutSqlOrModelLoading()
    {
        var fixture = PracticeSessionTests.Fixture();
        var recommendation = new DateTime(2026, 8, 10);
        var dates = Enumerable.Range(1, 430).Select(i => recommendation.AddDays(-i))
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(260).Order().ToArray();
        var inputs = Inputs(Path.Combine(Path.GetTempPath(), "unused-practice-synthetic-model.zip")) with
        {
            Daily = new() { ["XIU"] = [], ["SPY"] = [], ["ABC"] = [] },
            Membership = [new(recommendation.AddDays(-1), [new() { Symbol = "ABC" }], [], [])],
            Breadth = dates.Select((d, i) => new ADLineEntry { Date = d, Advancers = 120, Decliners = 80,
                DailyPlurality = 40, CumulativeDifferential = i * 40 }).ToList(),
            Obv = new() { ["ABC"] = dates.Select((d, i) => new OBV(d, i * 100_000, 100_000)).ToList() }
        };
        foreach (string symbol in inputs.Daily.Keys)
            inputs.Daily[symbol].AddRange(dates.Select((d, i) => new DailyBar { Date = d, Open = 20 + i * .01f,
                Close = 20 + i * .01f, High = 21 + i * .01f, Low = 19 + i * .01f, Volume = 200_000 }));
        async Task<DelphiWorkflowRunResult> Evaluate(DelphiHistoricalInputs data) => await new DelphiWorkflow(data, fixture.Calendar,
            () => new DateTime(2026, 8, 10, 13, 0, 0, DateTimeKind.Utc))
            { HistoricalEngineFactory = () => new TradeDecisionEngine([]) }.RunAsync(new(CalibrationRunPurpose.ExploratoryReplay,
                recommendation, AvailableCapital: 1000m, SaveToDatabase: false));
        var first = await Evaluate(inputs);
        first.Succeeded.ShouldBeTrue();
        first.EvaluatedPicks.Select(x => x.Lens).ShouldBe(["Continuation", "Breakout"]);
        foreach (var history in inputs.Daily.Values) history.Add(new() { Date = recommendation, Close = 99999 });
        inputs.Breadth.Add(new() { Date = recommendation, DailyPlurality = -100_000 });
        inputs.Obv["ABC"].Add(new(recommendation, -999_999_999, -999_999_999));
        inputs.Membership.Add(new(recommendation, [new() { Symbol = "FUTURE" }], [], []));
        var second = await Evaluate(inputs);
        JsonSerializer.Serialize(second).ShouldBe(JsonSerializer.Serialize(first));
    }

    [Fact]
    public void PackageCopiesPinnedModelsAndDetectsDataAndModelTampering()
    {
        string root = Path.Combine(Path.GetTempPath(), "tradervi-practice-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string model = Path.Combine(root, "fixture.zip");
            File.WriteAllText(model, "synthetic bytes; not an ML model");
            var input = PracticeSessionTests.Fixture().Document with { Daily = Inputs(model) };
            string inputPath = Path.Combine(root, "input.json");
            File.WriteAllText(inputPath, JsonSerializer.Serialize(input, PracticeDataset.Json));
            string package = Path.Combine(root, "package");
            PracticeDataset.Pack(inputPath, package);
            var loaded = PracticeDataset.Load(package);
            loaded.Sessions.Count.ShouldBe(20);
            loaded.Document.Daily.Models.Models.ShouldAllBe(m => m.Registry.ZipPath.StartsWith(package));
            File.WriteAllText(model, "changed original source");
            PracticeDataset.Load(package).Identity.ShouldBe(loaded.Identity);
            string manifestPath = Path.Combine(package, "dataset.json");
            string originalManifest = File.ReadAllText(manifestPath);
            File.AppendAllText(manifestPath, " ");
            Should.Throw<InvalidDataException>(() => PracticeDataset.Load(package)).Message.ShouldContain("dataset checksum");
            File.WriteAllText(manifestPath, originalManifest);
            File.WriteAllText(loaded.Document.Daily.Models.Models[0].Registry.ZipPath, "changed packaged bytes");
            Should.Throw<InvalidDataException>(() => PracticeDataset.Load(package)).Message.ShouldContain("model checksum");
            Should.Throw<IOException>(() => PracticeDataset.Pack(inputPath, package));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FailureRetainsInspectionTraceAndCannotBeLoadedAsSuccessfulPlayback()
    {
        string root = Path.Combine(Path.GetTempPath(), "tradervi-practice-failure-" + Guid.NewGuid().ToString("N"));
        var session = new PracticeSession(PracticeSessionTests.Fixture(), (_, _) => throw new ArithmeticException("Fixture failure"));
        try
        {
            await Should.ThrowAsync<ArithmeticException>(() => session.RunToDirectoryAsync(root));
            File.Exists(Path.Combine(root, "failure.json")).ShouldBeTrue();
            File.Exists(Path.Combine(root, "summary.json")).ShouldBeFalse();
            Should.Throw<InvalidDataException>(() => PracticePlayback.Load(Path.Combine(root, "frames.ndjson")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PlaybackRoundTripsAndRejectsForeignOrOutOfOrderFrames()
    {
        var source = new PracticeSession(PracticeSessionTests.Fixture());
        var first = await source.StepAsync();
        var next = await source.StepAsync();
        string path = Path.GetTempFileName();
        var compact = new JsonSerializerOptions(PracticeDataset.Json) { WriteIndented = false };
        try
        {
            void Write(params PracticeFrame[] frames) => File.WriteAllLines(path, frames.Select(f => JsonSerializer.Serialize(f, compact)));
            Write(first, next);
            PracticePlayback.Load(path).Count.ShouldBe(2);
            Write(next, first);
            Should.Throw<InvalidDataException>(() => PracticePlayback.Load(path));
            Write(first with { Label = "OfficialPaper" });
            Should.Throw<InvalidDataException>(() => PracticePlayback.Load(path));
            Write(first with { Accounts = first.Accounts.Select(a => a with { Code = "OperationalAccount" }).ToArray() });
            Should.Throw<InvalidDataException>(() => PracticePlayback.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void MissingSlotsAreReportedWithoutChangingTheDataset()
    {
        var full = PracticeSessionTests.Fixture();
        var partial = PracticeSessionTests.Fixture(b => b.TimestampUtc.Minute == 0 ? null : b);
        PracticeCoverage.Inspect(full).ShouldAllBe(c => c.MissingSlots == 0 && c.InvalidRows == 0);
        var coverage = PracticeCoverage.Inspect(partial);
        coverage.ShouldContain(c => c.MissingSlots > 0);
        partial.Sessions.ShouldBe(full.Sessions);
    }

    [Fact]
    public async Task CompletedOfflineRunWritesPlayableFramesAndFourSeparateSummaries()
    {
        string root = Path.Combine(Path.GetTempPath(), "tradervi-practice-complete-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new PracticeSession(PracticeSessionTests.Fixture(noBars: true));
            await session.RunToDirectoryAsync(root);
            var frames = PracticePlayback.Load(Path.Combine(root, "frames.ndjson"));
            frames.Count.ShouldBe(20 * 397); // pre-open daily frame plus every minute 09:30–16:05, inclusive
            frames[^1].Session.ShouldBe(new DateOnly(2026, 9, 4));
            frames[^1].Accounts.ShouldAllBe(a => a.Cash == 1000m && a.NetAssetValue == 1000m);
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "summary.json")));
            summary.RootElement.GetProperty("Sessions").GetInt32().ShouldBe(20);
            summary.RootElement.GetProperty("Accounts").GetArrayLength().ShouldBe(4);
            File.Exists(Path.Combine(root, "failure.json")).ShouldBeFalse();
            File.Exists(Path.Combine(root, "coverage.json")).ShouldBeTrue();
            using var checkpoint = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "checkpoint.json")));
            checkpoint.RootElement.GetProperty("Completed").GetBoolean().ShouldBeTrue();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
