# Local read-only database checks

Use the installed helper for task-relevant local TraderDB diagnostics. The operator authorized
this reusable path on October 18, 2026, to avoid repeated sandbox approval prompts.
This permits reads, not account changes, collection, training, broker access or application launches.
The security decision is [ADR-0091](../../Docs/adr/0091-local-read-only-database-checks.md).

## Run a check

1. Read/review the intended SQL. Write its UTF-8 text to
   `C:\src\TraderVI\artifacts\read-only-db\request.sql` using normal sandbox file access.
   Use aggregate output where possible; keep private results in ignored artifacts.
2. Run exactly this standalone command, without extra arguments, redirection or chained commands:

   ```powershell
   & 'C:\Users\joseph.mawer\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe' -NoProfile -File 'C:\Users\joseph.mawer\.codex\tools\tradervi-read-only-db\v2\Invoke.ps1'
   ```

3. Read the JSON receipt's `ResultFile` inside the sandbox. Verify `QuerySha256` matches the UTF-8
   query text you submitted before interpreting it. Results contain `ResultSets`, each with ordered
   `Columns` and positional `Rows` arrays, preserving duplicate column names and multiple result sets.
   A `FOR JSON PATH` query returns its JSON as a string field inside that structure.
4. Summarize only the task-relevant findings. Do not print private records or raw SQL error messages.

The mailbox supports one submitted check at a time. The helper locks execution and snapshots its
input, but different chats must coordinate submissions; a receipt hash mismatch requires resubmission.
Results have unique filenames and are never overwritten. On failure, no result is published.
The original query is not automatically retried or rewritten.

Use `functions.exec` / `exec_command` with `sandbox_permissions=require_escalated` if the runner
requires it: the installed narrow allow rule is intended to satisfy that check without another
manual approval. A saved rule permits running outside the sandbox; it does not grant SQL access
inside the sandbox. New rules load at Codex startup; restart Codex to refresh existing chats.
Do not replace this with a broad approval for PowerShell, dotnet, sqlcmd, or repository scripts.

## Enforced boundary

The reviewed [Delphi Live decision summary](DelphiLiveDecisionSummary.sql) can be copied into the
same mailbox without extending the helper. It reads the new optional private decision traces as
aggregate reasons/counts, no symbols/account values. No trace on an older build is unavailable
evidence, not a successful check. Pending quotes/selection, acknowledgement and confirmed fills
remain distinct; the trace timestamp may precede the latest account-synchronization revision.

- Hard-coded endpoint `localhost`, database `TraderDB`, integrated Windows authentication.
- Dedicated database user `TraderVIReadOnlyChecks`, created **WITHOUT LOGIN**: no password or new
  server login. Only CONNECT and SELECT on dbo are granted; mutation, procedure execution, ownership,
  DDL, backup and dbo impersonation are denied. No application identity's permissions are changed.
- Before connecting, Microsoft's SQL parser accepts only SELECTs, scalar DECLARE/SET and NOCOUNT.
  It rejects DML/DDL, EXECUTE/REVERT/USE, SELECT INTO, sequences, cross-database/server object and
  function references, external rowsets, CLR types/methods and unsupported statements. Use SELECTs
  and common table expressions (CTEs: named query expressions) for ordinary aggregate diagnostics.
  SQL permissions provide a second boundary; syntax validation also excludes guest/public procedures
  and metadata outside TraderDB. The fixed guard rejects external dependencies, synonyms and user CLR
  assemblies in dbo rather than allowing hidden external access through local schema objects.
- A separate fixed guard command switches to that user with `EXECUTE AS USER ... WITH NO REVERT`
  before caller SQL runs. Pooling is disabled. REVERT cannot restore the original Windows privileges.
  Unsafe trust/chaining settings, unexpected roles/ownership/grants and permission drift fail closed.
- SQL is sent directly through SqlClient: SQLCMD `:r`, `:!!`, variables and `GO` are not interpreted.
  This supports the limited read-only T-SQL grammar above, not arbitrary scripts or procedure workflows.
- Limits: 1 MiB query, 10,000 returned rows, 100 result sets, 8 MiB output, 30-second query timeout,
  45-second cancellation budget, 5-second initial lock timeout and low deadlock priority.
  SELECTs still use SQL Server resources/locks and can use temporary workspace; this is not a
  resource-isolated replica. A single large field may be buffered before its output limit is checked.
- The installed copy and dependency hashes live outside sandbox-writable roots. The no-profile
  launcher checks hashes, rejects arguments, and clears inherited .NET startup-hook/profiler settings.
  The helper rejects mailbox reparse points and creates result files without overwriting existing files.

## Installation, verification and upgrades

Build/publish this standalone project; it does not reference Core. ScriptDom is copied from the
installed SSDT library into the reviewed build; override `ScriptDomPath` for a different Visual Studio
installation. It is hash-checked with the other installed dependencies. Run the build's `--self-test`
for 33 offline SQL-policy cases (no database connection). Build the SQL security definition
with Visual Studio MSBuild/SSDT. `Install.ps1` is a **one-time operational installer**, never an
approved recurring command. It requires explicit setup authority and access outside the sandbox.
It creates/checksums/verifies a fresh full backup, copies and hashes it at the established backup
destination, manually applies migration 046, copies the reviewed published build to the versioned
installation directory, and runs `Verify.ps1`. A matching backup copy does not establish remote
OneDrive synchronization or a restored-database test.

Only after twelve native checks pass does it save the narrow user-layer rules and remove the previous
general `sqlcmd -S localhost -d TraderDB -E` allow rule. Other existing approvals are preserved.
Native checks cover SELECT/identity, INSERT/UPDATE/DELETE rejection, REVERT/dbo impersonation rejection,
absence of server-control/ALTER ANY DATABASE authority, DDL/DML/EXECUTE permissions, SQLCMD shell-directive
rejection, row limits, cross-database references and public system-procedure rejection.
They neither place orders nor launch operational hosts. Evidence stays in ignored `artifacts/read-only-db/`.

The initial v1 verification separately proved SQL error 229 for zero-row DML and 15196 for REVERT.
It found guest/public system metadata readable in master, motivating the v2 parser boundary;
v1 rules were replaced. Its dbo impersonation probe returned Windows-resolution error 15404;
effective-permission checks independently report no impersonation authority. Preserve the v1
receipts as bounded evidence, not as a complete cross-database isolation claim.

Review upgrades as new installations, including dependencies, launcher and changed permissions.
Never point the rule at the editable repository build. Remove the narrow rule first to disable this
path; removing the database user or installed files is a separate reviewed cleanup. Do not restore
the old broad sqlcmd approval as a workaround for a failed check.

Sources: [Codex rules](https://learn.chatgpt.com/docs/agent-configuration/rules),
[SQL execution context](https://learn.microsoft.com/en-us/sql/t-sql/statements/execute-as-transact-sql?view=sql-server-ver17).
