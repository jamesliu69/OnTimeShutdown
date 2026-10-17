# 自動關機工具

這是一個 Windows 桌面自動關機工具。它執行一般 Windows 關機作業；Windows Update 等系統流程由 Windows 自行處理，本程式不保證等待更新完成後再關機。

## 功能

- 設定每日關機時間並從通知區管理排程。
- 關機前提醒 30 秒，可順延 5 分鐘。
- 停用自動關機或修改時間會取消尚未完成的舊提醒。
- 電腦在設定時間睡眠而錯過觸發時，會補處理當日已追蹤的排程；首次啟動或設定新時間時若時間已過，則從明天開始，不補跑昨天的排程。
- 日誌檔名為 `shutdown_log_yyyy-MM-dd.txt`。

## 使用

啟動 `Showdown.exe` 後，可在通知區圖示右鍵選單查看配置、修改時間、啟用或停用排程、切換強制關機及退出。立即關機請使用通知區選單，不提供立即關機 CLI 參數。

`ForceShutdown` 預設為 `false`。一般關機不強制關閉應用程式；未儲存資料的處理仍由 Windows 與應用程式決定，請事先儲存。啟用強制關機 (`true`) 可能造成未儲存資料遺失。關機失敗或作業取消後，程式不會改用更強制的 fallback，並會繼續等待下一次排程。

程式不會自動要求提升權限。若 Windows 回報權限不足，請確認系統關機權限，必要時自行以系統管理員身分重新啟動程式。

## 安全測試參數

```text
Showdown.exe --self-test
Showdown.exe --test-shutdown
Showdown.exe --test-shutdown --force
```

`--self-test` 在建立 UI、載入正式設定或啟動計時器前執行安全檢查並結束；設定存取測試僅使用暫存檔，不變更使用者設定。`--test-shutdown` 僅模擬並列印一般關機命令 `/s /t 0`；加上 `--force` 則模擬 `/s /f /t 0`，不會啟動外部關機程序。`--minimized` 與 `-m` 保留相容性。

**這些安全測試行為僅適用於此版本；舊版本的 `--test-shutdown` 可能會真的關機，請勿用舊版測試。**

## Windows 開機時啟動

建置後可建立捷徑，將捷徑的「目標」設為帶引號的 exe 完整路徑並附上 `--minimized`，例如：

```text
"C:\完整路徑\Showdown.exe" --minimized
```

將捷徑放入 `shell:startup` 開啟的啟動資料夾。也可將 `AutoStartShutdownTool.bat` 捷徑放入啟動資料夾，並在捷徑目標中傳入 exe 絕對路徑，例如：

```text
"C:\repo\AutoStartShutdownTool.bat" "C:\repo\bin\Release\net8.0-windows\Showdown.exe"
```

批次檔未收到參數時，會依序搜尋腳本同目錄的 `Showdown.exe`、`bin\Release\net8.0-windows\Showdown.exe` 及 `bin\Debug\net8.0-windows\Showdown.exe`。複製到啟動資料夾時，請使用捷徑並提供正確 exe 路徑；單獨複製批次檔不會保證找到程式。

## 設定檔

程式會在 exe 同目錄建立 `shutdown_config.json`，可透過通知區選單修改設定：

```json
{
  "Hour": 17,
  "Minute": 30,
  "Second": 0,
  "ForceShutdown": false,
  "EnableAutoShutdown": true
}
```

## 系統需求與建置

- Windows
- .NET 8 Desktop Runtime（執行已建置的桌面程式）
- 建置使用 `net8.0-windows` 目標框架；本機 .NET SDK 需能建置 .NET 8 目標。

```powershell
dotnet build
dotnet run
dotnet run -- --minimized
```

日誌會寫入程式執行目錄下的 `shutdown_log_yyyy-MM-dd.txt`。
