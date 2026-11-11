"use client";

export default function ContentPreviewModal({
  previewDoc,
  copiedText,
  onClose,
  onCopy,
}) {
  if (!previewDoc) return null;

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal-content" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div>
            <h3 style={{ fontSize: "16px", fontWeight: "700", color: "#fff" }}>
              [{previewDoc.index}] {previewDoc.title}
            </h3>
            <p
              style={{
                fontSize: "12px",
                color: "var(--cyan)",
                fontFamily: "var(--font-mono)",
                marginTop: "2px",
              }}
            >
              {previewDoc.url}
            </p>
          </div>

          <div style={{ display: "flex", gap: "8px", alignItems: "center" }}>
            <span className="chip chip-http">
              {previewDoc.textLength?.toLocaleString()} ký tự
            </span>
            <button
              type="button"
              className="btn btn-secondary"
              onClick={onClose}
              style={{ padding: "6px 12px" }}
            >
              ✕ Đóng
            </button>
          </div>
        </div>

        <div className="modal-body">{previewDoc.fullText}</div>

        <div className="modal-footer">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => onCopy(previewDoc.fullText)}
          >
            {copiedText ? "✓ Đã sao chép!" : "📋 Sao chép toàn bộ"}
          </button>
        </div>
      </div>
    </div>
  );
}

