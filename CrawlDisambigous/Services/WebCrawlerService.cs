using HtmlAgilityPack;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    public class WebCrawlerService
    {
        private readonly HttpClient _client;
        private readonly UrlFilterService _urlFilter;
        private static IPlaywright? _playwright;
        private static IBrowser? _browser;
        private static readonly System.Threading.SemaphoreSlim _browserLock = new(1, 1);

        public WebCrawlerService(UrlFilterService? urlFilter = null)
        {
            _urlFilter = urlFilter ?? new UrlFilterService();

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };

            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromSeconds(30);

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36"
            );

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8"
            );

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept-Language",
                "vi-VN,vi;q=0.9,en-US;q=0.8,en;q=0.7"
            );

            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "sec-ch-ua",
                "\"Chromium\";v=\"124\", \"Google Chrome\";v=\"124\", \"Not-A.Brand\";v=\"99\""
            );

            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-fetch-dest", "document");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-fetch-mode", "navigate");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-fetch-site", "none");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("sec-fetch-user", "?1");
            _client.DefaultRequestHeaders.TryAddWithoutValidation("upgrade-insecure-requests", "1");
        }

        public class CrawledDocResult
        {
            public string Content { get; set; } = "";
            public string Method { get; set; } = "HttpClient";
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        public async Task<string> GetContentAsync(string url)
        {
            var res = await GetContentDetailsAsync(url);
            return res.Content;
        }

        public async Task<CrawledDocResult> GetContentDetailsAsync(string url)
        {
            var result = new CrawledDocResult();

            if (_urlFilter.IsBlacklisted(url))
            {
                Console.WriteLine($"[CRAWL] URL nằm trong blacklist mạng xã hội, bỏ qua: {url}");
                result.Method = "Blacklisted";
                result.ErrorMessage = "URL thuộc mạng xã hội bị chặn.";
                return result;
            }

            string content = "";

            // 1. Dùng bình thường qua HttpClient
            try
            {
                using var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    Encoding encoding = Encoding.UTF8;

                    string? charset = response.Content.Headers.ContentType?.CharSet;
                    if (!string.IsNullOrWhiteSpace(charset))
                    {
                        try
                        {
                            encoding = Encoding.GetEncoding(charset);
                        }
                        catch
                        {
                            encoding = Encoding.UTF8;
                        }
                    }

                    var doc = new HtmlDocument();
                    doc.OptionDefaultStreamEncoding = Encoding.UTF8;
                    using (var ms = new MemoryStream(bytes))
                    {
                        doc.Load(ms, encoding);
                    }

                    Encoding? metaEncoding = doc.DeclaredEncoding;
                    if (metaEncoding != null && !Equals(metaEncoding, encoding))
                    {
                        try
                        {
                            using var ms = new MemoryStream(bytes);
                            doc.Load(ms, metaEncoding);
                        }
                        catch
                        {
                        }
                    }

                    content = ExtractTextFromDocument(doc, url);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        result.Content = content;
                        result.Method = "HttpClient";
                        result.Success = true;
                        return result;
                    }
                }
                else
                {
                    Console.WriteLine($"[CRAWL] HttpClient trả về HTTP {(int)response.StatusCode} cho URL: {url} -> Chuyển sang Playwright...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRAWL] Lỗi truy cập HttpClient ({ex.Message}) cho URL: {url} -> Chuyển sang Playwright...");
            }

            // 2. Nếu HttpClient không vào được hoặc không lấy được nội dung (do chặn hoặc SPA), dùng Playwright
            if (string.IsNullOrWhiteSpace(content))
            {
                string? pwHtml = await FetchWithPlaywrightFallbackAsync(url);
                if (!string.IsNullOrWhiteSpace(pwHtml))
                {
                    var doc = new HtmlDocument();
                    doc.LoadHtml(pwHtml);
                    content = ExtractTextFromDocument(doc, url);

                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        Console.WriteLine($"[CRAWL SUCCESS] Tải và bóc tách thành công bằng Playwright!");
                        result.Content = content;
                        result.Method = "Playwright";
                        result.Success = true;
                        return result;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"[CRAWL] Nội dung rỗng hoặc không tải được: {url}");
                result.Method = "Failed";
                result.ErrorMessage = "Nội dung rỗng hoặc không vượt được bảo vệ.";
                return result;
            }

            result.Content = content;
            result.Success = true;
            return result;
        }

        private static readonly HashSet<string> ExactGarbageTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            // Form & Authentication / Login / Register / Account
            "form", "login", "signin", "signup", "register", "auth", "authentication", "account",
            "password", "membership", "paywall", "dangnhap", "dangky", "taikhoan", "matkhau",

            // Ads & Sponsorships & Marketing
            "ad", "ads", "advert", "advertisement", "advertising", "advertorial", "banner",
            "sponsor", "sponsored", "promo", "promotion", "affiliate", "marketing",
            "quangcao", "taitro", "khuyenmai", "uudai", "adsbygoogle", "outbrain", "taboola",
            "criteo", "mgid", "popunder",

            // Products & E-Commerce & Shopping / Cart / Pricing
            "product", "products", "ecommerce", "shop", "shopping", "cart", "store",
            "sanpham", "giohang", "dathang", "muahang", "muangay", "pricing", "banggia", "checkout",

            // Auxiliary Popups, Modals, Drawers, Tabs & Floating elements
            "popup", "modal", "overlay", "dialog", "drawer", "lightbox", "toast", "cookie", "gdpr",
            "newsletter", "subscribe", "subscribebox", "notice", "alert",

            // Navigation, TOC, Comments, Social & Footers (lưu ý: KHÔNG đưa 'widget' vì Elementor dùng cho nội dung chính)
            "sidebar", "aside", "toc", "comment", "comments", "binhluan", "social", "share", "chiase",
            "breadcrumb", "breadcrumbs", "pagination", "pager", "reflist", "references", "navbox", "catlinks"
        };

        private static readonly string[] GarbageSubstrings = new[]
        {
            // Tiếng Việt có gạch nối
            "quang-cao", "tai-tro", "khuyen-mai", "uu-dai", "san-pham", "gio-hang", "dat-hang",
            "mua-hang", "mua-ngay", "dang-nhap", "dang-ky", "tai-khoan", "mat-khau",
            "tin-lien-quan", "bai-lien-quan", "tin-cung-chuyen-muc", "binh-luan", "chia-se",
            "nhan-tin", "ban-quyen", "bang-gia", "thanh-toan",

            // Tiếng Anh ghép từ
            "advert-", "ad-box", "ad-container", "ad-banner", "ad-wrapper", "ad-slot", "ads-slot",
            "ad-placement", "ad-middle", "ad-bottom", "ad-top", "adsbygoogle",
            "related-post", "related-news", "related-product", "related-item",
            "product-item", "product-card", "product-list", "product-slider", "product-box",
            "login-box", "login-form", "login-modal", "signin-box", "signup-box", "register-box", "auth-box",
            "shopping-cart", "buy-now", "add-to-cart", "price-box", "price-table",
            "cookie-consent", "cookie-banner", "newsletter-form", "subscribe-form", "sub-tab", "tab-content",
            "post-comment", "comments-area", "post-share", "social-share",
            "sidebar-detail", "left-sidebar", "right-sidebar", "main-sidebar", "widget-sidebar", "blog-sidebar",
            "widget-area", "widget_recent", "widget_categories", "widget_archive", "widget_search", "widget_meta",
            "elementor-widget-posts", "elementor-widget-share-buttons", "elementor-widget-table-of-contents",
            "elementor-widget-form", "elementor-widget-breadcrumbs", "elementor-widget-post-info",
            "elementor-widget-nav-menu", "elementor-widget-search-form", "elementor-widget-author-box",
            "table-of-contents", "elementor-toc"
        };

        private static readonly string[] ProtectedContentSubstrings = new[]
        {
            "mw-content-text", "bodycontent", "entry-content", "post-content", "theme-post-content",
            "article-content", "detail-content", "main-detail", "fck_detail", "theme-post-title", "page-title"
        };

        private static readonly string[] SafeClassPrefixes = new[]
        {
            "no-", "without-", "has-", "with-", "category-", "tag-", "topic-",
            "single-", "page-", "template-", "format-", "wp-", "postid-", "elementor-page-", "elementor-location-"
        };

        public static bool IsProtectedContentContainer(string? cls, string? id)
        {
            if (string.IsNullOrWhiteSpace(cls) && string.IsNullOrWhiteSpace(id))
                return false;

            string combined = ((cls ?? "") + " " + (id ?? "")).ToLowerInvariant();
            foreach (var p in ProtectedContentSubstrings)
            {
                if (combined.Contains(p))
                    return true;
            }
            return false;
        }

        public static bool IsGarbageClassOrId(string? cls, string? id)
        {
            if (string.IsNullOrWhiteSpace(cls) && string.IsNullOrWhiteSpace(id))
                return false;

            if (IsProtectedContentContainer(cls, id))
                return false;

            string combined = ((cls ?? "") + " " + (id ?? "")).ToLowerInvariant();

            foreach (var sub in GarbageSubstrings)
            {
                if (combined.Contains(sub))
                    return true;
            }

            // Kiểm tra từng class/id đầy đủ để bỏ qua các prefix layout/phân loại (như no-sidebar, category-marketing)
            var fullIdentifiers = combined.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var ident in fullIdentifiers)
            {
                bool hasSafePrefix = SafeClassPrefixes.Any(prefix => ident.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                if (hasSafePrefix)
                    continue;

                var tokens = ident.Split(new[] { '-', '_', ':', '.', '/' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var token in tokens)
                {
                    if (ExactGarbageTokens.Contains(token))
                        return true;
                }
            }

            return false;
        }

        private static bool HasGarbageAncestor(HtmlNode node, HtmlNode? root = null)
        {
            var current = node.ParentNode;
            while (current != null && current != root && current.Name != "#document")
            {
                if (current.Name == "form" || current.Name == "button" || current.Name == "nav" ||
                    current.Name == "aside" || current.Name == "footer" || current.Name == "header" ||
                    current.Name == "dialog" || current.Name == "fieldset" || current.Name == "select" ||
                    current.Name == "datalist")
                {
                    return true;
                }

                string cls = current.GetAttributeValue("class", "");
                string id = current.GetAttributeValue("id", "");
                if (IsGarbageClassOrId(cls, id))
                    return true;

                current = current.ParentNode;
            }
            return false;
        }

        private static bool IsBoilerplateOrAdText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string lower = text.ToLowerInvariant();

            // Quảng cáo, Mua bán & Đặt hàng
            if (Regex.IsMatch(lower, @"^(mua ngay|đặt hàng ngay|liên hệ đặt hàng|hotline\s*[:\.]|giá bán\s*[:\.]|giá ưu đãi\s*[:\.]|bảng giá sản phẩm|xem chi tiết sản phẩm|thêm vào giỏ hàng|liên hệ tư vấn|tổng đài hỗ trợ)\b"))
                return true;

            // Form đăng nhập / Đăng ký / Tài khoản
            if (Regex.IsMatch(lower, @"^(đăng nhập để|đăng ký tài khoản|nhập email|nhập số điện thoại|quên mật khẩu|tên đăng nhập|mật khẩu của bạn|bạn đã có tài khoản|chưa có tài khoản|gửi thông tin|xác nhận mật khẩu)\b"))
                return true;

            // Bản quyền / Điều khoản / Cookie
            if (Regex.IsMatch(lower, @"^(chính sách bảo mật|điều khoản sử dụng|bản quyền thuộc về|tất cả quyền được bảo lưu|chúng tôi sử dụng cookie|nhận thông báo mới|bấm vào đây để đăng ký)\b"))
                return true;

            return false;
        }

        /// <summary>
        /// Bóc tách văn bản sạch từ cây HTML DOM (loại bỏ quảng cáo, menu, footer, script, và giữ lại nội dung chính)
        /// </summary>
        private static string ExtractTextFromDocument(HtmlDocument doc, string url)
        {
            try
            {
                // Kiểm tra ngôn ngữ trang: Bỏ qua nếu là tiếng Trung (zh) hoặc tiếng Nga (ru)
                string? htmlLang = doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", "")?.ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(htmlLang) && (htmlLang.StartsWith("zh") || htmlLang.StartsWith("ru")))
                {
                    Console.WriteLine($"[CRAWL] Bỏ qua trang ngôn ngữ nước ngoài (lang='{htmlLang}'): {url}");
                    return "";
                }

                // 1. Loại bỏ các thẻ form, điều khiển nhập liệu, điều hướng, chân trang, quảng cáo, mạng xã hội
                var garbage = doc.DocumentNode.SelectNodes(
                    "//script|//style|//nav|" +
                    "//footer|//header|//form|" +
                    "//iframe|//noscript|//svg|//aside|//button|" +
                    "//input|//select|//textarea|//fieldset|//legend|//datalist|//dialog|" +
                    "//div[contains(@class,'navbox')]|//div[contains(@class,'vertical-navbox')]|//table[contains(@class,'navbox')]|" +
                    "//div[@id='toc']|//div[contains(@class,'reflist')]|//div[contains(@class,'references')]|" +
                    "//div[@id='catlinks']|//span[contains(@class,'mw-editsection')]|" +
                    "//div[contains(@class,'comment')]|//div[contains(@class,'social')]|//div[contains(@class,'share')]"
                );

                if (garbage != null)
                {
                    foreach (var node in garbage)
                        node.Remove();
                }

                // 2. Loại bỏ các container có class/id rác (quảng cáo, form đăng nhập, sản phẩm, popup, sidebar...)
                var styledNodes = doc.DocumentNode.SelectNodes("//*[@class or @id]");
                if (styledNodes != null)
                {
                    foreach (var node in styledNodes)
                    {
                        if (node.Name == "html" || node.Name == "body" || node.Name == "main" || node.Name == "article")
                            continue;

                        string nodeCls = node.GetAttributeValue("class", "");
                        string nodeId = node.GetAttributeValue("id", "");
                        if (node.ParentNode != null && IsGarbageClassOrId(nodeCls, nodeId))
                        {
                            node.Remove();
                        }
                    }
                }

                var lines = new List<string>();

                // 1. Tiêu đề bài viết (h1)
                var h1Node = doc.DocumentNode.SelectSingleNode("//h1");
                if (h1Node != null && !HasGarbageAncestor(h1Node))
                {
                    string h1Text = CleanText(h1Node.InnerText);
                    if (h1Text.Length >= 3 && !IsBoilerplateOrAdText(h1Text))
                        lines.Add(h1Text);
                }

                // 2. Sapo / Mô tả tóm tắt
                var sapoNode = doc.DocumentNode.SelectSingleNode("//*[contains(@class,'sapo') or contains(@class,'description') or contains(@class,'lead')]");
                if (sapoNode != null && !HasGarbageAncestor(sapoNode))
                {
                    string sapoText = CleanText(sapoNode.InnerText);
                    if (sapoText.Length >= 15 && !IsBoilerplateOrAdText(sapoText) && !lines.Contains(sapoText))
                        lines.Add(sapoText);
                }

                // 3. Vùng chứa nội dung chính bài viết (ưu tiên container có nhiều đoạn văn bản nhất, tránh popup/modal/form)
                HtmlNode contentRoot = FindBestContentRoot(doc);

                // 4. Bóc tách các thẻ nội dung: h2, h3, h4, p, li, blockquote
                var contentNodes = contentRoot.SelectNodes(".//h2|.//h3|.//h4|.//p|.//li|.//blockquote") ??
                                   doc.DocumentNode.SelectNodes("//p");

                if (contentNodes != null)
                {
                    foreach (var node in contentNodes)
                    {
                        if (HasGarbageAncestor(node, contentRoot))
                            continue;

                        string id = node.GetAttributeValue("id", "");
                        string cls = node.GetAttributeValue("class", "");
                        if (IsGarbageClassOrId(cls, id) || id == "toc" || cls.Contains("mw-editsection") || cls.Contains("catlinks"))
                            continue;

                        string text = CleanText(node.InnerText);
                        if (string.IsNullOrWhiteSpace(text) || IsBoilerplateOrAdText(text))
                            continue;

                        if (node.Name == "h2" || node.Name == "h3" || node.Name == "h4")
                        {
                            if (text.Length >= 3 && !lines.Contains(text))
                                lines.Add(text);
                        }
                        else if (node.Name == "li")
                        {
                            if (text.Length >= 10 && !lines.Contains(text))
                                lines.Add(text);
                        }
                        else
                        {
                            if (text.Length >= 15 && !lines.Contains(text))
                                lines.Add(text);
                        }
                    }
                }

                if (lines.Count == 0)
                    return "";

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRAWL EXTRACT FAIL] {url}: {ex.Message}");
                return "";
            }
        }

        private static string CleanText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            string text = WebUtility.HtmlDecode(raw).Trim();
            // Xóa các chú thích nguồn Wikipedia [1], [2], [cần dẫn nguồn], [citation needed]
            text = Regex.Replace(text, @"\[\d+\]|\[cần dẫn nguồn\]|\[citation needed\]", "");
            // Xóa các chuỗi gạch ngang, bằng, sao trang trí (---, ===, ___, ***) gây dính chữ và nhiễu EREL
            text = Regex.Replace(text, @"[-=_~*]{2,}", " ");
            // Xóa ký tự bullet đầu dòng nếu có
            text = Regex.Replace(text, @"^[\s•\-\*\u2022\u2023\u25E6\u2043\u2219]+\s*", "");
            // Chuẩn hóa khoảng trắng
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        /// <summary>
        /// Tìm vùng chứa nội dung chính bài viết (Article, Main, Content div, Wikipedia body...)
        /// Tránh chọn nhầm các popup, modal, login box có chứa từ khóa 'content'
        /// </summary>
        private static HtmlNode FindBestContentRoot(HtmlDocument doc)
        {
            // 1. Các vùng chuẩn thường gặp trên Wikipedia, báo chí lớn
            var standardRoot =
                doc.DocumentNode.SelectSingleNode("//div[@id='mw-content-text']") ??
                doc.DocumentNode.SelectSingleNode("//div[@id='bodyContent']") ??
                doc.DocumentNode.SelectSingleNode("//article") ??
                doc.DocumentNode.SelectSingleNode("//main") ??
                doc.DocumentNode.SelectSingleNode("//div[contains(@class,'entry-content') or contains(@class,'post-content') or contains(@class,'article-content') or contains(@class,'detail-content') or contains(@class,'main-detail') or contains(@class,'fck_detail')]");

            if (standardRoot != null)
                return standardRoot;

            // 2. Tìm tất cả các div có class chứa content/detail/post, lọc bỏ popup, modal, login, form, product, ad, menu, header, footer
            var candidateNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'content') or contains(@class,'detail') or contains(@class,'post') or contains(@class,'article')]");
            if (candidateNodes != null && candidateNodes.Count > 0)
            {
                HtmlNode? bestNode = null;
                int maxPCount = 0;

                foreach (var node in candidateNodes)
                {
                    string cls = node.GetAttributeValue("class", "");
                    string id = node.GetAttributeValue("id", "");

                    if (IsGarbageClassOrId(cls, id))
                    {
                        continue;
                    }

                    int pCount = node.SelectNodes(".//p")?.Count ?? 0;
                    if (pCount > maxPCount)
                    {
                        maxPCount = pCount;
                        bestNode = node;
                    }
                }

                if (bestNode != null && maxPCount >= 2)
                    return bestNode;
            }

            // 3. Fallback sang body hoặc toàn bộ document
            return doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        }

        public static async Task<IBrowser?> GetOrCreateBrowserAsync()
        {
            if (_browser != null && _browser.IsConnected)
                return _browser;

            await _browserLock.WaitAsync();
            try
            {
                if (_browser != null && _browser.IsConnected)
                    return _browser;

                Console.WriteLine("[CRAWL PLAYWRIGHT] Khởi tạo Playwright browser instance...");
                _playwright ??= await Playwright.CreateAsync();

                try
                {
                    _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true,
                        Channel = "msedge"
                    });
                }
                catch
                {
                    _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = true
                    });
                }

                return _browser;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRAWL PLAYWRIGHT INIT ERROR] {ex.Message}");
                return null;
            }
            finally
            {
                _browserLock.Release();
            }
        }

        /// <summary>
        /// Tải trang qua Playwright khi HttpClient không vào được (HTTP 403, 503, Cloudflare hoặc lỗi kết nối).
        /// </summary>
        private static async Task<string?> FetchWithPlaywrightFallbackAsync(string url)
        {
            IBrowserContext? context = null;
            IPage? page = null;
            try
            {
                var browser = await GetOrCreateBrowserAsync();
                if (browser == null)
                    return null;

                context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
                });

                page = await context.NewPageAsync();

                Console.WriteLine($"[CRAWL PLAYWRIGHT] Đang truy cập URL: {url}...");
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 30000
                });

                // Đợi 1.5 giây để JS hoàn tất render hoặc trang cập nhật
                await page.WaitForTimeoutAsync(1500);

                string html = await page.ContentAsync();
                string title = await page.TitleAsync();

                if (!string.IsNullOrWhiteSpace(html) &&
                    !title.Contains("Attention Required! | Cloudflare") &&
                    !html.Contains("Sorry, you have been blocked"))
                {
                    return html;
                }
                else
                {
                    Console.WriteLine($"[CRAWL PLAYWRIGHT] Trang vẫn bị Cloudflare chặn hoặc rỗng: {title}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRAWL PLAYWRIGHT ERROR] Lỗi khi chạy Playwright: {ex.Message}");
                return null;
            }
            finally
            {
                if (page != null)
                {
                    try { await page.CloseAsync(); } catch { }
                }
                if (context != null)
                {
                    try { await context.CloseAsync(); } catch { }
                }
            }
        }
    }
}
