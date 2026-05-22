# End-of-Thread Prompt — Doc Hygiene & ADR Capture

> Copy-paste this prompt at the end of any substantive thread (design,
> calibration, debugging, refactor) before closing the chat. The agent
> should treat the rest of this file as the prompt body.

---

We're wrapping up this thread. Before I close it, do a documentation pass
so the work we just did is preserved for future me. **Do not start any new
implementation work.** Only update docs, ADRs, flashcards, and the review
trail.

Follow these steps in order and report what you changed at the end.

## 1. Summarize the thread

In your reply, give me a 5-10 line recap covering:
- The **immediate problem** we worked on.
- The **decision(s) we actually reached** (including "we deferred X" or
  "we rejected Y" — negative results count).
- The **evidence** behind each decision (numbers, file paths, command
  invocations — concrete things I could rerun).
- Anything **still open** that we punted on.

Pause here and let me confirm the recap is accurate before continuing. If
I push back or correct anything, treat my correction as authoritative over
your reading of the transcript.

## 2. Decide what docs are affected

For each decision in the recap, classify it:

- **Meaningful design decision** (picked one approach over a viable
  alternative; introduced or deferred a threshold/parameter; added a
  dependency, table, file, or external service; consciously chose *not*
  to ship something with evidence) ⇒ needs an ADR.
- **Pure mechanical edit** (rename, format, missing using) ⇒ no ADR.
- **New conceptual idea that an ADR references** ⇒ also needs an entry
  under `Docs/concepts/`.
- **Deferred / open question** ⇒ goes into `Docs/reviews/open-questions.md`.

State the classification explicitly before making any file edits.

## 3. Create the ADR(s)

For every meaningful decision, create `Docs/adr/NNNN-kebab-title.md` using
the template in `Docs/adr/README.md`. **Use the next unused number.**

The ADR must include:
- **Status**, **Date** (today), and **1-4 domain tags** drawn from the
  taxonomy in `Docs/adr/README.md`.
- **Context** — what the problem was, what we knew going in, what data we
  had. Cite file paths for any probe / experiment we built.
- **Decision** — stated as an imperative ("Use X for Y" or "Defer X
  until Z").
- **Alternatives considered** — at least the realistic ones we discussed,
  with why each was rejected. If we rejected an option for a reason that
  could change later (e.g., "too small a sample"), say so.
- **Consequences** — what's locked in, what we gained even if we shipped
  no code, and **what observation would tell us this decision was wrong**.
- **Review questions** — 2-4 questions a future me should be able to
  answer. Favor "why" over "what".

Negative results (we deferred / rejected something) are ADR-worthy. Don't
skip an ADR just because no production code shipped.

## 4. Update the indexes

For each new ADR:
- Add a row to the index table in `Docs/adr/README.md`.
- Add the ADR under **every** domain tag it carries in
  `Docs/adr/by-domain.md`. Create the section if the domain is currently
  `*(none yet)*`.
- If the ADR documents a feature with a status row in
  `Docs/design-rules.md` (e.g., a Granville category, a Score, a pipeline
  stage), update that row's status column and link to the new ADR.

## 5. Update flashcards

In `Docs/reviews/flashcards.md`, add 1-3 new Q/A cards per ADR. Each card:
- Has a `### Q:` heading, a `- **Domains:**` line, a `- **Source:**` line
  citing the ADR (or concept), and an `**A:**` answer of 1-4 sentences.
- Asks *why*, not *what*. Cards that just restate the decision are not
  useful for review.
- For deferral ADRs, include at least one card on "why didn't fix X work"
  or "what evidence would reverse this decision".

## 6. Log open questions

For anything we deferred or punted on, append a bullet under the relevant
section of `Docs/reviews/open-questions.md`. Each entry should make the
**condition that would close the question** explicit (e.g., "rerun probe
X after data Y is available; reopen if hit-rate ≥ Z"). Vague open
questions ("revisit eventually") are not allowed.

## 7. Update concepts (only if applicable)

If a decision introduced a new conceptual idea referenced by the ADR,
create `Docs/concepts/<topic>.md` with a focused explanation. Otherwise
skip this step.

## 8. Preserve experimental artifacts

If we built a calibration probe, backtest script, or one-shot Sandbox tool
during the thread:
- Confirm it's checked into the repo (typically under `Sandbox/Probes/` or
  `Tools/`).
- Confirm the ADR mentions **how to rerun it** (exact command line) so
  future threads can replay the result against new data without
  reconstructing the experiment.
- If the probe wrote a CSV / artifact, mention its path in the ADR.

## 9. Verify nothing is broken

Run `dotnet build` against the solution. If anything failed because of
edits made earlier in the thread, surface the errors but do **not** start
fixing them — flag them so I can decide whether to fix now or open a new
thread.

## 10. Report

End your reply with a bulleted list of every file you touched, grouped by:
- **Created:** new ADRs / concepts.
- **Updated:** indexes, design-rules, flashcards, open-questions.
- **Skipped (with reason):** anything you considered touching but didn't.

Then stop. Don't start follow-up work; this prompt is the closer.

---

## Notes on tone

- **Negative results are first-class.** A thread that ended in "we deferred
  this" deserves the same documentation rigor as one that shipped a
  feature, often more — the next person (probably me) will be tempted to
  re-try and needs the evidence trail.
- **Cite, don't paraphrase, numbers.** If we ran a calibration that
  produced hit-rate 32.3% at h=5, put 32.3% in the ADR, not "low".
- **Keep ADRs tight.** Granularity is a feature; long prose is not.
