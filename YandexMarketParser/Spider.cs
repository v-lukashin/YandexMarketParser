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
        public static List<Catalog> _curPages = new List<Catalog>();

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
            queue = new Queue<Catalog>();
            _visitedPages = new HashSet<string>();

                                                                                                        //do{
                                                                                                        //    Console.WriteLine("Восстановить предыдущее состояние?(y/n):");
                                                                                                        //    char ans = Console.ReadKey().KeyChar;
                                                                                                        //    if (ans.Equals('y') || ans.Equals('д'))
                                                                                                        //    {
                                                                                                        //        ReadState();
                                                                                                        //        break;
                                                                                                        //    }else if (ans.Equals('n') || ans.Equals('н')) break;
                                                                                                        //    else Console.WriteLine("Не то. Попробуем еще раз..");
                                                                                                        //}while(true);
            ReadState();

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
            Task saver = new Task(() => { Thread.Sleep(600000); Saving(); });
            saver.Start();

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
                while (a < _poolSize - 2)
                {
                    ThreadPool.GetAvailableThreads(out a, out s);
                    Thread.Sleep(10000);
                }
                Saving();
                Console.WriteLine("Finish {0}\tTime worked {1}min\nPress any key to exit", DateTime.Now, sw.Elapsed.TotalMinutes);
                Console.ReadKey();
            }
        }
        static void Proc(){
            Catalog cat = new Catalog { IsGuru = true, Name = "bla", Parent = "superBla", Uri = @"http://market.yandex.ru/guru.xml?hid=765280&CMD=-RR=0,0,0,0-VIS=8070-CAT_ID=975895-BPOS=30-EXC=1-PG=10&greed_mode=false" };
            ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, cat);
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
                    Console.WriteLine("WebException({0}). Repeat", i);
                    Console.WriteLine(wexc.Message + wexc.StackTrace);
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
            var vis = _visitedPages.ToArray();
            var que = queue.ToArray();
            var pag = _curPages.ToArray();

            var val = AllSites.Values.ToArray();
            foreach (var v in val)
            {
                _rep.Save(v);
            }
            Console.WriteLine("done");

            SaveState(que, vis, pag);
        }

        void SaveState(Catalog[] que, string[] vis, Catalog[] pag)
        {
            Console.Write("Saving state...");
            using (FileStream fs = new FileStream("Queue.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(que);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            using (FileStream fs = new FileStream("VisitedPages.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(vis);
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            using (FileStream fs = new FileStream("CurrentPages.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(pag);
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
                Catalog[] res = JsonConvert.DeserializeObject<Catalog[]>(System.Text.Encoding.UTF8.GetString(byteArr));
                if (res == null || res.Length == 0) res = new Catalog[] { _rootCatalog };
                foreach (var c in res)
                {
                    queue.Enqueue(c);
                }
            }
            using (FileStream fs = new FileStream("VisitedPages.txt", FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                string[] res = JsonConvert.DeserializeObject<string[]>(System.Text.Encoding.UTF8.GetString(byteArr)) ?? new string[0];
                foreach (var v in res)
                {
                    _visitedPages.Add(v);
                }

            }
            using (FileStream fs = new FileStream("CurrentPages.txt", FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                Catalog[] res = JsonConvert.DeserializeObject<Catalog[]>(System.Text.Encoding.UTF8.GetString(byteArr)) ?? new Catalog[0];
                foreach (var c in res)
                {
                    ThreadPool.QueueUserWorkItem(Downloader.WaitCallback, c);
                }
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
