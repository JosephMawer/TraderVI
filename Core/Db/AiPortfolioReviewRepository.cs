#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Core.Oracle;
using Core.Oracle.Learning;
using Core.Trader.Risk;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Core.Db;

/// <summary>Append-only, queryable review evidence. No schema creation, assignment or order changes.</summary>
public sealed class AiPortfolioReviewRepository : SQLBase, IAiReviewStore
{
    public AiReviewScheduleScope Scope { get; }
    public AiPortfolioReviewRepository(AiReviewScheduleScope scope)
    {
        if (scope.AccountScopeId == Guid.Empty || scope.PortfolioId == Guid.Empty || scope.AccountMode != "paper")
            throw new ArgumentException("An identified paper review scope is required.");
        Scope = scope;
    }

    public async Task SaveAsync(AiLearningReview review, CancellationToken ct = default)
    {
        var json = Serialize(review);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(SaveSql, new
        {
            Scope.AccountScopeId, Scope.PortfolioId, Scope.AccountMode,
            review.ReviewId, review.Status, review.RecordedUtc, RecordJson = json
        }, cancellationToken: ct));
    }

    internal string Serialize(AiLearningReview review)
    {
        if (review.ReviewId == Guid.Empty || review.ReviewId != review.Packet.PacketId ||
            review.ConsultationId != review.Packet.Learning?.ConsultationId ||
            review.Packet.Learning.AccountMode != Scope.AccountMode ||
            review.Status is not ("Pending" or "Reviewed" or "Rejected") ||
            (review.Status == "Reviewed") != (review.Proposal is not null) || review.RecordedUtc < review.Packet.AsOfUtc)
            throw new InvalidDataException("Review identity, learning, disposition or timestamp is invalid.");
        if (review.Status != "Rejected") AiPortfolioReviewer.ValidatePacket(review.Packet, review.RecordedUtc);
        // The learning journal can contain multiple accounts. A recovery save must retain the
        // account embedded in the original packet, not relabel it with this repository's scope.
        var managed = review.Packet.Evidence.SingleOrDefault(e => e.Id == "managed-account")
            ?? throw new InvalidDataException("Managed account evidence is required.");
        using var book = JsonDocument.Parse(managed.Description);
        if (!book.RootElement.TryGetProperty("Scope", out var savedScope) ||
            savedScope.Deserialize<AiRiskJournalScope>() != new AiRiskJournalScope(Scope.AccountScopeId, Scope.PortfolioId, AiRiskAccountMode.Paper))
            throw new InvalidDataException("Review evidence belongs to a different account.");
        if (review.Proposal is not null)
            AiPortfolioReviewer.ParseAndValidate(JsonSerializer.Serialize(review.Proposal,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), review.Packet);
        return JsonSerializer.Serialize(review);
    }

    internal const string SaveSql = """
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @existing nvarchar(max), @pending nvarchar(max), @pendingTime datetimeoffset(7);
    -- Lock the complete review's key range, including its not-yet-written terminal row.
    IF EXISTS (SELECT 1 FROM dbo.AiPortfolioReview WITH (UPDLOCK,HOLDLOCK)
        WHERE ReviewId=@ReviewId AND (AccountScopeId<>@AccountScopeId OR PortfolioId<>@PortfolioId OR AccountMode<>@AccountMode))
        THROW 51810,'Review scope conflict.',1;
    SELECT @existing=RecordJson FROM dbo.AiPortfolioReview WITH (UPDLOCK,HOLDLOCK)
        WHERE ReviewId=@ReviewId AND Status=@Status;
    IF @existing IS NOT NULL
    BEGIN
        IF DATALENGTH(@existing)<>DATALENGTH(@RecordJson) OR @existing COLLATE Latin1_General_100_BIN2<>@RecordJson COLLATE Latin1_General_100_BIN2
            THROW 51811,'Review evidence cannot be replaced.',1;
    END
    ELSE
    BEGIN
        SELECT @pending=RecordJson,@pendingTime=RecordedUtc FROM dbo.AiPortfolioReview WITH (UPDLOCK,HOLDLOCK)
            WHERE ReviewId=@ReviewId AND Status='Pending';
        IF @Status<>'Pending' AND (@pending IS NULL OR @RecordedUtc<@pendingTime)
            THROW 51812,'An earlier saved input is required.',1;
        IF @Status<>'Pending' AND (
            DATALENGTH(JSON_QUERY(@pending,'$.Packet'))<>DATALENGTH(JSON_QUERY(@RecordJson,'$.Packet')) OR
            JSON_QUERY(@pending,'$.Packet') COLLATE Latin1_General_100_BIN2<>JSON_QUERY(@RecordJson,'$.Packet') COLLATE Latin1_General_100_BIN2)
            THROW 51813,'A review cannot change its input.',1;
        IF EXISTS (SELECT 1 FROM dbo.AiPortfolioReview WITH (UPDLOCK,HOLDLOCK) WHERE ReviewId=@ReviewId AND Status<>'Pending')
            THROW 51814,'A review already has a terminal disposition.',1;
        INSERT dbo.AiPortfolioReview(ReviewId,Status,AccountScopeId,PortfolioId,AccountMode,RecordedUtc,RecordJson)
            VALUES(@ReviewId,@Status,@AccountScopeId,@PortfolioId,@AccountMode,@RecordedUtc,@RecordJson);
    END
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
""";
}
