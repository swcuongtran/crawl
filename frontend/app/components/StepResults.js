"use client";

export default function StepResults({
  session,
  activeResultTab,
  setActiveResultTab,
  selectedFileType,
  fileContent,
  isLoadingFile,
  copiedText,
  onLoadFile,
  onCopy,
  onGoToStep1,
  onGoToStep2,
}) {
  const crawlMs = session.metrics?.totalCrawlDurationMs || 0;
  const erelMs = session.metrics?.totalErelDurationMs || 0;
  const searchMs = session.metrics?.searchDurationMs || 0;
  const disamMs = session.metrics?.disambiguationDurationMs || 0;
  const sumMs = crawlMs + erelMs + searchMs + disamMs || 1;

  const crawlPct = Math.round((crawlMs / sumMs) * 100);
  const erelPct = Math.round((erelMs / sumMs) * 100);
  const searchPct = Math.round((searchMs / sumMs) * 100);
  const otherPct = Math.max(0, 100 - crawlPct - erelPct - searchPct);

  const fileTabs = [
    { key: "SessionResult", label: `🎯 ${session?.outputFiles?.SessionResultFileName || "File Phiên Này (.txt)"}` },
    { key: "CrawlerResult", label: "CrawlerResult.txt (Tổng hợp)" },
    { key: "Summary", label: "Summary_MCS_UMCS.txt" },
    { key: "Debug", label: "CrawlerDebug.txt" },
  ];

  return (
    <section>
      {session.metrics?.failedLinks > 0 && (
        <div
          className="glass-panel"
          style={{
            padding: "14px 20px",
            marginBottom: "18px",
            borderColor: "rgba(248, 113, 113, 0.45)",
            background: "rgba(127, 29, 29, 0.18)",
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            gap: "12px",
            flexWrap: "wrap",
          }}
        >
          <div style={{ fontSize: "13px", color: "#fecaca" }}>
            <strong>⚠️ Có {session.metrics.failedLinks}/{session.totalLinks} link bị lỗi khi cào hoặc quét EREL:</strong>{" "}
            {session.crawledLinks
              ?.filter((l) => l.status === "failed")
              .map((l) => `[#${l.index}] ${l.error || "Lỗi không xác định"}`)
              .join(" • ")}
          </div>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onGoToStep2}
            style={{ padding: "6px 12px", fontSize: "12px" }}
          >
            Xem chi tiết Bước 2
          </button>
        </div>
      )}

      {/* Top Metric Cards */}
      <div className="metrics-row">
        <div className="metric-card">
          <div className="metric-value">
            {session.metrics?.successfulLinks ?? session.completedLinks}
            {session.totalLinks > 0 && (
              <span style={{ fontSize: "15px", color: "var(--text-muted)", fontWeight: 500 }}>
                /{session.totalLinks}
              </span>
            )}
          </div>
          <div className="metric-label">Số bài viết đã cào thành công</div>
        </div>
        <div className="metric-card">
          <div className="metric-value" style={{ color: "#38bdf8" }}>
            {session.finalResults?.length || 0}
          </div>
          <div className="metric-label">Thực thể Entity 2 Liên kết</div>
        </div>
        <div className="metric-card">
          <div className="metric-value" style={{ color: "#34d399" }}>
            {(session.singleCandidateSummary?.length || 0) + (session.ambiguousGroups?.length || 0)}
          </div>
          <div className="metric-label">Tổng Mentions Quét được</div>
        </div>
        <div className="metric-card">
          <div className="metric-value" style={{ color: "#fbbf24" }}>
            {session.umcsTerms?.length || 0}
          </div>
          <div className="metric-label">Thuật ngữ UMCS Chưa nhận diện</div>
        </div>
      </div>

      {/* Result Tabs */}
      <div className="glass-panel" style={{ padding: "24px" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            borderBottom: "1px solid var(--border-subtle)",
            paddingBottom: "16px",
            marginBottom: "20px",
          }}
        >
          <div style={{ display: "flex", gap: "10px", flexWrap: "wrap" }}>
            <button
              className={`btn ${activeResultTab === "entities" ? "btn-primary" : "btn-secondary"}`}
              onClick={() => setActiveResultTab("entities")}
            >
              📊 Bảng Xếp Hạng Entity 2 ({session.finalResults?.length || 0})
            </button>
            <button
              className={`btn ${activeResultTab === "umcs" ? "btn-primary" : "btn-secondary"}`}
              onClick={() => setActiveResultTab("umcs")}
            >
              📑 Danh Sách UMCS ({session.umcsTerms?.length || 0})
            </button>
            <button
              className={`btn ${activeResultTab === "files" ? "btn-primary" : "btn-secondary"}`}
              onClick={() => setActiveResultTab("files")}
            >
              💾 Xem & Tải Tệp Kết Quả (.txt)
            </button>
            <button
              className={`btn ${activeResultTab === "benchmarks" ? "btn-primary" : "btn-secondary"}`}
              onClick={() => setActiveResultTab("benchmarks")}
            >
              ⏱️ Đo Thời Gian & Hiệu Năng
            </button>
          </div>

          <button className="btn btn-secondary" onClick={onGoToStep1}>
            + Cào Thực Thể Mới
          </button>
        </div>

        {/* TAB 1: Entity 2 Table */}
        {activeResultTab === "entities" && (
          <div className="data-table-wrapper">
            <table className="data-table">
              <thead>
                <tr>
                  <th style={{ width: "60px" }}>Hạng</th>
                  <th style={{ width: "120px" }}>Entity ID</th>
                  <th>Tên Thực thể (WikiName)</th>
                  <th style={{ width: "120px" }}>Số lần (Count)</th>
                  <th style={{ width: "240px" }}>Biểu đồ Tần suất</th>
                </tr>
              </thead>
              <tbody>
                {(session.finalResults || []).map((item, idx) => {
                  const maxCount = session.finalResults[0]?.count || 1;
                  const pct = Math.round((item.count / maxCount) * 100);

                  return (
                    <tr key={item.entityId}>
                      <td style={{ fontWeight: "700", color: idx < 3 ? "#fbbf24" : "var(--text-muted)" }}>
                        #{idx + 1}
                      </td>
                      <td>
                        <span
                          style={{
                            fontFamily: "var(--font-mono)",
                            color: "var(--cyan)",
                            fontSize: "12px",
                          }}
                        >
                          {item.entityId}
                        </span>
                      </td>
                      <td style={{ fontWeight: "600", fontSize: "14px" }}>{item.wikiName}</td>
                      <td style={{ fontWeight: "700", color: "#fff" }}>{item.count}</td>
                      <td>
                        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                          <div
                            style={{
                              flex: 1,
                              height: "8px",
                              background: "rgba(255,255,255,0.06)",
                              borderRadius: "4px",
                              overflow: "hidden",
                            }}
                          >
                            <div
                              style={{
                                width: `${pct}%`,
                                height: "100%",
                                background: "var(--gradient-brand)",
                                borderRadius: "4px",
                              }}
                            />
                          </div>
                          <span style={{ fontSize: "11px", color: "var(--text-muted)", width: "36px" }}>
                            {pct}%
                          </span>
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* TAB 2: UMCS List */}
        {activeResultTab === "umcs" && (
          <div>
            <p style={{ fontSize: "13px", color: "var(--text-muted)", marginBottom: "16px" }}>
              Tập các từ/cụm từ không tìm thấy trong Cơ sở Tri thức KB (Unmatched Candidate Set):
            </p>
            <div style={{ display: "flex", flexWrap: "wrap", gap: "10px" }}>
              {(session.umcsTerms || []).map((term, i) => (
                <span
                  key={i}
                  style={{
                    padding: "6px 12px",
                    borderRadius: "8px",
                    background: "rgba(245, 158, 11, 0.1)",
                    border: "1px solid rgba(245, 158, 11, 0.25)",
                    color: "#fbbf24",
                    fontSize: "13px",
                    fontWeight: "500",
                  }}
                >
                  {term}
                </span>
              ))}
            </div>
          </div>
        )}

        {/* TAB 3: Output Files Viewer */}
        {activeResultTab === "files" && (
          <div>
            <div
              style={{
                display: "flex",
                justifyContent: "space-between",
                alignItems: "center",
                marginBottom: "16px",
              }}
            >
              <div style={{ display: "flex", gap: "8px", flexWrap: "wrap" }}>
                {fileTabs.map((ft) => (
                  <button
                    key={ft.key}
                    className={`btn ${selectedFileType === ft.key ? "btn-primary" : "btn-secondary"}`}
                    onClick={() => onLoadFile(ft.key)}
                    style={{ fontSize: "12px" }}
                  >
                    {ft.label}
                  </button>
                ))}
              </div>

              <button
                className="btn btn-secondary"
                onClick={() => onCopy(fileContent)}
                disabled={!fileContent}
                style={{ fontSize: "12px" }}
              >
                {copiedText ? "✓ Đã sao chép!" : "📋 Sao chép nội dung"}
              </button>
            </div>

            {selectedFileType === "SessionResult" && session?.outputFiles?.SessionResult && (
              <div
                style={{
                  background: "rgba(99, 102, 241, 0.08)",
                  border: "1px solid rgba(99, 102, 241, 0.2)",
                  borderRadius: "8px",
                  padding: "8px 14px",
                  marginBottom: "12px",
                  fontSize: "12px",
                  color: "var(--text-muted)",
                  display: "flex",
                  alignItems: "center",
                  gap: "8px",
                  wordBreak: "break-all",
                }}
              >
                <span style={{ color: "var(--primary-light)", fontWeight: "600" }}>📁 File riêng phiên này:</span>
                <span style={{ color: "var(--cyan)", fontFamily: "var(--font-mono)" }}>
                  {session.outputFiles.SessionResult}
                </span>
              </div>
            )}

            <div
              style={{
                background: "#0a0e17",
                border: "1px solid var(--border-subtle)",
                borderRadius: "12px",
                padding: "18px",
                maxHeight: "500px",
                overflowY: "auto",
                fontFamily: "var(--font-mono)",
                fontSize: "12px",
                lineHeight: "1.6",
                color: "#cbd5e1",
                whiteSpace: "pre-wrap",
              }}
            >
              {isLoadingFile ? "Đang tải file..." : fileContent}
            </div>
          </div>
        )}

        {/* TAB 4: Benchmarks & Latency Measurements */}
        {activeResultTab === "benchmarks" && (
          <div>
            <div className="metrics-row" style={{ marginBottom: "24px" }}>
              <div className="metric-card">
                <div className="metric-value" style={{ color: "#38bdf8" }}>
                  {crawlMs.toLocaleString()} ms
                </div>
                <div className="metric-label">🌐 Tổng Thời Gian Cào Web</div>
                <p style={{ fontSize: "11px", color: "var(--text-subtle)", marginTop: "6px" }}>
                  Trung bình: <strong style={{ color: "#38bdf8" }}>{session.metrics?.averageCrawlPerLinkMs || 0} ms</strong> / link
                </p>
              </div>

              <div className="metric-card">
                <div className="metric-value" style={{ color: "#c084fc" }}>
                  {erelMs.toLocaleString()} ms
                </div>
                <div className="metric-label">🤖 Tổng Thời Gian EREL Trả Lời</div>
                <p style={{ fontSize: "11px", color: "var(--text-subtle)", marginTop: "6px" }}>
                  Trung bình: <strong style={{ color: "#c084fc" }}>{session.metrics?.averageErelPerLinkMs || 0} ms</strong> / link
                </p>
              </div>

              <div className="metric-card">
                <div className="metric-value" style={{ color: "#fbbf24" }}>
                  {searchMs.toLocaleString()} ms
                </div>
                <div className="metric-label">🔍 Thời Gian Tìm Kiếm</div>
                <p style={{ fontSize: "11px", color: "var(--text-subtle)", marginTop: "6px" }}>
                  Lấy {session.searchResults?.length || 0} kết quả hữu cơ
                </p>
              </div>

              <div className="metric-card">
                <div className="metric-value" style={{ color: "#34d399" }}>
                  {session.metrics?.totalSessionDurationMs
                    ? `${(session.metrics.totalSessionDurationMs / 1000).toFixed(2)}s`
                    : "0s"}
                </div>
                <div className="metric-label">⏱️ Tổng Thời Gian Toàn Bộ Phiên</div>
                <p style={{ fontSize: "11px", color: "var(--text-subtle)", marginTop: "6px" }}>
                  Bao gồm cào, quét EREL, phân định & ghi file
                </p>
              </div>
            </div>

            <div
              style={{
                background: "var(--bg-surface-1)",
                border: "1px solid var(--border-subtle)",
                borderRadius: "14px",
                padding: "20px",
                marginBottom: "24px",
              }}
            >
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "12px" }}>
                <h3 style={{ fontSize: "15px", fontWeight: "700" }}>Biểu Đồ Phân Bổ Tỷ Trọng Thời Gian</h3>
                <span style={{ fontSize: "12px", color: "var(--text-muted)", fontFamily: "var(--font-mono)" }}>
                  Tổng thời gian tác vụ: {Math.round(sumMs).toLocaleString()} ms
                </span>
              </div>

              <div className="latency-bar-container" style={{ marginBottom: "14px" }}>
                <div className="latency-segment" style={{ width: `${crawlPct}%`, background: "#38bdf8" }} title={`Crawl: ${crawlPct}%`} />
                <div className="latency-segment" style={{ width: `${erelPct}%`, background: "#a855f7" }} title={`EREL: ${erelPct}%`} />
                <div className="latency-segment" style={{ width: `${searchPct}%`, background: "#fbbf24" }} title={`Search: ${searchPct}%`} />
                <div className="latency-segment" style={{ width: `${otherPct}%`, background: "#34d399" }} title={`Khác: ${otherPct}%`} />
              </div>

              <div style={{ display: "flex", flexWrap: "wrap", gap: "24px", fontSize: "12px" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span style={{ width: "10px", height: "10px", borderRadius: "50%", background: "#38bdf8" }} />
                  <span>Cào dữ liệu (Web Crawl): <strong style={{ color: "#38bdf8" }}>{crawlPct}%</strong> ({crawlMs.toLocaleString()} ms)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span style={{ width: "10px", height: "10px", borderRadius: "50%", background: "#a855f7" }} />
                  <span>EREL phản hồi & phân tích: <strong style={{ color: "#c084fc" }}>{erelPct}%</strong> ({erelMs.toLocaleString()} ms)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span style={{ width: "10px", height: "10px", borderRadius: "50%", background: "#fbbf24" }} />
                  <span>Tìm kiếm URLs: <strong style={{ color: "#fbbf24" }}>{searchPct}%</strong> ({searchMs.toLocaleString()} ms)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                  <span style={{ width: "10px", height: "10px", borderRadius: "50%", background: "#34d399" }} />
                  <span>Phân định & Lưu file: <strong style={{ color: "#34d399" }}>{otherPct}%</strong> ({disamMs.toLocaleString()} ms)</span>
                </div>
              </div>
            </div>

            <div className="data-table-wrapper">
              <table className="data-table">
                <thead>
                  <tr>
                    <th style={{ width: "50px" }}>#</th>
                    <th>Bài Viết / URL</th>
                    <th style={{ width: "130px" }}>Phương Thức</th>
                    <th style={{ width: "100px" }}>Ký Tự</th>
                    <th style={{ width: "130px" }}>Thời Gian Crawl</th>
                    <th style={{ width: "150px" }}>EREL Phản Hồi</th>
                    <th style={{ width: "130px" }}>Tổng Thời Gian</th>
                    <th style={{ width: "110px" }}>Tỷ Lệ</th>
                    <th style={{ width: "100px" }}>Trạng Thái</th>
                  </tr>
                </thead>
                <tbody>
                  {(session.crawledLinks || []).map((link) => {
                    const crawlRatio = link.totalDurationMs > 0
                      ? Math.round((link.crawlDurationMs / link.totalDurationMs) * 100)
                      : 50;
                    const erelRatio = 100 - crawlRatio;

                    return (
                      <tr key={link.url}>
                        <td style={{ fontWeight: "700", color: "var(--text-muted)" }}>#{link.index}</td>
                        <td>
                          <div style={{ fontWeight: "600", fontSize: "13px", color: "var(--text-main)", marginBottom: "2px" }}>
                            {link.title}
                          </div>
                          <div
                            style={{
                              fontSize: "11px",
                              color: "var(--text-muted)",
                              fontFamily: "var(--font-mono)",
                              maxWidth: "340px",
                              overflow: "hidden",
                              textOverflow: "ellipsis",
                              whiteSpace: "nowrap",
                            }}
                          >
                            {link.url}
                          </div>
                        </td>
                        <td>
                          <span
                            className={`chip ${
                              link.fetchMethod === "Playwright"
                                ? "chip-playwright"
                                : link.fetchMethod === "HttpClient"
                                ? "chip-http"
                                : "chip-running"
                            }`}
                            style={{ fontSize: "11px" }}
                          >
                            {link.fetchMethod}
                          </span>
                        </td>
                        <td style={{ fontFamily: "var(--font-mono)", fontSize: "12px" }}>
                          {link.textLength > 0 ? link.textLength.toLocaleString() : "-"}
                        </td>
                        <td>
                          <span style={{ fontFamily: "var(--font-mono)", fontWeight: "600", color: "#38bdf8" }}>
                            {link.crawlDurationMs ? `${link.crawlDurationMs.toLocaleString()} ms` : "-"}
                          </span>
                        </td>
                        <td>
                          <span style={{ fontFamily: "var(--font-mono)", fontWeight: "600", color: "#c084fc" }}>
                            {link.erelDurationMs ? `${link.erelDurationMs.toLocaleString()} ms` : "-"}
                          </span>
                        </td>
                        <td>
                          <span
                            style={{
                              fontFamily: "var(--font-mono)",
                              fontWeight: "700",
                              color: link.totalDurationMs > 3000 ? "#fbbf24" : "#34d399",
                            }}
                          >
                            {link.totalDurationMs ? `${link.totalDurationMs.toLocaleString()} ms` : "-"}
                          </span>
                        </td>
                        <td>
                          <div className="mini-latency-bar" title={`Crawl: ${crawlRatio}% | EREL: ${erelRatio}%`}>
                            <div style={{ width: `${crawlRatio}%`, height: "100%", background: "#38bdf8" }} />
                            <div style={{ width: `${erelRatio}%`, height: "100%", background: "#a855f7" }} />
                          </div>
                        </td>
                        <td>
                          {link.status === "success" ? (
                            <span className="chip chip-success" style={{ fontSize: "11px" }}>✓ Xong</span>
                          ) : link.status === "failed" ? (
                            <span className="chip chip-failed" style={{ fontSize: "11px" }}>✕ Lỗi</span>
                          ) : (
                            <span className="chip chip-running" style={{ fontSize: "11px" }}>⏳ Đang chạy</span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </section>
  );
}

