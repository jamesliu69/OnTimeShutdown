# 自動關機可靠性修正計畫

> **For agentic workers:** 使用 subagent-driven-development；各工作區域不得重疊，不執行真正關機。

**Goal:** 修正本次審查列出的全部錯誤與文件落差。

**Architecture:** 維持 Windows Forms / .NET 8，使用原生控制項，不增加套件。時間計算保留本次待執行目標；命令建立與外部程序執行分離，以安全自我測試覆蓋行為。

**Spec:** 使用者已授權修正前次審查報告全部項目；不擴充新的排程種類。

## 全域限制

- 不修改全域設定、不安裝 SDK、不實際關機、不提交或推送。
- 保留每日排程、啟停、強制選項、30 秒提醒、順延 5 分鐘、通知區與主控台。
- 所有使用者介面與文件使用繁體中文；不新增外部套件。
- 採當日喚醒補觸發；首次啟動或重新設定時，已過的時間從下一次開始，不補跑歷史排程。
- 關機失敗或使用者取消提升權限，不使用更強制的替代命令、不反覆彈窗；繼續下一次每日排程。
- --test-shutdown 改為安全模擬；--self-test 執行無關機副作用的回歸檢查。

## 工作清單與驗證責任

- [ ] 核心：設定值域驗證、追蹤到期目標、提醒後重驗狀態、尊重強制選項、檢查程序退出碼、取消提升即停止、失敗恢復排程、可見的設定輸出及安全測試入口。核心工作者先寫安全回歸檢查，再實作；主代理核對來源並執行測試。
- [x] UI：新增原生時間設定對話框，使用數值控制項及確定／取消按鈕，避免 Console.ReadLine。設計工作者負責獨立表單檔案與鍵盤／取消／縮放驗證；核心工作者負責接線。
- [x] 建置與文件：修正 SDK 選擇、啟動腳本路徑、README 與專案指引中的日誌／版本／安全說明。建置工作者負責該區檔案；主代理負責實際 build、腳本與說明核對。
- [ ] 整合審查：核對全部需求、建置與自我測試；不以實際執行關機驗收。

## 檔案界面

- 核心擁有 Program.cs 及新建核心／自我測試 .cs 檔案，不修改表單、csproj、global.json、bat 或 README。
- UI 擁有 ShutdownTimeDialog.cs / ShutdownTimeDialog.Designer.cs；namespace Showdown，internal partial class ShutdownTimeDialog : Form，建構子 (int hour, int minute, int second)，唯讀 int Hour / Minute / Second；DialogResult.OK 才套用。
- 建置與文件擁有 global.json、Showdown.csproj、AutoStartShutdownTool.bat、README.md、.github/copilot-instructions.md；不修改核心、表單、其他個人腳本。
- 不同工作者的寫入範圍互斥；主代理整合前不修改工作者擁有檔案。

## 回歸案例

- 正常及強制的命令分別不帶／帶 /f；模擬入口不啟動程序、UI 或計時器。
- 超過設定時間數秒、睡眠跨越設定時間、過期順延均不會直接消失；每日最多處理一次。
- 提醒期間停用、改時間、退出，原提醒不得繼續關機。
- 關機失敗／UAC 取消後不強制重試，計時器恢復；成功退出碼與失敗退出碼可區分。
- 無效設定不造成失控或隱藏失效；儲存失敗需可見回饋。
- 取消時間編輯不改設定；主執行緒不再同步等候主控台輸入。
- 全新建置、--self-test、--test-shutdown、腳本路徑與文件一致性。

## 審查紀錄

基線：fa8d34093578b8d1488032cbd2293203d2a68dac；工作目錄原為乾淨。

基線建置受 global.json 無效 SDK 版本 8.0.0 阻擋；本機安裝 SDK 10.0.402，runtime 8.0.32 / 10.0.13。

Ruling: 在目前工作目錄保留未提交修正，不建立或切換 worktree／分支，避免未經同意改變使用者的 Git 工作環境；全部驗證均禁止實際關機。

工作者：核心 ses_eb8e3665bffedJbLZvoCpONdnr；UI ses_eb8ebf923ffeNZdZwte6Sqo0n1；建置文件 ses_eb8e3660effelUpSBk6dtvLCXq。三區寫入互斥，最終驗證由主代理執行。

建置文件檔案已由主代理逐檔核對；SDK 10.0.402 可由新 global.json 正常選出。批次檔不存在路徑分支以 ProcessStartInfo 指定 UTF-8 擷取後，繁中錯誤訊息正確且 exit=1；沒有執行任何可啟動本程式的分支。整合建置留待核心完成後執行。

UI 兩個新檔案已由主代理讀取核對契約、值域、Enter／Escape 與取消行為；尚待建置後安全執行期檢查。

首輪整合驗證：Release build 成功但有 3 個警告（CS8669、CS0649、CS8629）；--self-test、兩種 --test-shutdown 皆 exit=0，未新增／變更設定或日誌。安全 CLI 原始碼已確認先於 UI／計時器返回；沒有實際關機。

主代理於核准暫存目錄建立僅包含 ShutdownTimeDialog 兩檔的獨立 net8.0-windows 驗證器（不包含 Program.cs），10 個斷言通過：初值、3 欄位、時間值域、可存取標籤、Enter、Escape、取消按鈕結果、非法初值夾限、大字體控制項邊界、確定按鈕結果。

核心工作者補充：未在改碼前跑失敗測試，原 self-test 的命令部分僅測布林判定，尚非真實 production 執行接縫的測試。獨立審查 ses_eb8dbe2e3ffeK4vp7MPk5Fyp3K 正核對修復完整性；後續修正必須先補可安全失敗的回歸案例，再修正，且仍禁止外部關機。

獨立審查結論：spec／correctness 皆不通過。C1 取消與提交競爭；I1 設定失敗安全性；I2 點擊當下順延；I3 不補昨日；I4 啟用順序與 UI Join；I5 timer／退出／跨線程；I6 實際消費執行結果；I7 每日核心契約；I8 production seam 回歸；M1 停用 tooltip。交由原核心工作者一次合併修復，父方再做範圍限定重審。完整修復需求位於核准暫存目錄 OnTimeShutdown-fixwave-brief.md；暫時授權核心工作者僅機械修正 Designer 的兩個編譯警告，不變更視覺。

合併修復波交回後，主代理已逐檔核對；Debug 與 Release --warnaserror 皆成功（0 警告／0 錯誤），獨立表單 10 個執行期斷言再度通過。尚未可結案：實際 WinExe 的安全 CLI 啟動暴露新回歸，Program.Main 第 60 行直接設定 Console.OutputEncoding，在沒有 console 的重新導向程序拋 IOException「控制代碼無效」，exit=-532462766。這是實際 RED 證據，已送回原獨立審查者併入完整重審；安全 CLI 尚未驗收通過。

範圍限定重審仍不通過：N1 gate 包住 runas 的互動等待；N2 活動提醒先驗身分後 rollover，可能消耗今日；N3 Show 失敗未清 activePrompt；N4 無 console 的 CLI 崩潰。其他既有問題已判 ADDRESSED。

Ruling: 對新動態驗證與限定重審暴露的 4 個阻擋項追加一次小範圍修復，而非在存在確定崩潰時宣稱完成；成本是額外回歸與來源核對時間，不增加功能或套件。

Ruling: 移除自動 runas 提權重試，讓 gate 只包無互動的本機程序啟動；規格未要求自動提權，這比引入授權後再提交機制更小且安全。成本是權限不足時需由使用者自行以系統管理員身分重啟；程式會明確回報，不提升強制程度。
