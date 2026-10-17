# 專案指引

## 架構

Windows Forms 桌面應用程式，目標框架為 `net8.0-windows`，不新增外部套件。

- 設定檔 `shutdown_config.json` 與日誌位於執行檔同目錄。
- 日誌檔名為 `shutdown_log_yyyy-MM-dd.txt`。
- 排程執行一般 Windows 關機；Windows Update 等系統流程由 Windows 處理，不宣稱等待更新完成。
- 通知區提供排程控制、修改時間、強制關機切換及立即關機操作；立即關機由通知區操作，不提供 CLI 立即關機介面。

## 程式慣例

- 使用 nullable-aware C#、`<ImplicitUsings>enable</ImplicitUsings>`、`<LangVersion>latest</LangVersion>`。
- 輸出類型為 `WinExe`，使用 `<UseWindowsForms>true</UseWindowsForms>`。
- 檔案路徑以執行檔所在目錄為基準，不依賴目前工作目錄。
- 修改設定時保持共用狀態同步，儲存設定並更新通知區選單。
- 一般關機命令為 `shutdown /s /t 0`；強制關機為 `shutdown /s /f /t 0`。不可信輸入不得直接串入程序參數。
- 關機失敗或使用者取消 UAC 時不得執行更強制 fallback，應恢復並等待下次排程。
- 強制關機可能造成未儲存資料遺失。

## 排程與提醒

- 關機前提醒 30 秒，可順延 5 分鐘。
- 停用排程或修改時間時，取消舊提醒。
- 睡眠跨過設定時間時，補處理當日已追蹤的排程；首次啟動或新設定時間已過時從明天開始，不補昨天的排程。

## 建置與安全測試

```powershell
dotnet build
dotnet run
dotnet run -- --minimized
Showdown.exe --self-test
Showdown.exe --test-shutdown
Showdown.exe --test-shutdown --force
```

`--self-test` 必須在 UI、設定載入及計時器初始化前安全執行並結束。`--test-shutdown` 僅模擬並輸出普通 `/s /t 0`；加 `--force` 輸出 `/s /f /t 0`，兩者都不得啟動外部程序。`--minimized` / `-m` 保留相容性。以上安全參數行為僅適用於此版本；舊版 `--test-shutdown` 可能真的關機，勿以舊版測試。

建置輸出通常位於 `bin/Debug/net8.0-windows/Showdown.exe`。執行時設定檔與 `shutdown_log_yyyy-MM-dd.txt` 位於執行檔旁。

## 整合點

- `AutoStartShutdownTool.bat` 可接受 exe 絕對路徑參數；否則搜尋腳本同目錄及 repo 的 Release/Debug 輸出。若放入啟動資料夾，應使用捷徑並提供正確 exe 路徑。
- `shutdown_config.json` 欄位：`Hour`、`Minute`、`Second`、`ForceShutdown`、`EnableAutoShutdown`。
- 不要實際執行關機作為驗證；測試參數只能模擬，不啟動系統關機程序。
