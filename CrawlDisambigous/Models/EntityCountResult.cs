using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrawlDisambigous.Models
{
    public class EntityCountResult
    {
        public int EntityId { get; set; }

        public string WikiName { get; set; } = "";

        public int Count { get; set; }
    }
}
