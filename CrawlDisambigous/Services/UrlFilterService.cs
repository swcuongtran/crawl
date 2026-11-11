using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CrawlDisambigous.Services
{
    public class UrlFilterService
    {
        private static readonly HashSet<string> DefaultBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            // Mạng xã hội & Video & Diễn đàn hỏi đáp
            "facebook.com", "fb.com", "fb.watch",
            "x.com", "twitter.com", "t.co",
            "youtube.com", "youtu.be",
            "instagram.com",
            "tiktok.com",
            "linkedin.com",
            "pinterest.com", "pin.it",
            "reddit.com",
            "threads.net",
            "quora.com",
            "zhihu.com",
            "tumblr.com",
            "weibo.com",
            "twitch.tv",
            "discord.com", "discord.gg",
            "t.me", "telegram.me",
            "snapchat.com",
            "vk.com",
            "bilibili.com",
            // Các trang Trung Quốc & Nga thường xuyên xuất hiện
            "baidu.com", "csdn.net", "sohu.com", "sina.com", "qq.com", "163.com",
            "yandex.ru", "yandex.com", "mail.ru", "ok.ru",
            // Trang web người lớn & spam
            "mejav.tv", "xvideos.com", "pornhub.com", "xhamster.com", "xnxx.com", "jav.com", "javhd.com",
            // Trung tâm trợ giúp quốc tế & trang hỗ trợ không phải bài viết thực thể
            "support.google.com", "support.microsoft.com", "support.apple.com", "help.twitter.com"
        };


        private readonly HashSet<string> _blacklistedDomains;

        public UrlFilterService(string? customBlacklistPath = null)
        {
            _blacklistedDomains = new HashSet<string>(DefaultBlacklist, StringComparer.OrdinalIgnoreCase);
            LoadCustomBlacklist(customBlacklistPath);
        }

        private void LoadCustomBlacklist(string? customPath)
        {
            try
            {
                string filePath = customPath ?? Path.Combine(AppContext.BaseDirectory, "blacklist.txt");
                if (File.Exists(filePath))
                {
                    var lines = File.ReadAllLines(filePath);
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim().ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("#"))
                        {
                            _blacklistedDomains.Add(trimmed);
                        }
                    }
                }
                else
                {
                    // Tạo file blacklist.txt mẫu để người dùng có thể dễ dàng bổ sung domain mà không cần sửa code
                    var sampleLines = new List<string>
                    {
                        "# Danh sách blacklist các domain cần bỏ qua (1 domain mỗi dòng)",
                        "# Bạn có thể thêm bất kỳ tên miền nào vào đây (ví dụ: example.com)"
                    };
                    sampleLines.AddRange(DefaultBlacklist.OrderBy(x => x));
                    File.WriteAllLines(filePath, sampleLines);
                }
            }
            catch
            {
                // Fallback sử dụng danh sách mặc định nếu không đọc được file
            }
        }

        public bool IsBlacklisted(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return true;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
                return true;

            string host = uri.Host.ToLowerInvariant();

            // Loại bỏ các tên miền quốc gia không liên quan (Trung Quốc, Nga, Belarus, Đức...)
            if (host.EndsWith(".cn", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".ru", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".by", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".su", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".de", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return _blacklistedDomains.Any(domain =>
                host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyCollection<string> BlacklistedDomains => _blacklistedDomains;
    }
}
