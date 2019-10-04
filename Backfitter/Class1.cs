using System;

namespace Backfitter
{

    public class Class1
    {
        // to test a backfitter -- we first need a bunch of algorithms we can test against
        // sooo.. what algorithms shall we do
        // How to think about one of these algorithms??
        // -- we want to define a group of technical indicators based on some configuration
        //    of said group. i.e. when sma_50 crosses sma_200 do X, 
        // -- define some weighted based algorithm to determine, based on the current values
        //    of the technical indicators, when to sell/buy
        // -- we need a class/project that can manage implementing the rules we give it, 
        //    i.e. by passing it some configuration
        //    
        //    [this should be done in the AlphaVantage.Net.Stocks project]
        // -- each technical indicator should be it's own little thread/process that can be
        //    started or stopped (i.e. cancellation), runs asynchronously, reports values, etc.
        // -- for each indicators implementation we need a way to feed data into it or tell it
        //    what dates to use, so we can effectively back test it.
        // for each indicator, we can have metadata about it
        // -- so for the sma50, we can tell things like,
        //  52 week high/low, volume



        // Backfitting
        //   -- backfitter.core
        // AlphaVantage.API
        //   -- AlphaVantage.Net.Core
        //   -- AlphaVantage.Net.Stocks
        //
        //
        //
        //
        //



        // 1) this thing should take command line arguments 
        // 2) we need a database of some sort (good reason to work with EF Core 6)
        // 3) would be a nice time to include some cool linq queries

    }
}
