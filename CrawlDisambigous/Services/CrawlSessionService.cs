using CrawlDisambigous.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class CrawlSessionService
    {
        private readonly ConcurrentDictionary<string, CrawlSession> _sessions = new();
        private readonly SearxngSearchService _searxngService;
        private readonly WebCrawlerService _crawlerService;
        private readonly ErelApiService _erelService;
        private readonly ResultWriterService _writerService;
        private readonly UrlFilterService _urlFilter;
        private readonly DisamLinkService _disamLinkService;
        private readonly string _defaultOutputFolder;

        public CrawlSessionService(
            SearxngSearchService? searxngService = null,
            WebCrawlerService? crawlerService = null,
            ErelApiService? erelService = null,
            ResultWriterService? writerService = null,
            UrlFilterService? filterService = null,
            DisamLinkService? disamLinkService = null)
        {
            _urlFilter = filterService ?? new UrlFilterService();
            _searxngService = searxngService ?? new SearxngSearchService(_urlFilter);
            _crawlerService = crawlerService ?? new WebCrawlerService(_urlFilter);
            _erelService = erelService ?? new ErelApiService();
            _writerService = writerService ?? new ResultWriterService();
            _disamLinkService = disamLinkService ?? new DisamLinkService();

            _defaultOutputFolder = EntityAggregationHelper.GetDefaultOutputFolder();
            Directory.CreateDirectory(_defaultOutputFolder);
        }

        public string OutputFolder => _defaultOutputFolder;
        public string ErelServerUrl => _erelService.BaseUrl;
        public string SearxngServerUrl => _searxngService.BaseUrl;
        public Task<bool> CheckErelConnectionAsync(int timeoutSeconds = 2) => _erelService.CheckHealthAsync(timeoutSeconds);
        public Task<bool> CheckSearxngConnectionAsync(int timeoutSeconds = 1) => _searxngService.IsAvailableAsync(timeoutSeconds);
        public Task<bool> CheckDisamLinkConnectionAsync(int timeoutSeconds = 2) => _disamLinkService.CheckHealthAsync(timeoutSeconds);

        public CrawlSession? GetSession(string sessionId)
        {
            _sessions.TryGetValue(sessionId, out var session);
            return session;
        }

        public async Task<CrawlSession> SearchAsync(string wikiName, int entityId = 1, int searchCount = 10)
        {
            string sanitized = SearxngSearchService.SanitizeQuery(wikiName);
            var session = new CrawlSession
            {
                Source = new SourceEntity
                {
                    EntityId = entityId,
                    WikiName = sanitized
                },
                Status = "searching",
                CurrentMessage = $"Đang tìm kiếm link cho '{sanitized}'..."
            };

            _sessions[session.SessionId] = session;

            var searchSw = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"[SEARCH] Bắt đầu tìm kiếm cho '{sanitized}' (mục tiêu {searchCount} links)...");
            var searchResults = await _searxngService.SearchAsync(sanitized, searchCount);
            searchSw.Stop();

            session.Metrics.SearchDurationMs = Math.Round(searchSw.Elapsed.TotalMilliseconds, 1);
            session.SearchResults = searchResults.Select((r, idx) => new SearchResultDto
            {
                Index = idx + 1,
                Title = r.Title,
                Url = r.Url,
                Snippet = r.Snippet ?? ""
            }).ToList();

            session.Status = "ready_to_crawl";
            session.CurrentMessage = $"Tìm thấy {session.SearchResults.Count} link hữu cơ ({session.Metrics.SearchDurationMs} ms).";
            return session;
        }

        public CrawlSession StartCrawl(string? sessionId, string wikiName, int entityId, List<string> selectedUrls)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
            {
                session = new CrawlSession
                {
                    Source = new SourceEntity
                    {
                        EntityId = entityId,
                        WikiName = SearxngSearchService.SanitizeQuery(wikiName)
                    }
                };
                _sessions[session.SessionId] = session;
            }

            session.Status = "crawling";
            session.CompletedLinks = 0;
            session.TotalLinks = selectedUrls.Count;
            session.CrawledLinks.Clear();
            session.AmbiguousGroups.Clear();
            session.SingleCandidateSummary.Clear();
            session.FinalResults.Clear();
            session.UmcsTerms.Clear();

            for (int i = 0; i < selectedUrls.Count; i++)
            {
                var url = selectedUrls[i];
                var existingSearch = session.SearchResults.FirstOrDefault(s => s.Url.Equals(url, StringComparison.OrdinalIgnoreCase));
                session.CrawledLinks.Add(new LinkCrawlDetail
                {
                    Index = i + 1,
                    Url = url,
                    Title = existingSearch?.Title ?? url,
                    Status = "pending",
                    FetchMethod = "N/A"
                });
            }

            _ = Task.Run(() => ExecuteCrawlAsync(session));
            return session;
        }

        private async Task ExecuteCrawlAsync(CrawlSession session)
        {
            session.AllDocScans.Clear();
            var singleCandidateMap = new Dictionary<int, EntityCountResult>();
            var sessionSw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                for (int i = 0; i < session.CrawledLinks.Count; i++)
                {
                    await ProcessSingleLinkAsync(session, session.CrawledLinks[i], i + 1, singleCandidateMap, sessionSw);
                }

                session.UmcsTerms = EntityAggregationHelper.BuildUmcsTerms(session.AllDocScans);
                session.AmbiguousGroups = EntityAggregationHelper.BuildAmbiguousGroups(session.AllDocScans, session.Source.EntityId);
                session.SingleCandidateSummary = EntityAggregationHelper.BuildSingleCandidateSummary(session.AllDocScans, session.Source.EntityId);

                // Pipeline kiểm tra bảng [EREL].[dbo].[DisAmLinkData_New]:
                // Kiểm tra từng candidate có EntityID1 trong DB hay chưa, nếu tất cả candidate đều có -> AllCandidatesInDisamLink = true
                await _disamLinkService.EnrichDisambiguationGroupsAsync(session.AmbiguousGroups);

                if (session.AmbiguousGroups.Count > 0)
                {
                    session.Status = "awaiting_review";
                    session.CurrentMessage = $"Đã quét xong {session.CompletedLinks} link. Phát hiện {session.AmbiguousGroups.Count} mentions có nhiều hơn 1 ứng viên cần bạn xác nhận.";
                    session.OutputFiles["ManualReview"] = await _writerService.SaveManualReviewFileAsync(
                        _defaultOutputFolder,
                        session.Source,
                        session.AmbiguousGroups,
                        session.SingleCandidateSummary
                    );
                }
                else
                {
                    await FinalizeResultsAsync(session, singleCandidateMap);
                }
            }
            catch (Exception ex)
            {
                session.Status = "error";
                session.CurrentMessage = $"Lỗi trong quá trình cào: {ex.Message}";
                Console.WriteLine($"[CRAWL ERROR] {ex}");
            }
        }

        private async Task ProcessSingleLinkAsync(
            CrawlSession session,
            LinkCrawlDetail link,
            int linkIndex,
            Dictionary<int, EntityCountResult> singleCandidateMap,
            System.Diagnostics.Stopwatch sessionSw)
        {
            link.Status = "crawling";
            session.CurrentMessage = $"Đang cào dữ liệu [{linkIndex}/{session.TotalLinks}]: {link.Title}...";

            var crawlSw = System.Diagnostics.Stopwatch.StartNew();
            var fetchRes = await _crawlerService.GetContentDetailsAsync(link.Url);
            crawlSw.Stop();

            link.CrawlDurationMs = Math.Round(crawlSw.Elapsed.TotalMilliseconds, 1);
            link.FetchMethod = fetchRes.Method;

            if (!fetchRes.Success || string.IsNullOrWhiteSpace(fetchRes.Content))
            {
                link.Status = "failed";
                link.Error = string.IsNullOrWhiteSpace(fetchRes.ErrorMessage) ? "Nội dung rỗng" : fetchRes.ErrorMessage;
                link.TotalDurationMs = link.CrawlDurationMs;
                session.CompletedLinks++;
                UpdateSessionMetrics(session, sessionSw);
                return;
            }

            link.TextLength = fetchRes.Content.Length;
            link.FullText = fetchRes.Content;
            link.Snippet = fetchRes.Content.Length > 300
                ? fetchRes.Content.Substring(0, 300).Trim() + "..."
                : fetchRes.Content.Trim();

            var searchItem = new SearchResult { Title = link.Title, Url = link.Url };
            await _writerService.SaveCrawledContentAsync(_defaultOutputFolder, session.Source, linkIndex, searchItem, fetchRes.Content);

            link.Status = "scanning";
            string erelText = EntityCrawlerService.PrepareTextForErel(fetchRes.Content, maxChars: 0);
            var erelSw = System.Diagnostics.Stopwatch.StartNew();
            var scanResult = await _erelService.ScanAndExtractAsync(erelText);
            erelSw.Stop();

            link.ErelDurationMs = Math.Round(erelSw.Elapsed.TotalMilliseconds, 1);
            link.TotalDurationMs = Math.Round(link.CrawlDurationMs + link.ErelDurationMs, 1);

            scanResult.DocIndex = linkIndex;
            scanResult.DocTitle = link.Title;
            foreach (var m in scanResult.Mcs)
            {
                m.DocIndex = linkIndex;
                m.DocTitle = link.Title;
            }
            session.AllDocScans.Add(scanResult);

            link.McsCount = scanResult.Mcs.Count;
            link.UmcsCount = scanResult.Umcs.Count;
            bool erelFailed = !string.IsNullOrWhiteSpace(scanResult.ErrorMessage)
                              && scanResult.Mcs.Count == 0
                              && scanResult.Umcs.Count == 0;

            link.Status = erelFailed ? "failed" : "success";
            if (erelFailed)
                link.Error = scanResult.ErrorMessage;

            session.CompletedLinks++;
            UpdateSessionMetrics(session, sessionSw);

            var erelEntitiesForDoc = EntityAggregationHelper.BuildEntitiesForDoc(scanResult, session.Source.EntityId);
            await _writerService.SaveScanResultAsync(_defaultOutputFolder, session.Source, linkIndex, searchItem, scanResult, erelEntitiesForDoc);

            EntityAggregationHelper.AccumulateSingleCandidates(scanResult, session.Source.EntityId, singleCandidateMap);
        }

        private static void UpdateSessionMetrics(CrawlSession session, System.Diagnostics.Stopwatch sessionSw)
        {
            session.Metrics.TotalCrawlDurationMs = Math.Round(session.CrawledLinks.Sum(l => l.CrawlDurationMs), 1);
            session.Metrics.TotalErelDurationMs = Math.Round(session.CrawledLinks.Sum(l => l.ErelDurationMs), 1);

            int crawledCount = session.CrawledLinks.Count(l => l.CrawlDurationMs > 0);
            session.Metrics.AverageCrawlPerLinkMs = crawledCount > 0
                ? Math.Round(session.Metrics.TotalCrawlDurationMs / crawledCount, 1)
                : 0;

            int scannedCount = session.CrawledLinks.Count(l => l.ErelDurationMs > 0);
            session.Metrics.AverageErelPerLinkMs = scannedCount > 0
                ? Math.Round(session.Metrics.TotalErelDurationMs / scannedCount, 1)
                : 0;

            session.Metrics.SuccessfulLinks = session.CrawledLinks.Count(l => l.Status == "success");
            session.Metrics.FailedLinks = session.CrawledLinks.Count(l => l.Status == "failed");
            session.Metrics.TotalSessionDurationMs = Math.Round(session.Metrics.SearchDurationMs + sessionSw.Elapsed.TotalMilliseconds, 1);
        }

        public async Task<CrawlSession> ConfirmReviewAsync(string sessionId, List<ManualSelectionResult> selections)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new KeyNotFoundException($"Session {sessionId} không tồn tại.");

            var reviewSw = System.Diagnostics.Stopwatch.StartNew();
            var total = new Dictionary<int, EntityCountResult>();

            foreach (var sc in session.SingleCandidateSummary)
            {
                EntityAggregationHelper.AddOrIncrementEntityCount(total, (int)sc.EntityId, sc.WikiName, sc.TotalCount);
            }

            foreach (var sel in selections)
            {
                if (sel.SelectedEntityId > 0 && sel.SelectedEntityId != session.Source.EntityId)
                {
                    EntityAggregationHelper.AddOrIncrementEntityCount(total, (int)sel.SelectedEntityId, sel.SelectedWikiName, sel.MentionCount);
                }
            }

            reviewSw.Stop();
            session.Metrics.DisambiguationDurationMs = Math.Round(reviewSw.Elapsed.TotalMilliseconds, 1);
            session.Metrics.TotalSessionDurationMs = Math.Round(session.Metrics.TotalSessionDurationMs + session.Metrics.DisambiguationDurationMs, 1);

            await FinalizeResultsAsync(session, total);
            return session;
        }

        private async Task FinalizeResultsAsync(CrawlSession session, Dictionary<int, EntityCountResult> total)
        {
            var scans = session.AllDocScans;
            session.FinalResults = total.Values
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.EntityId)
                .ToList();

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeEntityName = ResultWriterService.SanitizeFileName(session.Source.WikiName);

            string resultsDir = Path.Combine(_defaultOutputFolder, "Results");
            Directory.CreateDirectory(resultsDir);

            string sessionResultFileName = $"{session.Source.EntityId}_{safeEntityName}_{timestamp}_Result.txt";
            string sessionResultPath = Path.Combine(resultsDir, sessionResultFileName);
            await _writerService.WriteAsync(sessionResultPath, session.Source.EntityId, session.FinalResults, append: false);

            string generalOutputPath = Path.Combine(_defaultOutputFolder, "CrawlerResult.txt");
            string debugPath = Path.Combine(_defaultOutputFolder, "CrawlerDebug.txt");
            await _writerService.WriteAsync(generalOutputPath, session.Source.EntityId, session.FinalResults, append: true);

            var searchResults = session.CrawledLinks
                .Select(l => new SearchResult { Position = l.Index, Title = l.Title, Url = l.Url })
                .ToList();

            string summaryPath = await _writerService.SaveScanSummaryAsync(_defaultOutputFolder, session.Source, searchResults, scans, session.FinalResults);
            await _writerService.WriteDebugAsync(debugPath, session.Source, searchResults, session.FinalResults, scans);

            session.OutputFiles["SessionResult"] = sessionResultPath;
            session.OutputFiles["SessionResultFileName"] = sessionResultFileName;
            session.OutputFiles["CrawlerResult"] = generalOutputPath;
            session.OutputFiles["CrawlerDebug"] = debugPath;
            if (!string.IsNullOrWhiteSpace(summaryPath))
            {
                session.OutputFiles["Summary"] = summaryPath;
                session.OutputFiles["SummaryFileName"] = Path.GetFileName(summaryPath);
            }

            session.Status = "completed";
            session.CompletedAt = DateTime.UtcNow;

            if (session.Metrics.SuccessfulLinks == 0 && session.Metrics.FailedLinks > 0)
            {
                var firstErr = session.CrawledLinks.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.Error))?.Error;
                session.CurrentMessage = $"Cảnh báo: Không cào/quét thành công bài viết nào ({session.Metrics.FailedLinks}/{session.TotalLinks} link lỗi: {firstErr ?? "Không xác định"}).";
            }
            else
            {
                session.CurrentMessage = $"Hoàn tất cào và phân định! Đã xuất file riêng: {sessionResultFileName} ({session.FinalResults.Count} thực thể liên kết).";
            }
        }

        public CrawlSession StartRescanErel(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new KeyNotFoundException($"Session {sessionId} không tồn tại.");

            session.Status = "rescanning";
            session.CurrentMessage = "Bắt đầu quét lại toàn bộ văn bản qua EREL...";
            _ = Task.Run(() => ExecuteRescanErelAsync(session));
            return session;
        }

        public async Task<CrawlSession> RescanErelAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new KeyNotFoundException($"Session {sessionId} không tồn tại.");

            await ExecuteRescanErelAsync(session);
            return session;
        }

        private async Task ExecuteRescanErelAsync(CrawlSession session)
        {
            try
            {
                var newDocScans = new List<ErelScanResponseDto>();
                var singleCandidateMap = new Dictionary<int, EntityCountResult>();

                var validLinks = session.CrawledLinks
                    .Where(l => !string.IsNullOrWhiteSpace(l.FullText) && l.Status == "success")
                    .ToList();

                for (int i = 0; i < validLinks.Count; i++)
                {
                    var link = validLinks[i];
                    session.CurrentMessage = $"Đang quét lại EREL bài {i + 1}/{validLinks.Count}: {link.Title}...";

                    string erelText = EntityCrawlerService.PrepareTextForErel(link.FullText, maxChars: 0);
                    var erelSw = System.Diagnostics.Stopwatch.StartNew();
                    var scanResult = await _erelService.ScanAndExtractAsync(erelText);
                    erelSw.Stop();

                    link.ErelDurationMs = Math.Round(erelSw.Elapsed.TotalMilliseconds, 1);
                    link.TotalDurationMs = Math.Round(link.CrawlDurationMs + link.ErelDurationMs, 1);
                    link.McsCount = scanResult.Mcs.Count;
                    link.UmcsCount = scanResult.Umcs.Count;

                    scanResult.DocIndex = link.Index;
                    scanResult.DocTitle = link.Title;
                    foreach (var m in scanResult.Mcs)
                    {
                        m.DocIndex = link.Index;
                        m.DocTitle = link.Title;
                    }

                    newDocScans.Add(scanResult);

                    var erelEntitiesForDoc = EntityAggregationHelper.BuildEntitiesForDoc(scanResult, session.Source.EntityId);
                    var searchItem = new SearchResult { Title = link.Title, Url = link.Url };
                    await _writerService.SaveScanResultAsync(_defaultOutputFolder, session.Source, link.Index, searchItem, scanResult, erelEntitiesForDoc);
                    EntityAggregationHelper.AccumulateSingleCandidates(scanResult, session.Source.EntityId, singleCandidateMap);
                }

                session.AllDocScans = newDocScans;
                session.UmcsTerms = EntityAggregationHelper.BuildUmcsTerms(session.AllDocScans);
                session.AmbiguousGroups = EntityAggregationHelper.BuildAmbiguousGroups(session.AllDocScans, session.Source.EntityId);
                session.SingleCandidateSummary = EntityAggregationHelper.BuildSingleCandidateSummary(session.AllDocScans, session.Source.EntityId);

                // Kiểm tra lại DisAmLinkData_New với forceRefresh = true
                session.CurrentMessage = "Đang kiểm tra cơ sở dữ liệu DisAmLinkData_New...";
                await _disamLinkService.EnrichDisambiguationGroupsAsync(session.AmbiguousGroups, forceRefresh: true);

                session.OutputFiles["ManualReview"] = await _writerService.SaveManualReviewFileAsync(
                    _defaultOutputFolder,
                    session.Source,
                    session.AmbiguousGroups,
                    session.SingleCandidateSummary
                );

                session.Status = "awaiting_review";
                session.CurrentMessage = $"Đã quét lại toàn bộ EREL ({newDocScans.Count} bài viết). Phát hiện {session.AmbiguousGroups.Count} mentions cần bạn xác nhận.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EREL RESCAN ERROR] {ex}");
                session.Status = "awaiting_review";
                session.CurrentMessage = $"Lỗi khi quét lại EREL: {ex.Message}";
            }
        }

        public async Task<CrawlSession> RecheckDisamLinkAsync(string sessionId)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                throw new KeyNotFoundException($"Session {sessionId} không tồn tại.");

            if (session.AmbiguousGroups != null && session.AmbiguousGroups.Count > 0)
            {
                await _disamLinkService.EnrichDisambiguationGroupsAsync(session.AmbiguousGroups, forceRefresh: true);

                if (session.OutputFiles.ContainsKey("ManualReview"))
                {
                    session.OutputFiles["ManualReview"] = await _writerService.SaveManualReviewFileAsync(
                        _defaultOutputFolder,
                        session.Source,
                        session.AmbiguousGroups,
                        session.SingleCandidateSummary
                    );
                }
            }

            return session;
        }

        public string ReadFileContent(string relativeOrFullPath)
        {
            try
            {
                string fullPath = Path.IsPathRooted(relativeOrFullPath)
                    ? relativeOrFullPath
                    : Path.Combine(_defaultOutputFolder, relativeOrFullPath);

                if (!File.Exists(fullPath))
                    return $"File không tồn tại: {fullPath}";

                using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                return $"Lỗi đọc file: {ex.Message}";
            }
        }
    }
}
