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

        public static bool IsReferenceSectionHeader(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string lower = text.Trim().ToLowerInvariant().TrimEnd(':', '.', '-');

            return lower == "tham khảo" ||
                   lower == "tài liệu tham khảo" ||
                   lower == "nguồn tham khảo" ||
                   lower == "nguồn dẫn" ||
                   lower == "chú thích" ||
                   lower == "chú thích trong bài" ||
                   lower == "ghi chú" ||
                   lower == "liên kết ngoài" ||
                   lower == "đường dẫn ngoài" ||
                   lower == "xem thêm" ||
                   lower == "đọc thêm" ||
                   lower == "thư mục" ||
                   lower == "thư mục tham khảo" ||
                   lower == "bài viết liên quan" ||
                   lower == "references" ||
                   lower == "external links" ||
                   lower == "see also" ||
                   lower == "further reading" ||
                   lower == "notes" ||
                   lower == "bibliography" ||
                   lower == "sources";
        }

        private static bool IsBoilerplateOrAdText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string lower = text.ToLowerInvariant();

            // 1. Tiêu đề mục tham khảo, chú thích, liên kết ngoài
            if (IsReferenceSectionHeader(text))
                return true;

            // 2. Chú thích nguồn, liên kết lưu trữ Wayback Machine, nguồn dẫn
            if (lower.Contains("lưu trữ ngày") || lower.Contains("wayback machine") ||
                lower.Contains("archived from") || lower.Contains("archive.org") ||
                lower.StartsWith("nguồn:") || lower.StartsWith("nguồn tham khảo:") ||
                lower.StartsWith("tham khảo:") || lower.StartsWith("nguồn trích dẫn:"))
            {
                return true;
            }

            // 3. Thông báo bài viết sơ khai Wikipedia, mời đóng góp
            if (lower.Contains("bài viết") && lower.Contains("vẫn còn sơ khai"))
                return true;
            if (lower.Contains("bạn hãy giúp wikipedia mở rộng nội dung"))
                return true;
            if (lower.Contains("bài viết này cần thêm chú thích nguồn gốc"))
                return true;
            if (lower.Contains("mời bạn giúp hoàn thiện bài viết"))
                return true;

            // 4. Dấu chú thích trích dẫn nguồn Wikipedia dạng "^ a b c" hoặc URL thuần
            if (Regex.IsMatch(text, @"^\s*\^\s+"))
                return true;
            if (Regex.IsMatch(text, @"^https?://\S+$"))
                return true;

            // 5. Quảng cáo, Mua bán & Đặt hàng
            if (Regex.IsMatch(lower, @"^(mua ngay|đặt hàng ngay|liên hệ đặt hàng|hotline\s*[:\.]|giá bán\s*[:\.]|giá ưu đãi\s*[:\.]|bảng giá sản phẩm|xem chi tiết sản phẩm|thêm vào giỏ hàng|liên hệ tư vấn|tổng đài hỗ trợ)\b"))
                return true;

            // 6. Form đăng nhập / Đăng ký / Tài khoản
            if (Regex.IsMatch(lower, @"^(đăng nhập để|đăng ký tài khoản|nhập email|nhập số điện thoại|quên mật khẩu|tên đăng nhập|mật khẩu của bạn|bạn đã có tài khoản|chưa có tài khoản|gửi thông tin|xác nhận mật khẩu)\b"))
                return true;

            // 7. Bản quyền / Điều khoản / Cookie
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
                    "//table[contains(@class,'vertical-navbox')]|//table[contains(@class,'sidebar')]|" +
                    "//table[contains(@class,'ambox')]|//table[contains(@class,'tmbox')]|//table[contains(@class,'cmbox')]|" +
                    "//table[contains(@class,'ombox')]|//table[contains(@class,'fmbox')]|//table[contains(@class,'mbox-small')]|" +
                    "//table[@id='asn-dismissable-notice']|" +
                    "//div[@id='toc']|//div[contains(@class,'reflist')]|//div[contains(@class,'references')]|//ol[contains(@class,'references')]|" +
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

                // 4. Bóc tách các thẻ nội dung: h2, h3, h4, p, ul, ol, li, blockquote, table
                var contentNodes = contentRoot.SelectNodes(".//h2|.//h3|.//h4|.//p|.//ul|.//ol|.//li|.//blockquote|.//table") ??
                                   doc.DocumentNode.SelectNodes("//p|//ul|//ol|//table");

                bool inReferenceSection = false;

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

                        // Kiểm tra nếu là tiêu đề phân đoạn: H2, H3, H4
                        if (node.Name == "h2" || node.Name == "h3" || node.Name == "h4")
                        {
                            string headingText = CleanText(node.InnerText);

                            // Nếu gặp mục Tham khảo / Chú thích / Liên kết ngoài / Xem thêm...
                            if (IsReferenceSectionHeader(headingText))
                            {
                                inReferenceSection = true;
                                continue;
                            }

                            // Nếu đã vào vùng reference nhưng gặp một H2 hợp lệ mới khác
                            if (inReferenceSection)
                            {
                                if (node.Name == "h2" && !IsReferenceSectionHeader(headingText))
                                {
                                    inReferenceSection = false;
                                }
                                else
                                {
                                    continue;
                                }
                            }

                            if (headingText.Length >= 3 && !lines.Contains(headingText) && !IsBoilerplateOrAdText(headingText))
                            {
                                lines.Add(headingText);
                            }
                            continue;
                        }

                        // Nếu đang nằm trong vùng Tham khảo / Chú thích / Liên kết ngoài -> Bỏ qua toàn bộ nội dung con
                        if (inReferenceSection)
                            continue;

                        // Bóc tách bảng dữ liệu / Infobox / Wikitable: gom các hàng thành 1 khối phân tách bằng dấu chấm '.'
                        // Chỉ xuống dòng \r\n khi ĐÓNG THẺ </table>
                        if (node.Name == "table")
                        {
                            if (node.Ancestors("table").Any())
                                continue;

                            var tableRows = ExtractTableRows(node);
                            var validRows = new List<string>();
                            foreach (var row in tableRows)
                            {
                                string cleanR = row.Trim();
                                if (cleanR.Length >= 8 && !IsBoilerplateOrAdText(cleanR))
                                {
                                    if (!cleanR.EndsWith("."))
                                        cleanR += ".";
                                    validRows.Add(cleanR);
                                }
                            }

                            if (validRows.Count > 0)
                            {
                                string tableBlock = string.Join(" ", validRows);
                                if (!lines.Contains(tableBlock))
                                {
                                    lines.Add(tableBlock);
                                }
                            }
                            continue;
                        }

                        // Bỏ qua các thẻ văn bản nằm trong bảng (vì bảng đã được trích xuất có cấu trúc riêng)
                        if (node.Ancestors("table").Any())
                            continue;

                        // Xử lý DANH SÁCH <ul> hoặc <ol>: Gom các thẻ <li> con phân tách bằng dấu chấm phẩy '; '
                        // Chỉ xuống dòng \r\n khi ĐÓNG THẺ </ul> hoặc </ol>
                        if (node.Name == "ul" || node.Name == "ol")
                        {
                            // Bỏ qua nếu là danh sách lồng trong danh sách khác (đã được danh sách cha lấy)
                            if (node.Ancestors("ul").Any() || node.Ancestors("ol").Any())
                                continue;

                            var liNodes = node.SelectNodes(".//li");
                            if (liNodes != null && liNodes.Count > 0)
                            {
                                var liTexts = new List<string>();
                                foreach (var li in liNodes)
                                {
                                    if (HasGarbageAncestor(li, contentRoot))
                                        continue;

                                    string t = CleanText(li.InnerText);
                                    if (t.Length >= 5 && !IsBoilerplateOrAdText(t))
                                    {
                                        t = t.TrimEnd(';', '.', ' ', '-').Trim();
                                        if (!string.IsNullOrWhiteSpace(t) && !liTexts.Contains(t))
                                        {
                                            liTexts.Add(t);
                                        }
                                    }
                                }

                                if (liTexts.Count > 0)
                                {
                                    string listBlock = string.Join("; ", liTexts);
                                    if (!listBlock.EndsWith("."))
                                        listBlock += ".";

                                    if (!lines.Contains(listBlock))
                                    {
                                        lines.Add(listBlock);
                                    }
                                }
                            }
                            continue;
                        }

                        // Bỏ qua các thẻ <li> nằm trong <ul> hoặc <ol> (đã được khối ul/ol xử lý ở trên)
                        if (node.Name == "li" && (node.Ancestors("ul").Any() || node.Ancestors("ol").Any()))
                            continue;

                        string text = CleanText(node.InnerText);
                        if (string.IsNullOrWhiteSpace(text) || IsBoilerplateOrAdText(text))
                            continue;

                        if (node.Name == "li")
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
        /// Bóc tách các hàng dữ liệu từ thẻ <table> (hỗ trợ bảng Wikipedia: infobox, wikitable, bảng dữ liệu)
        /// Mô phỏng lưới 2D Grid tính toán rowspan và colspan để chống lệch cột/mất ngữ cảnh
        /// </summary>
        private static List<string> ExtractTableRows(HtmlNode tableNode)
        {
            var tableLines = new List<string>();
            const int MaxRowsPerTable = 150;

            try
            {
                string tableCls = tableNode.GetAttributeValue("class", "").ToLowerInvariant();
                string tableId = tableNode.GetAttributeValue("id", "").ToLowerInvariant();

                // Bỏ qua các bảng điều hướng, hộp thông báo, dọn dẹp, bản quyền của Wikipedia
                if (tableCls.Contains("navbox") || tableCls.Contains("vertical-navbox") ||
                    tableCls.Contains("sidebar") || tableCls.Contains("ambox") ||
                    tableCls.Contains("tmbox") || tableCls.Contains("cmbox") ||
                    tableCls.Contains("ombox") || tableCls.Contains("fmbox") ||
                    tableCls.Contains("mbox-small") || tableCls.Contains("metadata") ||
                    tableCls.Contains("diff") || tableId == "toc" || tableCls.Contains("mw-empty-elt") ||
                    tableId == "asn-dismissable-notice")
                {
                    return tableLines;
                }

                string style = tableNode.GetAttributeValue("style", "").ToLowerInvariant();
                if (style.Contains("display:none") || style.Contains("display: none"))
                    return tableLines;

                // 1. Tiêu đề bảng từ <caption>
                var captionNode = tableNode.SelectSingleNode("./caption");
                if (captionNode != null)
                {
                    string captionText = CleanTableCell(captionNode);
                    if (captionText.Length >= 3 && !IsBoilerplateOrAdText(captionText))
                    {
                        tableLines.Add(captionText);
                    }
                }

                // 2. Lấy danh sách các dòng <tr> thuộc trực tiếp bảng này (lọc bỏ các dòng rác, dòng ẩn, mw-empty-elt)
                var allTrs = tableNode.SelectNodes(".//tr");
                if (allTrs == null || allTrs.Count == 0)
                    return tableLines;

                var trNodes = allTrs.Where(tr => tr.Ancestors("table").FirstOrDefault() == tableNode)
                                    .Where(tr =>
                                    {
                                        string cls = tr.GetAttributeValue("class", "").ToLowerInvariant();
                                        if (cls.Contains("mw-empty-elt") || cls.Contains("navbox") || cls.Contains("ambox"))
                                            return false;
                                        string st = tr.GetAttributeValue("style", "").ToLowerInvariant();
                                        if (st.Contains("display:none") || st.Contains("display: none"))
                                            return false;
                                        var cells = tr.SelectNodes("./th|./td");
                                        return cells != null && cells.Count > 0;
                                    })
                                    .ToList();
                if (trNodes.Count == 0)
                    return tableLines;

                bool isInfobox = tableCls.Contains("infobox");

                // 3. Xây dựng ma trận lưới 2D (grid) tính toán chính xác rowspan và colspan
                var grid = new Dictionary<(int r, int c), string>();
                var isHeaderGrid = new Dictionary<(int r, int c), bool>();
                var rowSpanOriginGrid = new Dictionary<(int r, int c), (int originR, int span)>();
                int maxCols = 0;

                for (int r = 0; r < trNodes.Count; r++)
                {
                    var tr = trNodes[r];
                    var cells = tr.SelectNodes("./th|./td");
                    if (cells == null || cells.Count == 0)
                        continue;

                    int c = 0;
                    foreach (var cell in cells)
                    {
                        while (grid.ContainsKey((r, c)))
                            c++;

                        int rowSpan = GetRowSpan(cell);
                        int colSpan = GetColSpan(cell);
                        bool isTh = cell.Name == "th";
                        string cellText = CleanTableCell(cell);

                        for (int dr = 0; dr < rowSpan; dr++)
                        {
                            for (int dc = 0; dc < colSpan; dc++)
                            {
                                int targetR = r + dr;
                                int targetC = c + dc;
                                grid[(targetR, targetC)] = cellText;
                                isHeaderGrid[(targetR, targetC)] = isTh;
                                rowSpanOriginGrid[(targetR, targetC)] = (r, rowSpan);
                                if (targetC + 1 > maxCols)
                                    maxCols = targetC + 1;
                            }
                        }

                        c += colSpan;
                        if (c > maxCols)
                            maxCols = c;
                    }
                }

                if (maxCols == 0)
                    return tableLines;

                // 4. Tìm dòng tiêu đề cột (Header row)
                int headerRowIdx = -1;
                var colHeaders = new List<string>();

                if (!isInfobox)
                {
                    for (int r = 0; r < trNodes.Count; r++)
                    {
                        int thCount = 0;
                        int totalCells = 0;
                        for (int c = 0; c < maxCols; c++)
                        {
                            if (grid.ContainsKey((r, c)))
                            {
                                totalCells++;
                                if (isHeaderGrid.TryGetValue((r, c), out bool isTh) && isTh)
                                    thCount++;
                            }
                        }

                        if (totalCells >= 2 && thCount >= totalCells / 2)
                        {
                            headerRowIdx = r;
                            for (int c = 0; c < maxCols; c++)
                            {
                                string h = grid.TryGetValue((r, c), out var txt) ? txt : "";
                                colHeaders.Add(h);
                            }
                            break;
                        }
                    }
                }

                // Tìm cột "Năm" nếu có (để nhóm theo năm khi cột Năm có rowspan > 1)
                int yearColIdx = -1;
                for (int c = 0; c < colHeaders.Count; c++)
                {
                    string h = colHeaders[c].Trim().ToLowerInvariant();
                    if (h == "năm" || h == "thời gian" || h == "year")
                    {
                        yearColIdx = c;
                        break;
                    }
                }

                string lastEmittedYear = "";

                // 5. Trích xuất từng hàng dữ liệu từ lưới 2D
                for (int r = 0; r < trNodes.Count; r++)
                {
                    if (tableLines.Count >= MaxRowsPerTable)
                        break;

                    if (r == headerRowIdx)
                        continue;

                    // Nếu là bảng Infobox hoặc bảng 2 cột Key-Value
                    if (isInfobox || (maxCols == 2 && colHeaders.Count == 0))
                    {
                        string c0 = grid.TryGetValue((r, 0), out var val0) ? val0 : "";
                        string c1 = grid.TryGetValue((r, 1), out var val1) ? val1 : "";

                        // Ô đơn trải dài cả hàng (colspan=2 hoặc chỉ có 1 ô)
                        if (c0.Equals(c1, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(c1))
                        {
                            string singleText = c0;
                            if (singleText.Length >= 3 && singleText.Length <= 100 &&
                                !singleText.StartsWith("Biểu trưng", StringComparison.OrdinalIgnoreCase) &&
                                !singleText.StartsWith("Trụ sở", StringComparison.OrdinalIgnoreCase) &&
                                !singleText.StartsWith("Ghi chú", StringComparison.OrdinalIgnoreCase) &&
                                !IsBoilerplateOrAdText(singleText))
                            {
                                tableLines.Add(singleText);
                            }
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(c0) && !string.IsNullOrWhiteSpace(c1) &&
                            !IsTrivialCellValue(c1) &&
                            !c0.StartsWith("Ghi chú", StringComparison.OrdinalIgnoreCase) &&
                            !c0.StartsWith("Nguồn", StringComparison.OrdinalIgnoreCase) &&
                            !c0.StartsWith("Tham khảo", StringComparison.OrdinalIgnoreCase) &&
                            !c0.StartsWith("Chú thích", StringComparison.OrdinalIgnoreCase) &&
                            !IsBoilerplateOrAdText(c0) &&
                            !IsBoilerplateOrAdText(c1))
                        {
                            string kvSentence = $"{c0}: {c1}";
                            if (!kvSentence.EndsWith("."))
                                kvSentence += ".";
                            tableLines.Add(kvSentence);
                            continue;
                        }
                    }

                    // Kiểm tra xem hàng này có bắt đầu một nhóm "Năm" mới (có rowspan > 1) hay không
                    bool isYearSpanned = false;
                    string currentYear = "";
                    if (yearColIdx >= 0 && grid.TryGetValue((r, yearColIdx), out string? yText) && !string.IsNullOrWhiteSpace(yText))
                    {
                        currentYear = yText.Trim();
                        if (rowSpanOriginGrid.TryGetValue((r, yearColIdx), out var originInfo) && originInfo.span > 1)
                        {
                            isYearSpanned = true;
                            // Nếu đây là hàng bắt đầu của nhóm năm mới -> xuất câu "Năm: 2018." làm mốc
                            if (originInfo.originR == r && !currentYear.Equals(lastEmittedYear, StringComparison.OrdinalIgnoreCase))
                            {
                                string yearHeading = $"Năm: {currentYear}";
                                if (!yearHeading.EndsWith("."))
                                    yearHeading += ".";
                                tableLines.Add(yearHeading);
                                lastEmittedYear = currentYear;
                            }
                        }
                    }

                    // Trường hợp bảng dữ liệu nhiều cột (Wikitable): ghép các cột có tên cột tương ứng
                    var rowParts = new List<string>();
                    for (int c = 0; c < maxCols; c++)
                    {
                        // Nếu cột này là cột Năm đang được nhóm (spanned > 1), không lặp lại trong từng hàng
                        if (c == yearColIdx && isYearSpanned)
                            continue;

                        string header = (c < colHeaders.Count) ? colHeaders[c] : "";
                        if (!string.IsNullOrWhiteSpace(header) && IsTrivialHeader(header))
                            continue;

                        if (grid.TryGetValue((r, c), out string? cellText) &&
                            !string.IsNullOrWhiteSpace(cellText) &&
                            !IsTrivialCellValue(cellText))
                        {
                            if (!string.IsNullOrWhiteSpace(header) &&
                                !header.Equals(cellText, StringComparison.OrdinalIgnoreCase))
                            {
                                rowParts.Add($"{header}: {cellText}");
                            }
                            else if (!IsTrivialHeader(cellText))
                            {
                                rowParts.Add(cellText);
                            }
                        }
                    }

                    // Bỏ qua các dòng chỉ có 1 trường thông tin phụ (như chỉ có mỗi "Năm: 2018" mà không có nội dung chính)
                    if (colHeaders.Count >= 2 && rowParts.Count == 1 &&
                        (rowParts[0].StartsWith("Năm:", StringComparison.OrdinalIgnoreCase) ||
                         rowParts[0].StartsWith("STT:", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    if (rowParts.Count > 0)
                    {
                        string rowSentence = string.Join(" - ", rowParts);
                        if (!rowSentence.EndsWith("."))
                            rowSentence += ".";

                        if (rowSentence.Length >= 8 && !IsBoilerplateOrAdText(rowSentence) && !tableLines.Contains(rowSentence))
                        {
                            tableLines.Add(rowSentence);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRAWL TABLE EXTRACT ERROR] {ex.Message}");
            }

            return tableLines;
        }

        private static int GetRowSpan(HtmlNode cell)
        {
            string spanStr = cell.GetAttributeValue("rowspan", "1");
            if (int.TryParse(spanStr, out int span) && span >= 1)
            {
                return Math.Min(span, 100);
            }
            return 1;
        }

        private static int GetColSpan(HtmlNode cell)
        {
            string spanStr = cell.GetAttributeValue("colspan", "1");
            if (int.TryParse(spanStr, out int span) && span >= 1)
            {
                return Math.Min(span, 10);
            }
            return 1;
        }

        private static bool IsTrivialHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return true;

            string h = header.Trim().ToLowerInvariant();
            return h == "stt" || h == "số thứ tự" || h == "#" || h == "no." || h == "no" ||
                   h == "hình ảnh" || h == "ảnh" || h == "hình" || h == "logo" || h == "biểu trưng" ||
                   h == "image" || h == "photo" || h == "picture" || h == "icon" ||
                   h == "thao tác" || h == "hành động" || h == "chức năng" ||
                   h == "ct" || h == "chú thích" || h == "nguồn" || h == "tham khảo";
        }

        private static bool IsTrivialCellValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            string v = value.Trim().ToLowerInvariant();
            return v == "-" || v == "—" || v == "–" || v == "..." || v == "." ||
                   v == "n/a" || v == "none" || v == "không" || v == "không có" || v == "trống";
        }

        private static string CleanTableCell(HtmlNode cell)
        {
            if (cell == null)
                return "";

            // Xóa các thẻ chú thích [1], [2], [cần dẫn nguồn] của Wikipedia
            var refNodes = cell.SelectNodes(".//sup[contains(@class,'reference')]|.//span[contains(@class,'reference')]");
            if (refNodes != null)
            {
                foreach (var r in refNodes)
                    r.Remove();
            }

            // Xóa các phần tử ẩn
            var hiddenNodes = cell.SelectNodes(".//*[@style and (contains(@style,'display:none') or contains(@style,'display: none'))]");
            if (hiddenNodes != null)
            {
                foreach (var h in hiddenNodes)
                    h.Remove();
            }

            // Thay thế <br> bằng dấu phẩy
            var brNodes = cell.SelectNodes(".//br");
            if (brNodes != null)
            {
                foreach (var br in brNodes)
                {
                    br.ParentNode?.ReplaceChild(cell.OwnerDocument.CreateTextNode(", "), br);
                }
            }

            // Xử lý danh sách con bên trong ô bảng
            var liNodes = cell.SelectNodes(".//li");
            if (liNodes != null && liNodes.Count > 0)
            {
                var liTexts = new List<string>();
                foreach (var li in liNodes)
                {
                    string t = CleanText(li.InnerText);
                    if (!string.IsNullOrWhiteSpace(t) && !liTexts.Contains(t))
                        liTexts.Add(t);
                }
                if (liTexts.Count > 0)
                {
                    return string.Join(", ", liTexts).Trim(',', ';', ':', '-', '•').Trim();
                }
            }

            string rawText = CleanText(cell.InnerText);
            return rawText.Trim(',', ';', ':', '-', '•').Trim();
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
