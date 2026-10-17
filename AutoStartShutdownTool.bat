@echo off
chcp 65001 >nul
setlocal
:: 可指定 Showdown.exe 的絕對路徑；未指定時依序搜尋腳本目錄及建置輸出目錄。
if "%~1"=="" goto search
if not exist "%~1" goto notfound
start "" "%~1" --minimized
exit /b 0

:search
set "APP=%~dp0Showdown.exe"
if exist "%APP%" goto launch
set "APP=%~dp0bin\Release\net8.0-windows\Showdown.exe"
if exist "%APP%" goto launch
set "APP=%~dp0bin\Debug\net8.0-windows\Showdown.exe"
if exist "%APP%" goto launch

echo 錯誤：找不到 Showdown.exe。請將 Showdown.exe 放在此腳本旁、在此 repo 建置 Release/Debug，或以第一個參數指定 exe 絕對路徑。
exit /b 1

:notfound
echo 錯誤：指定的 Showdown.exe 不存在："%~1"
exit /b 1

:launch
start "" "%APP%" --minimized
exit /b 0
