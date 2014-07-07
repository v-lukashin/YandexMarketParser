using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using YandexMarketParser.Models;

namespace YandexMarketParser
{
    class Downloader
    {
        #region -------------------Поля/конструкторы-----------------------
        private static Logger log = LogManager.GetCurrentClassLogger();

        private Catalog _catalog;
        private string catName;
        private readonly Dictionary<string, YandexMarket> _cache;
        private bool _checkCount;


        public static readonly string[] _proxyList;

        private static int[] _countPerProxy;
        private const int _limitPerProxy = 1;
        private const int _minTimeResponse = 10000;
        private static int _proxyIndex;
        
        private WebProxy _prx;
        private readonly WebClient cli;

        private const string patternTitle = @"<h3 class=""b-offers__title""><a (?:[-\w=""]*) class=""b-offers__name(?:.*?)"">(?<name>.*?)</a>";
        private const string patternPrice = @"<span class=""b(?:-old)?-prices__num"">(?<price>[\s\d]*)</span>";
        private const string patternDescription = @"<p class=""b-offers__spec"">(?<desc>[\w\p{P}\p{S}\s]*?)(?:<span class=""b-more""><span class=""b-more__dots"">.</span><span class=""b-more__text"">(?<desc2>.*?)</span>.*?</span>)?</p>";
        private const string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
        private const string patternCount = @"<p class=""search-stat"">Все цены\s. (?<cnt>\d+)\.";
        private const string patternOi = @"<strong class=""b-head-name"">ой...</strong>";

        static Downloader()
        {
            _proxyList = ReadProxyList();
            _countPerProxy = new int[_proxyList.Length];
            _proxyIndex = -1;
        }

        public Downloader(Catalog cat, WebProxy prx)
        {
            _catalog = cat;
            catName = _catalog.Name;
            _checkCount = true;
            _cache = Spider.AllSites;

            cli = new WebClient();
            cli.BaseAddress = "http://market.yandex.ru";
            _prx = prx;
            cli.Proxy = _prx;
            cli.Encoding = Encoding.UTF8;
        }
        #endregion
        public static void WaitCallback(object state)
        {
            WebProxy proxy = new WebProxy(Proxy);
            int index = _proxyIndex;
            new Downloader((Catalog)state, proxy).Processing();
            _countPerProxy[index]--;
        }

        private void Processing()
        {
            log.Info("Start : {0}", catName);
            do
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();

                    string root = DownloadPage(_catalog.Uri);

                    sw.Stop();

                    if (sw.ElapsedMilliseconds < _minTimeResponse)
                    {
                        Console.Write("`");
                        Thread.Sleep(_minTimeResponse - (int)sw.ElapsedMilliseconds);//для быстрых прокси, чтобы не палиться
                    }
                    if (root == null)
                    {
                        log.Error("Ошибка. Страница не получена({0}).\nProxy : {1}", _catalog.Uri, _prx.Address);
                        return;
                    }
                    if (new Regex(patternOi).Match(root).Success)
                    {
                        log.Info("Бежим, Джони! Нас спалили!!! Proxy : {0}", _prx.Address);
                        Thread.Sleep(3600000);
                        continue;
                    }
                    Regex regPrice = new Regex(patternPrice);

                    //Без этого обхода обработал 300к примерно. С ним появилась капча. Когда начал добавлять куки к запросу - забанили:( 
                    #region -------------Обход ограничения повторяющихся ссылок после 50 страницы---------
                    //****************************************************************************************
                    //*
                    //*Разделяем задание на 2 части, если предметов боьше 500.
                    //*
                    //****************************************************************************************
                    if (_checkCount && !_catalog.IsGuru)
                    {
                        Regex regCount = new Regex(patternCount);
                        Match mCount = regCount.Match(root);
                        if (mCount.Success && int.Parse(mCount.Groups["cnt"].Value) > 500)
                        {
                            Console.Write(",");
                            //Заменяем, иначе выкинет на первую страницу без ограничения по ценам
                            _catalog.Uri = _catalog.Uri.Replace("catalog", "search");

                            Regex regMin = new Regex(@"(?<=&mcpricefrom=)\d+");
                            Regex regMax = new Regex(@"(?<=&mcpriceto=)\d+");

                            if (!regMin.Match(_catalog.Uri).Success) _catalog.Uri += "&mcpricefrom=0";
                            if (!regMax.Match(_catalog.Uri).Success) _catalog.Uri += "&mcpriceto=15000000";

                            int max = int.Parse(regMax.Match(_catalog.Uri).Value);
                            int min = int.Parse(regMin.Match(_catalog.Uri).Value);

                            string tmpLink = _catalog.Uri;

                            int average = (max + min) / 2;

                            if (max != average && min != average)
                            {
                                //Выставляем верхнее значение для текущей задачи
                                _catalog.Uri = regMax.Replace(_catalog.Uri, "" + average);

                                //Выставляем нижнее значение для новой задачи
                                tmpLink = regMin.Replace(tmpLink, "" + (average + 1));

                                Catalog cat = new Catalog(_catalog.Name, _catalog.Parent, tmpLink, _catalog.IsGuru);
                                Spider.catalogs.Insert(Spider.catalogs.IndexOf(_catalog) + 1, cat);
                                new Downloader(cat, _prx).Processing();
                                continue;
                            }
                            else _checkCount = false;
                        }
                        else _checkCount = false;
                    }
                    #endregion

                    Regex reg = new Regex(patternTitle);
                    MatchCollection matches = reg.Matches(root);

                    Regex regDescr = new Regex(patternDescription);
                    int lastIndex = 0;
                    foreach (Match match in matches)
                    {
                        //Парсим название
                        string name = match.Groups["name"].Value;

                        if (!_cache.ContainsKey(name))//Если товара с таким именем не было
                        {
                            //Парсим описание
                            string descr = "";
                            Match descrMatch = regDescr.Match(root, match.Index);
                            if (descrMatch.Success)
                            {
                                descr = descrMatch.Groups["desc"].Value + descrMatch.Groups["desc2"].Value;//можно добавить костыль к кодом производителя
                            }
                            //Парсим цену
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
                    //Проверяем наличие следующей страницы
                    reg = new Regex(patternNext);
                    Match nextPage = reg.Match(root);

                    if (nextPage.Success)
                    {
                        _catalog.Uri = nextPage.Groups["uri"].Value;
                        _catalog.Uri = _catalog.Uri.Replace("amp;", "");//Удаляем, чтобы не перекидывало на первую страницу

                        Match matchPageNumer = new Regex(@"(?<=page=)\d+").Match(_catalog.Uri);
                        if ( matchPageNumer.Success && int.Parse(matchPageNumer.Value) > 50) break;
                    }
                    else break;//Если не найдено завершаем работу
                }
                catch (Exception exc)
                {
                    log.Error("DownloaderError {0} : {1}\nProxy : {2}", _catalog.Uri, exc, _prx.Address);
                }
            } while (true);
            Spider.catalogs.Remove(_catalog);
            //_catalog.Complited = true;
            //_countPerProxy[_currentProxyIndex]--;
            log.Info("Finish : {0}", catName);
        }
        #region -----------------Вспомогательные методы----------------------

        /// <summary>
        /// 10 попыток получить страницу. Если null, значит получить не удалось
        /// </summary>
        /// <param name="link"></param>
        /// <returns></returns>
        private string DownloadPage(string link)
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
                    Console.WriteLine("\nWebException({0}). Repeat downloading {1}", i, link);
                    Console.WriteLine(wexc.Message);
                    continue;
                }
            }
            return page;
        }
        private static string Proxy
        {
            get
            {
                int counter = 0;
                do
                {
                    if (counter++ > _proxyList.Length)
                    {
                        counter = 0;
                        Console.WriteLine("Не могу подобрать прокси. Возможно количество процессов больше чем {0}*{1}", _proxyList.Length, _limitPerProxy);
                        Thread.Sleep(60000);
                    }
                    _proxyIndex++;
                    _proxyIndex %= _proxyList.Length;
                } while (_countPerProxy[_proxyIndex] >= _limitPerProxy);

                _countPerProxy[_proxyIndex]++;
                return _proxyList[_proxyIndex];
            }
        }

        public static string UsedProxies(){
            string res = "[";
            foreach (var cnt in _countPerProxy)
            {
                res += cnt + "|";
            }
            res += "]";
            return res;
        }

        private static string[] ReadProxyList()
        {
            string[] result;
            using (FileStream fs = new FileStream("Proxies.txt", FileMode.OpenOrCreate))
            {
                byte[] b = new byte[fs.Length];
                fs.Read(b, 0, b.Length);
                string jsonStr = Encoding.UTF8.GetString(b);
                result = JsonConvert.DeserializeObject<string[]>(jsonStr);
                if (result == null || !result.Any())
                {
                    log.Fatal("Список прокси-серверов пуст");
                    Environment.Exit(0);
                }
            }
            return result;
        }
        #endregion
    }
}
