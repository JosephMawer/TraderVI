# Read-only broker readiness

This .NET 10 Windows console inspects the existing strategy/broker components. It has no order,
database write, migration, token-refresh or automation mode. The native app owns authentication;
sign in there with its saved-session option first. Do not print or copy protected session data.

```powershell
dotnet build Operations/BrokerReadiness/BrokerReadiness.csproj
dotnet run --project Operations/BrokerReadiness/BrokerReadiness.csproj --no-build -- strategy-review 77bcbaa9-43fe-4259-aff7-25f30554e450
dotnet run --project Operations/BrokerReadiness/BrokerReadiness.csproj --no-build -- verify-broker-readonly
```

Run only with explicit read-only broker verification authority. The console reads the existing
Windows CurrentUser-protected bearer without refreshing or saving it. A concurrent saved-login
change invalidates the result. Output omits broker identities and financial values. SQL strategy
review uses the project's existing configured connection; neither secrets nor model bytes are output.

The result deliberately keeps `QualifiedForAutomation=false`. A stable activity checkpoint and
matching holdings are not proof of cash-effective timing, opening method, local journal recovery,
strategy qualification or order lifecycle. Read the [activation review](../../Docs/reviews/supervised-session-20260923.md).
