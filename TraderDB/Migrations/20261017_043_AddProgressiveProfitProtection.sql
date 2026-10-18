-- Prepared only: persist per-position progressive protection alongside the existing Shadow ledger.
-- Preconditions: existing ShadowPosition and ShadowPortfolioEvent tables; new table must be absent.
-- Data effect: one empty additive table; no assignments, activation, orders, or existing stop changes.
-- Recovery: roll back this transaction on failure. After installation preserve state and disable new
-- opt-ins if needed; do not drop earned protection. Any later schema removal needs separate review.
USE [TraderDB];
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.ShadowPosition',N'U') IS NULL OR OBJECT_ID(N'dbo.ShadowPortfolioEvent',N'U') IS NULL
    THROW 51000, 'The existing Shadow ledger is required.', 1;
IF OBJECT_ID(N'dbo.ShadowProgressiveProtection',N'U') IS NOT NULL
    THROW 51000, 'Progressive protection is already installed; inspect rather than replacing it.', 1;
CREATE TABLE dbo.ShadowProgressiveProtection
(
    PositionId UNIQUEIDENTIFIER NOT NULL,
    StateJson NVARCHAR(MAX) NOT NULL,
    UpdatedUtc DATETIME2 NOT NULL CONSTRAINT DF_ShadowProgressiveProtection_UpdatedUtc DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ShadowProgressiveProtection PRIMARY KEY (PositionId),
    CONSTRAINT FK_ShadowProgressiveProtection_Position FOREIGN KEY (PositionId) REFERENCES dbo.ShadowPosition(PositionId),
    CONSTRAINT CK_ShadowProgressiveProtection_Json CHECK (ISJSON(StateJson)=1)
);
IF OBJECT_ID(N'dbo.ShadowProgressiveProtection',N'U') IS NULL
    THROW 51000, 'Progressive protection table was not created.', 1;
COMMIT;
