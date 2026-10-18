CREATE FUNCTION dbo.IsProgressiveProfitProtectionSuccessor
(
    @OriginalSettings NVARCHAR(MAX),
    @OriginalIdentity NVARCHAR(MAX),
    @Candidate NVARCHAR(MAX)
)
RETURNS BIT
AS
BEGIN
    IF ISNULL(ISJSON(@OriginalSettings),0)<>1 OR ISNULL(ISJSON(@Candidate),0)<>1 RETURN 0;
    IF @OriginalIdentity IS NOT NULL AND ISJSON(@OriginalIdentity)<>1 RETURN 0;
    DECLARE @Expected TABLE (PropertyName NVARCHAR(4000), PropertyValue NVARCHAR(MAX), JsonType INT);
    DECLARE @Actual TABLE (PropertyName NVARCHAR(4000), PropertyValue NVARCHAR(MAX), JsonType INT);
    INSERT @Expected
        SELECT LOWER([key]),value,type FROM OPENJSON(@OriginalSettings)
        WHERE LOWER([key])<>N'useprogressiveprofitprotection'
        UNION ALL SELECT LOWER([key]),value,type FROM OPENJSON(COALESCE(@OriginalIdentity,N'{}'));
    INSERT @Actual SELECT LOWER([key]),value,type FROM OPENJSON(@Candidate)
        WHERE LOWER([key])<>N'useprogressiveprofitprotection';
    IF (SELECT COUNT(*) FROM OPENJSON(@Candidate) WHERE LOWER([key])=N'useprogressiveprofitprotection')<>1
        OR NOT EXISTS(SELECT 1 FROM OPENJSON(@Candidate) WHERE LOWER([key])=N'useprogressiveprofitprotection' AND type=3 AND value=N'true') RETURN 0;
    IF EXISTS(SELECT PropertyName FROM @Expected GROUP BY PropertyName HAVING COUNT(*)<>1)
        OR EXISTS(SELECT PropertyName FROM @Actual GROUP BY PropertyName HAVING COUNT(*)<>1) RETURN 0;
    IF EXISTS(SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Expected
        EXCEPT SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Actual)
        OR EXISTS(SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Actual
        EXCEPT SELECT PropertyName COLLATE Latin1_General_100_BIN2,PropertyValue COLLATE Latin1_General_100_BIN2,JsonType FROM @Expected) RETURN 0;
    RETURN 1;
END;
