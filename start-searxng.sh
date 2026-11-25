#!/bin/bash
echo "==================================================="
echo "    KHOI DONG SEARXNG CHO CRAWL DISAMBIGUOUS (MACOS)"
echo "==================================================="

if ! command -v docker >/dev/null 2>&1; then
    echo "[LOI] May chua cai Docker hoac chua them Docker vao PATH!"
    exit 1
fi

echo "Dang khoi chay SearXNG container tren port 8080..."
docker compose up -d
if [ $? -eq 0 ]; then
    echo "[THANH CONG] SearXNG da san sang tai http://localhost:8080"
    echo "Backend C# se tu dong phat hien va su dung SearXNG!"
else
    echo "[CHU Y] Khong the ket noi den Docker daemon."
    echo "Vui long mo ung dung 'Docker Desktop' va chay lai file nay!"
fi
