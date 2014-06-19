using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YandexMarketParser.Models
{
    public class Catalog
    {
        public string Uri { get; set; }
        public string Name { get; set; }
        public string Parent { get; set; }
        public bool IsGuru { get; set; }
    }
}
