#!/bin/bash
# ======================================================================
#           EREL DISAMBIGUATION STUDIO (MACOS / LINUX LAUNCHER)
# ======================================================================

echo "======================================================================"
echo "          EREL DISAMBIGUATION STUDIO (NEXT.JS + ASP.NET CORE)"
echo "======================================================================"

# Giải phóng cổng 5000 và 3001 nếu đang bị chiếm
echo "[0/2] Đang kiểm tra cổng 5000 và 3001..."
lsof -ti:5000 | xargs kill -9 2>/dev/null
lsof -ti:3001 | xargs kill -9 2>/dev/null

DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo "[1/2] Đang khởi động Backend API (Port 5000)..."
cd "$DIR/CrawlDisambigous" || exit 1
dotnet run --urls "http://0.0.0.0:5000;http://127.0.0.1:5000" &
BACKEND_PID=$!

echo "[2/2] Đang khởi động Frontend Next.js (Port 3001)..."
cd "$DIR/frontend" || exit 1
PORT=3001 npm run dev &
FRONTEND_PID=$!

sleep 3
if command -v open >/dev/null 2>&1; then
    open "http://localhost:3001"
elif command -v xdg-open >/dev/null 2>&1; then
    xdg-open "http://localhost:3001"
fi

echo ""
echo "======================================================================"
echo "[THÀNH CÔNG] Hệ thống đang mở!"
echo " - Backend API: http://localhost:5000"
echo " - Frontend UI: http://localhost:3001"
echo " Nhấn Ctrl+C để dừng toàn bộ hệ thống."
echo "======================================================================"

trap "kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; exit" SIGINT SIGTERM
wait
