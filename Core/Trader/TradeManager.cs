using System;
using System.Threading.Tasks;
using Core.Db;

namespace Core.Trader
{
    /// <summary>
    /// Records the trades you actually place into [dbo].[TradeLog] and manages the
    /// matching [dbo].[ActivePosition] lifecycle. In ghost mode no real Wealthsimple
    /// order is sent - a simulated fill is printed and only the database is updated.
    /// </summary>
    public class TradeManager
    {
        // Risk constants (StrategyVersion v3.0 defaults; tune from Hercules outputs).
        private const decimal StopLossFraction = 0.10m;   // -10% hard stop (capital preservation rule).
        private const decimal WarningFraction = 0.08m;    // -8% early warning for Sentinel monitoring.

        private readonly bool ghost;
        private readonly wstrade.WSTrade _wsTrade;
        private readonly ActivePositionRepository _positions;
        private readonly PaperGhostTradeRepository _ghostTrades;
        private readonly TrackedPositionOpeningRepository _positionOpenings;

        public TradeManager(bool ghost)
        {
            _wsTrade = new wstrade.WSTrade();
            _positions = new ActivePositionRepository();
            _ghostTrades = new PaperGhostTradeRepository();
            _positionOpenings = new TrackedPositionOpeningRepository();
            this.ghost = ghost;
        }

        /// <summary>
        /// Logs a buy and opens a tracked position with a -10% stop price.
        /// A Real execution mode is an operator-reported fill and is never routed.
        /// Refuses to double-open the same symbol.
        /// </summary>
        public async Task<bool> Buy(
            string symbol,
            int shares,
            decimal price,
            string? notes = null,
            Guid? originalPickId = null,
            double? entryComposite = null,
            string reason = "Manual entry",
            TrackedExecutionMode executionMode = TrackedExecutionMode.Ghost,
            string? accountLabel = null)
        {
            symbol = symbol.ToUpperInvariant();

            string? normalizedAccount =
                TrackedExecutionModeContract.NormalizeAccountLabel(executionMode, accountLabel);

            if (shares <= 0 || price <= 0)
            {
                Console.WriteLine($"[TradeManager] Rejected BUY {symbol}: shares and price must be positive.");
                return false;
            }

            var amount = decimal.Round(price * shares, 2);
            var tradeDate = DateTime.Now;
            var stopLossPrice = decimal.Round(price * (1 - StopLossFraction), 2);
            var warningPrice = decimal.Round(price * (1 - WarningFraction), 2);
            TrackedPositionOpenResult? opened = await _positionOpenings.TryOpenAsync(
                new TrackedPositionOpenRequest(
                    symbol,
                    tradeDate,
                    shares,
                    price,
                    reason,
                    notes,
                    originalPickId,
                    entryComposite,
                    stopLossPrice,
                    warningPrice,
                    executionMode,
                    normalizedAccount));
            if (opened is null)
            {
                Console.WriteLine($"[TradeManager] Rejected BUY {symbol}: an active position already exists. Sell or reconcile it before re-entering.");
                return false;
            }

            PlaceOrder(
                wstrade.OrderSubType.buy_quantity,
                symbol,
                shares,
                price,
                amount,
                executionMode);

            Console.WriteLine($"[TradeManager] Logged {executionMode.ToStorageValue().ToUpperInvariant()} BUY {shares} {symbol} @ {price:C} = {amount:C}");
            Console.WriteLine($"               Position {opened.PositionId} opened | stop {stopLossPrice:C} (-{StopLossFraction:P0}), warning {warningPrice:C} (-{WarningFraction:P0})");
            return true;
        }

        /// <summary>
        /// Logs a sell: finds the open position, simulates/places the order, computes realized
        /// P&amp;L and holding days from the position cost basis, records a SELL trade, and closes the position.
        /// </summary>
        public async Task<bool> Sell(
            string symbol,
            decimal price,
            string? notes = null,
            string reason = "Manual exit")
        {
            symbol = symbol.ToUpperInvariant();

            if (price <= 0)
            {
                Console.WriteLine($"[TradeManager] Rejected SELL {symbol}: price must be positive.");
                return false;
            }

            var position = await _positions.GetPositionBySymbol(symbol);
            if (position is null)
            {
                Console.WriteLine($"[TradeManager] Rejected SELL {symbol}: no active position found.");
                return false;
            }

            if (position.ExecutionMode == TrackedExecutionMode.Real)
            {
                Console.WriteLine($"[TradeManager] Rejected SELL {symbol}: real holdings require an operator-confirmed manual broker fill; no order was sent.");
                return false;
            }

            var amount = decimal.Round(price * position.Shares, 2);
            PlaceOrder(
                wstrade.OrderSubType.sell_quantity,
                symbol,
                position.Shares,
                price,
                amount,
                position.ExecutionMode);

            PaperGhostExitResult? exit = await _ghostTrades.TryRecordExitAsync(
                position.PositionId,
                price,
                DateTime.Now,
                reason,
                notes);
            if (exit is null)
            {
                Console.WriteLine($"[TradeManager] Rejected SELL {symbol}: position was already closed.");
                return false;
            }

            var sign = exit.RealizedPnL >= 0 ? "+" : "";
            Console.WriteLine($"[TradeManager] Logged SELL {exit.Shares} {symbol} @ {price:C} = {exit.Amount:C}");
            Console.WriteLine($"               Realized P&L {sign}{exit.RealizedPnL:C} ({sign}{exit.RealizedPnLPct:P2}) over {exit.HoldingDays}d | position {exit.PositionId} closed");
            return true;
        }

        private void PlaceOrder(
            wstrade.OrderSubType side,
            string symbol,
            int shares,
            decimal price,
            decimal amount,
            TrackedExecutionMode executionMode)
        {
            if (executionMode == TrackedExecutionMode.Real)
            {
                Console.WriteLine($"[REAL RECORD] Operator-reported {side} fill for {shares} {symbol} @ {price:C} (market value {amount:C}) - no order sent.");
                return;
            }

            if (ghost)
            {
                Console.WriteLine($"[GHOST] Simulated Wealthsimple {side} {shares} {symbol} @ {price:C} (market value {amount:C}) - no order sent.");
                return;
            }

            // Live routing requires resolving the Wealthsimple security_id from the ticker,
            // which is not wired yet. Warn and fall through to logging so the book stays accurate.
            Console.WriteLine($"[TradeManager] Live Wealthsimple routing not yet wired for {symbol} (needs security_id lookup); trade will be logged only.");
        }
    }
}
