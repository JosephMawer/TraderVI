#nullable enable
using System;

namespace Core.Runtime;

/// <summary>Native qualification may observe local state but cannot connect to or mutate a broker account.</summary>
public static class EngineVerification
{
    public static void RequireLocalBrokerRead(EngineOperation operation)
    {
        if (operation is not (EngineOperation.Connection or EngineOperation.ExecutionStatus or
            EngineOperation.ExecutionDiagnostics or EngineOperation.Orders or EngineOperation.DisableExecution))
            throw new InvalidOperationException("Native verification mode disables broker access and all real order commands.");
    }
}
