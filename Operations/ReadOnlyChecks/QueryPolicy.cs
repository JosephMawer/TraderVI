using Microsoft.SqlServer.TransactSql.ScriptDom;
using System.Text.Json;

// SQL Server permissions remain the mutation boundary. This parser additionally rejects
// procedure/shell/external-data routes, including public procedures in master/tempdb.
internal static class QueryPolicy
{
    public const string Rejection = "Read-only SQL policy rejected the request.";

    public static void Validate(string query)
    {
        var tree = new TSql160Parser(true).Parse(new StringReader(query), out var errors);
        if (errors.Count != 0 || tree is not TSqlScript script || script.Batches.Count != 1
            || script.Batches[0].Statements.Count == 0) Reject();
        var batch = ((TSqlScript)tree).Batches[0];
        foreach (var statement in batch.Statements)
        {
            if (statement is not (SelectStatement or DeclareVariableStatement or SetVariableStatement or PredicateSetStatement)) Reject();
            // Restrict SET to scalar assignment and NOCOUNT; never caller-controlled server/session options.
            if (statement is PredicateSetStatement predicate && predicate.Options != SetOptions.NoCount) Reject();
            if (statement is SetVariableStatement variable
                && (variable.FunctionCallExists || variable.Identifier is not null || variable.CursorDefinition is not null)) Reject();
        }
        tree.Accept(new BoundaryVisitor());
    }

    private static void Reject() => throw new InvalidOperationException(Rejection);

    private sealed class BoundaryVisitor : TSqlFragmentVisitor
    {
        public override void ExplicitVisit(SelectStatement node)
        {
            if (node.Into is not null) Reject();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(SchemaObjectName node)
        {
            if (node.DatabaseIdentifier is not null || node.ServerIdentifier is not null) Reject();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(MultiPartIdentifierCallTarget node)
        {
            if (node.MultiPartIdentifier.Identifiers.Count > 1) Reject();
            base.ExplicitVisit(node);
        }
        public override void ExplicitVisit(OpenQueryTableReference node) => Reject();
        public override void ExplicitVisit(OpenRowsetTableReference node) => Reject();
        public override void ExplicitVisit(InternalOpenRowset node) => Reject();
        public override void ExplicitVisit(OpenRowsetCosmos node) => Reject();
        public override void ExplicitVisit(BulkOpenRowset node) => Reject();
        public override void ExplicitVisit(AdHocTableReference node) => Reject();
        public override void ExplicitVisit(DataModificationTableReference node) => Reject();
        public override void ExplicitVisit(NextValueForExpression node) => Reject();
        public override void ExplicitVisit(UserDefinedTypeCallTarget node) => Reject();
        public override void ExplicitVisit(UserDataTypeReference node) => Reject();
        public override void ExplicitVisit(ExpressionCallTarget node) => Reject();
    }

    public static int SelfTest()
    {
        string[] accepted =
        [
            "SELECT COUNT_BIG(*) FROM dbo.Symbols;",
            "SET NOCOUNT ON; DECLARE @n int=1; SET @n=@n+1; SELECT @n;",
            ";WITH x AS (SELECT 1 AS n) SELECT n FROM x;",
            "SELECT N'EXEC master..xp_cmdshell' AS Text; -- DROP TABLE dbo.Symbols",
            "SELECT * FROM INFORMATION_SCHEMA.TABLES;",
            "SELECT USER_NAME(), SYSUTCDATETIME(); SELECT * FROM sys.fn_my_permissions(NULL,'DATABASE');"
        ];
        string[] rejected =
        [
            "UPDATE TOP (0) dbo.Symbols SET IsActive=IsActive;",
            "DELETE TOP (0) FROM dbo.Symbols;",
            "INSERT INTO dbo.Symbols(Symbol) SELECT N'x' WHERE 1=0;",
            "SELECT 1 INTO dbo.__ReadOnlyProbe;",
            "DROP TABLE dbo.Symbols;", "CREATE TABLE dbo.X(i int);",
            "REVERT;", "EXECUTE AS USER='dbo';", "USE master; SELECT 1;",
            "EXEC master.sys.xp_dirtree N'C:\\';", "EXEC(N'SELECT 1');",
            "SELECT * FROM master.sys.databases;", "SELECT * FROM [master]..[sysdatabases];",
            "SELECT * FROM server.TraderDB.dbo.Symbols;",
            "SELECT master.dbo.SomeFunction();",
            "SELECT * FROM OPENQUERY(server,'SELECT 1');",
            "SELECT * FROM OPENROWSET('SQLNCLI','Server=localhost;Trusted_Connection=yes;','SELECT 1');",
            "SELECT * FROM OPENROWSET(BULK 'C:\\file', SINGLE_CLOB) AS x;",
            "SELECT NEXT VALUE FOR dbo.SomeSequence;",
            ":!! whoami", ":r unsafe.sql", "SELECT 1;\nGO\nSELECT 2;",
            "SET IMPLICIT_TRANSACTIONS ON; SELECT 1;",
            "DECLARE @x dbo.CustomType; SELECT @x;",
            "DECLARE @x xml; SELECT @x.query('/');",
            "WITH x AS (SELECT 1 AS n) DELETE FROM dbo.Symbols;",
            "SELECT * FROM (DELETE TOP (0) FROM dbo.Symbols OUTPUT deleted.Symbol) AS x;"
        ];
        foreach (var query in accepted) Validate(query);
        foreach (var query in rejected)
        {
            bool denied = false;
            try { Validate(query); }
            catch (InvalidOperationException error) when (error.Message == Rejection) { denied = true; }
            if (!denied) throw new InvalidOperationException("SQL policy self-test failed.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { OfflinePolicyChecksPassed = accepted.Length + rejected.Length }));
        return 0;
    }
}
