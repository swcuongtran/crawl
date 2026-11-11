using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class ResultWriterService
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static void EnsureParentDirectory(string filePath)
        {
            string? directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public async Task WriteAsync(
            string outputPath,
            int sourceEntityId,
            IEnumerable<EntityCountResult> results,
            bool append = true)
        {
            try
            {
                EnsureParentDirectory(outputPath);

                using var writer = new StreamWriter(outputPath, append, Utf8NoBom);
                foreach (var result in results.OrderByDescending(x => x.Count).ThenBy(x => x.EntityId))
                {
                    await writer.WriteLineAsync($"{sourceEntityId}|{result.EntityId}|{result.Count}");
                }

                Console.WriteLine($"[OUTPUT] Đã lưu kết quả tại: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OUTPUT ERROR] Không thể ghi file {outputPath}: {ex.Message}");
            }
        }

        public async Task SaveCrawledContentAsync(
            string outputFolder,
            SourceEntity source,
            int linkIndex,
            SearchResult link,
            string content)
        {
            try
            {
                string contentDir = Path.Combine(outputFolder, "CrawledContent");
                Directory.CreateDirectory(contentDir);

                string safeEntityName = SanitizeFileName(source.WikiName);
                string singleFilePath = Path.Combine(contentDir, $"{source.EntityId}_{safeEntityName}_Link{linkIndex}.txt");

                var sb = new StringBuilder();
                sb.AppendLine("==================================================");
                sb.AppendLine($"THỰC THỂ: {source.EntityId} - {source.WikiName}");
                sb.AppendLine($"TIÊU ĐỀ: {link.Title}");
                sb.AppendLine($"URL: {link.Url}");
                sb.AppendLine($"THỜI GIAN CÀO: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"ĐỘ DÀI NỘI DUNG: {content.Length:N0} ký tự");
                sb.AppendLine("==================================================");
                sb.AppendLine();
                sb.AppendLine(content);

                await File.WriteAllTextAsync(singleFilePath, sb.ToString(), Utf8NoBom);
                Console.WriteLine($"[LƯU NỘI DUNG] Đã lưu file bài viết: {singleFilePath}");

                string allFilePath = Path.Combine(contentDir, $"{source.EntityId}_{safeEntityName}_FullContent.txt");
                var allSb = new StringBuilder();
                allSb.AppendLine($"\n\n### [LINK {linkIndex}] {link.Title} ###");
                allSb.AppendLine($"URL: {link.Url}");
                allSb.AppendLine("--------------------------------------------------");
                allSb.AppendLine(content);
                await File.AppendAllTextAsync(allFilePath, allSb.ToString(), Utf8NoBom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE ERROR] Không thể lưu nội dung cào: {ex.Message}");
            }
        }

        public async Task SaveScanResultAsync(
            string outputFolder,
            SourceEntity source,
            int linkIndex,
            SearchResult link,
            ErelScanResponseDto scanResult,
            List<ErelEntity> chosenEntities)
        {
            try
            {
                string scanDir = Path.Combine(outputFolder, "ScanResults");
                Directory.CreateDirectory(scanDir);

                string safeEntityName = SanitizeFileName(source.WikiName);
                string singleFilePath = Path.Combine(scanDir, $"{source.EntityId}_{safeEntityName}_Link{linkIndex}_MCS_UMCS.txt");

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("KẾT QUẢ QUÉT TẬP MCS VÀ UMCS CỦA CÁC TỪ TRONG VĂN BẢN");
                sb.AppendLine($"THỰC THỂ NGUỒN (Entity 1): [{source.EntityId}] {source.WikiName}");
                sb.AppendLine($"BÀI VIẾT #{linkIndex}: {link.Title}");
                sb.AppendLine($"URL: {link.Url}");
                sb.AppendLine($"THỜI GIAN QUÉT: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                var ambiguousGroups = EntityAggregationHelper.BuildAmbiguousGroups(new[] { scanResult });
                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("[1] TẬP MCS (Chỉ hiển thị các Mentions có > 1 Candidate trong CSDL)");
                sb.AppendLine($"Số lượng: {ambiguousGroups.Count} mentions");
                sb.AppendLine("--------------------------------------------------------------------------------");

                if (ambiguousGroups.Count == 0)
                {
                    sb.AppendLine("(Không có mention nào có > 1 candidate)");
                }
                else
                {
                    int mIdx = 1;
                    foreach (var group in ambiguousGroups)
                    {
                        var rep = scanResult.Mcs.First(m => m.Mention.Trim().Equals(group.Mention, StringComparison.OrdinalIgnoreCase));
                        string isSourceMarker = (group.ErelSelectedEntityId == source.EntityId) ? " [CHÍNH LÀ ENTITY 1]" : "";

                        sb.AppendLine($"[{mIdx++}] Mention: \"{group.Mention}\"");
                        sb.AppendLine($"    - Số lần xuất hiện trong doc: {group.TotalCount}");
                        sb.AppendLine($"    - POS Tag: {group.PosTag ?? "N/A"} | Loại: {group.Category ?? "N/A"}");
                        sb.AppendLine($"    - EREL Chọn (Disambiguated): [ID: {group.ErelSelectedEntityId}] {rep.SelectedWikiName} (Importance Score: {rep.ImportanceScore:F2}){isSourceMarker}");
                        sb.AppendLine($"    - Danh sách ứng viên trong KB ({group.Candidates.Count} Candidates):");
                        for (int cIdx = 0; cIdx < group.Candidates.Count; cIdx++)
                        {
                            var c = group.Candidates[cIdx];
                            string selTag = c.IsSelected ? " [EREL CHỌN]" : "";
                            sb.AppendLine($"        ({cIdx + 1}) ID: {c.EntityId,-6} | WikiName: {c.WikiName,-25} | RealName: {c.RealName,-20} | M: {c.M:F2} | Me: {c.Me:F2} | Mc: {c.Mc:F2} | Ms: {c.Ms:F2}{selTag}");
                        }
                        sb.AppendLine();
                    }
                }

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("[2] TẬP UMCS (Unmatched Candidate Set - Các từ/cụm từ KHÔNG có trong CSDL)");
                sb.AppendLine($"Tổng số mentions chưa nhận diện: {scanResult.Umcs.Count}");
                sb.AppendLine("--------------------------------------------------------------------------------");

                if (scanResult.Umcs.Count == 0)
                {
                    sb.AppendLine("(Không có từ chưa nhận diện)");
                }
                else
                {
                    var umcsGroups = scanResult.Umcs
                        .GroupBy(u => u.Mention.Trim(), StringComparer.OrdinalIgnoreCase)
                        .OrderByDescending(g => g.Count());

                    sb.AppendLine("Danh sách các từ/cụm từ quét được nhưng không có alias trong CSDL:");
                    int uIdx = 1;
                    foreach (var ug in umcsGroups)
                    {
                        sb.AppendLine($"  {uIdx++,3}. \"{ug.Key}\" (Số lần xuất hiện: {ug.Count()})");
                    }
                }
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine($"[3] DANH SÁCH ENTITY2 ĐƯỢC CHỌN ĐỂ KẾT NỐI VỚI ENTITY1 [{source.EntityId} - {source.WikiName}]");
                sb.AppendLine("--------------------------------------------------------------------------------");
                if (chosenEntities.Count == 0)
                {
                    sb.AppendLine("(Không có Entity2 nào được chọn từ bài viết này)");
                }
                else
                {
                    for (int eIdx = 0; eIdx < chosenEntities.Count; eIdx++)
                    {
                        var ce = chosenEntities[eIdx];
                        sb.AppendLine($"  [{eIdx + 1}] EntityId: {ce.EntityId,-6} | WikiName: {ce.WikiName,-30} | Số lần xuất hiện: {ce.Count}");
                    }
                }

                await File.WriteAllTextAsync(singleFilePath, sb.ToString(), Utf8NoBom);
                Console.WriteLine($"[LƯU FILE MCS & UMCS] Đã lưu kết quả quét tại: {singleFilePath}");

                string allFilePath = Path.Combine(scanDir, $"{source.EntityId}_{safeEntityName}_All_MCS_UMCS.txt");
                var allSb = new StringBuilder();
                allSb.AppendLine($"\n\n### [LINK #{linkIndex}] {link.Title} ###");
                allSb.AppendLine($"URL: {link.Url}");
                allSb.AppendLine("--------------------------------------------------------------------------------");
                allSb.AppendLine(sb.ToString());

                if (linkIndex == 1)
                    await File.WriteAllTextAsync(allFilePath, allSb.ToString(), Utf8NoBom);
                else
                    await File.AppendAllTextAsync(allFilePath, allSb.ToString(), Utf8NoBom);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE SCAN ERROR] Không thể lưu kết quả quét MCS/UMCS: {ex.Message}");
            }
        }

        public async Task<string> SaveScanSummaryAsync(
            string outputFolder,
            SourceEntity source,
            List<SearchResult> searchResults,
            List<ErelScanResponseDto> allScans,
            List<EntityCountResult> finalEntity2Results)
        {
            try
            {
                string scanDir = Path.Combine(outputFolder, "ScanResults");
                Directory.CreateDirectory(scanDir);

                string safeEntityName = SanitizeFileName(source.WikiName);
                string summaryFilePath = Path.Combine(scanDir, $"{source.EntityId}_{safeEntityName}_Summary_MCS_UMCS.txt");

                var sb = new StringBuilder();
                sb.AppendLine("================================================================================");
                sb.AppendLine("TỔNG HỢP TOÀN BỘ KẾT QUẢ SCAN TẬP MCS VÀ UMCS CỦA TẤT CẢ CÁC BÀI VIẾT");
                sb.AppendLine($"THỰC THỂ NGUỒN: [{source.EntityId}] {source.WikiName}");
                sb.AppendLine($"TỔNG SỐ BÀI VIẾT ĐÃ CÀO: {searchResults.Count}");
                sb.AppendLine($"THỜI GIAN HOÀN THÀNH: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("================================================================================");
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("[1] TỔNG HỢP TẬP MCS (CHỈ HIỂN THỊ CÁC MENTIONS CÓ > 1 CANDIDATE TRONG CSDL)");
                sb.AppendLine("--------------------------------------------------------------------------------");

                var ambiguousSummaryMentions = EntityAggregationHelper.BuildAmbiguousGroups(allScans);
                sb.AppendLine($"Số lượng: {ambiguousSummaryMentions.Count} mentions có > 1 candidate");
                if (ambiguousSummaryMentions.Count == 0)
                {
                    sb.AppendLine("(Không có mention nào có > 1 candidate)");
                }
                else
                {
                    int rank = 1;
                    foreach (var group in ambiguousSummaryMentions)
                    {
                        var rep = allScans.SelectMany(s => s.Mcs)
                            .First(m => m.Mention.Trim().Equals(group.Mention, StringComparison.OrdinalIgnoreCase));

                        sb.AppendLine($"#{rank++,3}. Mention: \"{group.Mention}\" (Tổng xuất hiện: {group.TotalCount} lần)");
                        sb.AppendLine($"      -> EREL khuyên chọn: [ID: {group.ErelSelectedEntityId}] {rep.SelectedWikiName}");
                        if (group.Candidates.Count > 0)
                        {
                            sb.AppendLine($"      * Candidates trong KB ({group.Candidates.Count}):");
                            foreach (var c in group.Candidates)
                            {
                                string selTag = c.IsSelected ? " [CHỌN]" : "";
                                sb.AppendLine($"        - ID: {c.EntityId,-6} | WikiName: {c.WikiName,-25} | M: {c.M:F2} | Me: {c.Me:F2} | Mc: {c.Mc:F2} | Ms: {c.Ms:F2}{selTag}");
                            }
                        }
                    }
                }
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine("[2] TỔNG HỢP TẬP UMCS (TẤT CẢ CÁC TỪ CHƯA CÓ TRONG CSDL QUA CÁC LINK)");
                sb.AppendLine("--------------------------------------------------------------------------------");

                var allUmcsMentions = allScans
                    .SelectMany(s => s.Umcs)
                    .GroupBy(u => u.Mention.Trim(), StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(g => g.Count());

                int uRank = 1;
                foreach (var ug in allUmcsMentions)
                {
                    sb.AppendLine($"  {uRank++,3}. \"{ug.Key}\" (Tổng xuất hiện: {ug.Count()} lần)");
                }
                sb.AppendLine();

                sb.AppendLine("--------------------------------------------------------------------------------");
                sb.AppendLine($"[3] BẢNG TỔNG HỢP CẶP LIÊN KẾT (Entity1: [{source.EntityId}] {source.WikiName} -> Entity2)");
                sb.AppendLine("--------------------------------------------------------------------------------");
                foreach (var r in finalEntity2Results.OrderByDescending(x => x.Count))
                {
                    sb.AppendLine($"{source.EntityId}|{r.EntityId}|{r.Count}  -->  ({source.WikiName} -> {r.WikiName}: {r.Count} lần)");
                }

                await File.WriteAllTextAsync(summaryFilePath, sb.ToString(), Utf8NoBom);
                Console.WriteLine($"[LƯU FILE TỔNG HỢP] Đã lưu file tổng hợp MCS & UMCS: {summaryFilePath}");
                return summaryFilePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE SUMMARY ERROR] Lỗi khi lưu file tổng hợp: {ex.Message}");
                return string.Empty;
            }
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "Entity";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder();
            foreach (char c in name)
            {
                sb.Append(invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c);
            }
            return sb.ToString().Trim('_');
        }

        /// <summary>
        /// Xuất file mẫu chứa các mention có > 1 candidate để người dùng đánh dấu [x] chọn thủ công
        /// </summary>
        public async Task<string> SaveManualReviewFileAsync(
            string outputFolder,
            SourceEntity source,
            List<AmbiguousMentionGroup> ambiguousMentions,
            List<SingleCandidateMentionGroup> singleCandidateMentions)
        {
            try
            {
                string reviewDir = Path.Combine(outputFolder, "ManualReview");
                Directory.CreateDirectory(reviewDir);

                string safeEntityName = SanitizeFileName(source.WikiName);
                string filePath = Path.Combine(reviewDir, $"{source.EntityId}_{safeEntityName}_ManualSelect.txt");

                var sb = new StringBuilder();
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine("# BẢNG ĐÁNH DẤU CHỌN ENTITY CHO CÁC MENTION CÓ NHIỀU ỨNG VIÊN (> 1 CANDIDATE)");
                sb.AppendLine($"# THỰC THỂ NGUỒN: [{source.EntityId}] {source.WikiName}");
                sb.AppendLine($"# THỜI GIAN TẠO: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine("# ------------------------------------------------------------------------------");
                sb.AppendLine("# HƯỚNG DẪN DÀNH CHO BẠN:");
                sb.AppendLine("# 1. Đặt dấu [x] hoặc [X] vào trước ứng viên bạn muốn chọn cho mỗi Mention.");
                sb.AppendLine("# 2. Mặc định EREL đã đánh dấu sẵn [x] vào ứng viên được dự đoán tốt nhất.");
                sb.AppendLine("# 3. Bạn có thể chuyển [x] sang ứng viên khác nếu thấy phù hợp hơn.");
                sb.AppendLine("# 4. Nếu không muốn tính Mention này vào bất kỳ Entity nào, hãy xóa [x] (để [ ] hết).");
                sb.AppendLine("# 5. KHÔNG sửa đổi cấu trúc dòng có 'ID: <số>' và tên Mention.");
                sb.AppendLine("# 6. Sau khi chỉnh sửa xong, nhấn LƯU FILE (Ctrl + S), rồi quay lại Console nhấn [ENTER].");
                sb.AppendLine("# ==============================================================================");
                sb.AppendLine();

                for (int i = 0; i < ambiguousMentions.Count; i++)
                {
                    var m = ambiguousMentions[i];
                    sb.AppendLine($"[{i + 1}] Mention: \"{m.Mention}\" | Xuất hiện: {m.TotalCount} lần | POS: {m.PosTag ?? "N/A"} | Loại: {m.Category ?? "N/A"}");

                    for (int cIdx = 0; cIdx < m.Candidates.Count; cIdx++)
                    {
                        var c = m.Candidates[cIdx];
                        bool isErelSelected = (c.EntityId == m.ErelSelectedEntityId);
                        string checkMark = isErelSelected ? "[x]" : "[ ]";
                        string erelTag = isErelSelected ? " [EREL GỢI Ý]" : "";

                        sb.AppendLine($"    {checkMark} ({cIdx + 1}) ID: {c.EntityId,-8} | WikiName: {c.WikiName,-28} | RealName: {c.RealName,-20} | M={c.M:F2} | Me={c.Me:F2} | Mc={c.Mc:F2} | Ms={c.Ms:F2}{erelTag}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("# ==============================================================================");
                sb.AppendLine($"# DANH SÁCH {singleCandidateMentions.Count} MENTION CÓ 1 CANDIDATE (ĐÃ ĐƯỢC TỰ ĐỘNG TÍNH COUNT, KHÔNG CẦN CHỌN):");
                sb.AppendLine("# ------------------------------------------------------------------------------");
                for (int i = 0; i < singleCandidateMentions.Count; i++)
                {
                    var s = singleCandidateMentions[i];
                    sb.AppendLine($"# ({i + 1}) Mention: \"{s.Mention}\" -> [ID: {s.EntityId}] {s.WikiName} | Xuất hiện: {s.TotalCount} lần");
                }
                sb.AppendLine("# ==============================================================================");

                await File.WriteAllTextAsync(filePath, sb.ToString(), Utf8NoBom);
                return filePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SAVE MANUAL REVIEW ERROR] Không thể lưu file đánh dấu: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Đọc lại file sau khi người dùng đã đánh dấu [x] để lấy danh sách Entity được chọn
        /// </summary>
        public List<ManualSelectionResult> ParseManualReviewFile(string filePath)
        {
            var results = new List<ManualSelectionResult>();
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[PARSE ERROR] Không tìm thấy file: {filePath}");
                return results;
            }

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            string? currentMention = null;
            int currentCount = 1;
            long selectedId = 0;
            string selectedWikiName = "";

            var mentionRegex = new Regex(@"Mention:\s*""([^""]+)""(?:.*?Xuất hiện:\s*(\d+))?", RegexOptions.IgnoreCase);
            var candidateRegex = new Regex(@"\[[xX]\]\s*(?:\(\d+\)\s*)?ID:\s*(\d+)\s*\|\s*WikiName:\s*([^|]+)", RegexOptions.IgnoreCase);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();

                if (line.StartsWith("#"))
                {
                    if (line.Contains("DANH SÁCH") && line.Contains("1 CANDIDATE"))
                        break;
                    continue;
                }

                var mMatch = mentionRegex.Match(line);
                if (mMatch.Success)
                {
                    if (!string.IsNullOrWhiteSpace(currentMention))
                    {
                        results.Add(new ManualSelectionResult
                        {
                            Mention = currentMention,
                            MentionCount = currentCount,
                            SelectedEntityId = selectedId,
                            SelectedWikiName = selectedWikiName
                        });
                    }

                    currentMention = mMatch.Groups[1].Value.Trim();
                    currentCount = (mMatch.Groups[2].Success && int.TryParse(mMatch.Groups[2].Value, out int parsedCount))
                        ? Math.Max(1, parsedCount)
                        : 1;
                    selectedId = 0;
                    selectedWikiName = "";
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentMention))
                {
                    var cMatch = candidateRegex.Match(line);
                    if (cMatch.Success && long.TryParse(cMatch.Groups[1].Value, out long id))
                    {
                        selectedId = id;
                        selectedWikiName = cMatch.Groups[2].Value.Trim();
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(currentMention))
            {
                results.Add(new ManualSelectionResult
                {
                    Mention = currentMention,
                    MentionCount = currentCount,
                    SelectedEntityId = selectedId,
                    SelectedWikiName = selectedWikiName
                });
            }

            return results;
        }

        public async Task WriteDebugAsync(
            string logPath,
            SourceEntity source,
            List<SearchResult> searchResults,
            IEnumerable<EntityCountResult> results,
            List<ErelScanResponseDto>? docScans = null)
        {
            try
            {
                EnsureParentDirectory(logPath);

                using var writer = new StreamWriter(logPath, append: true, encoding: Utf8NoBom);

                await writer.WriteLineAsync("==============================================");
                await writer.WriteLineAsync($"SOURCE: {source.EntityId}|{source.WikiName}");
                await writer.WriteLineAsync($"TIME: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await writer.WriteLineAsync();

                await writer.WriteLineAsync("SEARCH RESULTS:");
                foreach (var search in searchResults)
                {
                    await writer.WriteLineAsync($"{search.Position}. {search.Title}");
                    await writer.WriteLineAsync(search.Url);
                }
                await writer.WriteLineAsync();

                if (docScans != null && docScans.Count > 0)
                {
                    await writer.WriteLineAsync("EREL SCAN DETAILS (MCS & UMCS):");
                    for (int k = 0; k < docScans.Count; k++)
                    {
                        var scan = docScans[k];
                        await writer.WriteLineAsync($"[LINK #{k + 1}]");

                        var ambMcs = scan.Mcs.Where(m => (m.Candidates?.Count ?? 0) > 1).ToList();
                        await writer.WriteLineAsync($"  * Tập MCS (> 1 Candidate, {ambMcs.Count} mentions):");
                        foreach (var m in ambMcs.Take(25))
                        {
                            await writer.WriteLineAsync($"    - Mention: \"{m.Mention}\" -> Chọn: [{m.SelectedEntityId}] {m.SelectedWikiName} (Candidates: {m.Candidates.Count})");
                        }
                        if (ambMcs.Count > 25)
                        {
                            await writer.WriteLineAsync($"    ... và {ambMcs.Count - 25} mentions khác.");
                        }

                        await writer.WriteLineAsync($"  * Tập UMCS ({scan.Umcs.Count} từ chưa có trong KB):");
                        await writer.WriteLineAsync("    " + string.Join(", ", scan.Umcs.Take(25).Select(u => u.Mention)));
                        if (scan.Umcs.Count > 25)
                        {
                            await writer.WriteLineAsync($"    ... và {scan.Umcs.Count - 25} từ khác.");
                        }
                        await writer.WriteLineAsync();
                    }
                }

                await writer.WriteLineAsync("ENTITY RESULTS:");
                foreach (var entity in results.OrderByDescending(x => x.Count))
                {
                    await writer.WriteLineAsync($"{entity.EntityId}|{entity.WikiName}|{entity.Count}");
                }
                await writer.WriteLineAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OUTPUT ERROR] Không thể ghi log debug {logPath}: {ex.Message}");
            }
        }
    }
}
