const { execSync } = require("child_process");

try {
  if (process.platform === "win32") {
    const out = execSync("netstat -ano", { encoding: "utf8" });
    const lines = out.split("\n");
    const pids = new Set();
    for (const line of lines) {
      if (line.includes(":3001") && line.includes("LISTENING")) {
        const parts = line.trim().split(/\s+/);
        const pid = parts[parts.length - 1];
        if (pid && !isNaN(pid) && pid !== String(process.pid)) {
          pids.add(pid);
        }
      }
    }
    for (const pid of pids) {
      try {
        execSync(`taskkill /F /T /PID ${pid}`, { stdio: "ignore" });
        console.log(`[Auto-Clean] Đã tự động giải phóng cổng 3001 (PID: ${pid})`);
      } catch {}
    }
  } else {
    try {
      execSync("fuser -k 3001/tcp", { stdio: "ignore" });
    } catch {}
  }
} catch {}
