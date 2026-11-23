using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrawlDisambigous.Services
{
    /// <summary>
    /// Tập hợp các hàm dùng chung để tổng hợp, đếm tần suất và nhóm ứng viên MCS/UMCS từ kết quả quét EREL.
    /// </summary>
    public static class EntityAggregationHelper
    {
        public static string GetDefaultOutputFolder() =>
            Directory.Exists(@"D:\")
                ? @"D:\CrawlerData"
                : Path.Combine(AppContext.BaseDirectory, "Output");

        public static void AddOrIncrementEntityCount(
            Dictionary<int, EntityCountResult> total,
            int entityId,
            string? wikiName,
            int delta = 1)
        {
            if (!total.TryGetValue(entityId, out var existing))
            {
                total[entityId] = new EntityCountResult
                {
                    EntityId = entityId,
                    WikiName = wikiName ?? "",
                    Count = delta
                };
                return;
            }

            if (string.IsNullOrWhiteSpace(existing.WikiName) && !string.IsNullOrWhiteSpace(wikiName))
            {
                existing.WikiName = wikiName;
            }

            existing.Count += delta;
        }

        public static List<ErelEntity> BuildEntitiesForDoc(ErelScanResponseDto scanResult, int sourceEntityId)
        {
            return scanResult.Mcs
                .Where(m => m.SelectedEntityId > 0 && m.SelectedEntityId != sourceEntityId)
                .GroupBy(m => m.SelectedEntityId)
                .Select(g => new ErelEntity
                {
                    EntityId = (int)g.Key,
                    WikiName = g.First().SelectedWikiName,
                    Count = g.Count()
                })
                .ToList();
        }

        public static void AccumulateSingleCandidates(
            ErelScanResponseDto scanResult,
            int sourceEntityId,
            Dictionary<int, EntityCountResult> total)
        {
            var singleCandidates = scanResult.Mcs
                .Where(m => m.SelectedEntityId > 0 && m.SelectedEntityId != sourceEntityId && (m.Candidates?.Count ?? 0) == 1);

            foreach (var sc in singleCandidates)
            {
                AddOrIncrementEntityCount(total, (int)sc.SelectedEntityId, sc.SelectedWikiName, 1);
            }
        }

        public static (int ParagraphIndex, string ContextSentence) ExtractContext(string inputText, int minCharIdx, int maxCharIdx)
        {
            if (string.IsNullOrEmpty(inputText) || minCharIdx < 0 || minCharIdx >= inputText.Length)
                return (1, "");

            // 1. Tính ParagraphIndex (đoạn 1, 2, ...) bằng cách đếm ký tự xuống dòng trước minCharIdx
            int paragraphIndex = 1;
            for (int i = 0; i < minCharIdx; i++)
            {
                if (inputText[i] == '\n')
                    paragraphIndex++;
            }

            // 2. Tìm ranh giới của câu chứa mention
            int sentStart = 0;
            for (int i = minCharIdx - 1; i >= 0; i--)
            {
                char c = inputText[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n' || c == '\r')
                {
                    sentStart = i + 1;
                    break;
                }
            }

            int sentEnd = inputText.Length;
            for (int i = Math.Max(minCharIdx, maxCharIdx); i < inputText.Length; i++)
            {
                char c = inputText[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n' || c == '\r')
                {
                    sentEnd = i + 1;
                    break;
                }
            }

            if (sentEnd > sentStart)
            {
                string rawSent = inputText.Substring(sentStart, sentEnd - sentStart);
                string cleanSent = System.Text.RegularExpressions.Regex.Replace(rawSent.Trim(), @"\s+", " ");
                return (paragraphIndex, cleanSent);
            }

            return (paragraphIndex, "");
        }

        public static List<AmbiguousMentionGroup> BuildAmbiguousGroups(
            IEnumerable<ErelScanResponseDto> scans,
            int? excludeSourceEntityId = null)
        {
            int counter = 1;
            return scans
                .SelectMany(s => s.Mcs)
                .Where(m => (!excludeSourceEntityId.HasValue || m.SelectedEntityId != excludeSourceEntityId.Value)
                            && (m.Candidates?.Count ?? 0) > 1)
                // Phân nhóm CHUẨN THEO ĐOẠN VĂN: Cùng 1 đoạn thì gom các câu lại, khác đoạn mới tách riêng
                .GroupBy(m => $"{m.Mention.Trim().ToLowerInvariant()}__p{m.ParagraphIndex}")
                .Select(g =>
                {
                    var rep = g.First();
                    var allCandidates = g
                        .SelectMany(m => m.Candidates ?? new List<EntityCandidateDto>())
                        .GroupBy(c => c.EntityId)
                        .Select(cg => cg.First())
                        .ToList();

                    long defaultSelectedId = g
                        .GroupBy(m => m.SelectedEntityId)
                        .OrderByDescending(cg => cg.Count())
                        .First().Key;

                    return new AmbiguousMentionGroup
                    {
                        Id = $"amb_{counter++}",
                        Mention = rep.Mention.Trim(),
                        ParagraphIndex = rep.ParagraphIndex,
                        ContextSentence = rep.ContextSentence,
                        TotalCount = g.Count(),
                        PosTag = rep.PosTag,
                        Category = rep.Category,
                        ErelSelectedEntityId = defaultSelectedId,
                        Candidates = allCandidates
                    };
                })
                .OrderBy(g => g.ParagraphIndex)
                .ThenBy(g => g.Mention)
                .ToList();
        }

        public static List<SingleCandidateMentionGroup> BuildSingleCandidateSummary(
            IEnumerable<ErelScanResponseDto> scans,
            int sourceEntityId)
        {
            return scans
                .SelectMany(s => s.Mcs)
                .Where(m => m.SelectedEntityId > 0 && m.SelectedEntityId != sourceEntityId && (m.Candidates?.Count ?? 0) == 1)
                // Gom chuẩn theo đoạn văn
                .GroupBy(m => $"{m.Mention.Trim().ToLowerInvariant()}__p{m.ParagraphIndex}")
                .Select(g => new SingleCandidateMentionGroup
                {
                    Mention = g.First().Mention.Trim(),
                    ParagraphIndex = g.First().ParagraphIndex,
                    ContextSentence = g.First().ContextSentence,
                    TotalCount = g.Count(),
                    EntityId = g.First().SelectedEntityId,
                    WikiName = g.First().SelectedWikiName
                })
                .OrderBy(g => g.ParagraphIndex)
                .ThenByDescending(g => g.TotalCount)
                .ToList();
        }

        public static List<string> BuildUmcsTerms(IEnumerable<ErelScanResponseDto> scans, int limit = 100)
        {
            return scans
                .SelectMany(s => s.Umcs)
                .Select(u => u.Mention.Trim())
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .GroupBy(m => m, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} ({g.Count()})")
                .Take(limit)
                .ToList();
        }
    }
}

