using Abot2.Crawler;
using Abot2.Poco;
using AngleSharp.Html.Dom;
using System;
using System.Threading.Tasks;

namespace Core.TMX
{
    // https://www.tmxmoney.com/sitemap.xml
    // https://www.tmxmoney.com/robots.txt

    public abstract class TMXBase
    {
        // Abot web crawler
        protected IWebCrawler crawler;

        protected IHtmlDocument HtmlDocument { get; set; }

        public TMXBase() { }


        protected async Task Crawler(string url)
        {
            var config = new CrawlConfiguration
            {
                MaxPagesToCrawl = 1, //Only crawl 10 pages
                MinCrawlDelayPerDomainMilliSeconds = 3000 //Wait this many millisecs between requests
            };
            crawler = new PoliteWebCrawler(config);
            crawler.PageCrawlCompleted += PageCrawlCompleted;
            Uri uriToCrawl = new Uri(url);

            var result = await crawler.CrawlAsync(uriToCrawl);
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
