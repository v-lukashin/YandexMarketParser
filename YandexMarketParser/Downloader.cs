using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
        private bool _countItemsOK; //Укладывается ли количество предметов в ограничение
        private static readonly string[] _proxy = new string[] { "http://77.43.143.31:3128", "http://77.50.220.92:8080", "http://109.172.51.147:80", "http://31.28.23.219:8118",
            "http://62.176.28.41:8088","http://176.194.189.56:8080","http://195.62.78.1:3128","http://31.28.6.13:8118","http://94.228.205.33:8080", "http://62.176.13.22:8088", //Россия
        "http://93.181.161.198:8080",//Польша
        "http://79.135.207.34:8080",//Украина
        //"http://212.69.8.2:8080",//Сербия
        //"http://118.69.205.202:4624",//Вьетнам
        "http://178.33.249.19:80"//, "http://93.184.33.166:8080"//Франция
        };

        private static int _currentProxyNumber = -1;
        public static string Proxy { get {
            _currentProxyNumber++;
            _currentProxyNumber %= _proxy.Length;
            return _proxy[_currentProxyNumber]; 
        } }

        WebProxy prx;
        private readonly WebClient cli;

        private const string patternTitle = @"<h3 class=""b-offers__title""><a (?:[-\w=""]*) class=""b-offers__name(?:.*?)"">(?<name>.*?)</a>";
        private const string patternPrice = @"<span class=""b(?:-old)?-prices__num"">(?<price>[\s\d]*)</span>";
        private const string patternDescription = @"<p class=""b-offers__spec"">(?<desc>[\w\p{P}\p{S}\s]*?)(?:<span class=""b-more""><span class=""b-more__dots"">.</span><span class=""b-more__text"">(?<desc2>.*?)</span>.*?</span>)?</p>";
        private const string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
        private const string patternCount = @"<p class=""search-stat"">Все цены\s. (?<cnt>\d+)\.";
        private const string patternOi = @"<strong class=""b-head-name"">ой...</strong>";
        
        public Downloader(Catalog cat)
        {
            _catalog = cat;
            catName = _catalog.Name;
            //_link = cat.Uri;
            _cache = Spider.AllSites;

            cli = new WebClient();
            cli.BaseAddress = "http://market.yandex.ru";
            //cli.Proxy = new WebProxy("http://62.176.13.22:8088");
            prx = new WebProxy(Proxy);
            cli.Proxy = prx;
            cli.Encoding = Encoding.UTF8;
        }
        public static void WaitCallback(object state)
        {
            new Downloader((Catalog)state).Processing();
        }

        public void Processing()
        {
            log.Info("Start : {0}", catName);
            do
            {
                try
                {
                    string root = DownloadPage(_catalog.Uri);

                    if (root == null)
                    {
                        log.Error("Ошибка. Страница не получена({0}).\nProxy : {1}", _catalog.Uri, prx.Address);
                        return;
                    }
                    if (new Regex(patternOi).Match(root).Success)
                    {
                        log.Info("Бежим, Джонни, нас спалили!!!");
                        Thread.Sleep(600000);
                        continue;
                    }
                    Regex regPrice = new Regex(patternPrice);

                    //Без этого обхода обработал 300к примерно. С ним появилась капча. Когда начал добавлять куки к запросу - забанили:( 
                    #region -------------Обход ограничения повторяющихся ссылок после 50 страницы---------
                    //****************************************************************************************
                    //*
                    //*Разделяем задание на 2 части, если предметов боьше 500. Одна часть остается в этом потоке, вторая запускается в новом.
                    //*Границу разделения определяем как среднее арифметическое всех цен данной страницы.
                    //*
                    //****************************************************************************************
                    if (_countItemsOK || !_catalog.IsGuru)
                    {
                        Regex regCount = new Regex(patternCount);
                        Match mCount = regCount.Match(root);
                        if (mCount.Success && int.Parse(mCount.Groups["cnt"].Value) > 500)
                        {
                            //Заменяем, иначе выкинет на первую страницу без ограничения по ценам
                            _catalog.Uri = new Regex("catalog").Replace(_catalog.Uri, "search");

                            //Считаем среднее арифметическое всех цен на данной странице
                            MatchCollection mcPrice = regPrice.Matches(root);
                            int totalPrice = 0;
                            foreach (Match match in mcPrice)
                            {
                                totalPrice += int.Parse(new Regex(@"\s").Replace(match.Groups["price"].Value, ""));
                            }
                            int average = totalPrice / mcPrice.Count;

                            Regex regMin = new Regex(@"(?<=&mcpricefrom=)\d+");
                            Regex regMax = new Regex(@"(?<=&mcpriceto=)\d+");

                            Match mMin = regMin.Match(_catalog.Uri);
                            Match mMax = regMax.Match(_catalog.Uri);

                            string tmpLink = _catalog.Uri;

                            //Выставляем верхнее значение для текущей задачи
                            if (mMax.Success)
                            {
                                _catalog.Uri = regMax.Replace(_catalog.Uri, "" + average);
                            }
                            else
                            {
                                _catalog.Uri += "&mcpriceto=" + average;
                            }

                            //Выставляем нижнее значение для новой задачи
                            if (mMin.Success)
                            {
                                tmpLink = regMin.Replace(tmpLink, "" + (average + 1));
                            }
                            else
                            {
                                tmpLink += "&mcpricefrom=" + (average + 1);
                            }

                            //Запускаем новую задачу и добавляем каталог этой задачи ко всем очтальным
                            Catalog tmpCatalog = new Catalog(_catalog.Name, _catalog.Parent, tmpLink, _catalog.IsGuru);
                            Spider.catalogs.Add(tmpCatalog);
                            ThreadPool.QueueUserWorkItem(WaitCallback, tmpCatalog);

                            //Продолжаем текущую задачу
                            continue;
                        }
                        else _countItemsOK = true;
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
                        _catalog.Uri = new Regex("amp;").Replace(_catalog.Uri, "");//Удаляем, чтобы не перекидывало на первую страницу
                    }
                    else break;//Если не найдено завершаем работу
                }
                catch (Exception exc)
                {
                    log.Error("DownloaderError {0} : {1}\nProxy : {2}", _catalog.Uri, exc, prx.Address);
                }
            } while (true);
            _catalog.Complited = true;
            log.Info("Finish : {0}", catName);
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
                    Console.WriteLine("\nWebException({0}). Repeat downloading {1}", i, link);
                    Console.WriteLine(wexc.Message);
                    continue;
                }
            }
            return page;
        }
    }
}
