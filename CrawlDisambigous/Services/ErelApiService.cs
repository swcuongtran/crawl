using CrawlDisambigous.Models;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class ErelApiService
    {
        public const string DefaultErelUrl = "http://103.98.152.201:3000/";
        public const string ExtractEntitiesDetailEndpoint = "api/extract-entities-detail";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions PrettyRequestJsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly HttpClient _client;

        public string BaseUrl => _client.BaseAddress?.ToString() ?? DefaultErelUrl;
        public string ExtractEndpointUrl => new Uri(new Uri(BaseUrl), ExtractEntitiesDetailEndpoint).ToString();

        public static string BuildRequestJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "{}";

            var request = new { message = text, text };
            return JsonSerializer.Serialize(request, PrettyRequestJsonOptions);
        }

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

            string rawRequestJson = BuildRequestJson(text);

            try
            {
                using var content = new StringContent(rawRequestJson, Encoding.UTF8, "application/json");
                using var response = await _client.PostAsync(ExtractEntitiesDetailEndpoint, content);
                int statusCode = (int)response.StatusCode;
                string responseString = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errDetail = $"EREL tại {_client.BaseAddress} trả về mã lỗi HTTP {statusCode} ({response.StatusCode}).";
                    Console.WriteLine($"[EREL ERROR] {errDetail}");
                    return new ErelScanResponseDto
                    {
                        ErrorMessage = errDetail,
                        RawRequestJson = rawRequestJson,
                        RawResponseJson = responseString ?? "",
                        HttpStatusCode = statusCode
                    };
                }

                if (string.IsNullOrWhiteSpace(responseString))
                {
                    return new ErelScanResponseDto
                    {
                        RawRequestJson = rawRequestJson,
                        RawResponseJson = "",
                        HttpStatusCode = statusCode
                    };
                }

                var dto = JsonSerializer.Deserialize<ErelScanResponseDto>(responseString, JsonOptions)
                    ?? new ErelScanResponseDto();
                dto.InputText = text;
                dto.RawRequestJson = rawRequestJson;
                dto.RawResponseJson = responseString;
                dto.HttpStatusCode = statusCode;

                // Tự động tính chỉ số đoạn (ParagraphIndex) và trích xuất câu văn ngữ cảnh (ContextSentence) cho từng mention
                foreach (var m in dto.Mcs)
                {
                    var (pIdx, sentence) = EntityAggregationHelper.ExtractContext(text, m.MinCharIdx, m.MaxCharIdx);
                    m.ParagraphIndex = pIdx;
                    m.ContextSentence = sentence;
                }

                return dto;
            }
            catch (Exception ex)
            {
                string errDetail = $"Lỗi kết nối/timeout tới EREL tại {_client.BaseAddress}: {ex.Message}";
                Console.WriteLine($"[EREL ERROR] {errDetail}");
                return new ErelScanResponseDto
                {
                    ErrorMessage = errDetail,
                    RawRequestJson = rawRequestJson,
                    RawResponseJson = ""
                };
            }
        }
    }
}
