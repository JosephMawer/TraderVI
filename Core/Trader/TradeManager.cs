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
        private readonly TradeLogRepository _tradeLog;
        private readonly ActivePositionRepository _positions;
        private readonly PaperGhostTradeRepository _ghostTrades;

        public TradeManager(bool ghost)
        {
            _wsTrade = new wstrade.WSTrade();
            _tradeLog = new TradeLogRepository();
            _positions = new ActivePositionRepository();
            _ghostTrades = new PaperGhostTradeRepository();
            this.ghost = ghost;
        }

        /// <summary>
        /// Logs a buy: simulates/places the order, records a BUY trade, and opens a position
        /// with a -10% stop price. Refuses to double-open the same symbol.
        /// </summary>
        public async Task<bool> Buy(
            string symbol,
            int shares,
            decimal price,
            string? notes = null,
            Guid? originalPickId = null,
            double? entryComposite = null,
            string reason = "Manual entry")
        {
            symbol = symbol.ToUpperInvariant();

            if (shares <= 0 || price <= 0)
            {
                Console.WriteLine($"[TradeManager] Rejected BUY {symbol}: shares and price must be positive.");
                return false;
            }

            var existing = await _positions.GetPositionBySymbol(symbol);
            if (existing is not null)
            {
                Console.WriteLine($"[TradeManager] Rejected BUY {symbol}: an active position already exists (entered {existing.EntryDate:yyyy-MM-dd} @ {existing.EntryPrice:C}). Sell it before re-entering.");
                return false;
            }

            var amount = decimal.Round(price * shares, 2);
            PlaceOrder(wstrade.OrderSubType.buy_quantity, symbol, shares, price, amount);

            var tradeDate = DateTime.Now;
            await _tradeLog.InsertTrade(
                symbol: symbol,
                tradeType: "BUY",
                tradeDate: tradeDate,
                shares: shares,
                price: price,
                amount: amount,
                commission: 0m,
                netAmount: amount,
                reason: reason,
                entryComposite: entryComposite,
                notes: notes);

            var stopLossPrice = decimal.Round(price * (1 - StopLossFraction), 2);
            var warningPrice = decimal.Round(price * (1 - WarningFraction), 2);

            var positionId = await _positions.InsertPosition(
                symbol: symbol,
                entryDate: tradeDate.Date,
                entryPrice: price,
                shares: shares,
                costBasis: amount,
                originalPickId: originalPickId,
                stopLossPrice: stopLossPrice,
                warningPrice: warningPrice,
                notes: notes);

            Console.WriteLine($"[TradeManager] Logged BUY {shares} {symbol} @ {price:C} = {amount:C}");
            Console.WriteLine($"               Position {positionId} opened | stop {stopLossPrice:C} (-{StopLossFraction:P0}), warning {warningPrice:C} (-{WarningFraction:P0})");
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

            var amount = decimal.Round(price * position.Shares, 2);
            PlaceOrder(
                wstrade.OrderSubType.sell_quantity,
                symbol,
                position.Shares,
                price,
                amount);

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

        private void PlaceOrder(wstrade.OrderSubType side, string symbol, int shares, decimal price, decimal amount)
        {
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
