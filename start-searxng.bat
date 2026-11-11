@echo off
echo ===================================================
echo     KHOI DONG SEARXNG CHO CRAWL DISAMBIGUOUS
echo ===================================================
echo Dang kiem tra Docker...
docker --version >nul 2>&1
if %errorlevel% neq 0 (
    echo [LOI] May tinh chua cai Docker hoac chua them Docker vao PATH!
    pause
    exit /b 1
)

echo Dang khoi chay SearXNG container tren port 8080...
docker compose up -d
if %errorlevel% equ 0 (
    echo [THANH CONG] SearXNG da san sang tai http://localhost:8080
    echo Backend C# se tu dong phat hien va su dung SearXNG!
) else (
    echo [CHU Y] Khong the ket noi den Docker daemon.
    echo Vui long mo 'Docker Desktop' va chay lai file nay!
)
pause

