#nullable enable
using Dapper;
using System;
using System.Data;

namespace Core.Db;

internal static class PaperSourceSqlParameters
{
    // Dapper's default DateTime mapping uses SQL datetime (3.33 ms rounding).
    // Source identity and causality require the stored datetime2 precision.
    internal static DynamicParameters Create(object values, params (string Name, DateTime Value)[] timestamps)
    {
        var parameters = new DynamicParameters(values);
        foreach (var timestamp in timestamps) parameters.Add(timestamp.Name, timestamp.Value, DbType.DateTime2);
        return parameters;
    }
}
