using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CrawlDisambigous.Models
{
    public class SearchRequest
    {
        public string WikiName { get; set; } = "";
        public int EntityId { get; set; } = 1;
        public int SearchCount { get; set; } = 10;
    }

    public class SearchResultDto
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Snippet { get; set; } = "";
    }

    public class StartCrawlRequest
    {
        public string? SessionId { get; set; }
        public string WikiName { get; set; } = "";
        public int EntityId { get; set; } = 1;
        public List<string> SelectedUrls { get; set; } = new();
    }

    public class LinkCrawlDetail
    {
        public int Index { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Status { get; set; } = "pending"; // pending, crawling, scanning, success, failed
        public string FetchMethod { get; set; } = "N/A"; // HttpClient, Playwright, Blacklisted
        public int TextLength { get; set; }
        public string Snippet { get; set; } = "";
        public string FullText { get; set; } = "";
        public int McsCount { get; set; }
        public int UmcsCount { get; set; }
        public string? Error { get; set; }

        // Đo thời gian chi tiết (milliseconds)
        public double CrawlDurationMs { get; set; }
        public double ErelDurationMs { get; set; }
        public double TotalDurationMs { get; set; }
    }

    public class PerformanceMetrics
    {
        public double SearchDurationMs { get; set; }
        public double TotalCrawlDurationMs { get; set; }
        public double TotalErelDurationMs { get; set; }
        public double DisambiguationDurationMs { get; set; }
        public double TotalSessionDurationMs { get; set; }
        public double AverageCrawlPerLinkMs { get; set; }
        public double AverageErelPerLinkMs { get; set; }
        public int SuccessfulLinks { get; set; }
        public int FailedLinks { get; set; }
    }

    public class CrawlSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public string Status { get; set; } = "idle"; // idle, searching, ready_to_crawl, crawling, awaiting_review, completed, error
        public string CurrentMessage { get; set; } = "";
        public SourceEntity Source { get; set; } = new();
        public int TotalLinks { get; set; }
        public int CompletedLinks { get; set; }
        public List<SearchResultDto> SearchResults { get; set; } = new();
        public List<LinkCrawlDetail> CrawledLinks { get; set; } = new();
        public List<AmbiguousMentionGroup> AmbiguousGroups { get; set; } = new();
        public List<SingleCandidateMentionGroup> SingleCandidateSummary { get; set; } = new();
        public List<EntityCountResult> FinalResults { get; set; } = new();
        public List<string> UmcsTerms { get; set; } = new();
        public Dictionary<string, string> OutputFiles { get; set; } = new();
        [JsonIgnore]
        public List<ErelScanResponseDto> AllDocScans { get; set; } = new();
        public PerformanceMetrics Metrics { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }

    public class ConfirmReviewRequest
    {
        public string SessionId { get; set; } = "";
        public List<ManualSelectionResult> Selections { get; set; } = new();
    }
}
