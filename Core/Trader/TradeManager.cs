using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Core.Trader
{
    public class TradeManager
    {
        private readonly bool ghost;
        private wstrade.WSTrade _wsTrade;

        public TradeManager(bool ghost) 
        {
            _wsTrade = new wstrade.WSTrade();
            this.ghost = ghost;
        }

        public void Buy(string symbol, string exchange = "TSX")
        {
            if (!ghost)
            {
                //_wsTrade.PlaceOrder(new wstrade.LimitOrder(wstrade.OrderSubType.buy_quantity, ))
            }

            // log trade

            // add to watch list

        }

        public void Sell(string symbol)
        {
            if (!ghost)
            {
                //_wsTrade.PlaceOrder(new wstrade.LimitOrder(wstrade.OrderSubType.buy_quantity, ))
            }

            // log sell

            // remove from watch list


        }
    }
}
