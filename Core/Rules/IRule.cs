using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Rules
{
    public interface IRule
    {
        // condition to be evaluated

        // action to execute 

        bool Execute();
    }

    public class SimpleRule : IRule
    {
        public SimpleRule()
        {

        }

        public bool Execute()
        {
            // check if 5%
            //if (quote.price < trade.BoughtAt)
            //{
                // sell
            //}

            return false;
        }
    }
}
