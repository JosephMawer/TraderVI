USE [TraderDB];
GO

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

/*
    Reconcile the remaining canonical write contracts under ADR-0049.

    Expected preconditions:
      - dbo.LeadershipData, dbo.Quotes, and dbo.Symbols exist with the column
        types represented by TraderDB.sqlproj.
      - FK_Quotes_Symbols exists on Quotes(Symbol) -> Symbols(Symbol), is enabled,
        and may be either trusted or untrusted.
      - The two new LeadershipData checks are either absent, or already enabled
        and trusted from an exact earlier application of this script.

    Data effects:
      - None. The migration refuses invalid leadership values or orphan Quotes.
      - Adds trusted checks for bounded NHNL counts and positive optional
        benchmark prices.
      - Revalidates FK_Quotes_Symbols so its existing rows become trusted.

    Recovery:
      - Constraint additions/trust changes are one SERIALIZABLE transaction with
        XACT_ABORT. A failed guard rolls back the unit.
      - After a committed result, use the required fresh verified backup or a
        separately authorized corrective migration. Do not edit this script.

    Review and execute manually only after a fresh verified backup and explicit
    authorization. Do not deploy a DACPAC.
*/

IF OBJECT_ID(N'dbo.LeadershipData', N'U') IS NULL
    OR OBJECT_ID(N'dbo.Quotes', N'U') IS NULL
    OR OBJECT_ID(N'dbo.Symbols', N'U') IS NULL
    THROW 51101, 'Required leadership, quotes, or symbols table is missing.', 1;

IF
(
    SELECT COUNT(*)
    FROM sys.columns AS c
    JOIN sys.types AS t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.LeadershipData')
      AND
      (
          (c.name IN (N'NewHighs', N'NewLows', N'IssuesTraded')
              AND t.name = N'int' AND c.max_length = 4 AND c.is_nullable = 0)
          OR (c.name IN (N'Tsx60Close', N'EqualWeightClose')
              AND t.name = N'decimal' AND c.precision = 10 AND c.scale = 2 AND c.is_nullable = 1)
      )
) <> 5
    THROW 51102, 'LeadershipData columns do not match the expected write contract.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.LeadershipData
    WHERE [NewHighs] < 0
       OR [NewLows] < 0
       OR [IssuesTraded] <= 0
       OR [NewHighs] > [IssuesTraded]
       OR [NewLows] > [IssuesTraded]
)
    THROW 51103, 'Invalid new-high/new-low counts must be reviewed before migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.LeadershipData
    WHERE [Tsx60Close] <= 0 OR [EqualWeightClose] <= 0
)
    THROW 51104, 'Non-positive leadership benchmark prices must be reviewed before migration.', 1;

IF EXISTS
(
    SELECT 1
    FROM dbo.Quotes AS q
    LEFT JOIN dbo.Symbols AS s ON s.Symbol = q.Symbol
    WHERE s.Symbol IS NULL
)
    THROW 51105, 'Orphan Quotes rows must be reviewed before trusting FK_Quotes_Symbols.', 1;

IF
(
    SELECT COUNT(*)
    FROM sys.foreign_keys AS fk
    JOIN sys.foreign_key_columns AS fkc ON fkc.constraint_object_id = fk.object_id
    WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Quotes')
      AND fk.referenced_object_id = OBJECT_ID(N'dbo.Symbols')
      AND fk.name = N'FK_Quotes_Symbols'
      AND fkc.parent_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Quotes'), N'Symbol', 'ColumnId')
      AND fkc.referenced_column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Symbols'), N'Symbol', 'ColumnId')
      AND fk.delete_referential_action = 0
      AND fk.update_referential_action = 0
      AND fk.is_disabled = 0
) <> 1
    THROW 51106, 'The exact enabled FK_Quotes_Symbols relationship is required.', 1;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.CK_LeadershipData_NhnlObservation', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.LeadershipData WITH CHECK
        ADD CONSTRAINT [CK_LeadershipData_NhnlObservation] CHECK
        (
            [NewHighs] >= 0
            AND [NewLows] >= 0
            AND [IssuesTraded] > 0
            AND [NewHighs] <= [IssuesTraded]
            AND [NewLows] <= [IssuesTraded]
        );
END;

ALTER TABLE dbo.LeadershipData WITH CHECK
    CHECK CONSTRAINT [CK_LeadershipData_NhnlObservation];

IF OBJECT_ID(N'dbo.CK_LeadershipData_BenchmarkPrices', N'C') IS NULL
BEGIN
    ALTER TABLE dbo.LeadershipData WITH CHECK
        ADD CONSTRAINT [CK_LeadershipData_BenchmarkPrices] CHECK
        (
            ([Tsx60Close] IS NULL OR [Tsx60Close] > 0)
            AND ([EqualWeightClose] IS NULL OR [EqualWeightClose] > 0)
        );
END;

ALTER TABLE dbo.LeadershipData WITH CHECK
    CHECK CONSTRAINT [CK_LeadershipData_BenchmarkPrices];

ALTER TABLE dbo.Quotes WITH CHECK
    CHECK CONSTRAINT [FK_Quotes_Symbols];

IF
(
    SELECT COUNT(*)
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.LeadershipData')
      AND name IN
      (
          N'CK_LeadershipData_NhnlObservation',
          N'CK_LeadershipData_BenchmarkPrices'
      )
      AND is_disabled = 0
      AND is_not_trusted = 0
      AND is_not_for_replication = 0
) <> 2
    THROW 51107, 'The two LeadershipData write checks are not enabled and trusted.', 1;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.foreign_keys
    WHERE parent_object_id = OBJECT_ID(N'dbo.Quotes')
      AND name = N'FK_Quotes_Symbols'
      AND is_disabled = 0
      AND is_not_trusted = 0
      AND is_not_for_replication = 0
)
    THROW 51108, 'FK_Quotes_Symbols was not enabled and trusted.', 1;

COMMIT TRANSACTION;

SELECT
    (SELECT COUNT_BIG(*) FROM dbo.LeadershipData) AS [LeadershipRowsPreserved],
    (SELECT COUNT_BIG(*) FROM dbo.Quotes) AS [QuoteRowsPreserved],
    (SELECT COUNT_BIG(*) FROM dbo.Quotes AS q LEFT JOIN dbo.Symbols AS s ON s.Symbol = q.Symbol WHERE s.Symbol IS NULL) AS [QuoteOrphans],
    (SELECT is_not_trusted FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.Quotes') AND name = N'FK_Quotes_Symbols') AS [QuotesForeignKeyIsNotTrusted];
