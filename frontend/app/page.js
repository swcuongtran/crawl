"use client";

import { useState, useEffect, useRef } from "react";
import StepSearchConfig from "./components/StepSearchConfig";
import StepCrawlProgress from "./components/StepCrawlProgress";
import StepDisambiguation from "./components/StepDisambiguation";
import StepResults from "./components/StepResults";
import ContentPreviewModal from "./components/ContentPreviewModal";

const BACKEND_URL = "/api/backend";

export default function Home() {
  const [activeStep, setActiveStep] = useState(1);
  const [health, setHealth] = useState({
    backend: false,
    erel: false,
    searxng: false,
    outputFolder: "",
  });

  // Step 1: Config & Search
  const [entityId, setEntityId] = useState(1);
  const [wikiName, setWikiName] = useState("Công cụ");
  const [searchCount, setSearchCount] = useState(10);
  const [isSearching, setIsSearching] = useState(false);
  const [searchResults, setSearchResults] = useState([]);
  const [selectedUrls, setSelectedUrls] = useState([]);
  const [searchError, setSearchError] = useState("");

  // Step 2: Crawl Progress & Session
  const [sessionId, setSessionId] = useState(null);
  const [session, setSession] = useState(null);
  const [isStartingCrawl, setIsStartingCrawl] = useState(false);
  const [previewDoc, setPreviewDoc] = useState(null);
  const pollingRef = useRef(null);

  // Step 3: Disambiguation Studio
  const [userSelections, setUserSelections] = useState({});
  const [mentionFilter, setMentionFilter] = useState("");
  const [showSingleCandidates, setShowSingleCandidates] = useState(false);
  const [isSubmittingReview, setIsSubmittingReview] = useState(false);

  // Step 4: Results & Exports
  const [activeResultTab, setActiveResultTab] = useState("entities");
  const [selectedFileType, setSelectedFileType] = useState("SessionResult");
  const [fileContent, setFileContent] = useState("");
  const [isLoadingFile, setIsLoadingFile] = useState(false);
  const [copiedText, setCopiedText] = useState(false);

  // Check health on mount & poll every 8s
  useEffect(() => {
    let isCancelled = false;
    const fetchHealth = () => {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 5000);
      fetch(`${BACKEND_URL}/health`, { signal: controller.signal })
        .then((res) => (res.ok ? res.json() : Promise.reject()))
        .then((data) => {
          if (!isCancelled) {
            setHealth({
              backend: true,
              erel: data.erelConnected,
              searxng: data.searxngConnected,
              outputFolder: data.outputFolder || "D:\\CrawlerData",
            });
          }
        })
        .catch(() => {
          if (!isCancelled) {
            setHealth((h) => ({ ...h, backend: false }));
          }
        })
        .finally(() => {
          clearTimeout(timeoutId);
        });
    };

    fetchHealth();
    const timer = setInterval(fetchHealth, 8000);
    return () => {
      isCancelled = true;
      clearInterval(timer);
    };
  }, []);

  useEffect(() => {
    return () => {
      if (pollingRef.current) clearInterval(pollingRef.current);
    };
  }, []);

  // Load output file content in Step 4
  useEffect(() => {
    if (activeStep !== 4 || activeResultTab !== "files") return;

    let isCancelled = false;
    const outputMap = {
      SessionResult: session?.outputFiles?.SessionResult || "",
      CrawlerResult: session?.outputFiles?.CrawlerResult || `${health.outputFolder}\\CrawlerResult.txt`,
      Summary: session?.outputFiles?.Summary || `${health.outputFolder}\\Summary_MCS_UMCS.txt`,
      Debug: session?.outputFiles?.CrawlerDebug || `${health.outputFolder}\\CrawlerDebug.txt`,
    };
    const path = outputMap[selectedFileType] || outputMap.Debug;

    Promise.resolve().then(() => {
      if (isCancelled) return;
      if (!path) {
        setFileContent("Chưa có thông tin file kết quả riêng cho phiên này.");
        setIsLoadingFile(false);
        return;
      }

      setIsLoadingFile(true);
      fetch(`${BACKEND_URL}/file?path=${encodeURIComponent(path)}`)
        .then((res) => (res.ok ? res.json() : Promise.reject(new Error("Không thể tải file"))))
        .then((data) => {
          if (!isCancelled) setFileContent(data.content || "");
        })
        .catch((err) => {
          if (!isCancelled) setFileContent(`Không thể tải file hoặc file chưa được tạo: ${err.message}`);
        })
        .finally(() => {
          if (!isCancelled) setIsLoadingFile(false);
        });
    });

    return () => {
      isCancelled = true;
    };
  }, [activeStep, activeResultTab, selectedFileType, session?.outputFiles, health.outputFolder]);

  const initSelections = (groups) => {
    const initial = {};
    (groups || []).forEach((g) => {
      const rec = g.candidates.find((c) => c.entityId === g.erelSelectedEntityId);
      initial[g.mention] = {
        selectedEntityId: g.erelSelectedEntityId,
        selectedWikiName: rec ? rec.wikiName : "",
        mentionCount: g.totalCount,
      };
    });
    setUserSelections(initial);
  };

  const startPolling = (sid) => {
    if (pollingRef.current) clearInterval(pollingRef.current);

    pollingRef.current = setInterval(async () => {
      try {
        const res = await fetch(`${BACKEND_URL}/session/${sid}`);
        if (!res.ok) return;

        const data = await res.json();
        setSession(data);

        if (data.status === "awaiting_review") {
          clearInterval(pollingRef.current);
          initSelections(data.ambiguousGroups);
          setActiveStep(3);
        } else if (data.status === "completed") {
          clearInterval(pollingRef.current);
          setActiveStep(4);
        } else if (data.status === "error") {
          clearInterval(pollingRef.current);
        }
      } catch (err) {
        console.error("Polling error:", err);
      }
    }, 1500);
  };

  const handleSearch = async (e) => {
    if (e) e.preventDefault();
    if (!wikiName.trim()) return;

    setIsSearching(true);
    setSearchError("");
    setSearchResults([]);

    try {
      const res = await fetch(`${BACKEND_URL}/search`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          wikiName: wikiName.trim(),
          entityId: Number(entityId) || 1,
          searchCount: Number(searchCount) || 10,
        }),
      });

      if (!res.ok) {
        const err = await res.json();
        throw new Error(err.error || "Lỗi khi tìm kiếm link.");
      }

      const data = await res.json();
      setSessionId(data.sessionId);
      setSearchResults(data.searchResults || []);
      setSelectedUrls((data.searchResults || []).map((r) => r.url));
    } catch (err) {
      setSearchError(err.message);
    } finally {
      setIsSearching(false);
    }
  };

  const toggleUrlSelection = (url) => {
    setSelectedUrls((prev) =>
      prev.includes(url) ? prev.filter((u) => u !== url) : [...prev, url]
    );
  };

  const handleStartCrawl = async () => {
    if (selectedUrls.length === 0) return;

    setIsStartingCrawl(true);
    setActiveStep(2);

    try {
      const res = await fetch(`${BACKEND_URL}/crawl/start`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          sessionId,
          wikiName: wikiName.trim(),
          entityId: Number(entityId) || 1,
          selectedUrls,
        }),
      });

      if (!res.ok) throw new Error("Không thể khởi động quá trình cào.");

      const data = await res.json();
      setSession(data);
      startPolling(data.sessionId);
    } catch (err) {
      alert(err.message);
    } finally {
      setIsStartingCrawl(false);
    }
  };

  const handleSelectCandidate = (mention, count, candidateId, candidateWikiName) => {
    setUserSelections((prev) => ({
      ...prev,
      [mention]: {
        selectedEntityId: candidateId,
        selectedWikiName: candidateWikiName,
        mentionCount: count,
      },
    }));
  };

  const clearAllSelections = () => {
    if (!session?.ambiguousGroups) return;
    const cleared = {};
    session.ambiguousGroups.forEach((g) => {
      cleared[g.mention] = {
        selectedEntityId: 0,
        selectedWikiName: "",
        mentionCount: g.totalCount,
      };
    });
    setUserSelections(cleared);
  };

  const handleConfirmReview = async () => {
    if (!session) return;
    setIsSubmittingReview(true);

    try {
      const selectionsList = Object.keys(userSelections).map((mention) => ({
        mention,
        mentionCount: userSelections[mention].mentionCount,
        selectedEntityId: userSelections[mention].selectedEntityId,
        selectedWikiName: userSelections[mention].selectedWikiName,
      }));

      const res = await fetch(`${BACKEND_URL}/review/confirm`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          sessionId: session.sessionId,
          selections: selectionsList,
        }),
      });

      const updated = await res.json();
      setSession(updated);
      setSelectedFileType("SessionResult");
      setActiveStep(4);
    } catch (err) {
      alert(err.message);
    } finally {
      setIsSubmittingReview(false);
    }
  };

  const loadFile = (type) => {
    setSelectedFileType(type);
    setIsLoadingFile(true);
    setFileContent("");
  };

  const copyToClipboard = (text) => {
    navigator.clipboard.writeText(text);
    setCopiedText(true);
    setTimeout(() => setCopiedText(false), 2000);
  };

  const steps = [
    { num: 1, label: "Tìm kiếm & Cấu hình", desc: "Chọn URLs cào dữ liệu", canClick: () => true },
    { num: 2, label: "Tiến trình Cào", desc: "Bypass CF & Quét EREL", canClick: () => Boolean(session) },
    { num: 3, label: "Studio Phân định", desc: "Duyệt ứng viên thủ công", canClick: () => session?.ambiguousGroups?.length > 0 },
    { num: 4, label: "Kết quả & Xuất File", desc: "Entity2 & Đo thời gian", canClick: () => session?.finalResults?.length > 0 || session?.crawledLinks?.length > 0 },
  ];

  return (
    <div className="app-container">
      {/* App Header */}
      <header className="app-header">
        <div className="brand-wrapper">
          <div className="brand-icon">E</div>
          <div className="brand-title">
            <h1>EREL Disambiguation Studio</h1>
            <p className="brand-subtitle">Crawl & Entity Disambiguation Platform</p>
          </div>
        </div>

        <div className="status-pills">
          <div className="status-badge">
            <span className={`dot-pulse ${health.backend ? "online" : "offline"}`}></span>
            <span>API {health.backend ? "5000 Online" : "Disconnected"}</span>
          </div>
          <div className="status-badge">
            <span className={`dot-pulse ${health.erel ? "online" : "offline"}`}></span>
            <span>EREL {health.erel ? "Remote Active" : "Remote Offline"}</span>
          </div>
          <div className="status-badge">
            <span className={`dot-pulse ${health.searxng ? "online" : "offline"}`}></span>
            <span>{health.searxng ? "SearXNG Active" : "Bing Fallback"}</span>
          </div>
        </div>
      </header>

      {/* Stepper Navigation */}
      <nav className="stepper-nav">
        {steps.map((st) => (
          <div
            key={st.num}
            className={`step-item ${activeStep === st.num ? "active" : ""} ${activeStep > st.num ? "completed" : ""}`}
            onClick={() => st.canClick() && setActiveStep(st.num)}
          >
            <div className="step-num">{st.num}</div>
            <div className="step-text">
              <span className="step-label">{st.label}</span>
              <span className="step-desc">{st.desc}</span>
            </div>
          </div>
        ))}
      </nav>

      {activeStep === 1 && (
        <StepSearchConfig
          wikiName={wikiName}
          setWikiName={setWikiName}
          entityId={entityId}
          setEntityId={setEntityId}
          searchCount={searchCount}
          setSearchCount={setSearchCount}
          isSearching={isSearching}
          searchError={searchError}
          searchResults={searchResults}
          selectedUrls={selectedUrls}
          isStartingCrawl={isStartingCrawl}
          onSearch={handleSearch}
          onToggleUrl={toggleUrlSelection}
          onSelectAll={() => setSelectedUrls(searchResults.map((r) => r.url))}
          onDeselectAll={() => setSelectedUrls([])}
          onStartCrawl={handleStartCrawl}
        />
      )}

      {activeStep === 2 && session && (
        <StepCrawlProgress
          session={session}
          onGoToReview={() => setActiveStep(3)}
          onPreviewDoc={setPreviewDoc}
        />
      )}

      {activeStep === 3 && session && (
        <StepDisambiguation
          session={session}
          userSelections={userSelections}
          mentionFilter={mentionFilter}
          setMentionFilter={setMentionFilter}
          showSingleCandidates={showSingleCandidates}
          setShowSingleCandidates={setShowSingleCandidates}
          isSubmittingReview={isSubmittingReview}
          onSelectCandidate={handleSelectCandidate}
          onSelectAllErel={() => session?.ambiguousGroups && initSelections(session.ambiguousGroups)}
          onClearAll={clearAllSelections}
          onConfirmReview={handleConfirmReview}
        />
      )}

      {activeStep === 4 && session && (
        <StepResults
          session={session}
          activeResultTab={activeResultTab}
          setActiveResultTab={setActiveResultTab}
          selectedFileType={selectedFileType}
          fileContent={fileContent}
          isLoadingFile={isLoadingFile}
          copiedText={copiedText}
          onLoadFile={loadFile}
          onCopy={copyToClipboard}
          onGoToStep1={() => {
            setActiveStep(1);
            setSearchResults([]);
          }}
          onGoToStep2={() => setActiveStep(2)}
        />
      )}

      <ContentPreviewModal
        previewDoc={previewDoc}
        copiedText={copiedText}
        onClose={() => setPreviewDoc(null)}
        onCopy={copyToClipboard}
      />
    </div>
  );
}
