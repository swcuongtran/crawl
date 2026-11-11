using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class EntityCrawlerService
    {
        private readonly SearxngSearchService _searchService;
        private readonly WebCrawlerService _crawlerService;
        private readonly ErelApiService _erelService;
        private readonly ResultWriterService _writerService;
        private readonly UrlFilterService _urlFilter;

        public EntityCrawlerService(
            SearxngSearchService? searchService = null,
            WebCrawlerService? crawlerService = null,
            ErelApiService? erelService = null,
            ResultWriterService? writerService = null,
            UrlFilterService? filterService = null)
        {
            _urlFilter = filterService ?? new UrlFilterService();
            _searchService = searchService ?? new SearxngSearchService(_urlFilter);
            _crawlerService = crawlerService ?? new WebCrawlerService(_urlFilter);
            _erelService = erelService ?? new ErelApiService();
            _writerService = writerService ?? new ResultWriterService();
        }

        public async Task<List<EntityCountResult>> ProcessEntityAsync(
            SourceEntity source,
            int searchCount = 10,
            string? outputPath = null,
            string? debugLogPath = null,
            bool manualSelect = true)
        {
            Console.WriteLine($"\n==================================================");
            Console.WriteLine($"[CRAWLER] Processing {source.EntityId} - {source.WikiName}");
            Console.WriteLine($"==================================================");

            List<SearchResult> searchResults = await _searchService.SearchAsync(source.WikiName, searchCount);
            if (searchResults.Count == 0)
            {
                Console.WriteLine($"[CRAWLER] Không tìm thấy URL nào cho từ khóa: {source.WikiName}");
                return new List<EntityCountResult>();
            }

            List<SearchResult> targetResults = searchResults;
            if (manualSelect)
            {
                Console.WriteLine($"\n--- DANH SÁCH {searchResults.Count} LINK TÌM THẤY ---");
                for (int i = 0; i < searchResults.Count; i++)
                {
                    Console.WriteLine($"[{i + 1}] {searchResults[i].Title}");
                    Console.WriteLine($"    URL: {searchResults[i].Url}");
                }

                Console.Write("\nChọn các link muốn cào (ví dụ: 1,3,5 hoặc 1-5 | Enter = Chọn tất cả | 's' = Bỏ qua): ");
                string? input = Console.ReadLine();

                if (input?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Console.WriteLine("[INFO] Bỏ qua cào thực thể này theo yêu cầu.");
                    return new List<EntityCountResult>();
                }

                targetResults = ParseUserSelection(searchResults, input);
                Console.WriteLine($"\n[INFO] Đã chọn {targetResults.Count}/{searchResults.Count} link để cào.");
            }

            return await ProcessSelectedResultsAsync(source, targetResults, outputPath, debugLogPath);
        }

        public async Task<List<EntityCountResult>> ProcessSelectedResultsAsync(
            SourceEntity source,
            List<SearchResult> selectedResults,
            string? outputPath = null,
            string? debugLogPath = null)
        {
            var total = new Dictionary<int, EntityCountResult>();
            var allDocScans = new List<ErelScanResponseDto>();

            string defaultFolder = EntityAggregationHelper.GetDefaultOutputFolder();
            outputPath ??= Path.Combine(defaultFolder, "CrawlerResult.txt");
            debugLogPath ??= Path.Combine(defaultFolder, "CrawlerDebug.txt");

            for (int i = 0; i < selectedResults.Count; i++)
            {
                var item = selectedResults[i];
                Console.WriteLine($"\n[CRAWL {i + 1}/{selectedResults.Count}] Fetching: {item.Url}");
                string text = await _crawlerService.GetContentAsync(item.Url);

                if (string.IsNullOrWhiteSpace(text))
                {
                    Console.WriteLine("[CRAWL] Nội dung rỗng hoặc không tải được, bỏ qua.");
                    continue;
                }

                Console.WriteLine($"\n[CRAWL SUCCESS] Đã cào được {text.Length:N0} ký tự từ: {item.Title}");
                await _writerService.SaveCrawledContentAsync(defaultFolder, source, i + 1, item, text);

                string erelText = PrepareTextForErel(text, maxChars: 0);
                Console.WriteLine("[EREL SCAN] Đang quét các từ và thực thể trong doc qua EREL...");
                var scanResult = await _erelService.ScanAndExtractAsync(erelText);
                allDocScans.Add(scanResult);

                DisplayScanResults(i + 1, item.Title, scanResult, source);

                EntityAggregationHelper.AccumulateSingleCandidates(scanResult, source.EntityId, total);

                var erelEntitiesForDoc = EntityAggregationHelper.BuildEntitiesForDoc(scanResult, source.EntityId);
                await _writerService.SaveScanResultAsync(defaultFolder, source, i + 1, item, scanResult, erelEntitiesForDoc);
            }

            var ambiguousMentionGroups = EntityAggregationHelper.BuildAmbiguousGroups(allDocScans, source.EntityId);
            var singleCandidateSummary = EntityAggregationHelper.BuildSingleCandidateSummary(allDocScans, source.EntityId);

            if (ambiguousMentionGroups.Count > 0)
            {
                await PromptManualFileReviewAsync(defaultFolder, source, ambiguousMentionGroups, singleCandidateSummary, total);
            }
            else
            {
                Console.WriteLine("\n[INFO] Không có mention nào có > 1 candidate cần duyệt.");
            }

            var resultsList = total.Values.ToList();
            await SaveConsoleSessionOutputsAsync(defaultFolder, source, selectedResults, resultsList, allDocScans, outputPath, debugLogPath);
            return resultsList;
        }

        private async Task PromptManualFileReviewAsync(
            string defaultFolder,
            SourceEntity source,
            List<AmbiguousMentionGroup> ambiguousMentionGroups,
            List<SingleCandidateMentionGroup> singleCandidateSummary,
            Dictionary<int, EntityCountResult> total)
        {
            string reviewFilePath = await _writerService.SaveManualReviewFileAsync(
                defaultFolder,
                source,
                ambiguousMentionGroups,
                singleCandidateSummary
            );

            Console.WriteLine("\n================================================================================");
            Console.WriteLine($"[TỰ ĐỘNG XONG] Đã tự động tính count cho {singleCandidateSummary.Count} mention có 1 candidate duy nhất.");
            Console.WriteLine($"[TẠM DỪNG ĐỂ ĐÁNH DẤU CHỌN ENTITY] Phát hiện {ambiguousMentionGroups.Count} mention có nhiều hơn 1 candidate.");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine($"-> File đánh dấu đã được lưu tại:\n   {reviewFilePath}");
            Console.WriteLine("  - Đặt [x] vào ứng viên muốn chọn (mặc định EREL đã chọn sẵn), lưu file rồi nhấn [ENTER].");
            Console.WriteLine("================================================================================");
            Console.Write("Nhấn [ENTER] sau khi bạn đã chỉnh sửa & lưu file: ");
            Console.ReadLine();

            var manualResults = _writerService.ParseManualReviewFile(reviewFilePath);
            Console.WriteLine($"\n[ĐỌC FILE THÀNH CÔNG] Đã nhận diện lựa chọn cho {manualResults.Count} mentions:");

            foreach (var mr in manualResults)
            {
                if (mr.SelectedEntityId > 0 && mr.SelectedEntityId != source.EntityId)
                {
                    int chosenId = (int)mr.SelectedEntityId;
                    EntityAggregationHelper.AddOrIncrementEntityCount(total, chosenId, mr.SelectedWikiName, mr.MentionCount);
                    Console.WriteLine($"  + Mention \"{mr.Mention}\" (x{mr.MentionCount}) -> Đã tính cho [{chosenId}] {mr.SelectedWikiName}");
                }
                else
                {
                    Console.WriteLine($"  - Mention \"{mr.Mention}\" (x{mr.MentionCount}) -> [BỎ QUA / KHÔNG CHỌN]");
                }
            }
        }

        private async Task SaveConsoleSessionOutputsAsync(
            string defaultFolder,
            SourceEntity source,
            List<SearchResult> selectedResults,
            List<EntityCountResult> resultsList,
            List<ErelScanResponseDto> allDocScans,
            string outputPath,
            string debugLogPath)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeEntityName = ResultWriterService.SanitizeFileName(source.WikiName);
            string resultsDir = Path.Combine(defaultFolder, "Results");
            Directory.CreateDirectory(resultsDir);

            string sessionResultFile = Path.Combine(resultsDir, $"{source.EntityId}_{safeEntityName}_{timestamp}_Result.txt");
            await _writerService.WriteAsync(sessionResultFile, source.EntityId, resultsList, append: false);
            await _writerService.WriteAsync(outputPath, source.EntityId, resultsList, append: true);
            await _writerService.SaveScanSummaryAsync(defaultFolder, source, selectedResults, allDocScans, resultsList);
            await _writerService.WriteDebugAsync(debugLogPath, source, selectedResults, resultsList, allDocScans);

            Console.WriteLine($"\n[SUCCESS] File kết quả riêng: {sessionResultFile}");
            Console.WriteLine($"[DONE] Hoàn tất thực thể {source.WikiName}. Tổng cộng {resultsList.Count} thực thể liên quan.");
        }

        public static void DisplayScanResults(int linkIndex, string title, ErelScanResponseDto scanResult, SourceEntity source)
        {
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine($"  [EREL SCAN RESULTS] - BÀI VIẾT #{linkIndex}: {title}");
            Console.WriteLine("================================================================================");

            var ambiguousGroups = EntityAggregationHelper.BuildAmbiguousGroups(new[] { scanResult });
            Console.WriteLine($"\n--- [1] TẬP MCS (> 1 Candidate trong CSDL) - {ambiguousGroups.Count} mentions ---");

            if (ambiguousGroups.Count == 0)
            {
                Console.WriteLine("  (Không có mention nào có > 1 candidate)");
            }
            else
            {
                for (int i = 0; i < ambiguousGroups.Count; i++)
                {
                    var group = ambiguousGroups[i];
                    var rep = scanResult.Mcs.First(m => m.Mention.Trim().Equals(group.Mention, StringComparison.OrdinalIgnoreCase));
                    string isSourceMarker = (group.ErelSelectedEntityId == source.EntityId) ? " [CHÍNH LÀ ENTITY 1]" : "";

                    Console.WriteLine($"  [{i + 1}] Mention: \"{group.Mention}\" (Xuất hiện: {group.TotalCount} lần | POS: {group.PosTag ?? "N/A"} | Loại: {group.Category ?? "N/A"})");
                    Console.WriteLine($"      -> EREL chọn: [ID: {group.ErelSelectedEntityId}] {rep.SelectedWikiName} (Điểm: {rep.ImportanceScore:F2}){isSourceMarker}");

                    for (int cIdx = 0; cIdx < group.Candidates.Count; cIdx++)
                    {
                        var c = group.Candidates[cIdx];
                        string selTag = c.IsSelected ? " [EREL CHỌN]" : "";
                        Console.WriteLine($"        ({cIdx + 1}) ID: {c.EntityId,-6} | WikiName: {c.WikiName,-25} | M={c.M:F2} | Me={c.Me:F2} | Mc={c.Mc:F2} | Ms={c.Ms:F2}{selTag}");
                    }
                }
            }

            Console.WriteLine($"\n--- [2] TẬP UMCS - {scanResult.Umcs.Count} từ/cụm từ CHƯA CÓ trong CSDL ---");
            if (scanResult.Umcs.Count == 0)
            {
                Console.WriteLine("  (Không có từ chưa nhận diện)");
            }
            else
            {
                var umcsTerms = EntityAggregationHelper.BuildUmcsTerms(new[] { scanResult }, 30);
                Console.WriteLine("  " + string.Join(", ", umcsTerms));
            }
            Console.WriteLine();
        }

        public static List<SearchResult> ParseUserSelection(List<SearchResult> allResults, string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<SearchResult>(allResults);

            var selectedIndices = new HashSet<int>();
            var parts = input.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                if (part.Contains('-'))
                {
                    var range = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                    {
                        for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                        {
                            if (i >= 1 && i <= allResults.Count)
                                selectedIndices.Add(i - 1);
                        }
                    }
                }
                else if (int.TryParse(part, out int idx) && idx >= 1 && idx <= allResults.Count)
                {
                    selectedIndices.Add(idx - 1);
                }
            }

            if (selectedIndices.Count == 0)
            {
                Console.WriteLine("[WARN] Không nhận dạng được số thứ tự hợp lệ, mặc định chọn TẤT CẢ.");
                return new List<SearchResult>(allResults);
            }

            return selectedIndices.OrderBy(i => i).Select(i => allResults[i]).ToList();
        }

        public static string PrepareTextForErel(string fullText, int maxChars = 0)
        {
            if (string.IsNullOrWhiteSpace(fullText) || maxChars <= 0 || fullText.Length <= maxChars)
                return fullText;

            int lastBreak = fullText.LastIndexOfAny(new[] { '.', '\n', '!' }, maxChars);
            return lastBreak > maxChars / 2
                ? fullText.Substring(0, lastBreak + 1)
                : fullText.Substring(0, maxChars);
        }
    }
}
