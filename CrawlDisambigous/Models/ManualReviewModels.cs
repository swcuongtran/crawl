using System.Collections.Generic;

namespace CrawlDisambigous.Models
{
    public class AmbiguousMentionGroup
    {
        public string Id { get; set; } = "";
        public int DocIndex { get; set; } = 1;
        public string DocTitle { get; set; } = "";
        public string Mention { get; set; } = "";
        public int ParagraphIndex { get; set; } = 1;
        public int MinCharIdx { get; set; } = 0;
        public string ContextSentence { get; set; } = "";
        public int TotalCount { get; set; } = 1;
        public string? PosTag { get; set; }
        public string? Category { get; set; }
        public long ErelSelectedEntityId { get; set; }
        public List<EntityCandidateDto> Candidates { get; set; } = new();
    }

    public class SingleCandidateMentionGroup
    {
        public int DocIndex { get; set; } = 1;
        public string DocTitle { get; set; } = "";
        public string Mention { get; set; } = "";
        public int ParagraphIndex { get; set; } = 1;
        public int MinCharIdx { get; set; } = 0;
        public string ContextSentence { get; set; } = "";
        public int TotalCount { get; set; } = 1;
        public long EntityId { get; set; }
        public string WikiName { get; set; } = "";
    }

    public class ManualSelectionResult
    {
        public string Id { get; set; } = "";
        public int DocIndex { get; set; } = 1;
        public string DocTitle { get; set; } = "";
        public string Mention { get; set; } = "";
        public int ParagraphIndex { get; set; } = 1;
        public int MinCharIdx { get; set; } = 0;
        public string ContextSentence { get; set; } = "";
        public int MentionCount { get; set; }
        public long SelectedEntityId { get; set; }
        public string SelectedWikiName { get; set; } = "";
    }
}
