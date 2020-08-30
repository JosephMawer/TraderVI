using Abot2.Crawler;
using Abot2.Poco;
using AlphaVantage.Net.Stocks;
using AlphaVantage.Net.Stocks.TimeSeries;
using Core;
using Core.Db;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using AngleSharp.Dom;
using ConsoleTables;
using AngleSharp.Html.Dom;

namespace Core.TMX
{
    // https://www.tmxmoney.com/sitemap.xml
    // https://www.tmxmoney.com/robots.txt

    public abstract class TMXBase
    {
        // Abot web crawler
        protected static IWebCrawler crawler;

        protected IHtmlDocument HtmlDocument { get; set; }

        public TMXBase()
        {
            var config = new CrawlConfiguration
            {
                MaxPagesToCrawl = 1, //Only crawl 10 pages
                MinCrawlDelayPerDomainMilliSeconds = 3000 //Wait this many millisecs between requests
            };
            crawler = new PoliteWebCrawler(config);
            crawler.PageCrawlCompleted += PageCrawlCompleted;
        }

        /// <summary>
        /// This event is fired each time the crawler completes and sets the base html
        /// document ready for parsing 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void PageCrawlCompleted(object sender, PageCrawlCompletedArgs e)
        {
            HtmlDocument = e.CrawledPage.AngleSharpHtmlDocument;
        }
    }
}
