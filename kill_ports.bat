@echo off
chcp 65001 >nul
title Giải phóng cổng 3001 và 5000
echo ======================================================================
echo           ĐANG GIẢI PHÓNG CỔNG 5000 (BACKEND) VÀ 3001 (FRONTEND)
echo ======================================================================

echo [1/3] Đang tắt các tiến trình đang chiếm cổng 5000...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    echo   - Tắt PID %%a trên cổng 5000
    taskkill /F /T /PID %%a >nul 2>&1
)

echo [2/3] Đang tắt các tiến trình đang chiếm cổng 3001...
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":3001" ^| findstr "LISTENING"') do (
    echo   - Tắt PID %%a trên cổng 3001
    taskkill /F /T /PID %%a >nul 2>&1
)

echo [3/3] Đang đóng tiến trình CrawlDisambigous.exe nếu còn treo...
taskkill /F /IM CrawlDisambigous.exe >nul 2>&1

echo.
echo ======================================================================
echo [THÀNH CÔNG] Đã giải phóng hoàn toàn cổng 5000 và 3001!
echo ======================================================================
timeout /t 2 >nul
