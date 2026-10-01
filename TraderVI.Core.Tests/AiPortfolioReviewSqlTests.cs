#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Core.Db;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Trader.Risk;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace TraderVI.Core.Tests;

/// <summary>Explicitly authorized native tests: synthetic review rows inside rolled-back transactions only.</summary>
public sealed class AiPortfolioReviewSqlTests
{
    private static (AiPortfolioReviewRepository Repository, AiLearningReview Pending, AiLearningReview Reviewed) Fixture()
    {
        var id = Guid.NewGuid();
        var scope = new AiReviewScheduleScope(Guid.NewGuid(), Guid.NewGuid(), "paper");
        var now = DateTimeOffset.UtcNow;
        var learning = new AiLearningContext(Guid.NewGuid(), Guid.NewGuid(), 1, "paper", now, now.AddHours(1), []);
        var packet = new AiPortfolioPacket(id, 3, now, now.AddMinutes(5), null,
            [new("synthetic", "v1", "DailyShadow", true, "Rollback-only native test", [])],
            [new("managed-account", now, JsonSerializer.Serialize(new { Scope = new AiRiskJournalScope(scope.AccountScopeId, scope.PortfolioId, AiRiskAccountMode.Paper) }))], [], learning);
        var proposal = new AiPortfolioProposal(id, "wait", null, null, "Synthetic SQL rehearsal",
            ["managed-account"], [], "New data", [], [], new(5, "Synthetic timing"));
        return (new(scope), new(id, learning.ConsultationId, packet, null, "Pending", now),
            new(id, learning.ConsultationId, packet, proposal, "Reviewed", now.AddSeconds(1)));
    }

    [NativeAiReviewSqlFact]
    public async Task SavesExactEvidenceAndRetriesWithoutDuplicatesThenRollsBack()
    {
        var f = Fixture();
        await using var c = new SqlConnection(f.Repository.ConnectionString);
        await c.OpenAsync();
        await using (var t = (SqlTransaction)await c.BeginTransactionAsync())
        {
            await f.Repository.SaveAsync(c, t, f.Pending);
            await f.Repository.SaveAsync(c, t, f.Pending);
            await f.Repository.SaveAsync(c, t, f.Reviewed);
            await f.Repository.SaveAsync(c, t, f.Reviewed);
            var rows = (await c.QueryAsync("SELECT Status,RecordJson,Action FROM dbo.AiPortfolioReview WHERE ReviewId=@Id ORDER BY Status;",
                new { Id = f.Pending.ReviewId }, t)).ToArray();
            Assert.Equal(2, rows.Length);
            Assert.Equal(JsonSerializer.Serialize(f.Pending), (string)rows[0].RecordJson);
            Assert.Equal(JsonSerializer.Serialize(f.Reviewed), (string)rows[1].RecordJson);
            Assert.Equal("wait", (string)rows[1].Action);
            Assert.Equal(1, await c.ExecuteScalarAsync<int>("SELECT @@TRANCOUNT;", transaction: t));
            await t.RollbackAsync();
        }
        Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AiPortfolioReview WHERE ReviewId=@Id;", new { Id = f.Pending.ReviewId }));
    }

    [NativeAiReviewSqlFact]
    public async Task RejectsChangedInputIdentityAndTerminalOutcomeWithoutLeavingRows()
    {
        foreach (var defect in new[] { "ChangedRetry", "ChangedPacket", "TerminalConflict", "MissingPending", "ScopeConflict" })
        {
            var f = Fixture();
            await using var c = new SqlConnection(f.Repository.ConnectionString);
            await c.OpenAsync();
            await using (var t = (SqlTransaction)await c.BeginTransactionAsync())
            {
                if (defect != "MissingPending") await f.Repository.SaveAsync(c, t, f.Pending);
                if (defect == "TerminalConflict") await f.Repository.SaveAsync(c, t, f.Reviewed);
                var changed = defect switch
                {
                    "ChangedRetry" => f.Pending with { RecordedUtc = f.Pending.RecordedUtc.AddTicks(1) },
                    "ChangedPacket" => f.Reviewed with { Packet = f.Reviewed.Packet with { Candidates =
                        [f.Reviewed.Packet.Candidates[0] with { Summary = "Changed after admission" }] } },
                    "TerminalConflict" => f.Reviewed with { Status = "Rejected", Proposal = null },
                    _ => f.Reviewed
                };
                var exception = await Assert.ThrowsAsync<SqlException>(async () =>
                {
                    if (defect == "ScopeConflict")
                        await c.ExecuteAsync(AiPortfolioReviewRepository.SaveSql, new {
                            AccountScopeId = Guid.NewGuid(), f.Repository.Scope.PortfolioId, f.Repository.Scope.AccountMode,
                            f.Reviewed.ReviewId, f.Reviewed.Status, f.Reviewed.RecordedUtc, RecordJson = f.Repository.Serialize(f.Reviewed)
                        }, t);
                    else await f.Repository.SaveAsync(c, t, changed);
                });
                Assert.Equal(defect switch { "ChangedRetry" => 51811, "ChangedPacket" => 51813,
                    "TerminalConflict" => 51814, "ScopeConflict" => 51810, _ => 51812 }, exception.Number);
            }
            Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AiPortfolioReview WHERE ReviewId=@Id;", new { Id = f.Pending.ReviewId }));
        }
    }

    [NativeAiReviewSqlFact]
    public async Task ImmutableTriggerRejectsChangesToSyntheticEvidenceAndRollsBack()
    {
        foreach (var operation in new[] { "UPDATE dbo.AiPortfolioReview SET RecordJson=RecordJson WHERE ReviewId=@Id;",
            "DELETE FROM dbo.AiPortfolioReview WHERE ReviewId=@Id;" })
        {
            var f = Fixture();
            await using var c = new SqlConnection(f.Repository.ConnectionString);
            await c.OpenAsync();
            await using (var t = (SqlTransaction)await c.BeginTransactionAsync())
            {
                await f.Repository.SaveAsync(c, t, f.Pending);
                var exception = await Assert.ThrowsAsync<SqlException>(() => c.ExecuteAsync(operation, new { Id = f.Pending.ReviewId }, t));
                Assert.Equal(51815, exception.Number);
            }
            Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AiPortfolioReview WHERE ReviewId=@Id;", new { Id = f.Pending.ReviewId }));
        }
    }

    [NativeAiReviewSqlFact]
    public async Task RecoveredLocalAnswerCanRepairSqlWithoutAModelCall()
    {
        var f = Fixture();
        var path = Path.Combine(Path.GetTempPath(), "tradervi-sql-review-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try
        {
            var savedPath = Path.Combine(path, "synthetic-review.json");
            await File.WriteAllTextAsync(savedPath, JsonSerializer.Serialize(f.Reviewed));
            await using var c = new SqlConnection(f.Repository.ConnectionString);
            await c.OpenAsync();
            await using (var t = (SqlTransaction)await c.BeginTransactionAsync())
            {
                await f.Repository.SaveAsync(c, t, f.Pending);
                var recovered = JsonSerializer.Deserialize<AiLearningReview>(await File.ReadAllTextAsync(savedPath))!;
                await f.Repository.SaveAsync(c, t, recovered);
                await f.Repository.SaveAsync(c, t, recovered);
                Assert.Equal(JsonSerializer.Serialize(f.Reviewed), await c.ExecuteScalarAsync<string>(
                    "SELECT RecordJson FROM dbo.AiPortfolioReview WHERE ReviewId=@Id AND Status='Reviewed';", new { Id = f.Reviewed.ReviewId }, t));
                await t.RollbackAsync();
            }
            Assert.Equal(0, await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.AiPortfolioReview WHERE ReviewId=@Id;", new { Id = f.Pending.ReviewId }));
        }
        finally { Directory.Delete(path, true); }
    }
}

public sealed class NativeAiReviewSqlFactAttribute : FactAttribute
{
    public NativeAiReviewSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("TRADERVI_RUN_AI_REVIEW_SQL") != "1")
            Skip = "Requires explicit authorization: native review-storage rehearsal with synthetic rows and rollback.";
    }
}
