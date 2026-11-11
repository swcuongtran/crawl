using CrawlDisambigous.Models;
using CrawlDisambigous.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Kiểm tra nếu người dùng muốn chạy chế độ Console CLI truyền thống
if (args.Length > 0 && args[0].Equals("--console", StringComparison.OrdinalIgnoreCase))
{
    await RunConsoleModeAsync();
    return;
}

// Chạy Web API Backend phục vụ giao diện Next.js
var builder = WebApplication.CreateBuilder(args);

// Cấu hình CORS để Next.js (port 3001) gọi API tự do
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddSingleton<UrlFilterService>();
builder.Services.AddSingleton<SearxngSearchService>();
builder.Services.AddSingleton<WebCrawlerService>();
builder.Services.AddSingleton<ErelApiService>();
builder.Services.AddSingleton<ResultWriterService>();
builder.Services.AddSingleton<CrawlSessionService>();

var app = builder.Build();

app.UseCors("AllowAll");

// API Health & Config
app.MapGet("/api/health", async (CrawlSessionService sessionService) =>
{
    bool erelOnline = await sessionService.CheckErelConnectionAsync(timeoutSeconds: 2);
    bool searxngOnline = await sessionService.CheckSearxngConnectionAsync(timeoutSeconds: 1);
    return Results.Ok(new
    {
        status = "healthy",
        erelConnected = erelOnline,
        erelServer = sessionService.ErelServerUrl,
        searxngConnected = searxngOnline,
        searxngServer = sessionService.SearxngServerUrl,
        outputFolder = sessionService.OutputFolder,
        version = "2.0.0 (SearXNG & Remote EREL Edition)"
    });
});

// API 1: Tìm kiếm URLs (SearXNG / Web Search)
app.MapPost("/api/search", async (SearchRequest request, CrawlSessionService sessionService) =>
{
    if (string.IsNullOrWhiteSpace(request.WikiName))
        return Results.BadRequest(new { error = "Tên thực thể (WikiName) không được để trống." });

    int count = request.SearchCount > 0 ? request.SearchCount : 10;
    try
    {
        var session = await sessionService.SearchAsync(request.WikiName, request.EntityId, count);
        return Results.Ok(session);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[SEARCH API ERROR] {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
});

// API 2: Bắt đầu cào và quét EREL cho các link đã chọn
app.MapPost("/api/crawl/start", (StartCrawlRequest request, CrawlSessionService sessionService) =>
{
    if (request.SelectedUrls == null || request.SelectedUrls.Count == 0)
        return Results.BadRequest(new { error = "Chưa chọn link nào để cào." });

    var session = sessionService.StartCrawl(
        request.SessionId,
        request.WikiName,
        request.EntityId,
        request.SelectedUrls
    );
    return Results.Ok(session);
});

// API 3: Kiểm tra tiến trình cào và quét EREL (Polling)
app.MapGet("/api/session/{sessionId}", (string sessionId, CrawlSessionService sessionService) =>
{
    var session = sessionService.GetSession(sessionId);
    if (session == null)
        return Results.NotFound(new { error = $"Không tìm thấy phiên làm việc: {sessionId}" });

    return Results.Ok(session);
});

// API 4: Người dùng xác nhận lựa chọn candidate cho các mention nhập nhằng (> 1 candidate)
app.MapPost("/api/review/confirm", async (ConfirmReviewRequest request, CrawlSessionService sessionService) =>
{
    if (string.IsNullOrWhiteSpace(request.SessionId))
        return Results.BadRequest(new { error = "SessionId không được để trống." });

    try
    {
        var session = await sessionService.ConfirmReviewAsync(request.SessionId, request.Selections ?? new());
        return Results.Ok(session);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[REVIEW CONFIRM ERROR] {ex.Message}");
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
});

// API 5: Đọc nội dung file kết quả để hiển thị trên UI
app.MapGet("/api/file", (string path, CrawlSessionService sessionService) =>
{
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(new { error = "Đường dẫn file không hợp lệ." });

    string content = sessionService.ReadFileContent(path);
    return Results.Ok(new { path, content });
});

Console.WriteLine("==================================================================");
Console.WriteLine("    CRAWL DISAMBIGUOUS STUDIO - BACKEND API SERVER");
Console.WriteLine("==================================================================");
Console.WriteLine("[BACKEND] Web API đang lắng nghe tại: http://0.0.0.0:5000");
Console.WriteLine("[BACKEND] Giao diện Next.js chạy tại: http://localhost:3001");
Console.WriteLine("[BACKEND] Remote EREL Server: http://103.98.152.201:3000/");
Console.WriteLine("==================================================================");

app.Run("http://0.0.0.0:5000");

// ==========================================
// Chế độ Console CLI (khi chạy --console)
// ==========================================
static async Task RunConsoleModeAsync()
{
    Console.WriteLine("=== CRAWL DISAMBIGUATION TOOL (CONSOLE MODE) ===");
    string outputFolder = EntityAggregationHelper.GetDefaultOutputFolder();

    Directory.CreateDirectory(outputFolder);
    string outputPath = Path.Combine(outputFolder, "CrawlerResult.txt");
    string logPath = Path.Combine(outputFolder, "CrawlerDebug.txt");

    var crawlerService = new EntityCrawlerService(
        erelService: new ErelApiService()
    );

    while (true)
    {
        Console.WriteLine();
        Console.WriteLine(new string('-', 50));
        Console.Write("Nhập tên thực thể (WikiName) hoặc 'q' để thoát: ");
        string? wikiName = Console.ReadLine();

        if (string.IsNullOrWhiteSpace(wikiName))
            continue;

        string sanitized = SearxngSearchService.SanitizeQuery(wikiName);
        if (sanitized.Equals("q", StringComparison.OrdinalIgnoreCase))
            break;

        Console.Write("Nhập Entity ID (nhấn Enter lấy mặc định 1): ");
        string? idInput = Console.ReadLine();
        int entityId = int.TryParse(idInput, out int parsedId) ? parsedId : 1;

        var source = new SourceEntity
        {
            EntityId = entityId,
            WikiName = sanitized
        };

        try
        {
            await crawlerService.ProcessEntityAsync(
                source,
                searchCount: 10,
                outputPath: outputPath,
                debugLogPath: logPath,
                manualSelect: true
            );
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[LỖI] {ex.Message}");
            Console.ResetColor();
        }
    }
}