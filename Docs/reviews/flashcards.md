# Review Flashcards

Question/answer pairs distilled from ADRs and concepts. Used in review
sessions ("quiz me" / "review").

## Conventions

- One card per `### Q:` heading.
- Tag each card with the same domain tags as ADRs/concepts.
- Cite the source (`Source: ADR-0001` or `Source: concepts/<file>.md`).
- Keep answers short — 1–4 sentences. The point is recall, not exposition.

## Cards

### Q: Why are Granville indicators implemented as per-category classes rather than one big class?
- **Domains:** architecture, technical-indicators
- **Source:** ADR-0001

**A:** Categories share derived state internally (e.g., Plurality #1–#4 all
reuse the advancers/decliners comparison) but have different data
dependencies between each other. Per-category grouping matches the natural
cohesion, allows incremental implementation, and isolates failures.

### Q: Why XIU instead of the raw S&P/TSX Composite?
- **Domains:** finance-fundamentals, decision-engine
- **Source:** ADR-0002

**A:** XIU is a tradable ETF with reliable TMX data, captures the high-volume
core of the TSX where the momentum strategy actually operates, and gives us
a single benchmark across all subsystems for consistency.

### Q: Why doesn't Granville's literal #15/#16 work on XIU?
- **Domains:** technical-indicators, finance-fundamentals
- **Source:** ADR-0003

**A:** Granville's #15/#16 exploit the DJIA's *price-weighted* construction,
where a few high-priced names can carry the index. XIU is cap-weighted,
which already neutralizes that distortion. We reformulated the *idea*
("narrow leadership predicts a stalled move") as ScoreB (top-3
concentration) + ScoreC (narrowness) instead of porting the rule directly.

### Q: What do ScoreB and ScoreC measure, and why do we need both?
- **Domains:** technical-indicators, math-statistics
- **Source:** ADR-0003, concepts/price-weighted-contribution.md

**A:** ScoreB measures how top-heavy the same-direction push was (top-3
share of |contribution|). ScoreC measures how many constituents disagreed
with XIU's direction. They aren't redundant: a move can be top-heavy
without being narrow (and vice versa). The AND-gate captures Granville's
"narrow *and* top-heavy" thesis, which is what predicts a stall.

### Q: Why is the Weighting indicator one-sided (fires only on up-days)?
- **Domains:** technical-indicators, decision-engine
- **Source:** ADR-0003

**A:** Backtest showed v1-rule down-day triggers had a positive 1d forward
return — i.e., a mean-reversion bounce, not a narrow-decline warning. The
indicator is interpreted as a long-side warning gate, so firing on down-days
would change its meaning. A down-side analogue, if ever justified, becomes
its own indicator — not an expansion of this one.

### Q: Why is the price-weighted contribution called a "proxy" rather than an index?
- **Domains:** finance-fundamentals, math-statistics
- **Source:** concepts/price-weighted-contribution.md

**A:** The sum of price-weighted `contribution_i` values does *not* equal
XIU's actual cap-weighted return. We use the proxy only to derive ScoreB
and ScoreC — structural descriptors of the day's move — not to predict
returns. The proxy preserves the Granville-style narrowness signal that
cap weights would smooth away.
