namespace Core.Indicators.Models
{
    public struct PriceRange
    {
        /// <summary>
        /// represents a dollar value, such a 1, 2, or 3 (dollars)
        /// </summary>
        public int Low { get; set; }

        /// <summary>
        /// represents a dollar value, such as 15, 20, 45 (dollars)
        /// </summary>
        public int High { get; set; }

        public PriceRange(int low, int high)
        {
            Low = low;
            High = high;
        }
    }
}
