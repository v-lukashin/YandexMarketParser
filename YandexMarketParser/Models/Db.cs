using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YandexMarketParser.Models
{
    public class Db: Targetix.MongoDB.Database.Abstract.DB   
    {
        public Db(string connectionString):base(connectionString){
        }
    }
}
