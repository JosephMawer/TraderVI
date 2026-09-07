# Delphi Live closed-market collection trial — 2026-09-06

The user authorized a controlled collection trial while the exchange was closed. The completed portion
was a historical source check using the production `TmxDelphiLiveMarketDataSource`, with actual current
request/receipt timestamps. It did not run a historical cycle as though it had been observed live.

## Bounded run and results

- Historical session: Friday, 2026-09-04.
- Symbols: XIU, RY and ENB; five-minute endpoints 09:35, 09:50 and 16:00 Toronto.
- Nine sequential logical requests, each with a twenty-second limit and the existing three-attempt
  transport policy; four-minute overall budget; stop after the first failed or missing exact response.
- The first invocation inside the sandbox failed at the first request with `HttpRequestException`.
  It stopped and retained a separate incomplete report. The user approved the network-enabled rerun.
- The approved rerun returned all nine exact completed bars, using nine transport attempts with no
  retries. The shared client validated UTC interval alignment, duplicate timestamps and OHLCV structure.
- Request latency: minimum 56.64 ms, median 81.51 ms, maximum 496.89 ms. These are historical retrieval
  timings for this small sample; they are not estimates of publication latency or full-watchlist capacity.
- Database connections, operational receipts, clean engineering cohorts, portfolio actions and broker
  operations: zero. The only saved artifact was a local metadata report; raw price/volume values were omitted.

Successful report:
`artifacts/delphi-live-collection-trials/closed-market-20260906-044525-994825f1b9bd46cebe06a1be5eac6d23.json`

SHA-256: `4C22E776CBBBCB4CBA164EF2064CD21243EAFFACC9853C1790C3ED4628337BCE`.
The detailed report is a local ignored artifact. This review note retains the non-price aggregate result.

## Remaining live trial

The reviewed calendar and [TMX's Labour Day notice](https://www.tsx.com/en/news?id=1185) place the next
session on Tuesday, September 8. A separate live trial must observe newly completed bars at the actual
collection times, verify the SQL commit deadline, measure the full target set alongside existing
collectors, and review missingness/recovery evidence. Historical requests cannot count toward the frozen
ten clean engineering cohorts. No future task, collection schedule or simulation portfolio was activated.

## Source and validation

`Sandbox/Probes/DelphiLiveClosedMarketTrialProbe.cs` is registered under the explicit slug
`delphi-live-closed-market-trial`. Its pinned dates and request limits are documented in the probe;
rerunning it is another external-service operation requiring authorization. Sandbox builds passed.
Existing compiler warnings remained; the focused Sandbox builds emitted no dependency-security warnings.
No dependency audit or package change was performed.
