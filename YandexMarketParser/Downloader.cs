using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YandexMarketParser.Models;

namespace YandexMarketParser
{
    class Downloader
    {
        Logger log = LogManager.GetCurrentClassLogger();

        //private string _link;
        private Catalog _catalog;
        private int _threadId;
        private readonly Dictionary<string, YandexMarket> _cache;

        private const string patternTitle = @"<h3 class=""b-offers__title""><a (?:[-\w=""]*) class=""b-offers__name(?:.*?)"">(?<name>.*?)</a>";
        private const string patternPrice = @"<span class=""b(?:-old)?-prices__num"">(?<price>[\s\d]*)</span>";
        private const string patternDescription = @"<p class=""b-offers__spec"">(?<desc>[\w\p{P}\p{S}\s]*?)(?:<span class=""b-more""><span class=""b-more__dots"">.</span><span class=""b-more__text"">(?<desc2>.*?)</span>.*?</span>)?</p>";
        private const string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";

        public Downloader(Catalog cat)
        {
            _catalog = cat;
            //_link = cat.Uri;
            _cache = Spider.AllSites;
            _threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static void WaitCallback(object state)
        { 
            //StateOptions opt = (StateOptions)state;
            new Downloader((Catalog)state).Processing();
        }

        public void Processing()
        {
            Spider._curPages.Add(_threadId, _catalog);
            Console.WriteLine("Start : {0}", _catalog.Name);
            do
            {
                try
                {
                    string root = Spider.DownloadPage(_catalog.Uri);

                    if (root == null)
                    {
                        log.Error("Ошибка. Страница не получена({0}).", _catalog.Uri);
                        Console.WriteLine("Ошибка. Страница не получена({0}).", _catalog.Uri);
                        return;
                    }

                    Regex reg = new Regex(patternTitle);
                    MatchCollection matches = reg.Matches(root);

                    Regex regDescr = new Regex(patternDescription);
                    Regex regPrice = new Regex(patternPrice);
                    int lastIndex = 0;
                    foreach(Match match in matches)
                    {
                        string name = match.Groups["name"].Value;

                        YandexMarket s = null;
                        if (_cache.ContainsKey(name))
                        {
                            s = _cache[name];
                        }
                        else
                        {
                            string descr = "";
                            Match descrMatch = regDescr.Match(root, match.Index);
                            if (descrMatch.Success)
                            {
                                descr = descrMatch.Groups["desc"].Value + descrMatch.Groups["desc2"].Value;//можно добавить костыль к кодом производителя
                            }
                            int price = -1;
                            Match priceMatch = regPrice.Match(root, lastIndex, match.Index - lastIndex);
                            lastIndex = match.Index;

                            if (priceMatch.Success)
                            {
                                string strPrice = priceMatch.Groups["price"].Value;
                                string priceReplace = new Regex(@"\s").Replace(strPrice, "");
                                price = int.Parse(priceReplace);
                            }

                            s = new YandexMarket { Name = name, Price = price, Description = descr, Catalog = _catalog.Name, Parent = _catalog.Parent };
                            _cache.Add(name, s);
                        }
                    }
                    Spider.countVisitsOnPages++;
                    Console.Write(".");

                    reg = new Regex(patternNext);
                    Match nextPage = reg.Match(root);

                    if (nextPage.Success)
                    {
                        _catalog.Uri = nextPage.Groups["uri"].Value;
                        //Spider._curPages[_threadId].Uri = _link;
                        //Console.WriteLine(_link);
                    }
                    else break;

                }
                catch (Exception exc)
                {
                    log.Error("DownloaderError {0} : {1}", _catalog.Uri, exc);
                    Console.WriteLine("#####DownloaderError##{0}", _catalog.Uri);
                }
            } while (true);
            Spider._curPages.Remove(_threadId);
            Console.WriteLine("Finish : {0}", _catalog.Name);
        }
    }
}
