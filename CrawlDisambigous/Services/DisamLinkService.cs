using CrawlDisambigous.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace CrawlDisambigous.Services
{
    /// <summary>
    /// Service kết nối SQL Server và kiểm tra thực thể trong bảng [EREL].[dbo].[DisAmLinkData_New]
    /// </summary>
    public class DisamLinkService
    {
        private readonly string[] _connectionStrings = new[]
        {
            "Server=103.98.152.201,1433;Database=EREL;User Id=teamadmin;Password=ThayVinh2005;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=20;",
            "Server=103.98.152.201,1433;Database=EREL;User Id=team_admin;Password=ThayVinh2005;TrustServerCertificate=True;MultipleActiveResultSets=True;Connect Timeout=20;"
        };

        private string? _cachedWorkingConnStr;
        private HashSet<long>? _cachedDistinctEntity1Ids;
        private DateTime _lastCacheTime = DateTime.MinValue;
        private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);
        private readonly object _lock = new();

        public async Task<bool> CheckHealthAsync(int timeoutSeconds = 3)
        {
            // Ưu tiên chuỗi kết nối đã hoạt động thành công gần nhất
            var candidates = _cachedWorkingConnStr != null
                ? new[] { _cachedWorkingConnStr }
                : _connectionStrings;

            foreach (var connStr in candidates)
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(connStr)
                    {
                        ConnectTimeout = Math.Max(timeoutSeconds, 3)
                    };
                    using var conn = new SqlConnection(builder.ConnectionString);
                    await conn.OpenAsync();
                    _cachedWorkingConnStr = connStr;
                    return true;
                }
                catch
                {
                    // Thử chuỗi kết nối tiếp theo nếu có
                }
            }

            // Nếu chuỗi cache bị lỗi, thử lại toàn bộ danh sách
            if (_cachedWorkingConnStr != null)
            {
                _cachedWorkingConnStr = null;
                return await CheckHealthAsync(timeoutSeconds);
            }

            return false;
        }

        private async Task<SqlConnection> OpenWorkingConnectionAsync()
        {
            var candidates = _cachedWorkingConnStr != null
                ? new[] { _cachedWorkingConnStr }
                : _connectionStrings;

            Exception? lastEx = null;
            foreach (var connStr in candidates)
            {
                try
                {
                    var conn = new SqlConnection(connStr);
                    await conn.OpenAsync();
                    _cachedWorkingConnStr = connStr;
                    return conn;
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
            }

            if (_cachedWorkingConnStr != null)
            {
                _cachedWorkingConnStr = null;
                return await OpenWorkingConnectionAsync();
            }

            throw lastEx ?? new InvalidOperationException("Không thể kết nối tới cơ sở dữ liệu DisAmLinkData_New.");
        }

        /// <summary>
        /// Lấy tập tất cả EntityID1 duy nhất hiện có trong bảng [EREL].[dbo].[DisAmLinkData_New] (có cache 5 phút)
        /// </summary>
        public async Task<HashSet<long>> GetAllExistingEntity1IdsAsync(bool forceRefresh = false)
        {
            lock (_lock)
            {
                if (!forceRefresh && _cachedDistinctEntity1Ids != null && (DateTime.UtcNow - _lastCacheTime) < _cacheDuration)
                {
                    return _cachedDistinctEntity1Ids;
                }
            }

            try
            {
                using var conn = await OpenWorkingConnectionAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT DISTINCT [EntityID1] FROM [EREL].[dbo].[DisAmLinkData_New] WHERE [EntityID1] IS NOT NULL";
                cmd.CommandTimeout = 30;

                var result = new HashSet<long>();
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (!reader.IsDBNull(0))
                    {
                        result.Add(Convert.ToInt64(reader.GetValue(0)));
                    }
                }

                lock (_lock)
                {
                    _cachedDistinctEntity1Ids = result;
                    _lastCacheTime = DateTime.UtcNow;
                }

                Console.WriteLine($"[DISAMLINK DB] Đã nạp {result.Count} EntityID1 từ [DisAmLinkData_New].");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DISAMLINK DB ERROR] Lỗi khi truy vấn [DisAmLinkData_New]: {ex.Message}");
                lock (_lock)
                {
                    return _cachedDistinctEntity1Ids ?? new HashSet<long>();
                }
            }
        }

        /// <summary>
        /// Pipeline kiểm tra: với mỗi candidate thuộc từng mention, kiểm tra xem EntityId có trong DisAmLinkData_New (cột EntityID1) hay chưa.
        /// Nếu tất cả candidate thuộc mention đều đã có trong DB -> đánh dấu AllCandidatesInDisamLink = true để highlight.
        /// </summary>
        public async Task EnrichDisambiguationGroupsAsync(List<AmbiguousMentionGroup> ambiguousGroups, bool forceRefresh = false)
        {
            if (ambiguousGroups == null || ambiguousGroups.Count == 0)
                return;

            try
            {
                var existingEntity1Ids = await GetAllExistingEntity1IdsAsync(forceRefresh);

                int totalCandidatesChecked = 0;
                int candidatesFoundInDb = 0;
                int fullyMatchedMentionsCount = 0;

                foreach (var group in ambiguousGroups)
                {
                    if (group.Candidates == null || group.Candidates.Count == 0)
                    {
                        group.AllCandidatesInDisamLink = false;
                        continue;
                    }

                    bool allInDb = true;

                    foreach (var cand in group.Candidates)
                    {
                        totalCandidatesChecked++;
                        cand.IsInDisamLink = existingEntity1Ids.Contains(cand.EntityId);
                        if (cand.IsInDisamLink)
                        {
                            candidatesFoundInDb++;
                        }
                        else
                        {
                            allInDb = false;
                        }
                    }

                    group.AllCandidatesInDisamLink = allInDb;
                    if (allInDb)
                    {
                        fullyMatchedMentionsCount++;
                    }
                }

                Console.WriteLine($"[DISAMLINK PIPELINE] Đã kiểm tra {totalCandidatesChecked} candidates ({candidatesFoundInDb} có trong DB). " +
                                  $"Phát hiện {fullyMatchedMentionsCount}/{ambiguousGroups.Count} mentions có ĐẦY ĐỦ tất cả candidate trong [DisAmLinkData_New].");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DISAMLINK PIPELINE ERROR] {ex.Message}");
            }
        }
    }
}
