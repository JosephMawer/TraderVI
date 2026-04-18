# Training Metrics (Hercules Output)

This doc explains how to interpret the metrics printed during training by `UnifiedProfitTrainer`.

The system trains multiple **profit model kinds**:

- `Regression` (predicts a numeric forward return; used as a ranking hint)
- `ThreeWayClassification` (predicts Buy/Hold/Sell; used for decision confirmation)
- `BinaryClassification` (predicts whether an event will happen; used as additional “hints”)

---

## Regression training + ranking diagnostics

### What regression is doing
Regression models try to predict a number:
- “What will the return be over the next N days?”

You will see standard regression metrics:
- **RMSE**: typical prediction error size (lower is better)
- **MAE**: average absolute error (lower is better)
- **R²**: how much better the model is than always predicting the average (can be negative in finance)

### Why ranking diagnostics are printed
The trading strategy is “pick the best stock”, so *ranking quality* matters more than exact numeric accuracy.

The trainer prints:

- **Spearman correlation**: measures whether higher predictions tend to correspond to higher realized returns (ranking agreement).
  - `+1` perfect ordering, `0` no relationship, `-1` reversed ordering.

- **Top buckets** (top 10% / 5% / 1%): takes the highest predicted samples and computes the *actual realized mean return* inside those buckets.
  - If the top buckets outperform the overall average, the regression model can still be useful as a ranking hint.

- **Directional accuracy**: whether the predicted sign of return matches the actual sign (up vs down).

---

## 3-way training + confusion matrix + per-class precision/recall

### What 3-way classification is doing
3-way models predict a discrete outcome:
- `Sell`, `Hold`, or `Buy`

This shifts the problem from “predict the exact number” to “predict the decision category”.

### What the confusion matrix means
The **confusion matrix** is a table of counts:

- Rows = actual class
- Columns = predicted class

It shows where the model is confused (e.g., predicting Hold when it should be Buy).

### Per-class precision/recall
Printed per class (Sell/Hold/Buy):

- **Precision (Buy)**: when the model predicts Buy, how often was it actually Buy?
- **Recall (Buy)**: of all true Buy cases, how many did the model detect?

In trading, Buy/Sell precision is often more important than Hold performance.

---

## Binary training + positive-class precision/recall + predicted-positive rate

### What binary classification is doing
Binary event models predict yes/no events within the horizon, such as:
- breakout vs prior high
- breakout by ATR multiple
- volatility expansion

### Why “positive-class” stats matter
Events can be rare. In that case:
- Accuracy can look good even if the model always predicts “no event”.

So the trainer prints positive-class stats on the test set:

- **Precision (Event=true)**: when the model predicts the event, how often did it actually happen?
- **Recall (Event=true)**: of all true events, how many did the model catch?
- **Actual positive rate**: how often the event actually occurs
- **Predicted positive rate**: how often the model predicts the event

If predicted positive rate is near zero, the model is effectively “always negative” and not useful as a hint yet.

---

## Forward-return outlier skip (only applies when ForwardReturn != 0)

Some forward returns are absurd due to:
- splits / reverse splits
- corrupted bars
- data glitches

Those can dominate loss and distort training.

So training samples are skipped when:
- `abs(ForwardReturn) > MaxAbsForwardReturn`

But this is only applied when `ForwardReturn != 0` so that:
- event labelers (which intentionally output `ForwardReturn = 0`) are not affected.

---

## Class-balance printing for 3-way and binary models

The trainer prints how many training/test samples fall into:
- Buy / Hold / Sell (for 3-way)
- (and for binary models it still shows Buy/Hold distribution, since events are encoded using Buy vs Hold)

This is a key sanity check:
- If one class dominates, accuracy becomes less meaningful.
- It also indicates whether label thresholds are too strict or too loose.

---

## Related files
- Trainer: `Core/ML/Engine/Profit/UnifiedProfitTrainer.cs`
- Model definitions: `Core/ML/Engine/Profit/ProfitModelDefinition.cs`
- Model registry: `Core/ML/Engine/Profit/ProfitModelRegistry.cs`
- Runtime inference: `Core/ML/Engine/Profit/UnifiedProfitSignalModel.cs`