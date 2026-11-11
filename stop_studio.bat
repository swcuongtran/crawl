@echo off
chcp 65001 >nul
title Dừng EREL Disambiguation Studio
echo ======================================================================
echo           DỪNG EREL DISAMBIGUATION STUDIO
echo ======================================================================

echo [1/2] Đang dừng Backend API (Port 5000)...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    echo   - Đang tắt tiến trình PID: %%a
    taskkill /F /PID %%a >nul 2>&1
)

echo [2/2] Đang dừng Frontend Next.js (Port 3001)...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3001" ^| findstr "LISTENING"') do (
    echo   - Đang tắt tiến trình PID: %%a
    taskkill /F /PID %%a >nul 2>&1
)

echo.
echo ======================================================================
echo [THÀNH CÔNG] Đã dừng toàn bộ dịch vụ (Port 5000 & 3001)!
echo ======================================================================
timeout /t 3 >nul
