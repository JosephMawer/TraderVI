#nullable enable

using Core.Db;
using Core.Trader.DelphiLive;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace TraderVI.Core.Tests;

public sealed class DelphiLiveSessionPersistenceTests
{
    private static readonly DateOnly Date = new(2026, 9, 8);
    private static readonly DateTime Open = new(2026, 9, 8, 13, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void PolicyCodec_LoadsTheActualInactiveMigrationSeedWithoutDefaults()
    {
        string json = SeedJson();
        DelphiLivePolicyDefinition policy = DelphiLivePolicyStorage.Read(Identity(), json, Hash(json));
        policy.PolicyVersionId.ShouldBe(DelphiLiveIdentities.PolicyVersionId);
        policy.SelectedRawMoveThreshold.ShouldBe(0.25m);
        policy.SelectedExcessMoveThreshold.ShouldBe(0.05m);
        policy.SelectedRulerSessions.ShouldBe(10);
        policy.MaximumHoldings.ShouldBe(5);
        policy.EntryCutoff.ShouldBe(new TimeOnly(15, 45));
        policy.PrimaryExitReasonOrder.ShouldBe(DelphiLivePolicyDefinition.Version1.PrimaryExitReasonOrder);
    }

    [Fact]
    public void PolicyCodec_RejectsCorruptionIdentityOverridesUnknownFieldsAndMissingSettings()
    {
        string json = SeedJson();
        Should.Throw<InvalidOperationException>(() => DelphiLivePolicyStorage.Read(Identity(), json + " ", Hash(json)));
        foreach (string name in new[] { "selectedRawMoveThreshold", "selectedRulerSessions", "quoteAttemptWindow", "entryCutoff" })
        {
            var node = JsonNode.Parse(json)!.AsObject();
            node.Remove(name);
            string missing = node.ToJsonString();
            Should.Throw<JsonException>(() => DelphiLivePolicyStorage.Read(Identity(), missing, Hash(missing)));
        }
        foreach (string addition in new[] { ",\"unknownSetting\":1", ",\"policyVersionId\":\"00000000-0000-0000-0000-000000000001\"", ",\"selectedRulerSessions\":10" })
        {
            string invalid = json[..^1] + addition + "}";
            Should.Throw<JsonException>(() => DelphiLivePolicyStorage.Read(Identity(), invalid, Hash(invalid)));
        }
        Should.Throw<ArgumentException>(() => DelphiLivePolicyStorage.Read(Identity(), json, new byte[31]));
    }

    [Fact]
    public void CompleteEvaluation_RoundTripsItsCurrentFactsRollingStateAndDerivedDecisions()
    {
        DelphiLiveEvaluationInput input = Input();
        DelphiLiveEvaluationResult result = DelphiLiveEvaluationEngine.Evaluate(input);
        var stored = new DelphiLiveStoredEvaluation(input, result, 2);
        string json = DelphiLiveLedgerJson.Serialize(stored);
        var restored = DelphiLiveLedgerJson.Deserialize<DelphiLiveStoredEvaluation>(json);

        restored.Input.Stock.Bars.Length.ShouldBe(5);
        restored.Input.Stock.Bars[^1].ObservationId.ShouldBe(input.Stock.Bars[^1].ObservationId);
        restored.Input.Stock.Bars[^1].ReceivedUtc.Kind.ShouldBe(DateTimeKind.Utc);
        restored.Input.Stock.OperationalContinuityStartUtc.ShouldBe(Open);
        restored.Input.DailySetup!.BestSelectedSourceRank.ShouldBe(1);
        restored.Result.RankCandidate!.Symbol.ShouldBe("ABC");
        restored.Result.RawValues.ShouldContainKey("PreviousCloseXiuReturn");
        restored.Result.NextState.Confidence.ShouldBe(result.NextState.Confidence);
        restored.Result.Counterfactuals.Length.ShouldBe(result.Counterfactuals.Length);
        JsonElement.DeepEquals(JsonDocument.Parse(json).RootElement,
            JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(restored)).RootElement).ShouldBeTrue();
    }

    [Fact]
    public void LeanDiagnosticProjection_RoundTripsTheActualStoredJsonPathsAndSafetyInput()
    {
        var input = Input();
        var result = DelphiLiveEvaluationEngine.Evaluate(input);
        var stored = new DelphiLiveStoredEvaluation(input, result, 2);
        using var inputJson = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(input));
        using var resultJson = JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(result));
        using var table = new DataTable();
        var values = new List<object>
        {
            input.EvaluationId, input.SessionId, input.Stock.Symbol, Date.ToDateTime(TimeOnly.MinValue),
            DateTime.SpecifyKind(input.BarEndUtc, DateTimeKind.Unspecified), result.ObservationIsValid,
            result.FamiliesMature, result.ConfirmedLiveEligible
        };
        // Project the exact paths embedded in the production SQL against the
        // complete serialized envelopes, then deserialize through its row reader.
        foreach (Match match in Regex.Matches(DelphiLiveExperimentRepository.ChampionDiagnosticSql,
            @"JSON_QUERY\(e\.(InputJson|ResultJson),'(\$\.[^']+)'\)"))
        {
            var node = match.Groups[1].Value == "InputJson" ? inputJson.RootElement : resultJson.RootElement;
            foreach (string part in match.Groups[2].Value[2..].Split('.')) node = node.GetProperty(part);
            values.Add(node.GetRawText());
        }
        values.Count.ShouldBe(19);
        foreach (object value in values) table.Columns.Add("Column" + table.Columns.Count, value.GetType());
        table.Rows.Add(values.ToArray());
        using var reader = table.CreateDataReader();
        reader.Read().ShouldBeTrue();
        var projected = DelphiLiveExperimentRepository.ReadDiagnosticProjection(reader);
        projected.BarEndUtc.Kind.ShouldBe(DateTimeKind.Utc);
        projected.SafetyInput.CompletedBarOpen.ShouldBe(result.SafetyInput.CompletedBarOpen);
        projected.SafetyInput.CompletedBarClose.ShouldBe(result.SafetyInput.CompletedBarClose);
        JsonElement.DeepEquals(JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(projected)).RootElement,
            JsonDocument.Parse(DelphiLiveLedgerJson.Serialize(DelphiLiveDiagnosticEvaluation.FromStored(stored))).RootElement)
            .ShouldBeTrue();
        DelphiLiveExperimentRepository.ChampionDiagnosticSql.ShouldNotContain("$.stock.bars");
        DelphiLiveExperimentRepository.ChampionDiagnosticSql.ShouldContain("p.RoleSlot=0 AND p.PolicyRole=N'OperationalChampion'");
    }

    [Fact]
    public async Task DiagnosticReaders_ImplementProductionInterfaceAndRejectUnboundedRangesBeforeSql()
    {
        IDelphiLiveDiagnosticSource source = new DelphiLiveExperimentRepository();
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => source.ReadChampionEvaluationsAsync(Date, Date.AddDays(366)));
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => source.ReadPortfolioHistoryAsync(Date, Date.AddDays(-1)));
        DelphiLiveExperimentRepository.ValidateDiagnosticRange(Date, Date.AddDays(365));
        string sql = DelphiLiveExperimentRepository.PortfolioHistorySql;
        sql.ShouldContain("g.EndExclusiveTradingDate>@From");
        sql.ShouldContain("FROM dbo.DelphiLivePortfolioRevision r");
        sql.ShouldContain("r.PersistedUtc<DATEADD(DAY,1,CAST(@Through AS DATETIME2))");
        sql.ShouldContain("r.PersistedUtc<CAST(g.EndExclusiveTradingDate AS DATETIME2)");
        sql.ShouldContain("ORDER BY r.Revision DESC");
    }

    [Fact]
    public void SessionContext_RoundTripsFrozenAttributionBaselinesAndObservationMembership()
    {
        var input = Input();
        var policy = input.Policy;
        var run = Guid.NewGuid();
        var strategy = Guid.NewGuid();
        var candidateId = input.DailySetup!.CandidateId;
        var candidate = new DelphiLiveFrozenCandidate(candidateId,"ABC",0.7m,"{}",
            [new(Guid.NewGuid(),candidateId,"Continuation",true,true,1,0.7m,null,"[]")]);
        var context = new DelphiLiveSessionContext(
            new(input.SessionId,Date,run,strategy,"FrozenOfficialRun",Open,["ABC","XIU"]),
            new(Date,Open,Open.AddHours(6.5)),
            [new(Guid.NewGuid(),policy.PolicyVersionId,DelphiLivePolicyRole.OperationalChampion,Date)],
            new Dictionary<Guid,DelphiLivePolicyDefinition> { [policy.PolicyVersionId] = policy },
            new Dictionary<string,DelphiLiveFrozenCandidate> { ["ABC"] = candidate },
            new Dictionary<string,DelphiLiveFrozenBaseline>
            {
                ["ABC"] = new(100m,[new(Guid.NewGuid(),"ABC",Date.AddDays(-4),99m,101m,98m,100m,1000)],
                    [Date.AddDays(-4)],input.VolatilityRulers)
            })
        {
            ObservationMembership = new Dictionary<string,DelphiLiveObservationMembership>
            {
                ["ABC"] = new("ABC",true,false,false,false,false,false,Open.AddMinutes(5),Open.AddHours(6.5))
            }
        };
        var restored = DelphiLiveLedgerJson.Deserialize<DelphiLiveSessionContext>(DelphiLiveLedgerJson.Serialize(context));
        restored.Candidates["ABC"].BestSourceRank.ShouldBe(1);
        restored.Baselines["ABC"].Bars.Single().SessionDate.ShouldBe(Date.AddDays(-4));
        restored.ObservationMembership["ABC"].IsFrozenDailyCandidate.ShouldBeTrue();
        restored.Policies[policy.PolicyVersionId].Validate().SelectedRulerSessions.ShouldBe(10);
    }

    [Fact]
    public void ObservationPlanning_PreservesOnlyOwnPolicyCarryAndNeverGrantsRealAuthority()
    {
        var portfolio = Portfolio();
        Guid soldId = Guid.NewGuid(), heldId = Guid.NewGuid(), oldId = Guid.NewGuid();
        portfolio = portfolio with { Positions =
        [
            new(soldId,"SOLD",10,10m,Open.AddDays(-1),Guid.NewGuid(),"{}",DelphiLiveProfitProtectionState.Open(soldId,10m),Open.AddMinutes(20)),
            new(heldId,"HELD",10,10m,Open.AddDays(-1),Guid.NewGuid(),"{}",DelphiLiveProfitProtectionState.Open(heldId,10m)),
            new(oldId,"OLD",10,10m,Open.AddDays(-2),Guid.NewGuid(),"{}",DelphiLiveProfitProtectionState.Open(oldId,10m),Open.AddDays(-1))
        ] };
        var observed = new[] { new DelphiLiveObservedHolding(" real ","Real",Guid.NewGuid(),true) };
        var plans = DelphiLiveSessionRepository.PlanObservationSources(observed,[portfolio],Open,Open.AddHours(6.5));
        plans.Select(x=>x.Symbol).ShouldBe(["HELD","REAL","SOLD"]);
        plans.Single(x=>x.Symbol=="REAL").IsTrackedHolding.ShouldBeTrue();
        plans.Single(x=>x.Symbol=="REAL").IsDelphiLiveHolding.ShouldBeFalse();
        plans.Single(x=>x.Symbol=="SOLD").IsSessionCarryCandidate.ShouldBeTrue();
        plans.Single(x=>x.Symbol=="SOLD").IsDelphiLiveHolding.ShouldBeFalse();
        plans.Single(x=>x.Symbol=="HELD").IsDelphiLiveHolding.ShouldBeTrue();
    }

    [Fact]
    public void EvaluationEnvelope_RejectsMissingDurabilityAndReusedIdentityBeforeIo()
    {
        var input = Input();
        var result = DelphiLiveEvaluationEngine.Evaluate(input);
        var lease = new DelphiLiveLease(Guid.NewGuid(),"host",2,Open,Open.AddDays(1));
        DelphiLiveEvaluationRepository.ValidateEnvelope(input,result,1,lease);
        Should.Throw<ArgumentException>(() => DelphiLiveEvaluationRepository.ValidateEnvelope(
            input with { ExactPairPersistedOnTime=false },result,1,lease));
        Should.Throw<ArgumentException>(() => DelphiLiveEvaluationRepository.ValidateEnvelope(
            input,result with { EvaluationId=Guid.NewGuid() },1,lease));
        Should.Throw<ArgumentException>(() => DelphiLiveEvaluationRepository.ValidateEnvelope(input,result,0,lease));
    }

    [Fact]
    public void SessionAndEvaluationSql_PreservePointInTimeIdentityAndExactDurability()
    {
        string session = Read("Core/Db/DelphiLiveSessionRepository.cs");
        session.ShouldContain("CreatedUtc<=@Open");
        session.ShouldContain("ORDER BY CreatedUtc DESC,StartedUtc DESC,RunId");
        session.ShouldContain("CreatedAt<=@Cutoff");
        session.ShouldContain("bounds.OpenUtc, now, symbols");
        string sync = Read("Core/Db/DelphiLiveSessionRepository.ObservationSet.cs");
        sync.ShouldContain("before the cycle expected set is frozen");
        sync.ShouldNotContain("UPDATE dbo.DelphiLiveFrozenCandidate",Case.Insensitive);
        sync.ShouldNotContain("DELETE ",Case.Insensitive);
        string evaluation = Read("Core/Db/DelphiLiveEvaluationRepository.cs");
        evaluation.ShouldContain("EpochNumber=@Epoch");
        evaluation.ShouldContain("s.EvidenceBarId=@Evidence");
        evaluation.ShouldContain("s.OperationallyUsable=1 AND x.OperationallyUsable=1");
        evaluation.ShouldContain("InputJson=@Input AND ResultJson=@Result");
        evaluation.ShouldNotContain("UPDATE dbo.DelphiLiveEvaluation",Case.Insensitive);
    }

    private static DelphiLiveEvaluationInput Input() => new()
    {
        EvaluationId=Guid.NewGuid(),SessionId=Guid.NewGuid(),BarEndUtc=Open.AddMinutes(25),EvaluatedUtc=Open.AddMinutes(27),
        Stock=Series("ABC",true),Xiu=Series("XIU",false),Policy=DelphiLivePolicyDefinition.Version1,
        PreviousState=DelphiLiveEvaluationState.Initial(true),ExactPairPersistedOnTime=true,
        VolatilityRulers=new(Ruler(5),Ruler(10),Ruler(14),Ruler(20)),PreviousStockSessionClose=100m,PreviousXiuSessionClose=100m,
        DailySetup=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),0.7m,
            [new(DelphiLiveSourceLens.Continuation,true,true,1,0.7m,"Reasons","[]")])
    };
    private static DelphiLiveFiveMinuteSeries Series(string symbol,bool rising) => new(symbol,Date,Open,Open,
        Enumerable.Range(0,5).Select(i=>new DelphiLiveFiveMinuteBar(Guid.NewGuid(),symbol,Date,
            Open.AddMinutes(i*5),Open.AddMinutes((i+1)*5),100m+(rising?i:0),101.1m+(rising?i:0),
            99.9m+(rising?i:0),100m+(rising?i+1:0),100,Open.AddMinutes((i+1)*5+2),"TMXMoney",1,
            DelphiLiveEvidenceDisposition.OperationalOnTime)));
    private static DelphiLiveTrueRangeRulerMeasurement Ruler(int sessions) =>
        new(sessions,Date.AddDays(-4),DelphiLiveScalarMeasurement.Available(0.04m));
    private static DelphiLivePortfolioSnapshot Portfolio() => DelphiLiveLedgerIntegrity.Create(new(
        Guid.NewGuid(),Guid.NewGuid(),DelphiLiveIdentities.PolicyVersionId,"OperationalChampion",null,10_000m,"CAD",
        Date,Open,Open.AddDays(-1),"Test","Explicit test capital"));
    private static DelphiLiveStoredPolicyIdentity Identity()
    {
        var p=DelphiLivePolicyDefinition.Version1;
        return new(p.PolicyVersionId,p.PolicyDefinitionName,p.PolicyDefinitionSchemaVersion,p.EvaluatorVersion,
            p.CollectorVersion,p.CollectorSourceContractVersion,p.DecisionDossierVersion,p.DecisionDossierSchemaVersion,
            p.QuoteFillVersion,p.ShadowPortfolioVersion,p.ResearchOutcomeVersion,p.RankingDiagnosticVersion,p.PromotionProtocolVersion);
    }
    private static string SeedJson() => Regex.Match(Read("TraderDB/Migrations/20260905_022_AddDelphiLiveIdentityAndCollectionFoundation.sql"),
        "DECLARE @SettingsJson NVARCHAR\\(MAX\\) = N'([^']+)'").Groups[1].Value;
    private static byte[] Hash(string json) => SHA256.HashData(Encoding.UTF8.GetBytes(json));
    private static string Read(string relative)
    {
        DirectoryInfo? directory=new(AppContext.BaseDirectory);
        while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"TraderVI.sln"))) directory=directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException(),relative));
    }
}
