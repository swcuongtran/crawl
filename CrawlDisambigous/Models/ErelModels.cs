using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrawlDisambigous.Models
{
    public class ErelScanResponseDto
    {
        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("relatedEntityIds")]
        public string? RelatedEntityIds { get; set; }

        [JsonPropertyName("mcs")]
        public List<McsMentionDto> Mcs { get; set; } = new();

        [JsonPropertyName("umcs")]
        public List<UmcsMentionDto> Umcs { get; set; } = new();

        [JsonIgnore]
        public string? ErrorMessage { get; set; }
    }

    public class McsMentionDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("mention")]
        public string Mention { get; set; } = "";

        [JsonPropertyName("posTag")]
        public string? PosTag { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("selectedEntityId")]
        public long SelectedEntityId { get; set; }

        [JsonPropertyName("selectedWikiName")]
        public string SelectedWikiName { get; set; } = "";

        [JsonPropertyName("importanceScore")]
        public double ImportanceScore { get; set; }

        [JsonPropertyName("mentionCount")]
        public int MentionCount { get; set; }

        [JsonPropertyName("minCharIdx")]
        public int MinCharIdx { get; set; }

        [JsonPropertyName("maxCharIdx")]
        public int MaxCharIdx { get; set; }

        [JsonPropertyName("isCoreferenced")]
        public bool IsCoreferenced { get; set; }

        [JsonPropertyName("refMention")]
        public string? RefMention { get; set; }

        [JsonPropertyName("candidates")]
        public List<EntityCandidateDto> Candidates { get; set; } = new();
    }

    public class EntityCandidateDto
    {
        [JsonPropertyName("entityId")]
        public long EntityId { get; set; }

        [JsonPropertyName("wikiName")]
        public string WikiName { get; set; } = "";

        [JsonPropertyName("realName")]
        public string RealName { get; set; } = "";

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("sig")]
        public double Sig { get; set; }

        [JsonPropertyName("m")]
        public double M { get; set; }

        [JsonPropertyName("me")]
        public double Me { get; set; }

        [JsonPropertyName("mc")]
        public double Mc { get; set; }

        [JsonPropertyName("ms")]
        public double Ms { get; set; }

        [JsonPropertyName("isSelected")]
        public bool IsSelected { get; set; }
    }

    public class UmcsMentionDto
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("mention")]
        public string Mention { get; set; } = "";

        [JsonPropertyName("posTag")]
        public string? PosTag { get; set; }

        [JsonPropertyName("minCharIdx")]
        public int MinCharIdx { get; set; }

        [JsonPropertyName("maxCharIdx")]
        public int MaxCharIdx { get; set; }
    }
}
