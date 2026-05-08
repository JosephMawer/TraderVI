# Open Questions

Things we punted on and need to revisit. Cleared as decisions are made
(those then become ADRs).

## Active

### Weighting category (Granville #15–#16) — design questions in flight
- **Q:** How do we know **40%** is the right breadth-gate threshold for
  detecting "narrow leadership"? Currently a placeholder. Needs empirical
  tuning once we have historical XIU constituent data.
- **Q (Q-new A):** AND-gate vs. weighted-average for combining ScoreB
  (concentration) + ScoreC (narrowness)?
- **Q (Q-new B):** Initial thresholds — `ScoreB ≥ 0.50`, `ScoreC ≥ 0.60`,
  `K = 3`. Reasonable starting defaults?
- **Q (Q-new C):** Path 2 (price-weighted Dow-style proxy) for v1, with
  Path 3 (true cap weights) as a future upgrade behind the same provider
  interface?
- **Q (Q-new D):** Static C# constituent list (`Core/Config/Xiu60Constituents.cs`)
  for v1, promote to DB table later?
- **Q (Q-new E):** Use a correlated subquery for "previous trading day" in
  the SQL, or introduce a calendar table?
- **Q (Q-new F):** Skip Hermes-side validation that all 60 constituents are
  present in the universe; rely on the runtime ≥50/60 Neutral guard?

## Resolved

*(none yet — entries move here with the ADR number that closed them)*
