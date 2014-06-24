using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YandexMarketParser.Models
{
    public class Catalog
    {
        public Catalog(string name, string parent,string uri, bool isGuru)
        {
            Uri = uri;
            Name = name;
            Parent = parent;
            IsGuru = isGuru;
        }
        public string Uri { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public bool IsGuru { get; set; }
    }
}
