# Offline recovery codec profile

Synthetic in-memory measurement for the [restart design review](../../Docs/reviews/restart-recovery-design-20261030.md).
No SQL, broker, market, host, local protected journal or production-state input is read.
The only input is an explicitly selected existing Core.dll. The program reports its SHA256/MVID,
uses synthetic format-shaped objects, and invokes only codec/hash functions. It does not validate
complete financial sessions, broker transitions or the proposed restart protocol.

Run from the repository root with .NET 10 and an existing reviewed Core assembly:

```powershell
dotnet run --project Operations/RecoveryCodecProfile/RecoveryCodecProfile.csproj --configuration Release --artifacts-path artifacts/build -p:CoreAssemblyPath=C:/src/TraderVI/Core/bin/Release/net10.0/Core.dll -p:NuGetAudit=false
```

This builds only the diagnostic executable, not Core or an operational host. Package restore has no
third-party dependencies. Output is JSON lines; save to ignored artifacts if needed. Do not point
the assembly property at an untrusted binary. The measured assembly is an explicit input: results
do not establish that a different installed or newly compiled build behaves identically.

The two fixtures have 32/2,048 synthetic records and test reconstruction through the real packed
format. Each stage has one warmup and five measured calls; the process lowers its priority and does
not force GC. Metrics include exact UTF-8 sizes, fragment count/reference depth, parse/serialization,
raw hash/canonical fingerprint, fragment traversal and one/two restorations, per-thread allocation,
process CPU and GC collections. A final process peak working set covers the complete run.

Stages overlap: do not sum them. Timings are short, affected by tiered JIT, GC and concurrent machine
load; CPU measurements have coarse resolution. Reference depth supplies a source-based expectation
of fragment queries, **not measured SQL round trips**. No transition/compatibility or database costs
are measured. It rejects changed and missing fragments for both fixtures and fails the process if
either corruption is accepted. These four probes protect interpretation of the benchmark, not a
substitute for the new recovery protocol's fault-injection suite.
