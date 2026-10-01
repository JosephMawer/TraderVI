-- Review-only AI evidence; no account, risk, assignment or order records are changed.
-- Requires separately authorized manual execution after a verified fresh full backup.
-- New table only. An existing object is an error, never a request to replace data.
-- After commit retain the evidence; rollback/correction requires separate review.
:ON ERROR EXIT
USE [TraderDB];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.AiPortfolioReview') IS NOT NULL
    THROW 51800,'AiPortfolioReview already exists; inspect before proceeding.',1;
IF OBJECT_ID(N'dbo.TR_AiPortfolioReview_Immutable') IS NOT NULL
    THROW 51801,'Review trigger already exists; inspect before proceeding.',1;
GO
CREATE TABLE [dbo].[AiPortfolioReview]
(
    [ReviewId] UNIQUEIDENTIFIER NOT NULL,
    [Status] VARCHAR(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [AccountScopeId] UNIQUEIDENTIFIER NOT NULL,
    [PortfolioId] UNIQUEIDENTIFIER NOT NULL,
    [AccountMode] VARCHAR(8) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [RecordedUtc] DATETIMEOFFSET(7) NOT NULL,
    [RecordJson] NVARCHAR(MAX) NOT NULL,
    [Action] AS CONVERT(NVARCHAR(16),JSON_VALUE([RecordJson],'$.Proposal.Action')),
    [SelectedPortfolioId] AS CONVERT(NVARCHAR(256),JSON_VALUE([RecordJson],'$.Proposal.PortfolioId')),
    CONSTRAINT [PK_AiPortfolioReview] PRIMARY KEY ([ReviewId],[Status]),
    CONSTRAINT [CK_AiPortfolioReview_Status] CHECK ([Status] IN ('Pending','Reviewed','Rejected')),
    CONSTRAINT [CK_AiPortfolioReview_Mode] CHECK ([AccountMode]='paper'),
    CONSTRAINT [CK_AiPortfolioReview_Json] CHECK (ISJSON([RecordJson])=1)
);

GO
CREATE TRIGGER [dbo].[TR_AiPortfolioReview_Immutable]
ON [dbo].[AiPortfolioReview]
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51815,'AI review evidence is append-only.',1;
END;

GO
IF OBJECT_ID(N'dbo.AiPortfolioReview',N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.triggers WHERE object_id=OBJECT_ID(N'dbo.TR_AiPortfolioReview_Immutable')
        AND parent_id=OBJECT_ID(N'dbo.AiPortfolioReview') AND is_disabled=0)
    THROW 51802,'AI review storage postcondition failed.',1;
COMMIT TRANSACTION;
