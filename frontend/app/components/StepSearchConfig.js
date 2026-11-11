"use client";

export default function StepSearchConfig({
  wikiName,
  setWikiName,
  entityId,
  setEntityId,
  searchCount,
  setSearchCount,
  isSearching,
  searchError,
  searchResults,
  selectedUrls,
  isStartingCrawl,
  onSearch,
  onToggleUrl,
  onSelectAll,
  onDeselectAll,
  onStartCrawl,
}) {
  return (
    <section className="glass-panel" style={{ padding: "28px" }}>
      <h2 style={{ fontSize: "18px", fontWeight: "700", marginBottom: "18px" }}>
        Cấu hình Tìm kiếm Thực thể Nguồn (Entity 1)
      </h2>

      <form onSubmit={onSearch} className="config-grid">
        <div className="input-group">
          <label htmlFor="wikiNameInput">Tên Thực thể (WikiName)</label>
          <input
            id="wikiNameInput"
            type="text"
            className="input-field"
            placeholder="Ví dụ: Công cụ, Vinamilk, Trí tuệ nhân tạo..."
            value={wikiName}
            onChange={(e) => setWikiName(e.target.value)}
            required
          />
        </div>

        <div className="input-group">
          <label htmlFor="entityIdInput">Entity ID</label>
          <input
            id="entityIdInput"
            type="number"
            className="input-field"
            value={entityId}
            onChange={(e) => setEntityId(e.target.value)}
          />
        </div>

        <div className="input-group">
          <label htmlFor="searchCountInput">Số link tìm kiếm</label>
          <input
            id="searchCountInput"
            type="number"
            min="1"
            max="50"
            className="input-field"
            value={searchCount}
            onChange={(e) => setSearchCount(e.target.value)}
          />
        </div>

        <div>
          <button
            id="btnSearch"
            type="submit"
            className="btn btn-primary"
            disabled={isSearching}
            style={{ height: "46px", padding: "0 24px" }}
          >
            {isSearching ? "Đang tìm kiếm..." : "🔍 Tìm kiếm URLs"}
          </button>
        </div>
      </form>

      {searchError && (
        <div
          style={{
            marginTop: "16px",
            padding: "12px 16px",
            borderRadius: "10px",
            background: "rgba(239, 68, 68, 0.15)",
            border: "1px solid rgba(239, 68, 68, 0.3)",
            color: "#f87171",
            fontSize: "13px",
          }}
        >
          {searchError}
        </div>
      )}

      {searchResults.length > 0 && (
        <div style={{ marginTop: "32px" }}>
          <div
            style={{
              display: "flex",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: "14px",
            }}
          >
            <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
              <h3 style={{ fontSize: "16px", fontWeight: "700" }}>
                Danh sách {searchResults.length} Link Tìm Thấy
              </h3>
              <span className="chip chip-http">
                Đã chọn {selectedUrls.length}/{searchResults.length} link
              </span>
            </div>

            <div style={{ display: "flex", gap: "10px" }}>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onSelectAll}
                style={{ padding: "6px 14px", fontSize: "12px" }}
              >
                Chọn tất cả
              </button>
              <button
                type="button"
                className="btn btn-secondary"
                onClick={onDeselectAll}
                style={{ padding: "6px 14px", fontSize: "12px" }}
              >
                Bỏ chọn
              </button>
            </div>
          </div>

          <div className="link-list">
            {searchResults.map((item) => {
              const isChecked = selectedUrls.includes(item.url);
              return (
                <div
                  key={item.url}
                  className={`link-card ${isChecked ? "selected" : ""}`}
                  onClick={() => onToggleUrl(item.url)}
                  style={{ cursor: "pointer" }}
                >
                  <div className="link-card-left">
                    <input
                      type="checkbox"
                      checked={isChecked}
                      onChange={() => {}}
                      style={{
                        width: "18px",
                        height: "18px",
                        accentColor: "var(--primary)",
                        cursor: "pointer",
                      }}
                    />
                    <div style={{ minWidth: 0 }}>
                      <p className="link-title">[{item.index}] {item.title}</p>
                      <p className="link-url">{item.url}</p>
                    </div>
                  </div>

                  <a
                    href={item.url}
                    target="_blank"
                    rel="noreferrer"
                    className="btn btn-secondary"
                    onClick={(e) => e.stopPropagation()}
                    style={{ padding: "6px 12px", fontSize: "12px", textDecoration: "none" }}
                  >
                    Mở link ↗
                  </a>
                </div>
              );
            })}
          </div>

          <div style={{ marginTop: "24px", display: "flex", justifyContent: "flex-end" }}>
            <button
              id="btnStartCrawl"
              type="button"
              className="btn btn-glow"
              disabled={selectedUrls.length === 0 || isStartingCrawl}
              onClick={onStartCrawl}
              style={{ padding: "12px 28px", fontSize: "15px" }}
            >
              🚀 Bắt đầu cào & Quét EREL ({selectedUrls.length} link)
            </button>
          </div>
        </div>
      )}
    </section>
  );
}

