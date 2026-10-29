#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Core.Runtime;

/// <summary>Safe operational failure metadata. Never includes messages, symbols, SQL or account values.</summary>
public sealed record CollectorFailureDiagnostic(string Stage, string ExceptionType, ImmutableArray<int> SqlErrors)
{
    public static CollectorFailureDiagnostic From(string stage, Exception error) => new(stage,
        error.GetType().Name, error is SqlException sql
            ? sql.Errors.Cast<SqlError>().Select(e => e.Number).Distinct().ToImmutableArray() : []);
}
