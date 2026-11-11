"use client";

function renderFetchMethodChip(method) {
  if (method === "Playwright") {
    return <span className="chip chip-playwright">⚡ Playwright Bypass</span>;
  }
  if (method === "HttpClient") {
    return <span className="chip chip-http">HttpClient Fast</span>;
  }
  return <span className="chip chip-running">{method}</span>;
}

export default function StepCrawlProgress({
  session,
  onGoToReview,
  onPreviewDoc,
}) {
  const progressPct = session.totalLinks > 0
    ? Math.round((session.completedLinks / session.totalLinks) * 100)
    : 0;

  const statusChipClass =
    session.status === "completed"
      ? "chip-success"
      : session.status === "awaiting_review"
      ? "chip-playwright"
      : "chip-running";

  return (
    <section className="glass-panel" style={{ padding: "28px" }}>
      <div
        style={{
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: "20px",
        }}
      >
        <div>
          <h2 style={{ fontSize: "18px", fontWeight: "700" }}>
            Tiến trình Cào dữ liệu & Quét EREL
          </h2>
          <p style={{ fontSize: "13px", color: "var(--text-muted)", marginTop: "4px" }}>
            Thực thể: [{session.source?.entityId}] {session.source?.wikiName}
          </p>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
          <span className={`chip ${statusChipClass}`}>
            Trạng thái: {session.status}
          </span>

          {session.status === "awaiting_review" && (
            <button
              className="btn btn-primary"
              onClick={onGoToReview}
              style={{ padding: "8px 18px", fontSize: "13px" }}
            >
              👉 Vào Studio Phân định ({session.ambiguousGroups?.length} mentions)
            </button>
          )}
        </div>
      </div>

      {/* Progress Bar */}
      <div style={{ marginBottom: "24px" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            fontSize: "13px",
            marginBottom: "8px",
          }}
        >
          <span style={{ color: "var(--text-muted)" }}>{session.currentMessage}</span>
          <span style={{ fontWeight: "700", color: "var(--cyan)" }}>
            {session.completedLinks} / {session.totalLinks} links
          </span>
        </div>

        <div
          style={{
            height: "10px",
            background: "rgba(255,255,255,0.06)",
            borderRadius: "99px",
            overflow: "hidden",
          }}
        >
          <div
            style={{
              height: "100%",
              width: `${progressPct}%`,
              background: "var(--gradient-brand)",
              transition: "width 0.4s ease",
            }}
          />
        </div>
      </div>

      {/* Live Latency Metrics Row */}
      <div
        style={{
          display: "grid",
          gridTemplateColumns: "repeat(4, 1fr)",
          gap: "12px",
          marginBottom: "24px",
        }}
      >
        <div className="metric-card" style={{ padding: "12px 16px" }}>
          <div className="metric-value" style={{ fontSize: "18px", color: "#38bdf8" }}>
            {session.metrics?.totalCrawlDurationMs?.toLocaleString() || 0} ms
          </div>
          <div className="metric-label" style={{ fontSize: "11px" }}>
            🌐 Crawl (TB: {session.metrics?.averageCrawlPerLinkMs || 0} ms)
          </div>
        </div>

        <div className="metric-card" style={{ padding: "12px 16px" }}>
          <div className="metric-value" style={{ fontSize: "18px", color: "#c084fc" }}>
            {session.metrics?.totalErelDurationMs?.toLocaleString() || 0} ms
          </div>
          <div className="metric-label" style={{ fontSize: "11px" }}>
            🤖 EREL Scan (TB: {session.metrics?.averageErelPerLinkMs || 0} ms)
          </div>
        </div>

        <div className="metric-card" style={{ padding: "12px 16px" }}>
          <div className="metric-value" style={{ fontSize: "18px", color: "#34d399" }}>
            {session.metrics?.totalSessionDurationMs
              ? `${(session.metrics.totalSessionDurationMs / 1000).toFixed(1)}s`
              : "0s"}
          </div>
          <div className="metric-label" style={{ fontSize: "11px" }}>
            ⏱️ Thời gian phiên
          </div>
        </div>

        <div className="metric-card" style={{ padding: "12px 16px" }}>
          <div className="metric-value" style={{ fontSize: "18px", color: "#fbbf24" }}>
            {session.metrics?.searchDurationMs || 0} ms
          </div>
          <div className="metric-label" style={{ fontSize: "11px" }}>
            🔍 Tìm kiếm URLs
          </div>
        </div>
      </div>

      {/* Crawled Links Table */}
      <div className="link-list">
        {(session.crawledLinks || []).map((link) => (
          <div key={link.url} className="link-card">
            <div className="link-card-left">
              <div style={{ minWidth: 0 }}>
                <p className="link-title">[{link.index}] {link.title}</p>
                <p className="link-url">{link.url}</p>
                {link.status === "failed" && link.error && (
                  <p style={{ fontSize: "12px", color: "#f87171", marginTop: "4px" }}>
                    ⚠️ {link.error}
                  </p>
                )}
              </div>
            </div>

            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: "10px",
                flexWrap: "wrap",
                justifyContent: "flex-end",
              }}
            >
              {renderFetchMethodChip(link.fetchMethod)}

              {link.crawlDurationMs > 0 && (
                <span className="chip chip-timing-crawl">🌐 {link.crawlDurationMs}ms</span>
              )}

              {link.erelDurationMs > 0 && (
                <span className="chip chip-timing-erel">🤖 EREL: {link.erelDurationMs}ms</span>
              )}

              {link.status === "success" ? (
                <span className="chip chip-success">
                  ✓ {link.textLength?.toLocaleString()} ký tự
                </span>
              ) : link.status === "failed" ? (
                <span className="chip chip-failed">✕ Lỗi / Rỗng</span>
              ) : (
                <span className="chip chip-running">⏳ Đang cào...</span>
              )}

              {link.fullText && (
                <button
                  type="button"
                  className="btn btn-secondary"
                  onClick={() => onPreviewDoc(link)}
                  style={{ padding: "6px 12px", fontSize: "12px" }}
                >
                  👁 Xem văn bản
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}

