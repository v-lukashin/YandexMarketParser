using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Targetix.MongoDB.Extensions.Model;

namespace YandexMarketParser.Models
{
    public class Repository : Targetix.Repository.MongoShardKeyRepository<YandexMarket>
    {
        public Repository(Targetix.MongoDB.Database.Abstract.DB db) : base(db) { }
    }
}
