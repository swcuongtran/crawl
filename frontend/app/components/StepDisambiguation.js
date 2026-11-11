"use client";

const SCORE_METRICS = [
  {
    key: "m",
    fallbackKey: "M",
    label: "M",
    title: "Điểm tổng hợp M = f(Me, Mc, Ms)",
    color: "#38bdf8",
    weight: "700",
    bg: "rgba(56, 189, 248, 0.12)",
    border: "rgba(56, 189, 248, 0.3)",
  },
  {
    key: "me",
    fallbackKey: "Me",
    label: "Me",
    title: "Điểm ngữ cảnh (Entity Prior / Context) Me",
    color: "#a78bfa",
    weight: "600",
    bg: "rgba(167, 139, 250, 0.1)",
    border: "rgba(167, 139, 250, 0.25)",
  },
  {
    key: "mc",
    fallbackKey: "Mc",
    label: "Mc",
    title: "Độ gắn kết ngữ cảnh (Coherence) Mc",
    color: "#34d399",
    weight: "600",
    bg: "rgba(52, 211, 153, 0.1)",
    border: "rgba(52, 211, 153, 0.25)",
  },
  {
    key: "ms",
    fallbackKey: "Ms",
    label: "Ms",
    title: "Độ tương đồng ngữ nghĩa (Semantic Similarity) Ms",
    color: "#f472b6",
    weight: "600",
    bg: "rgba(244, 114, 182, 0.1)",
    border: "rgba(244, 114, 182, 0.25)",
  },
];

export default function StepDisambiguation({
  session,
  userSelections,
  mentionFilter,
  setMentionFilter,
  showSingleCandidates,
  setShowSingleCandidates,
  isSubmittingReview,
  onSelectCandidate,
  onSelectAllErel,
  onClearAll,
  onConfirmReview,
}) {
  const filteredAmbiguousGroups = (session.ambiguousGroups || []).filter((g) =>
    g.mention.toLowerCase().includes(mentionFilter.toLowerCase())
  );

  const selectedCount = Object.values(userSelections).filter(
    (s) => s.selectedEntityId > 0
  ).length;

  return (
    <section>
      {/* Header Card */}
      <div className="glass-panel" style={{ padding: "24px", marginBottom: "20px" }}>
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "flex-start",
            marginBottom: "16px",
          }}
        >
          <div>
            <h2 style={{ fontSize: "20px", fontWeight: "700" }}>
              Studio Phân định Thực thể (Manual Disambiguation)
            </h2>
            <p style={{ fontSize: "13px", color: "var(--text-muted)", marginTop: "4px" }}>
              Phát hiện <strong style={{ color: "#38bdf8" }}>{session.ambiguousGroups?.length}</strong> mention
              có nhiều hơn 1 ứng viên trong cơ sở tri thức (KB). Đánh dấu chọn thực thể chính xác
              trước khi tính tần suất xuất hiện!
            </p>
          </div>

          <div style={{ display: "flex", gap: "10px" }}>
            <button
              type="button"
              className="btn btn-primary"
              onClick={onSelectAllErel}
              style={{ fontSize: "13px" }}
            >
              ⚡ Chọn tất cả theo EREL
            </button>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={onClearAll}
              style={{ fontSize: "13px" }}
            >
              ✕ Bỏ chọn tất cả
            </button>
          </div>
        </div>

        {/* Filter toolbar */}
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            gap: "16px",
          }}
        >
          <input
            type="text"
            className="input-field"
            placeholder="Lọc mention theo từ khóa..."
            value={mentionFilter}
            onChange={(e) => setMentionFilter(e.target.value)}
            style={{ maxWidth: "360px" }}
          />

          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setShowSingleCandidates(!showSingleCandidates)}
            style={{ fontSize: "12px" }}
          >
            {showSingleCandidates ? "Ẩn" : "Xem"} {session.singleCandidateSummary?.length} mention
            đã tự động tính count (1 candidate) ▼
          </button>
        </div>
      </div>

      {/* Drawer for Single Candidates Summary */}
      {showSingleCandidates && (
        <div
          className="glass-panel"
          style={{
            padding: "20px",
            marginBottom: "20px",
            background: "rgba(16, 185, 129, 0.05)",
            borderColor: "rgba(16, 185, 129, 0.2)",
          }}
        >
          <h3 style={{ fontSize: "14px", fontWeight: "700", color: "#34d399", marginBottom: "12px" }}>
            ✓ {session.singleCandidateSummary?.length} Mention có đúng 1 Candidate (Hệ thống đã tự động tính Count):
          </h3>
          <div style={{ display: "flex", flexWrap: "wrap", gap: "8px" }}>
            {(session.singleCandidateSummary || []).map((sc, i) => (
              <span
                key={i}
                style={{
                  padding: "4px 10px",
                  borderRadius: "6px",
                  background: "rgba(15, 23, 42, 0.7)",
                  border: "1px solid rgba(16, 185, 129, 0.3)",
                  fontSize: "12px",
                  color: "#e2e8f0",
                }}
              >
                <strong>&quot;{sc.mention}&quot;</strong> → [{sc.entityId}] {sc.wikiName} (x{sc.totalCount})
              </span>
            ))}
          </div>
        </div>
      )}

      {/* Ambiguous Mention Cards */}
      <div>
        {filteredAmbiguousGroups.map((group, gIdx) => {
          const currentSel = userSelections[group.mention]?.selectedEntityId ?? group.erelSelectedEntityId;

          return (
            <div key={group.mention} className="mention-card">
              <div className="mention-header">
                <div className="mention-title-row">
                  <span style={{ fontSize: "14px", color: "var(--text-muted)", fontWeight: "600" }}>
                    #{gIdx + 1}
                  </span>
                  <span className="mention-name">&quot;{group.mention}&quot;</span>
                  <span className="badge-count">Xuất hiện {group.totalCount} lần</span>
                </div>

                <div style={{ display: "flex", gap: "8px" }}>
                  <span className="chip chip-http">POS: {group.posTag || "N/A"}</span>
                  <span className="chip chip-http">Loại: {group.category || "N/A"}</span>
                  <span className="chip chip-playwright">
                    {group.candidates?.length} Ứng viên trong KB
                  </span>
                </div>
              </div>

              <div className="candidate-options">
                {group.candidates?.map((cand) => {
                  const isSelected = currentSel === cand.entityId;
                  const isErelRec = cand.entityId === group.erelSelectedEntityId;

                  return (
                    <div
                      key={cand.entityId}
                      className={`candidate-row ${isSelected ? "is-selected" : ""} ${
                        isErelRec ? "is-erel-rec" : ""
                      }`}
                      onClick={() =>
                        onSelectCandidate(
                          group.mention,
                          group.totalCount,
                          cand.entityId,
                          cand.wikiName
                        )
                      }
                    >
                      <div className="candidate-info">
                        <input
                          type="radio"
                          name={`mention-${group.mention}`}
                          checked={isSelected}
                          onChange={() => {}}
                          style={{ accentColor: "var(--primary)", width: "16px", height: "16px" }}
                        />
                        <div>
                          <span style={{ fontWeight: "700", color: "#fff", fontSize: "14px" }}>
                            {cand.wikiName}
                          </span>
                          {cand.realName && cand.realName !== cand.wikiName && (
                            <span
                              style={{
                                fontSize: "12px",
                                color: "var(--text-muted)",
                                marginLeft: "8px",
                              }}
                            >
                              ({cand.realName})
                            </span>
                          )}
                        </div>
                      </div>

                      <div className="candidate-meta" style={{ display: "flex", alignItems: "center", gap: "8px", flexWrap: "wrap" }}>
                        <span
                          style={{
                            fontFamily: "var(--font-mono)",
                            fontSize: "12px",
                            color: "var(--cyan)",
                            background: "rgba(6, 182, 212, 0.1)",
                            padding: "2px 6px",
                            borderRadius: "4px",
                          }}
                        >
                          ID: {cand.entityId}
                        </span>
                        <div style={{ display: "inline-flex", gap: "6px", alignItems: "center", fontSize: "12px", fontFamily: "var(--font-mono)", flexWrap: "wrap" }}>
                          {SCORE_METRICS.map((m) => (
                            <span
                              key={m.key}
                              title={m.title}
                              style={{
                                color: m.color,
                                fontWeight: m.weight,
                                background: m.bg,
                                border: `1px solid ${m.border}`,
                                padding: "2px 6px",
                                borderRadius: "4px",
                              }}
                            >
                              {m.label}: {(cand[m.key] ?? cand[m.fallbackKey] ?? 0).toFixed(2)}
                            </span>
                          ))}
                        </div>
                        {isErelRec && (
                          <span className="chip chip-playwright" style={{ fontSize: "10px" }}>
                            ★ EREL GỢI Ý
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })}

                <div
                  className={`candidate-row ${currentSel === 0 ? "is-selected" : ""}`}
                  onClick={() => onSelectCandidate(group.mention, group.totalCount, 0, "")}
                  style={{ opacity: 0.8 }}
                >
                  <div className="candidate-info">
                    <input
                      type="radio"
                      name={`mention-${group.mention}`}
                      checked={currentSel === 0}
                      onChange={() => {}}
                      style={{ accentColor: "var(--error)", width: "16px", height: "16px" }}
                    />
                    <span style={{ fontSize: "13px", color: "var(--text-muted)", fontStyle: "italic" }}>
                      Không chọn / Bỏ qua mention này (Không cộng dồn vào thực thể nào)
                    </span>
                  </div>
                </div>
              </div>
            </div>
          );
        })}
      </div>

      {/* Sticky Bottom Bar */}
      <div className="sticky-review-bar">
        <div>
          <p style={{ fontWeight: "700", fontSize: "15px" }}>
            Đã chọn: <span style={{ color: "#38bdf8" }}>{selectedCount}</span> /{" "}
            {session.ambiguousGroups?.length} mentions
          </p>
          <p style={{ fontSize: "12px", color: "var(--text-muted)" }}>
            Hệ thống sẽ cộng dồn các lựa chọn này với {session.singleCandidateSummary?.length} mentions 1 candidate.
          </p>
        </div>

        <button
          id="btnConfirmReview"
          type="button"
          className="btn btn-glow"
          disabled={isSubmittingReview}
          onClick={onConfirmReview}
          style={{ padding: "12px 32px", fontSize: "15px" }}
        >
          {isSubmittingReview ? "Đang tính toán..." : "✓ Xác nhận & Tính toán kết quả"}
        </button>
      </div>
    </section>
  );
}

