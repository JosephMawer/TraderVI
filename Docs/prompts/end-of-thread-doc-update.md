# Optional end-of-chat documentation checkpoint

Use this when asking for a final reconciliation of substantive work. Routine checkpoint rules
already live in [AGENTS.md](../../AGENTS.md#session-completion-and-project-tracking).

> Reconcile this chat's decisions, changed state and unfinished work with their authoritative
> records. Do not start new implementation or operational work.
>
> Update only the owners whose facts, priorities, dependencies, rules or navigation changed.
> Preserve rejected alternatives, explicit deferrals, uncertainty and evidence qualifications.
> Link detailed evidence once; do not repeat the milestone in every tracking file.
>
> Record a meaningful new design decision in an ADR and repair its navigation. An ordinary fix
> implementing an existing decision does not need another ADR. Add a concept note, report or
> learning card only if it serves a distinct purpose. Keep consequential open choices in
> open questions and implementation tasks in their owning checklist.
>
> Inspect the exact diff and relevant links/references. Use validation appropriate to the changes;
> documentation-only work does not require a build or application launch. Preserve unrelated
> work and commit coherent reviewed changes locally under the standing commit rules. Never push.
>
> Give a concise handoff: changed state, evidence and limits, remaining work, where to resume,
> and local commit IDs. Ask only when consequential meaning or intent remains unresolved.

Question-only chats with no new finding or decision need no progress entries. This checkpoint
does not grant database, model, external-service or trading authority.
