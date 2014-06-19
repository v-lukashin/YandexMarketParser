using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
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
    class Spider
    {
        public static Dictionary<string, YandexMarket> AllSites = new Dictionary<string, YandexMarket>(150000);//здесь хранится весь каталог

        public static long countVisitsOnPages = 0;//количество пройденых страниц
        private HashSet<string> _visitedPages;//uri посещенных страниц
        public static Dictionary<int, Catalog> _curPages;

        private const int _poolSize = 100;

        private Stopwatch sw;

        Logger log = LogManager.GetCurrentClassLogger();

        Queue<Catalog> queue;
        private readonly Catalog _rootCatalog = new Catalog { Name = "ROOT", Parent = "", IsGuru = false, Uri = "/catalog.xml" };

        private readonly Repository _rep;
        private const string _connectionString = "mongodb://localhost:27017/YandexMarket";

        private const string patternCatalog = @"<div class=""supcat(?<guru> guru)?""><a href=""(?<uri>/catalog.xml\?hid=\d*)"">(?:<img[\w\p{P}\p{S} ]*>)?(?<name>[-\w,. ]*)</a>";
        private const string patternAll = @"<a class=""top-3-models__title-link"" href=""(?<uri>[-\w\p{P}\p{S} ]*)"">Посмотреть все модели</a>";

        public Spider()
        {
            ThreadPool.SetMaxThreads(_poolSize, _poolSize);
            _rep = new Repository(new Db(_connectionString));
            _curPages = new Dictionary<int, Catalog>();
            ReadState();
            if (queue == null || queue.Count == 0)
            {
                queue = new Queue<Catalog>(new Catalog[] { _rootCatalog });
            }

            Console.Write("Download from db...");
            var all = _rep.GetAll();
            foreach (var it in all)
            {
                AllSites.Add(it.Name, it);
            }
            Console.WriteLine(AllSites.Count + " done");

            Console.WriteLine("Start {0}", DateTime.Now);

            Task consoleTask = new Task(ConsoleComand);
            consoleTask.Start();

            sw = Stopwatch.StartNew();
            try
            {
                log.Info("Start processing");
                //Proc();
                Processing();
                //ProcOther();
                log.Info("Finish processing");
            }
            finally
            {
                //сохранить после освобождения пула
                int a = 0, s;
                while (a < _poolSize - 1)
                {
                    ThreadPool.GetAvailableThreads(out a, out s);
                    Thread.Sleep(10000);
                }
                Saving();
                Console.WriteLine("Finish {0}\tTime worked {1}min\nPress any key to exit", DateTime.Now, sw.Elapsed.TotalMinutes);
                Console.ReadKey();
            }
        }

        void Proc()
        {
            //new Downloader("http://market.yandex.ru/guru.xml?hid=765280&CMD=-RR=0,0,0,0-VIS=8070-CAT_ID=975895-BPOS=50-EXC=1-PG=10&greed_mode=false", "Root", y => y.Catalog = "ROOT").Processing();
        }

        void Processing()
        {
            Regex reg = new Regex(patternCatalog);
            string link = "";

            while (queue.Any())
            {
                try
                {
                    //SaveState();
                    Catalog catalog = queue.Dequeue();
                    log.Info("Извлечен следующий каталог {0}", catalog.Uri);
                    Action<YandexMarket> action = y =>
                    {
                        y.Catalog = catalog.Name;
                        y.Parent = catalog.Parent;
                    };

                    link = catalog.Uri;
                    string page = DownloadPage(link);

                    //Ищем подкаталоги
                    MatchCollection matches = reg.Matches(page);
                    if (matches.Count == 0)
                    {
                        Console.WriteLine("Найден лист {0}", catalog.Name);
                        Regex regAll = new Regex(patternAll);//встречается только в guru
                        Match match = regAll.Match(page);
                        if (match.Success)
                        {
                            catalog.Uri = match.Groups["uri"].Value;
                        }
                        Console.WriteLine("Uri : {0}", catalog.Uri);
                        ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, catalog);
                        continue;
                    }
                    foreach (Match match in matches)
                    {
                        string uri = match.Groups["uri"].Value;
                        string name = match.Groups["name"].Value;
                        bool guru = match.Groups["guru"].Value == " guru";

                        //Если не посещали
                        if (!_visitedPages.Contains(uri))
                        {
                            //добавить в очередь
                            queue.Enqueue(new Catalog { Uri = uri, Name = name, Parent = catalog.Parent + "/" + catalog.Name, IsGuru = guru });

                            Console.WriteLine("href : {0}// Name : {1}", uri, name);
                            _visitedPages.Add(uri);
                        }
                        else
                        {
                            Console.WriteLine("------Каталог уже был--{0}--{1}-------", uri, name);
                            log.Info("------Каталог уже был--{0}--{1}-------", uri, name);
                        }
                    }
                }
                catch (Exception e)
                {
                    log.Error("SpiderError {0} : {1}", link, e);
                    Console.WriteLine("###SpiderError#{0}", link);
                }
            }
            Saving();
        }

        /// <summary>
        /// 10 попыток получить страницу. Если null, значит получить не удалось
        /// </summary>
        /// <param name="link"></param>
        /// <returns></returns>
        public static string DownloadPage(string link)
        {
            //Stopwatch sw = Stopwatch.StartNew();
            WebClient cli = new WebClient();
            cli.BaseAddress = "http://market.yandex.ru";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;

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
                    Console.WriteLine("TimeoutError({0}). Repeat", i);
                    continue;
                }
            }
            //sw.Stop();
            //Console.WriteLine("D time = {0}", sw.Elapsed.TotalMilliseconds);
            return page;
        }

        void Saving()
        {
            Console.Write("Saving...");
            var val = AllSites.Values.ToArray();
            foreach (var v in val)
            {
                _rep.Save(v);
            }
            //SaveState();
            Console.WriteLine("done");
        }

        void SaveState()
        {
            Console.Write("Saving state...");
            using (FileStream fs = new FileStream("Queue.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(queue);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            using (FileStream fs = new FileStream("VisitedPages.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(_visitedPages);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            Console.WriteLine("done");
        }

        void ReadState()
        {
            Console.Write("Reading state...");
            using (FileStream fs = new FileStream("Queue.txt", FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                Queue<Catalog> res = JsonConvert.DeserializeObject<Queue<Catalog>>(System.Text.Encoding.UTF8.GetString(byteArr));
                queue = res;
            }
            using (FileStream fs = new FileStream("VisitedPages.txt", FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                HashSet<string> res = JsonConvert.DeserializeObject<HashSet<string>>(System.Text.Encoding.UTF8.GetString(byteArr));
                _visitedPages = res ?? new HashSet<string>();
            }
            Console.WriteLine("done");
        }

        void ConsoleComand()
        {
            while (true)
            {
                string line = Console.ReadLine();
                Console.Write("\t\t\t\t\t\t\t");
                int a, s;
                switch (line)
                {
                    case "save": Saving(); break;
                    case "vis": Console.WriteLine("Visits on pages {0}", countVisitsOnPages); break;
                    case "que": Console.WriteLine("Queue lenght {0}", queue.Count); break;
                    case "pool": ThreadPool.GetAvailableThreads(out a, out s); Console.WriteLine("Available threads {0}/{1}", a, _poolSize); break;
                    case "time": Console.WriteLine("Time working {0}min", sw.Elapsed.TotalMinutes); break;
                    case "cnt":
                    default: Console.WriteLine("cnt = {0}", AllSites.Count); break;
                }
            }
        }
    }
}
