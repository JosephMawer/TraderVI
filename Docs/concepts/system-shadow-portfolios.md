# System Shadow portfolios, in plain language

Shadow answers a practical question: “If TraderVI had made the choices itself, using the amount of money I
actually made available, what would have happened?”

It runs four separate virtual accounts. Two follow Delphi's Continuation list and two follow Breakout; each
lens gets a Top 3 and a Top 5 version. They are alternatives, not one pile of money.

Every morning, Shadow freezes that day's valid Delphi list. Starting around 09:50, it may virtually buy the
highest-ranked stocks that are not going down. It uses whole shares, realistic friction, limited cash, and
pending orders so it never claims a price that was already gone before the decision was knowable.
Each buy order has one exact next-bar fill window. If the app restarts or misses that window, Shadow cancels
the old order and makes the candidate qualify again from current five-minute evidence before it can try
again.

Shadow keeps good holdings as long as they stay healthy. It removes losses quickly, can rotate a weak
second-day holding into a stronger current candidate, and stops taking new risk after serious portfolio
losses. It can only make Ghost trades. There is no Wealthsimple connection and no real order.

Fifteen-minute trailing state is keyed by bar start time. One bar can arm or raise a stop exactly once; its
earlier low is never compared with a stop that did not exist until that same bar closed.

If a portfolio falls 10% from its closing high-water mark, it waits for an explicit review. Resuming means
the operator accepts the current virtual value as the new drawdown baseline; the earlier breach remains in
the audit history.

Athena remains separate. Athena asks whether Delphi's predictions were good at fixed checkpoints. Shadow
asks whether one complete, capital-limited trading policy made money. We need both because they measure
different things.
