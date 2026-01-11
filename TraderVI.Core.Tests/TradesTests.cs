using Shouldly;
using System;
using Xunit;

namespace TraderVI.Core.Tests
{
    public class TradesTests
    {
        //[Fact]
        //public void NegativePriceMovementShouldRaiseSellSignal()
        //{
        //    var activeTrade = new Trade("WPRT", 10.00, 10, DateTime.Now, false);

        //    var priceMovement = new[] { 9.99, 9.93, 9.90, 9.86, 9.85, 9.80 };
        //    foreach (var price in priceMovement)
        //    {
        //        activeTrade.Price = price;
        //        if (price > 9.85)
        //            activeTrade.Sell.ShouldBeFalse();
        //        else activeTrade.Sell.ShouldBeTrue();
        //    }
        //}
        //[Fact]
        //public void PositiveSellSignalShouldNotRaiseSellSignal()
        //{
        //    var activeTrade = new Trade("WPRT", 10.00, 10, DateTime.Now, false);

        //    var priceMovement = new[] { 10, 10.05, 10.10, 10.20, 10.30, 12, 20 };
        //    foreach (var price in priceMovement)
        //    {
        //        activeTrade.Price = price;
        //        activeTrade.Sell.ShouldBeFalse();
        //    }
        //}

        //[Fact]
        //public void SellsOnDip()
        //{
        //    var activeTrade = new Trade("WPRT", 10.00, 10, DateTime.Now, false);

        //    var priceMovement = new[] { 10.01, 10.05, 10.10, 10, 9.99};
        //    foreach (var price in priceMovement)
        //    {
        //        activeTrade.Price = price;
        //        if (price > 10)
        //            activeTrade.Sell.ShouldBeFalse();
        //        else activeTrade.Sell.ShouldBeTrue();

        //    }
        //}
    }
}
