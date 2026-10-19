#nullable enable
using Microsoft.Data.SqlClient;
using System;
using System.Data;

namespace TraderVI.WPF.Viewmodels;

/// <summary>Actionable categories only; exception messages may contain private SQL/broker facts.</summary>
public static class BrokerRiskSetupDiagnostic
{
    public static string Describe(Exception error)
    {
        if (error is SqlException sql) return $"SQL error {sql.Number}";
        if (error is DataException { InnerException: { } inner })
            return $"Data mapping error ({inner.GetType().Name})";
        return error.GetType().Name;
    }
}
