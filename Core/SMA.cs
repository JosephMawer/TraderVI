using System;

namespace Core
{

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
}
