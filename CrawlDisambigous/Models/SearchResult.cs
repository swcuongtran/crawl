using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrawlDisambigous.Models
{
    public class SearchResult
    {
        public int Position { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;
    }
}
