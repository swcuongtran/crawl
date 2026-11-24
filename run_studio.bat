@echo off
chcp 65001 >nul
title EREL Disambiguation Studio Launcher
echo ======================================================================
echo           EREL DISAMBIGUATION STUDIO (NEXT.JS + ASP.NET CORE)
echo ======================================================================
echo [0/2] Đang kiểm tra và giải phóng cổng 5000 và 3001 (tránh lỗi trùng port)...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    taskkill /F /T /PID %%a >nul 2>&1
)
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3001" ^| findstr "LISTENING"') do (
    taskkill /F /T /PID %%a >nul 2>&1
)
taskkill /F /IM CrawlDisambigous.exe >nul 2>&1
timeout /t 1 >nul

echo [1/2] Đang khởi động Backend API (Port 5000)...
start "EREL Backend API (Port 5000)" cmd /k "cd /d "%~dp0CrawlDisambigous" && dotnet run --urls "http://0.0.0.0:5000;http://127.0.0.1:5000""

echo [2/2] Đang khởi động Frontend Next.js (Port 3001)...
start "EREL Frontend Next.js (Port 3001)" cmd /k "cd /d "%~dp0frontend" && npm run dev"

echo.
echo ======================================================================
echo [THÀNH CÔNG] Hệ thống đang mở!
echo  - Backend API: http://localhost:5000
echo  - Frontend UI: http://localhost:3001
echo ======================================================================
timeout /t 3 >nul
start http://localhost:3001
