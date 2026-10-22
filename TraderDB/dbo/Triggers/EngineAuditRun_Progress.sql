CREATE TRIGGER [dbo].[EngineAuditRun_Progress] ON [dbo].[EngineAuditRun]
AFTER UPDATE,DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS(SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.RunId=d.RunId
        WHERE i.RunId IS NULL OR d.Status<>N'Running' OR i.Revision<>d.Revision+1
        OR i.Kind<>d.Kind OR i.BoundaryUtc<>d.BoundaryUtc OR i.PlanHash<>d.PlanHash
        OR CONVERT(varbinary(max),i.PlanJson)<>CONVERT(varbinary(max),d.PlanJson)
        OR i.UpdatedUtc<d.UpdatedUtc
        OR TRY_CONVERT(int,JSON_VALUE(i.ProgressJson,'$.TargetIndex')) IS NULL
        OR TRY_CONVERT(bigint,JSON_VALUE(i.ProgressJson,'$.VerifiedUnits')) IS NULL
        OR TRY_CONVERT(int,JSON_VALUE(i.ProgressJson,'$.TargetIndex'))<TRY_CONVERT(int,JSON_VALUE(d.ProgressJson,'$.TargetIndex'))
        OR TRY_CONVERT(bigint,JSON_VALUE(i.ProgressJson,'$.VerifiedUnits'))<TRY_CONVERT(bigint,JSON_VALUE(d.ProgressJson,'$.VerifiedUnits')))
        THROW 54452,'Audit boundaries and completed or failed receipts cannot be rewritten.',1;
END;
GO
