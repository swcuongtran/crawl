using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class SearxngSearchService
    {
        private static readonly string[] NewsDomains = new[]
        {
            "vnexpress.net", "tuoitre.vn", "dantri.com.vn", "thanhnien.vn", "vietnamnet.vn",
            "vtv.vn", "laodong.vn", "tienphong.vn", "plo.vn", "vov.vn", "cand.com.vn",
            "baochinhphu.vn", "znews.vn", "cafef.vn", "cafebiz.vn", "vietnamplus.vn",
            "nhandan.vn", "kinhtedothi.vn", "sggp.org.vn", "baogiaothong.vn", "congluan.vn"
        };

        private static readonly string[] KnowledgeDomains = new[]
        {
            "thuvienphapluat.vn", "luatvietnam.vn", "luatminhkhue.vn", "luatduonggia.vn",
            "genk.vn", "tinhte.vn", "quantrimang.com", "vietnix.vn", "base.vn",
            "topcv.vn", "glints.com", "monkey.edu.vn", "careerbuilder.vn", "viblo.asia"
        };

        private static readonly string[] ArticlePathIndicators = new[]
        {
            "/wiki/", "/blog/", "/bai-viet/", "/kien-thuc/", "/giai-thich/",
            "/la-gi", "/khai-niem/", "/dinh-nghia/", "/article/", "/post/",
            "/news/", "/goc-nhin/", "/phan-tich/", "/huong-dan/", "/kinh-nghiem/",
            "/y-nghia/", "/chuyen-de/", "/chuyen-muc/", "/tin-tuc/", "/thong-tin/"
        };

        private static readonly string[] ExplanatoryKeywords = new[]
        {
            "là gì", "khái niệm", "định nghĩa", "tổng quan", "phân loại",
            "đánh giá", "ý nghĩa", "nguồn gốc", "lịch sử", "hướng dẫn",
            "chi tiết", "toàn diện", "vai trò", "tìm hiểu", "nguyên lý",
            "đặc điểm", "cơ chế", "bản chất", "ứng dụng", "báo cáo", "nghiên cứu"
        };

        private static readonly string[] CommercialPathIndicators = new[]
        {
            "/san-pham/", "/product/", "/products/", "/item/", "/items/",
            "/shop/", "/store/", "/cart/", "/checkout/", "/mua-ban/",
            "/gia-re/", "/danh-muc/", "/catalog/", "/catalogue/", "/bao-gia/",
            "/tai-ve/", "/download/", "/downloads/", "/game-app/"
        };

        private static readonly string[] CommercialKeywords = new[]
        {
            "giá rẻ", "chính hãng", "báo giá", "khuyến mãi", "mua ngay",
            "giảm giá", "freeship", "bán chạy", "thương hiệu", "đặt mua",
            "mua online", "shop", "giá tốt nhất", "mua bán", "giá ưu đãi",
            "liên hệ báo giá", "thông số kỹ thuật"
        };

        private readonly HttpClient _httpClient;
        private readonly UrlFilterService _urlFilter;
        private readonly string _baseUrl;

        public SearxngSearchService(UrlFilterService? filterService = null, string? baseUrl = null)
        {
            _urlFilter = filterService ?? new UrlFilterService();
            _baseUrl = (baseUrl ?? Environment.GetEnvironmentVariable("SEARXNG_URL") ?? "http://localhost:8080").TrimEnd('/');
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        }

        public string BaseUrl => _baseUrl;

        public static string SanitizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            return Regex.Replace(query, @"\s+", " ").Trim();
        }

        public async Task<bool> IsAvailableAsync(int timeoutSeconds = 1)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var response = await _httpClient.GetAsync($"{_baseUrl}/healthz", cts.Token);
                if (response.IsSuccessStatusCode)
                    return true;

                using var rootResponse = await _httpClient.GetAsync($"{_baseUrl}/", cts.Token);
                return rootResponse.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<SearchResult>> SearchAsync(
            string query,
            int targetCount = 10,
            string? engines = "bing,google,wikipedia,duckduckgo")
        {
            // 1. Tự động tìm kiếm bài viết Wikipedia tương ứng (chạy nền, timeout 2s, không bắt buộc)
            var wikiTask = TryGetWikipediaArticleAsync(query);

            // 2. Lấy mẫu nhiều hơn (over-fetch) để hệ thống lọc và ưu tiên các bài viết dài, giải thích, báo chí
            int overfetchCount = Math.Max(targetCount * 2 + 5, 25);
            List<SearchResult> rawResults = new();

            // Nếu SearXNG (Docker) đang bật, tìm kiếm qua SearXNG API
            if (await IsAvailableAsync(timeoutSeconds: 1))
            {
                try
                {
                    Console.WriteLine($"[SEARCH] Tìm kiếm qua SearXNG ({_baseUrl}) cho '{query}'...");
                    rawResults = await SearchViaSearxngApiAsync(query, overfetchCount, engines);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SEARCH WARNING] Lỗi SearXNG: {ex.Message} -> Chuyển sang Web Search Fallback...");
                }
            }

            // Nếu SearXNG chưa bật hoặc không trả kết quả: Dùng Web Search Fallback trực tiếp
            if (rawResults.Count == 0)
            {
                Console.WriteLine($"[SEARCH] SearXNG chưa bật -> Tự động tìm kiếm qua Web Search Fallback cho '{query}'...");
                rawResults = await SearchViaWebFallbackAsync(query, overfetchCount);
            }

            // Đợi kết quả Wikipedia (nếu có bài sẽ ghim đầu, nếu không có hoặc timeout thì bỏ qua an toàn)
            SearchResult? wikiResult = null;
            try
            {
                wikiResult = await wikiTask;
            }
            catch { }

            // 3. Chấm điểm chất lượng nội dung, ưu tiên bài văn bản dài/giải thích và chọn top targetCount
            return RankAndSelectResults(rawResults, wikiResult, targetCount);
        }

        private async Task<SearchResult?> TryGetWikipediaArticleAsync(string query, CancellationToken cancellationToken = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));

                string encoded = Uri.EscapeDataString(query.Trim());
                string url = $"https://vi.wikipedia.org/w/api.php?action=opensearch&search={encoded}&limit=1&namespace=0&format=json";

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent", "ErelDisambiguationStudio/1.0 (Windows NT 10.0; Win64; x64)");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                using var response = await _httpClient.SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                    return null;

                string json = await response.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Chuẩn OpenSearch JSON: [ query, [titles], [descriptions], [urls] ]
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 4)
                {
                    var titles = root[1];
                    var snippets = root[2];
                    var urls = root[3];

                    if (urls.GetArrayLength() > 0 && titles.GetArrayLength() > 0)
                    {
                        string? wikiUrl = urls[0].GetString();
                        string? wikiTitle = titles[0].GetString();
                        string? wikiSnippet = snippets.GetArrayLength() > 0 ? snippets[0].GetString() : null;

                        if (!string.IsNullOrWhiteSpace(wikiUrl) && !string.IsNullOrWhiteSpace(wikiTitle))
                        {
                            Console.WriteLine($"[WIKI MATCH] Đã tìm thấy bài viết Wikipedia: {wikiTitle} -> {wikiUrl}");
                            return new SearchResult
                            {
                                Position = 1,
                                Title = $"{wikiTitle} – Wikipedia tiếng Việt",
                                Url = wikiUrl.Trim(),
                                Snippet = string.IsNullOrWhiteSpace(wikiSnippet)
                                    ? $"Bách khoa toàn thư mở Wikipedia – Khái niệm và giải thích tổng quan về '{wikiTitle}'."
                                    : wikiSnippet.Trim(),
                                QualityScore = 1000.0 // Ưu tiên cao nhất
                            };
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WIKI NOTICE] Không lấy được link Wiki: {ex.Message}");
            }
            return null;
        }

        private double CalculateQualityScore(string url, string title, string? snippet)
        {
            double score = 10.0;

            if (string.IsNullOrWhiteSpace(url))
                return -100.0;

            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                string host = uri.Host.ToLowerInvariant();
                string path = uri.AbsolutePath.ToLowerInvariant();

                // 1. Wikipedia (bách khoa toàn thư, giải thích chi tiết)
                if (host.Contains("wikipedia.org"))
                {
                    return 500.0;
                }

                // 2. Báo chí chính thống (đoạn văn chuẩn, tin tức phân tích)
                if (NewsDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 50.0;
                }

                // 3. Trang kiến thức, kỹ thuật, pháp luật, blog học thuật
                if (KnowledgeDomains.Any(d => host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 40.0;
                }

                // 4. URL path báo hiệu bài viết / blog / giải thích chuyên sâu
                if (ArticlePathIndicators.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    score += 30.0;
                }

                // 5. URL path báo hiệu trang bán hàng, thương mại điện tử
                if (CommercialPathIndicators.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    score -= 50.0;
                }
            }

            string combinedText = $"{title} {snippet}".ToLowerInvariant();

            // 6. Tiêu đề / tóm tắt chứa từ khóa giải thích, định nghĩa
            foreach (var kw in ExplanatoryKeywords)
            {
                if (combinedText.Contains(kw))
                {
                    score += 20.0;
                    break;
                }
            }

            // 7. Độ dài đoạn trích dẫn (snippet dài chứng minh có đoạn văn hoàn chỉnh)
            if (!string.IsNullOrWhiteSpace(snippet) && snippet.Length >= 70)
            {
                score += 15.0;
            }

            // 8. Trừ điểm nặng nếu có từ khóa thương mại / bán lẻ
            foreach (var cw in CommercialKeywords)
            {
                if (combinedText.Contains(cw))
                {
                    score -= 40.0;
                    break;
                }
            }

            return score;
        }

        private List<SearchResult> RankAndSelectResults(
            List<SearchResult> rawResults,
            SearchResult? wikiResult,
            int targetCount)
        {
            var finalResults = new List<SearchResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Nếu có bài viết Wikipedia, luôn ghim vào vị trí đầu tiên
            if (wikiResult != null && !string.IsNullOrWhiteSpace(wikiResult.Url))
            {
                seenUrls.Add(wikiResult.Url);
                finalResults.Add(wikiResult);
            }

            // 2. Chấm điểm chất lượng từng link ứng viên
            var scoredResults = new List<SearchResult>();
            foreach (var item in rawResults)
            {
                if (string.IsNullOrWhiteSpace(item.Url) || !seenUrls.Add(item.Url))
                    continue;

                if (_urlFilter.IsBlacklisted(item.Url))
                    continue;

                item.QualityScore = CalculateQualityScore(item.Url, item.Title, item.Snippet);
                scoredResults.Add(item);
            }

            // 3. Sắp xếp các link theo điểm chất lượng giảm dần (bài báo & bài giải thích lên trước)
            var sortedCandidates = scoredResults
                .OrderByDescending(x => x.QualityScore)
                .ToList();

            // 4. Ưu tiên chọn các link có điểm dương (bài báo, kiến thức, giải thích)
            foreach (var item in sortedCandidates)
            {
                if (finalResults.Count >= targetCount)
                    break;

                // Nếu đã có đủ bài viết tốt, bỏ qua các link có điểm âm (trang bán hàng/tải app)
                if (item.QualityScore < 0 && sortedCandidates.Count(x => x.QualityScore >= 0) >= targetCount)
                    continue;

                finalResults.Add(item);
            }

            // 5. Đánh số thứ tự Position 1..N
            for (int i = 0; i < finalResults.Count; i++)
            {
                finalResults[i].Position = i + 1;
            }

            return finalResults;
        }

        private async Task<List<SearchResult>> SearchViaSearxngApiAsync(string query, int targetCount, string? engines)
        {
            string encodedQuery = Uri.EscapeDataString(query);
            string engineParam = string.IsNullOrWhiteSpace(engines) ? "" : $"&engines={Uri.EscapeDataString(engines)}";
            string url = $"{_baseUrl}/search?q={encodedQuery}&format=json&language=vi-VN{engineParam}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var searchData = await response.Content.ReadFromJsonAsync<SearxngResponse>();
            var results = new List<SearchResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (searchData?.Results == null || searchData.Results.Count == 0)
                return results;

            foreach (var item in searchData.Results)
            {
                if (results.Count >= targetCount)
                    break;

                if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.Title))
                    continue;

                string cleanUrl = item.Url.Trim();
                if (!seenUrls.Add(cleanUrl))
                    continue;

                if (_urlFilter.IsBlacklisted(cleanUrl))
                    continue;

                results.Add(new SearchResult
                {
                    Position = results.Count + 1,
                    Title = item.Title.Trim(),
                    Url = cleanUrl,
                    Snippet = item.Content?.Trim() ?? ""
                });
            }

            return results;
        }

        private async Task<List<SearchResult>> SearchViaWebFallbackAsync(string query, int targetCount)
        {
            var results = new List<SearchResult>();
            var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var values = new Dictionary<string, string>
                {
                    { "q", query }
                };
                using var content = new FormUrlEncodedContent(values);
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
                {
                    Content = content
                };
                request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                request.Headers.TryAddWithoutValidation("Accept-Language", "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7");

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlAgilityPack.HtmlDocument();
                    doc.LoadHtml(html);

                    var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]");
                    if (resultNodes != null)
                    {
                        foreach (var node in resultNodes)
                        {
                            if (results.Count >= targetCount)
                                break;

                            var linkNode = node.SelectSingleNode(".//h2/a") ?? node.SelectSingleNode(".//a[contains(@class, 'result__a')]");
                            if (linkNode == null)
                                continue;

                            string href = DecodeDuckDuckGoRedirect(linkNode.GetAttributeValue("href", "").Trim());
                            string title = WebUtility.HtmlDecode(linkNode.InnerText).Trim();

                            if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
                                continue;

                            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                                continue;

                            if (_urlFilter.IsBlacklisted(href) || !seenUrls.Add(href))
                                continue;

                            var snippetNode = node.SelectSingleNode(".//*[contains(@class, 'result__snippet')]");
                            string snippet = snippetNode != null ? WebUtility.HtmlDecode(snippetNode.InnerText).Trim() : "";

                            results.Add(new SearchResult
                            {
                                Position = results.Count + 1,
                                Title = title,
                                Url = href,
                                Snippet = snippet
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEARCH FALLBACK ERROR] {ex.Message}");
            }

            return results;
        }

        private static string DecodeDuckDuckGoRedirect(string href)
        {
            int idx = href.IndexOf("uddg=", StringComparison.Ordinal);
            if (idx == -1)
                return href;

            string encoded = href.Substring(idx + 5);
            int amp = encoded.IndexOf('&');
            if (amp != -1)
                encoded = encoded.Substring(0, amp);

            return Uri.UnescapeDataString(encoded);
        }

        public class SearxngResponse
        {
            [JsonPropertyName("query")]
            public string? Query { get; set; }

            [JsonPropertyName("number_of_results")]
            public int? NumberOfResults { get; set; }

            [JsonPropertyName("results")]
            public List<SearxngResultItem>? Results { get; set; }
        }

        public class SearxngResultItem
        {
            [JsonPropertyName("url")]
            public string? Url { get; set; }

            [JsonPropertyName("title")]
            public string? Title { get; set; }

            [JsonPropertyName("content")]
            public string? Content { get; set; }

            [JsonPropertyName("engine")]
            public string? Engine { get; set; }

            [JsonPropertyName("score")]
            public double? Score { get; set; }
        }
    }
}
