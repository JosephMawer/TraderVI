/*
Purpose: Persist the identity of the last fifteen-minute bar consumed by each
         Shadow position so trailing-profit state cannot process one bar twice.
Precondition: Migration 019 is installed, ShadowPosition exists, and the new
              column is absent. Take a fresh verified backup before execution.
Data effect: Adds one nullable timestamp column. Existing ledger rows and all
             of their values remain unchanged; their prior consumed-bar identity
             stays unknown rather than being inferred.
Recovery: Restore the verified backup if the schema operation fails review.
          Do not drop the column after Shadow has begun recording identities.
*/

USE [TraderDB];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.ShadowPosition', N'U') IS NULL
        THROW 51000, 'Migration 019 must be installed before migration 021.', 1;

    IF COL_LENGTH(N'dbo.ShadowPosition', N'LastFifteenMinuteBarUtc') IS NOT NULL
        THROW 51001, 'LastFifteenMinuteBarUtc already exists; refusing an unexpected starting schema.', 1;

    ALTER TABLE [dbo].[ShadowPosition]
        ADD [LastFifteenMinuteBarUtc] DATETIME2 NULL;

    IF COL_LENGTH(N'dbo.ShadowPosition', N'LastFifteenMinuteBarUtc') IS NULL
        THROW 51002, 'LastFifteenMinuteBarUtc was not added.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE [object_id] = OBJECT_ID(N'dbo.ShadowPosition', N'U')
          AND [name] = N'LastFifteenMinuteBarUtc'
          AND [system_type_id] = TYPE_ID(N'datetime2')
          AND [is_nullable] = 1
    )
        THROW 51003, 'LastFifteenMinuteBarUtc does not match the reviewed nullable datetime2 contract.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    c.[name] AS [ColumnName],
    TYPE_NAME(c.[system_type_id]) AS [DataType],
    c.[is_nullable] AS [IsNullable]
FROM sys.columns AS c
WHERE c.[object_id] = OBJECT_ID(N'dbo.ShadowPosition', N'U')
  AND c.[name] = N'LastFifteenMinuteBarUtc';
GO
