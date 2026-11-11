using System.Collections.Generic;

namespace CrawlDisambigous.Models
{
    public class AmbiguousMentionGroup
    {
        public string Mention { get; set; } = "";
        public int TotalCount { get; set; }
        public string? PosTag { get; set; }
        public string? Category { get; set; }
        public long ErelSelectedEntityId { get; set; }
        public List<EntityCandidateDto> Candidates { get; set; } = new();
    }

    public class SingleCandidateMentionGroup
    {
        public string Mention { get; set; } = "";
        public int TotalCount { get; set; }
        public long EntityId { get; set; }
        public string WikiName { get; set; } = "";
    }

    public class ManualSelectionResult
    {
        public string Mention { get; set; } = "";
        public int MentionCount { get; set; }
        public long SelectedEntityId { get; set; }
        public string SelectedWikiName { get; set; } = "";
    }
}
