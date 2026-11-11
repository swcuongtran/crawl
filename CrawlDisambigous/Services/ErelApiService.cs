using CrawlDisambigous.Models;
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class ErelApiService
    {
        public const string DefaultErelUrl = "http://103.98.152.201:3000/";
        private const string ExtractEntitiesDetailEndpoint = "api/extract-entities-detail";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly HttpClient _client;

        public string BaseUrl => _client.BaseAddress?.ToString() ?? DefaultErelUrl;

        public ErelApiService(string? baseUrl = null)
        {
            string targetUrl = !string.IsNullOrWhiteSpace(baseUrl)
                ? baseUrl
                : Environment.GetEnvironmentVariable("EREL_URL") ?? DefaultErelUrl;
            string normalizedUrl = targetUrl.TrimEnd('/') + "/";

            _client = new HttpClient
            {
                BaseAddress = new Uri(normalizedUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public async Task<bool> CheckHealthAsync(int timeoutSeconds = 2)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var resp = await _client.GetAsync("swagger/index.html", cts.Token);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<ErelScanResponseDto> ScanAndExtractAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new ErelScanResponseDto();

            var request = new { message = text, text };

            try
            {
                using var response = await _client.PostAsJsonAsync(ExtractEntitiesDetailEndpoint, request);
                if (!response.IsSuccessStatusCode)
                {
                    string errDetail = $"Remote EREL tại {_client.BaseAddress} trả về mã lỗi HTTP {(int)response.StatusCode} ({response.StatusCode}).";
                    Console.WriteLine($"[EREL REMOTE ERROR] {errDetail}");
                    return new ErelScanResponseDto { ErrorMessage = errDetail };
                }

                string responseString = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(responseString))
                    return new ErelScanResponseDto();

                return JsonSerializer.Deserialize<ErelScanResponseDto>(responseString, JsonOptions)
                    ?? new ErelScanResponseDto();
            }
            catch (Exception ex)
            {
                string errDetail = $"Lỗi kết nối/timeout tới Remote EREL tại {_client.BaseAddress}: {ex.Message}";
                Console.WriteLine($"[EREL REMOTE ERROR] {errDetail}");
                return new ErelScanResponseDto { ErrorMessage = errDetail };
            }
        }
    }
}
