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

        private Catalog _catalog;
        private string catName;
        private readonly Dictionary<string, YandexMarket> _cache;

        private readonly WebClient cli;

        private const string patternTitle = @"<h3 class=""b-offers__title""><a (?:[-\w=""]*) class=""b-offers__name(?:.*?)"">(?<name>.*?)</a>";
        private const string patternPrice = @"<span class=""b(?:-old)?-prices__num"">(?<price>[\s\d]*)</span>";
        private const string patternDescription = @"<p class=""b-offers__spec"">(?<desc>[\w\p{P}\p{S}\s]*?)(?:<span class=""b-more""><span class=""b-more__dots"">.</span><span class=""b-more__text"">(?<desc2>.*?)</span>.*?</span>)?</p>";
        private const string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
        private const string patternCountGuru = @"<p>[\s]*выбрано.моделей[\s]*.+?(?<cnt>[\d]+)</p></div></form>";//к.о.с.т.ы.л.ь(начало)

        public Downloader(Catalog cat)
        {
            _catalog = cat;
            catName = _catalog.Name;
            //_link = cat.Uri;
            _cache = Spider.AllSites;

            cli = new WebClient();
            cli.BaseAddress = "http://market.yandex.ru";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;
        }

        public static void WaitCallback(object state)
        {
            //StateOptions opt = (StateOptions)state;
            new Downloader((Catalog)state).Processing();
        }

        public void Processing()
        {
            Console.WriteLine("Start : {0}", catName);
            do
            {
                try
                {
                    string root = DownloadPage(_catalog.Uri);

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
                    foreach (Match match in matches)
                    {
                        string name = match.Groups["name"].Value;

                        if (!_cache.ContainsKey(name))
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

                            YandexMarket s = new YandexMarket { Name = name, Price = price, Description = descr, Catalog = _catalog.Name, Parent = _catalog.Parent };
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
                        if (_catalog.IsGuru) _catalog.Uri = new Regex("amp;").Replace(_catalog.Uri, "");
                    }
                    else break;
                }
                catch (Exception exc)
                {
                    log.Error("DownloaderError {0} : {1}", _catalog.Uri, exc);
                    Console.WriteLine("#####DownloaderError##{0}", _catalog.Uri);
                }
            } while (true);
            _catalog.Uri = null;
            Console.WriteLine("Finish : {0}", catName);
        }

        /// <summary>
        /// 10 попыток получить страницу. Если null, значит получить не удалось
        /// </summary>
        /// <param name="link"></param>
        /// <returns></returns>
        public string DownloadPage(string link)
        {
            string page = null;
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    page = cli.DownloadString(link);
                    break;
                }
                catch (WebException wexc)
                {
                    Console.WriteLine("WebException({0}). Repeat downloading {1}", i, link);
                    Console.WriteLine(wexc.Message + wexc.StackTrace);
                    continue;
                }
            }
            return page;
        }
    }
}
