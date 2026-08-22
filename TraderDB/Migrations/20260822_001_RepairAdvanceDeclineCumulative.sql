/*
Purpose: Repair AdvanceDeclineLine.CumulativeDifferential after the incremental
         calculation counted lookback-day pluralities more than once.
Expected precondition: The audited 2026-08-22 snapshot contains 262 rows from
         2025-08-07 through 2026-08-21, 0 DailyPlurality errors, and exactly
         132 cumulative values that differ from the canonical running sum.
Data effect: Updates only CumulativeDifferential on those 132 existing rows.
         No rows or other column values are inserted, deleted, or changed.
Rollback/recovery: The transaction rolls back on any failed guard or
         postcondition. After commit, restore the fresh verified pre-repair
         backup if rollback is required.
*/

USE [TraderDB];

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.AdvanceDeclineLine', N'U') IS NULL
        THROW 51100, 'Expected table dbo.AdvanceDeclineLine does not exist.', 1;

    IF COL_LENGTH(N'dbo.AdvanceDeclineLine', N'Date') IS NULL
       OR COL_LENGTH(N'dbo.AdvanceDeclineLine', N'Advancers') IS NULL
       OR COL_LENGTH(N'dbo.AdvanceDeclineLine', N'Decliners') IS NULL
       OR COL_LENGTH(N'dbo.AdvanceDeclineLine', N'DailyPlurality') IS NULL
       OR COL_LENGTH(N'dbo.AdvanceDeclineLine', N'CumulativeDifferential') IS NULL
        THROW 51101, 'AdvanceDeclineLine does not have the expected columns.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM msdb.dbo.backupset
        WHERE database_name = N'TraderDB'
          AND [type] = N'D'
          AND backup_finish_date >= DATEADD(HOUR, -4, SYSDATETIME())
          AND has_backup_checksums = 1
          AND is_damaged = 0
    )
        THROW 51102, 'A checksum full backup completed within the last four hours is required.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AdvanceDeclineLine
        WHERE DailyPlurality <> Advancers - Decliners
    )
        THROW 51103, 'DailyPlurality contains an inconsistency; cumulative repair was not attempted.', 1;

    DECLARE @Expected TABLE
    (
        [Date] date NOT NULL PRIMARY KEY,
        ExpectedCumulative int NOT NULL
    );

    INSERT @Expected ([Date], ExpectedCumulative)
    SELECT
        [Date],
        SUM(DailyPlurality) OVER
            (ORDER BY [Date] ROWS UNBOUNDED PRECEDING)
    FROM dbo.AdvanceDeclineLine;

    DECLARE @rowCount int = (SELECT COUNT(*) FROM dbo.AdvanceDeclineLine);
    DECLARE @firstDate date = (SELECT MIN([Date]) FROM dbo.AdvanceDeclineLine);
    DECLARE @lastDate date = (SELECT MAX([Date]) FROM dbo.AdvanceDeclineLine);
    DECLARE @mismatchCount int =
    (
        SELECT COUNT(*)
        FROM dbo.AdvanceDeclineLine AS actual
        INNER JOIN @Expected AS expected ON expected.[Date] = actual.[Date]
        WHERE actual.CumulativeDifferential <> expected.ExpectedCumulative
    );

    IF @rowCount <> 262
       OR @firstDate <> CONVERT(date, '2025-08-07')
       OR @lastDate <> CONVERT(date, '2026-08-21')
       OR @mismatchCount <> 132
        THROW 51104, 'AdvanceDeclineLine changed after review; this repair is intentionally snapshot-specific.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM dbo.AdvanceDeclineLine AS actual
        INNER JOIN @Expected AS expected ON expected.[Date] = actual.[Date]
        WHERE actual.[Date] = CONVERT(date, '2026-08-21')
          AND actual.CumulativeDifferential = 10872
          AND expected.ExpectedCumulative = 7307
    )
        THROW 51105, 'The reviewed final cumulative values no longer match; repair was not attempted.', 1;

    UPDATE actual
    SET CumulativeDifferential = expected.ExpectedCumulative
    FROM dbo.AdvanceDeclineLine AS actual
    INNER JOIN @Expected AS expected ON expected.[Date] = actual.[Date]
    WHERE actual.CumulativeDifferential <> expected.ExpectedCumulative;

    DECLARE @updatedRows int = @@ROWCOUNT;

    IF @updatedRows <> 132
        THROW 51106, 'Unexpected repair row count; transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AdvanceDeclineLine
        WHERE DailyPlurality <> Advancers - Decliners
    )
        THROW 51107, 'DailyPlurality postcondition failed; transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.AdvanceDeclineLine AS actual
        INNER JOIN @Expected AS expected ON expected.[Date] = actual.[Date]
        WHERE actual.CumulativeDifferential <> expected.ExpectedCumulative
    )
        THROW 51108, 'Cumulative running-sum postcondition failed; transaction will be rolled back.', 1;

    COMMIT TRANSACTION;

    SELECT
        @updatedRows AS RowsUpdated,
        @firstDate AS FirstDate,
        @lastDate AS LastDate,
        (SELECT CumulativeDifferential
         FROM dbo.AdvanceDeclineLine
         WHERE [Date] = @lastDate) AS FinalCumulative;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
