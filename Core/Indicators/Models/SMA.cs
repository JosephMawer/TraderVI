using System;

namespace Core
{
    // Created SMA and EMA structs, even though they are identical, just for readability purposes
    public struct SMA
    {
        public readonly decimal Price;
        public readonly DateTime Date;
        public SMA(decimal price, DateTime date)
        {
            Price = price;
            Date = date;
        }
    }

    public struct EMA
    {
        public readonly decimal Price;
        public readonly DateTime Date;
        public EMA(decimal price, DateTime date)
        {
            Price = price;
            Date = date;
        }
    }
}
