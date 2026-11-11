using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class SearxngSearchService
    {
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
            // 1. Nếu SearXNG (Docker) đang bật, tìm kiếm siêu tốc qua SearXNG API (100ms)
            if (await IsAvailableAsync(timeoutSeconds: 1))
            {
                try
                {
                    Console.WriteLine($"[SEARCH] Tìm kiếm qua SearXNG ({_baseUrl}) cho '{query}'...");
                    var searxResults = await SearchViaSearxngApiAsync(query, targetCount, engines);
                    if (searxResults.Count > 0)
                        return searxResults;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SEARCH WARNING] Lỗi SearXNG: {ex.Message} -> Chuyển sang Web Search Fallback...");
                }
            }

            // 2. Nếu SearXNG chưa bật (Docker chưa bật): Tự động tìm kiếm qua Web Search Fallback trực tiếp
            Console.WriteLine($"[SEARCH] SearXNG chưa bật -> Tự động tìm kiếm qua Web Search Fallback cho '{query}'...");
            return await SearchViaWebFallbackAsync(query, targetCount);
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
                    Url = cleanUrl
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

                    var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]//h2/a");
                    if (resultNodes != null)
                    {
                        foreach (var node in resultNodes)
                        {
                            if (results.Count >= targetCount)
                                break;

                            string href = DecodeDuckDuckGoRedirect(node.GetAttributeValue("href", "").Trim());
                            string title = WebUtility.HtmlDecode(node.InnerText).Trim();

                            if (!Uri.TryCreate(href, UriKind.Absolute, out Uri? uri))
                                continue;

                            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                                continue;

                            if (_urlFilter.IsBlacklisted(href) || !seenUrls.Add(href))
                                continue;

                            results.Add(new SearchResult
                            {
                                Position = results.Count + 1,
                                Title = title,
                                Url = href
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
