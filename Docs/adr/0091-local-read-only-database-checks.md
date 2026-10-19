# ADR-0091 — A fixed local helper for read-only database checks

**Date:** October 18, 2026. **Status:** Accepted by the operator's setup request.
**Domains:** architecture, data-pipeline.

## Problem and goal

Repeated SQL connection failures inside the Windows sandbox lead to repeated approvals for new
dated diagnostics. The parent problem is dependable local inspection without granting general SQL
or shell execution. The root goal is reliable TraderVI work with the sandbox and database protections intact.

## Decision

Install a reviewed standalone helper outside the sandbox-writable repository. Approve only its
constant no-profile invocation. Caller SQL enters through an ignored local mailbox; it is never
interpreted as PowerShell or SQLCMD. Windows authentication opens a non-pooled connection to local
TraderDB; fixed SQL irreversibly switches to a loginless database user before caller SQL runs.
SQL Server grants dbo SELECT and denies mutation, execution, ownership and DDL. Permission/trust
drift is rejected. Microsoft's SQL parser independently limits caller SQL to SELECT, scalar DECLARE/SET
and NOCOUNT, rejecting mutation, procedure calls, cross-database/server references, sequences and
external rowsets before connecting. The fixed guard rejects external schema dependencies and user
CLR assemblies. The helper bounds reads/results and stores private results locally.

This applies the operator's explicit request to configure the reusable read-only path. It permits
security-user installation and narrow local approval configuration, not business-schema changes,
collection, broker requests, trading, training or pushes. The database user and canonical SQL project
definition are synchronized through a reviewed security-only migration under the existing backup procedure.

The existing broad integrated-authentication sqlcmd approval is removed only when native validation
passes and the narrow replacement is saved. Other historical approvals remain unchanged. A SELECT
label or `ApplicationIntent=ReadOnly` alone is not the permission boundary. SQL Server's restricted
execution context is the boundary; a no-argument, hash-checked installed launcher makes it reusable.

## Consequences and limits

Fresh sessions can run recurring local reads without new command-specific approvals after rules
reload. Existing chats may need an app restart. Unrelated approval prompts remain valid. Changing
the endpoint, permissions or installed binary requires a reviewed upgrade. Query results retain
their original evidence dates; a successful read never proves operational/trading readiness.
Reads still consume database resources. Temporary query work, Windows authentication and existing
SQL Server configuration retain their normal behavior. Hash verification protects the installed
copy from accidental drift; it does not protect against a Windows administrator modifying the installation.
Initial native probing found that database-scoped impersonation alone still allows guest/public
metadata in master. The v2 SQL parser closes direct cross-database and procedure routes rather than
relying on impersonation for that additional boundary. The initial native SQL-permission checks and
the final syntax-policy checks retain their separate scopes in the local evidence.

[Procedure and verification](../../Operations/ReadOnlyChecks/README.md).

## Review questions

1. Why does SELECT-only SQL still need access beyond the sandbox?
2. What prevents caller SQL from regaining the original Windows login's SQL permissions?
3. Why approve the installed helper rather than sqlcmd or an editable repository script?
