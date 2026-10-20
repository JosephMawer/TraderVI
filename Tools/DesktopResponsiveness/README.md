# Offline WPF responsiveness comparison

This executable creates a WPF dispatcher and fictional in-memory ledger history. It compares the
same verified snapshot reconstruction on the UI thread and through `PaperCycleWorker`, then checks
a synthetic setup idle boundary. It creates no application window and composes no desktop engine,
SQL connection, provider or broker workflow. Do not add native reads or private fixture inputs here.

From the repository root:

```powershell
dotnet run --project Tools/DesktopResponsiveness/DesktopResponsiveness.csproj --artifacts-path artifacts/desktop-responsiveness-20261020/synthetic -p:NuGetAudit=false
```

Each JSON line reports elapsed time, maximum dispatcher pulse gap, pulse count, context presence
and restored quote count. Compare on the same machine/configuration with warmed serializers; these
are synthetic thread-placement measurements, not native throughput or live acceptance. Core's
bounded fixed-stage diagnostics are best effort and may also be written by the worker helper.
See [the investigation](../../Docs/reviews/desktop-paper-responsiveness-20261020.md).
