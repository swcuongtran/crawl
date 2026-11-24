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

            // 1. Tính ParagraphIndex (đoạn 1, 2, ...) bằng cách đếm các khối phân tách dòng trước minCharIdx
            int paragraphIndex = 1;
            bool lastWasNewline = false;
            for (int i = 0; i < minCharIdx; i++)
            {
                if (inputText[i] == '\n')
                {
                    if (!lastWasNewline)
                    {
                        paragraphIndex++;
                        lastWasNewline = true;
                    }
                }
                else if (!char.IsWhiteSpace(inputText[i]))
                {
                    lastWasNewline = false;
                }
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
            var allMcs = scans
                .SelectMany(s =>
                {
                    foreach (var m in s.Mcs)
                    {
                        if (m.DocIndex == 0 && s.DocIndex > 0) m.DocIndex = s.DocIndex;
                        if (string.IsNullOrEmpty(m.DocTitle) && !string.IsNullOrEmpty(s.DocTitle)) m.DocTitle = s.DocTitle;
                    }
                    return s.Mcs;
                })
                .Where(m => (!excludeSourceEntityId.HasValue || m.SelectedEntityId != excludeSourceEntityId.Value)
                            && (m.Candidates?.Count ?? 0) > 1);

            // Gom nhóm theo Bài viết, Đoạn văn, và Từ
            var groups = allMcs
                .GroupBy(m => $"{m.DocIndex}__p{m.ParagraphIndex}__{m.Mention.Trim().ToLowerInvariant()}")
                .Select(g =>
                {
                    // Lấy từ xuất hiện sớm nhất trong đoạn làm đại diện
                    var rep = g.OrderBy(m => m.MinCharIdx).First();

                    long defaultSelectedId = g
                        .GroupBy(m => m.SelectedEntityId)
                        .OrderByDescending(cg => cg.Count())
                        .First().Key;

                    // Sắp xếp các ứng viên (candidates / aliases) bên trong thẻ:
                    // 1. Ứng viên EREL gợi ý lên đầu tiên
                    // 2. Điểm M cao nhất xếp trước
                    // 3. Tên WikiName theo alphabet
                    var allCandidates = g
                        .SelectMany(m => m.Candidates ?? new List<EntityCandidateDto>())
                        .GroupBy(c => c.EntityId)
                        .Select(cg => cg.First())
                        .OrderByDescending(c => c.EntityId == defaultSelectedId)
                        .ThenByDescending(c => c.M)
                        .ThenBy(c => c.WikiName)
                        .ToList();

                    return new AmbiguousMentionGroup
                    {
                        DocIndex = rep.DocIndex > 0 ? rep.DocIndex : 1,
                        DocTitle = rep.DocTitle ?? "",
                        Mention = rep.Mention.Trim(),
                        ParagraphIndex = rep.ParagraphIndex,
                        MinCharIdx = rep.MinCharIdx,
                        ContextSentence = rep.ContextSentence,
                        TotalCount = g.Count(),
                        PosTag = rep.PosTag,
                        Category = rep.Category,
                        ErelSelectedEntityId = defaultSelectedId,
                        Candidates = allCandidates
                    };
                })
                // SẮP XẾP CHUẨN ĐỌC TỰ NHIÊN:
                // 1. Thứ tự bài viết (Bài 1 -> Bài 2 -> ...)
                // 2. Thứ tự đoạn văn từ đầu đến cuối (Đoạn 1 -> Đoạn 2 -> ...)
                // 3. Thứ tự vị trí xuất hiện của từ trong câu/văn bản (Từ đầu câu -> từ cuối câu)
                .OrderBy(g => g.DocIndex)
                .ThenBy(g => g.ParagraphIndex)
                .ThenBy(g => g.MinCharIdx)
                .ToList();

            // Đánh ID tuần tự amb_1, amb_2... sau khi đã sắp xếp hoàn chỉnh
            int counter = 1;
            foreach (var grp in groups)
            {
                grp.Id = $"amb_{counter++}";
            }

            return groups;
        }

        public static List<SingleCandidateMentionGroup> BuildSingleCandidateSummary(
            IEnumerable<ErelScanResponseDto> scans,
            int sourceEntityId)
        {
            var singleCandidates = scans
                .SelectMany(s =>
                {
                    foreach (var m in s.Mcs)
                    {
                        if (m.DocIndex == 0 && s.DocIndex > 0) m.DocIndex = s.DocIndex;
                        if (string.IsNullOrEmpty(m.DocTitle) && !string.IsNullOrEmpty(s.DocTitle)) m.DocTitle = s.DocTitle;
                    }
                    return s.Mcs;
                })
                .Where(m => m.SelectedEntityId > 0 && m.SelectedEntityId != sourceEntityId && (m.Candidates?.Count ?? 0) == 1);

            return singleCandidates
                .GroupBy(m => $"{m.DocIndex}__p{m.ParagraphIndex}__{m.Mention.Trim().ToLowerInvariant()}")
                .Select(g =>
                {
                    var rep = g.OrderBy(m => m.MinCharIdx).First();
                    return new SingleCandidateMentionGroup
                    {
                        DocIndex = rep.DocIndex > 0 ? rep.DocIndex : 1,
                        DocTitle = rep.DocTitle ?? "",
                        Mention = rep.Mention.Trim(),
                        ParagraphIndex = rep.ParagraphIndex,
                        MinCharIdx = rep.MinCharIdx,
                        ContextSentence = rep.ContextSentence,
                        TotalCount = g.Count(),
                        EntityId = rep.SelectedEntityId,
                        WikiName = rep.SelectedWikiName
                    };
                })
                .OrderBy(g => g.DocIndex)
                .ThenBy(g => g.ParagraphIndex)
                .ThenBy(g => g.MinCharIdx)
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

