# TraderVI Work planning automation

GitHub issues own work progress; repository designs and dated evidence retain their
technical authority. [TraderVI Work](https://github.com/users/JosephMawer/projects/1)
uses labels as the authority for its **Planning** columns. This is one-way:

| Labels | Planning |
|---|---|
| `decision: agreed` | Agreed |
| `decision: needs discussion` | Needs discussion |
| Either decision label plus `planning: deferred` | Deferred |

Exactly one decision label is required, including on deferred work. Missing or
contradictory decisions clear Planning to **No Planning** and fail the workflow
with the affected issue numbers. This never invents agreement. Correct the labels
to recover. `Agreed` means scope/direction, not deployment or financial authority.

Change labels on issues or cards; do not drag cards between Planning columns.
Manual field edits are overwritten on the next reconciliation. **Status** (Todo,
In Progress, Done), issue state, type labels, titles and descriptions are untouched.
An agreed-but-deferred issue retains both meanings. Closing an issue does not erase
its planning classification; progress/closure workflows remain separate.

## Installation and permissions

The workflow must exist on the default branch (`master`). Its offline tests need
no secrets. Actual synchronization requires one repository Actions secret:

1. In GitHub account settings, Developer settings, Personal access tokens,
   **Tokens (classic)**, generate a token named `TraderVI planning sync` with an
   explicit expiration (for example 90 days) and only **project** selected.
   User-owned Projects currently require classic tokens rather than fine-grained
   tokens. This permission covers accessible projects, not just this board; the
   script itself is fixed to JosephMawer project 1 and validates its title/schema.
2. In TraderVI Settings, Secrets and variables, Actions, add repository secret
   **TRADERVI_PROJECT_TOKEN** with that token. Enter it directly in GitHub; never
   paste it into chat, source, logs or a local file. No `repo` or `workflow` scope
   is required for this token: public repository reads use the separate, automatic
   read-only `GITHUB_TOKEN` supplied as `GH_REPO_TOKEN`.
3. Under Actions, **Sync TraderVI planning**, choose **Run workflow** on `master`.
   Inspect both jobs and the synchronization summary, then inspect the board.
   A missing/expired/inaccessible token fails visibly rather than claiming success.
4. Renew the token before expiration and replace the same secret. Do not broaden
   permissions on an unexplained failure; investigate the reported access failure.

References: [Projects API authentication](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/using-the-api-to-manage-projects),
[token limitations](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens),
[Actions project access](https://docs.github.com/en/issues/planning-and-tracking-with-projects/automating-your-project/automating-projects-using-actions).

## Operation and limits

- Issue creation, reopening, closing and label changes trigger reconciliation;
  repository label edits/deletion also trigger it. A manual run and an hourly
  fallback catch missed events and manual card moves. GitHub scheduling can be
  delayed; synchronization is eventual, not a transactional lock on human edits.
- Each run reads current labels, not old event payloads, and reads back the saved
  Planning values. Concurrent runs are serialized. A later label edit during a run
  is resolved by the next event/manual/hourly reconciliation.
- All open repository issues are added idempotently; existing closed issue cards
  are reconciled without reopening them. Archived items, PRs, draft cards and
  items from other repositories are ignored. Existing auto-add can remain enabled.
- Pagination is supported with explicit bounds. API/schema/access failures fail
  the run. No retry loops or issue-comment notification spam are used. Partial
  progress can safely be rerun. Actions notifications follow the user's GitHub
  notification settings; this is not a separate alerting service.
- Only membership and Planning values can be written. This job never changes
  labels, Status, board structure, issue content, runtime settings or trading state.
  Project credentials are not exposed to pull-request jobs. The checkout action is
  commit-pinned and does not persist credentials. Python uses only its standard library.

## Offline validation

```powershell
python -m unittest discover -s Operations/GitHubPlanning -p 'test_*.py' -v
```

Tests cover the original ten-issue mapping, ambiguous labels, deferred precedence,
idempotence, live label changes, closed/archived/foreign items, schema changes,
pagination and failed read-back. Passing these tests does **not** prove GitHub
permission setup or live synchronization. Verify a successful Actions run after
adding the secret before calling the automation operational.

## Other-session capture prompt amendment

Use the existing session-reconciliation prompt, with this convention:

> Labels are authoritative. Read this README and inspect the latest synchronization
> run. Apply exactly one decision label and optionally `planning: deferred`. When
> synchronization is operational, let it derive Planning and verify the result;
> do not infer decisions from card placement. While automation is pending or failed,
> keep Planning manually aligned with labels and report that fallback. Ask before
> changing labels, fields, options, workflows, permissions or milestones. Ordinary
> issue capture using existing conventions remains authorized.

The relevance, duplication, privacy, ownership and approval checks in that prompt
remain required. The initial issues are #3–#12; search all current issues rather
than assuming this inventory remains complete.
