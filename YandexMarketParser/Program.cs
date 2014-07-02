using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YandexMarketParser.Models;

namespace YandexMarketParser
{
    class Program
    {
        static void Main(string[] args)
        {
            //RemoveNull();
            //ReconstructCatalogs();
            //TestPageNum();
            //TestNext();
            //TestRegMarket();
            ParseYandexMarket();
        }
        static string[] ReadProxyList()
        {
            string[] result;
            using (FileStream fs = new FileStream("Proxies.txt",FileMode.Open))
            {
                byte[] b = new byte[fs.Length];
                fs.Read(b, 0, b.Length);
                string jsonStr = Encoding.UTF8.GetString(b);
                result = JsonConvert.DeserializeObject<string[]>(jsonStr);
            }
            return result;
        }
        static void SaveProxyList(string [] prx)
        {
            using (FileStream fs = new FileStream("Proxies.txt", FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(prx);
                byte[] b = Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(b, 0, b.Length);
            }
        }
        static void MergeMarket(){
            Repository rep1 = new Repository(new Db("mongodb://localhost:27017/YandexMarket"));
            Repository repRes = new Repository(new Db("mongodb://localhost:27017/YandexMarketCompiled"));

            IEnumerable<YandexMarket> all = rep1.GetAll();
            foreach (var item in all)
            {
                repRes.Save(item);
            }
            Console.WriteLine("Rep1 complited");
        }

        static void RemoveNull()
        {
            Catalog[] cat = ReadState("Catalogs22.txt");
            List<Catalog> res = new List<Catalog>();
            foreach (var c in cat)
            {
                if (!(c.Uri.IndexOf("mcpriceto=0", 0) > 0)) res.Add(c);
                else Console.WriteLine();
            }
            SaveState("SaveCat.txt", res.ToArray());
        }

        static void ReconstructCatalogs()
        {
            Catalog[] source = ReadState("sourceCat.txt");
            Catalog[] forReconstruct = ReadState("destinationCat.txt");
            foreach (var cat in source)
            {
                Catalog dest = forReconstruct.First(x => x.Name == cat.Name);
                if (dest.Uri == null)
                {
                    dest.Uri = cat.Uri;
                    dest.Complited = true;
                }
            }
            SaveState("resultCat.txt", forReconstruct);
        }
        static void TestPageNum()
        {
            string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
            string root = Spider.DownloadPage("http://market.yandex.ru/search.xml?hid=90483&page=50");
            Regex reg = new Regex(patternNext);
            Match nextPage = reg.Match(root);

            string catalog = nextPage.Groups["uri"].Value;
            Match m = new Regex(@"page=(\d+)").Match(catalog);
            bool res = int.Parse(m.Groups[1].Value) > 50;
            Console.WriteLine(res);
        }

        static void TestNext()
        {
            WebClient cli = new WebClient();
            cli.BaseAddress = @"http://market.yandex.ru/";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;
            //string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
            string patternNext = @"<b class=""b-pager__current"">\d+</b> <a class=""b-pager__page"" data-mvc-page=""\d+"" href=""(?<uri>[\w\s\p{P}\p{S}]*?)"">\d+</a>";
            //string patternCount = @"<p>[\s]+выбрано&nbsp;моделей[\s]+&nbsp;— [\d]+</p></div></form>";
            string link = @"http://market.yandex.ru/guru.xml?CMD=-RR%3D0%2C0%2C0%2C0-VIS%3D8070-CAT_ID%3D8443229-EXC%3D1-PG%3D10&hid=90462";
            //string link = @"http://market.yandex.ru/guru.xml?CMD=-RR%3D0%2C0%2C0%2C0-VIS%3D8070-CAT_ID%3D975895-EXC%3D1-PG%3D10&hid=765280";
            //string link = @"http://market.yandex.ru/guru.xml?hid=765280&CMD=-RR=0,0,0,0-VIS=8070-CAT_ID=975895-BPOS=40-EXC=1-PG=10&greed_mode=false";
            //for (int i = 0; i < 10; i++)
            //{
            //    Regex reg = new Regex(patternNext);
            //    string link = @"http://market.yandex.ru/guru.xml?hid=765280&CMD=-RR=0,0,0,0-VIS=8070-CAT_ID=975895-BPOS="+i+"0-EXC=1-PG=10&greed_mode=false";
            //    string page = cli.DownloadString(link);
            //    Match match = reg.Match(page);
            //    link = match.Groups["uri"].Value;
            //    Console.WriteLine(link);
            //    using (FileStream fs = new FileStream("Bla"+i+10+".htm", FileMode.Create))
            //    {
            //        byte[] barr = Encoding.UTF8.GetBytes(page);
            //        fs.Write(barr, 0, barr.Length);
            //    }
            //}

            //using (FileStream fs = new FileStream("Bla.htm", FileMode.Create))
            //{
            //    byte[] barr = Encoding.UTF8.GetBytes(page);
            //    fs.Write(barr, 0, barr.Length);
            //}

            //            string page = cli.DownloadString(link);
            //            //string patternCount = @"<p>[\w\s\p{P}\p{S}]+?(?<res>[\d]+)</p></div></form>";
            //            string patternCountGuru = @"<p>[\s]*выбрано.моделей[\s]*.+?(?<res>[\d]+)</p></div></form>";

            ////            string page = @"</div></div><p>
            ////                выбрано моделей
            ////                 — 1111</p></div></form>
            ////            </div>";

            //            Regex rc = new Regex(patternCountGuru);
            //            Match match = rc.Match(page);
            //            Console.WriteLine(match.Groups["res"].Value);

            string str = @"http://market.yandex.ru/guru.xml?hid=765280&CMD=-RR=0,0,0,0-VIS=8070-CAT_ID=975895-BPOS=40-EXC=1-PG=10&greed_mode=false";

            Regex rb = new Regex(@"(?<=-BPOS=)\d+");
            string urr = rb.Replace(str, "##");

        }

        static void TestRegMarket()
        {
            WebClient cli = new WebClient();
            cli.BaseAddress = @"http://market.yandex.ru/";
            cli.Proxy = null;
            cli.Encoding = Encoding.UTF8;

            string page = cli.DownloadString("http://market.yandex.ru/search.xml?hid=90478");

            string patternCatalog = @"<div class=""supcat(?: guru)?""><a href=""(?<uri>/catalog.xml\?hid=\d*)"">(?:<img[\w\p{P}\p{S} ]*>)?(?<name>[-\w,. ]*)</a>";
            ///guru.xml?CMD=-RR%3D0%2C0%2C0%2C0-VIS%3D8070-CAT_ID%3D115828-EXC%3D1-PG%3D10&amp;hid=90565
            string patternAll = @"<a class=""top-3-models__title-link"" href=""(?<uri>[-\w\p{P}\p{S} ]*)"">Посмотреть все модели</a>";
            string patternPrice = @"<span class=""b(?:-old)?-prices__num"">(?<price>[\s\d]*)</span>";
            ///search.xml?hid=90403&amp;page=2
            string patternNext = @"<a class=""b-pager__next"" href=""(?<uri>[\w\p{P}\p{S}]*)"">[\w ]*</a>";
            string patternDescription = @"<p class=""b-offers__spec"">(?<desc>[\w\p{P}\p{S}\s]*?)(?:<span class=""b-more""><span class=""b-more__dots"">.</span><span class=""b-more__text"">(?<desc2>.*?)</span>.*?</span>)?</p>";
            string patternTitle = @"<h3 class=""b-offers__title""><a (?:[-\w=""]*) class=""b-offers__name(?:.*?)"">(?<name>.*?)</a>";
            string patternCount = @"<p class=""search-stat"">Все цены\s. (?<cnt>\d+)\.";
            string patternOi = @"<strong class=""b-head-name"">ой...</strong>";
            Regex reg = new Regex(patternOi);

            MatchCollection mc = reg.Matches(page);
            foreach (Match match in mc)
            {
                //string uri = match.Groups["uri"].Value;
                //string name = match.Groups["name"].Value;
                //Console.WriteLine("Name : {0}, Uri : {1}", name, uri);
                //Console.WriteLine("Uri : {0}", uri);
                //Console.WriteLine("Name : {0}", name);

                //string price = match.Groups["price"].Value;

                //Regex r = new Regex(@"\s");
                //string rr = r.Replace(price, "");

                //int res = int.Parse(rr);
                //Console.WriteLine("Price : {0}", res);

                //string desc = match.Groups["desc"].Value;
                //string desc2 = match.Groups["desc2"].Value;
                //Console.WriteLine("D1: \n{0}\nD2: \n{1}", desc, desc2);


                string count = match.Groups["cnt"].Value;
                Console.WriteLine(count);
            }
        }
        static void SaveState(string fileName, Catalog[] catalogs)
        {
            Console.Write("Saving state...");
            using (FileStream fs = new FileStream(fileName, FileMode.Create))
            {
                string jsonStr = JsonConvert.SerializeObject(catalogs.ToArray());
                byte[] res = System.Text.Encoding.UTF8.GetBytes(jsonStr);
                fs.Write(res, 0, res.Length);
            }
            Console.WriteLine("done");
        }

        static Catalog[] ReadState(string fileName)
        {
            Console.Write("Reading state...");
            Catalog[] res = null;
            using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate))
            {
                byte[] byteArr = new byte[fs.Length];
                fs.Read(byteArr, 0, byteArr.Length);
                res = JsonConvert.DeserializeObject<Catalog[]>(System.Text.Encoding.UTF8.GetString(byteArr)) ?? new Catalog[0];
            }
            Console.WriteLine("done");
            return res;
        }

        static void ParseYandexMarket()
        {
            new Spider();
        }
    }
}
