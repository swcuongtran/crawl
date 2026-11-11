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

        public static List<AmbiguousMentionGroup> BuildAmbiguousGroups(
            IEnumerable<ErelScanResponseDto> scans,
            int? excludeSourceEntityId = null)
        {
            return scans
                .SelectMany(s => s.Mcs)
                .Where(m => (!excludeSourceEntityId.HasValue || m.SelectedEntityId != excludeSourceEntityId.Value)
                            && (m.Candidates?.Count ?? 0) > 1)
                .GroupBy(m => m.Mention.Trim(), StringComparer.OrdinalIgnoreCase)
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
                        Mention = g.Key,
                        TotalCount = g.Count(),
                        PosTag = rep.PosTag,
                        Category = rep.Category,
                        ErelSelectedEntityId = defaultSelectedId,
                        Candidates = allCandidates
                    };
                })
                .OrderByDescending(g => g.TotalCount)
                .ToList();
        }

        public static List<SingleCandidateMentionGroup> BuildSingleCandidateSummary(
            IEnumerable<ErelScanResponseDto> scans,
            int sourceEntityId)
        {
            return scans
                .SelectMany(s => s.Mcs)
                .Where(m => m.SelectedEntityId > 0 && m.SelectedEntityId != sourceEntityId && (m.Candidates?.Count ?? 0) == 1)
                .GroupBy(m => m.Mention.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => new SingleCandidateMentionGroup
                {
                    Mention = g.Key,
                    TotalCount = g.Count(),
                    EntityId = g.First().SelectedEntityId,
                    WikiName = g.First().SelectedWikiName
                })
                .OrderByDescending(g => g.TotalCount)
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

