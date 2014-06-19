using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Targetix.Helpers.Security;
using Targetix.MongoDB.Extensions.Model;

namespace YandexMarketParser.Models
{
    public class YandexMarket : ICustomKeyDbModel
    {
        private string _id;


        [BsonIgnoreIfNull]
        public string Id
        {
            get
            {
                _id = _id ?? HashGenerator.Md5Hash(Name);//new Uri(Uri).GetShortUrl());
                return _id;
            }
            set
            {
                _id = value;
            }
        }

        [BsonIgnoreIfNull]
        public string Name { get; set; }
        
        [BsonIgnoreIfNull]
        public int Price { get; set; }
        
        [BsonIgnoreIfNull]
        public string Description { get; set; }
        
        [BsonIgnoreIfNull]
        public string Catalog { get; set; }
        
        [BsonIgnoreIfNull]
        public string Parent { get; set; }
        
        //[BsonIgnoreIfNull]
        //public List<string> ShopLink { get; set; }//?

        //[BsonIgnoreIfNull]
        //public int Rating { get; set; }//?
    }
}
