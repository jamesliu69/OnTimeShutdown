using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// 添加權限相關命名空間
// 添加反射命名空間，用於獲取執行檔路徑

namespace Showdown
{
	// Shutdown Configuration Class
	internal class ShutdownConfig
	{
		public int  Hour               { get; set; } = 17;
		public int  Minute             { get; set; } = 30;
		public int  Second             { get; set; }
		public bool ForceShutdown      { get; set; }
		public bool EnableAutoShutdown { get; set; } = true;

		public override string ToString()
		{
			return $"設定關機時間為: {Hour}:{Minute}:{Second}" + $"{(ForceShutdown ? " (強制關機)" : "")}" + $", 自動關機: {(EnableAutoShutdown ? "已啟用" : "已停用")}";
		}
	}

	internal class Program
	{
		// Show window commands
		private const int SW_HIDE     = 0;
		private const int SW_SHOW     = 5;

		// 修改設定檔和日誌檔案路徑，使用執行檔所在目錄
		private static readonly string               ExecutablePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
		private static readonly string               ConfigPath     = Path.Combine(ExecutablePath, "shutdown_config.json");
		private static readonly string               TrayIconPath   = Path.Combine(ExecutablePath, "Resources", "tray.ico");
		private static          System.Windows.Forms.Timer? shutdownTimer;
		private static          NotifyIcon?          trayIcon;      // 允許為 null
		private static          Icon?                trayIconResource;
		private static          ShutdownConfig?      config;        // 允許為 null
		private static          ShutdownFlow?        shutdownFlow;
		private static          bool                 exiting;
		private static          Form?                activePrompt;
		private static          Control?             uiDispatcher;
		private static readonly object               logLock = new object();

		// 每日一個日誌檔案
		private static string LogPath { get => Path.Combine(ExecutablePath, $"shutdown_log_{DateTime.Now:yyyy-MM-dd}.txt"); }

		// Import Windows API functions for window minimization
		[DllImport("kernel32.dll")]
		private static extern IntPtr GetConsoleWindow();

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		[STAThread]
		private static void Main(string[] args)
		{
			if(args.Contains("--self-test", StringComparer.Ordinal))
			{
				try { ReliabilitySelfTest.Run(SafeCliOutput.WriteStandardOutputLine); return; }
				catch(Exception ex) { SafeCliOutput.WriteStandardErrorLine(ex.ToString()); Environment.ExitCode = 1; return; }
			}
			if(args.Contains("--test-shutdown", StringComparer.Ordinal))
			{
				bool force = args.Contains("--force", StringComparer.Ordinal);
				SafeCliOutput.WriteStandardOutputLine($"安全模擬：{ShutdownCommand.ExecutablePath} {ShutdownCommand.Arguments(force)}（不會啟動程序）");
				return;
			}

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			// Initialize console
			AllocConsole();

			// 初始化日誌檔案
			InitializeLogFile();
			LogMessage("=================================");
			LogMessage("自動關機工具 - v1.2");
			LogMessage("=================================");

			// Load configuration
			ConfigLoadResult loaded = ShutdownConfigStore.Load(ConfigPath);
			config = loaded.Config;
			shutdownFlow = new ShutdownFlow(DateTime.Now, config.Hour, config.Minute, config.Second, config.EnableAutoShutdown, config.ForceShutdown);

			// Create system tray icon
			InitializeSystemTrayIcon();
			uiDispatcher = new Control();
			_ = uiDispatcher.Handle;
			UpdateTrayIconTooltip();
			if(loaded.Warning != null)
			{
				LogMessage(loaded.Warning);
				MessageBox.Show(loaded.Warning, "自動關機設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}

			HideConsoleWindow();

			// Immediately check time and display next shutdown time
			LogMessage("檢查目前時間和關機設定...");

			if(config != null) ShowNextShutdownTime(config);

			// Start shutdown timer
			if(config != null)
			{
				//記錄訊息
				StartShutdownTimer(config);
			}

			// Run application
			Application.Run();
		}

		private static void InitializeSystemTrayIcon()
		{
			trayIcon = new NotifyIcon
					   {
						   Icon    = LoadTrayIcon(),
						   Text    = "自動關機工具",
						   Visible = true
					   };

			// Create context menu
			ContextMenuStrip menu = new ContextMenuStrip();

			menu.Items.Add("顯示主控台", null, OnShowConsole);
			menu.Items.Add("隱藏主控台", null, OnHideConsole);
			menu.Items.Add("-"); // Separator

			menu.Items.Add("顯示設定",   null, OnShowConfig);
			menu.Items.Add("修改關機時間", null, OnModifyTime);

			if(config != null)
			{
				ToolStripMenuItem autoShutdownItem = new ToolStripMenuItem("啟用自動關機");
				autoShutdownItem.Checked =  config.EnableAutoShutdown;
				autoShutdownItem.Click   += OnToggleAutoShutdown;
				menu.Items.Add(autoShutdownItem);

				ToolStripMenuItem forceShutdownItem = new ToolStripMenuItem("強制關機");
				forceShutdownItem.Checked =  config.ForceShutdown;
				forceShutdownItem.Click   += OnToggleForceShutdown;
				menu.Items.Add(forceShutdownItem);
			}

			menu.Items.Add("-"); // Separator
			menu.Items.Add("立即關機", null, OnShutdownNow);
			menu.Items.Add("結束",   null, OnExit);

			trayIcon.ContextMenuStrip = menu;

			// Double-click to toggle console visibility
			trayIcon.DoubleClick += OnTrayIconDoubleClick;
		}

		private static Icon LoadTrayIcon()
		{
			if(!File.Exists(TrayIconPath))
			{
				LogMessage($"找不到系統匣圖示，改用預設圖示: {TrayIconPath}");
				return SystemIcons.Application;
			}

			try
			{
				trayIconResource = new Icon(TrayIconPath);
				return trayIconResource;
			}
			catch(IOException ex)
			{
				LogMessage($"載入系統匣圖示失敗，改用預設圖示: {ex.Message}");
			}
			catch(UnauthorizedAccessException ex)
			{
				LogMessage($"載入系統匣圖示失敗，改用預設圖示: {ex.Message}");
			}
			catch(ArgumentException ex)
			{
				LogMessage($"載入系統匣圖示失敗，改用預設圖示: {ex.Message}");
			}

			return SystemIcons.Application;
		}

		private static void OnTrayIconDoubleClick(object? sender, EventArgs e)
		{
			ToggleConsoleVisibility();
		}

		private static void OnShowConsole(object? sender, EventArgs e)
		{
			ShowConsoleWindow();
		}

		private static void OnHideConsole(object? sender, EventArgs e)
		{
			HideConsoleWindow();
		}

		private static void OnShowConfig(object? sender, EventArgs e)
		{
			if(config != null)
			{
				LogMessage($"目前設定: {config}");
				MessageBox.Show($"目前設定：{config}\n{(config.EnableAutoShutdown ? $"下次關機：{GetNextShutdownTime(config):yyyy-MM-dd HH:mm:ss}" : "自動關機已停用")}", "自動關機設定", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			else
			{
				LogMessage("設定檔尚未載入。");
			}
		}

		private static void OnModifyTime(object? sender, EventArgs e)
		{
			ShowConsoleWindow();

			if(config != null)
			{
				using ShutdownTimeDialog dialog = new ShutdownTimeDialog(config.Hour, config.Minute, config.Second);
				if(dialog.ShowDialog() != DialogResult.OK) return;
				config.Hour = dialog.Hour;
				config.Minute = dialog.Minute;
				config.Second = dialog.Second;
				shutdownFlow?.Update(config.EnableAutoShutdown, config.ForceShutdown, DateTime.Now, config.Hour, config.Minute, config.Second, true);
				SaveConfig(config);
				RestartShutdownTimer(config);
				ShowNextShutdownTime(config);

				// 更新菜單項的勾選狀態
				UpdateTrayMenuItems();
			}
			else
			{
				LogMessage("設定檔尚未載入。");
			}
		}

		private static void OnToggleAutoShutdown(object? sender, EventArgs e)
		{
			if(config == null) return;
			config.EnableAutoShutdown = !config.EnableAutoShutdown;
			shutdownFlow?.Update(config.EnableAutoShutdown, config.ForceShutdown, DateTime.Now, config.Hour, config.Minute, config.Second, true);
			LogMessage($"自動關機設定為: {(config.EnableAutoShutdown ? "已啟用" : "已停用")}");
			if(config.EnableAutoShutdown) StartShutdownTimer(config); else shutdownTimer?.Stop();
			SaveConfig(config);
			UpdateTrayIconTooltip();
			if(sender is ToolStripMenuItem item) item.Checked = config.EnableAutoShutdown;
		}

		private static void OnToggleForceShutdown(object? sender, EventArgs e)
		{
			if(config == null) return;
			config.ForceShutdown = !config.ForceShutdown;
			shutdownFlow?.Update(config.EnableAutoShutdown, config.ForceShutdown, DateTime.Now, config.Hour, config.Minute, config.Second, false);
			LogMessage($"強制關機設定為: {(config.ForceShutdown ? "已啟用" : "已停用")}");
			SaveConfig(config);
			if(sender is ToolStripMenuItem item) item.Checked = config.ForceShutdown;
		}

		private static void OnShutdownNow(object? sender, EventArgs e)
		{
			if(config != null)
			{
				ExecuteShutdown(config);
			}
		}

		private static void OnExit(object? sender, EventArgs e)
		{
			exiting = true;
			shutdownFlow?.Exit();
			activePrompt?.Close();
			shutdownTimer?.Stop();
			shutdownTimer?.Dispose();
			shutdownTimer = null;
			uiDispatcher?.Dispose();
			uiDispatcher = null;
			if(trayIcon != null)
			{
				trayIcon.Visible = false;
				trayIcon.Dispose();
			}

			trayIconResource?.Dispose();
			trayIconResource = null;

			Application.Exit();
		}

		private static void UpdateTrayMenuItems()
		{
			if((trayIcon?.ContextMenuStrip != null) && (config != null))
			{
				foreach(object? item in trayIcon.ContextMenuStrip.Items)
				{
					if(item is ToolStripMenuItem menuItem)
					{
						if(menuItem.Text == "啟用自動關機")
						{
							menuItem.Checked = config.EnableAutoShutdown;
						}
						else if(menuItem.Text == "強制關機")
						{
							menuItem.Checked = config.ForceShutdown;
						}
					}
				}
			}
		}

		private static void ToggleConsoleVisibility()
		{
			IntPtr handle = GetConsoleWindow();

			if(handle != IntPtr.Zero)
			{
				if(IsConsoleVisible())
				{
					ShowWindow(handle, SW_HIDE);
				}
				else
				{
					ShowWindow(handle, SW_SHOW);
				}
			}
		}

		private static bool IsConsoleVisible()
		{
			return IsWindowVisible(GetConsoleWindow());
		}

		[DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool IsWindowVisible(IntPtr hWnd);

		private static void ShowConsoleWindow()
		{
			IntPtr handle = GetConsoleWindow();

			if(handle != IntPtr.Zero)
			{
				ShowWindow(handle, SW_SHOW);
			}
		}

		private static void HideConsoleWindow()
		{
			IntPtr handle = GetConsoleWindow();

			if(handle != IntPtr.Zero)
			{
				ShowWindow(handle, SW_HIDE);
			}
		}

		[DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool AllocConsole();

		private static void SaveConfig(ShutdownConfig value)
		{
			try
			{
				ShutdownConfigStore.Save(ConfigPath, value);
				LogMessage("設定已保存");
			}
			catch(Exception ex)
			{
				LogMessage($"設定檔保存錯誤：{ex.Message}；目前設定僅於本次執行有效。");
				if(trayIcon != null) MessageBox.Show($"無法保存設定：{ex.Message}\n目前設定僅於本次執行有效。", "自動關機工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			UpdateTrayIconTooltip();
		}

		private static void UpdateTrayIconTooltip()
		{
			if(trayIcon == null || config == null || shutdownFlow == null) return;
			DateTime target = shutdownFlow.Target(DateTime.Now);
			trayIcon.Text = ShutdownStatus.Tooltip(config.EnableAutoShutdown, target);
		}

		private static DateTime GetNextShutdownTime(ShutdownConfig value) => shutdownFlow?.Target(DateTime.Now) ?? DateTime.Now;
		private static void ShowNextShutdownTime(ShutdownConfig config)
		{
			if(!config.EnableAutoShutdown)
			{
				LogMessage("自動關機功能目前已停用");
				UpdateTrayIconTooltip();
				return;
			}

			DateTime now          = DateTime.Now;
			DateTime shutdownTime = GetNextShutdownTime(config);

			// 用關機時間更新系統托盤圖示的提示文字
			UpdateTrayIconTooltip();

			// If shutdown time is tomorrow
			if(shutdownTime.Date > now.Date)
			{
				LogMessage($"下次預定關機: 明天 {shutdownTime.ToShortTimeString()}");
			}
			else
			{
				TimeSpan timeLeft = shutdownTime - now;

				LogMessage($"下次預定關機: 今天 {shutdownTime.ToShortTimeString()} (剩餘 {timeLeft.Hours}小時 {timeLeft.Minutes}分鐘 {timeLeft.Seconds}秒)");
			}
		}

		private static void ShowShutdownPrompt(DateTime target, long version)
		{
			if(exiting || activePrompt != null || shutdownFlow == null) return;
			trayIcon?.ShowBalloonTip(5000, "自動關機提醒", "30 秒內可選擇順延 5 分鐘，否則將照常關機。", ToolTipIcon.Warning);
			var prompt = new Form { Text = "自動關機提醒", Width = 420, Height = 220, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterScreen, TopMost = true, ShowInTaskbar = true };
			var title = new Label { Left = 20, Top = 20, Width = 360, Height = 55, Text = $"現在到達預定關機時間 {target:HH:mm:ss}。\r\n是否要順延 5 分鐘？", Font = new Font(SystemFonts.DefaultFont.FontFamily, 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
			var countdown = new Label { Left = 20, Top = 85, Width = 360, Height = 30, TextAlign = ContentAlignment.MiddleLeft };
			var postpone = new Button { Text = "順延 5 分鐘", Width = 130, Height = 35, Left = 70, Top = 130 };
			var shutdown = new Button { Text = "照常關機", Width = 130, Height = 35, Left = 220, Top = 130 };
			int seconds = 30;
			var countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
			DateTime? clickedAt = null;
			bool shouldPostpone = false;
			void Finish(bool postponeChoice)
			{
				if(prompt.IsDisposed) return;
				shouldPostpone = postponeChoice;
				if(postponeChoice) clickedAt = DateTime.Now;
				prompt.Close();
			}
			postpone.Click += (_, _) => Finish(true);
			shutdown.Click += (_, _) => Finish(false);
			countdownTimer.Tick += (_, _) =>
			{
				seconds--;
				if(seconds <= 0) { countdownTimer.Stop(); Finish(false); }
				else countdown.Text = $"若 {seconds} 秒內未選擇，系統將照常執行關機。";
			};
			prompt.Controls.AddRange(new Control[] { title, countdown, postpone, shutdown });
			prompt.AcceptButton = postpone;
			prompt.CancelButton = shutdown;
			countdown.Text = "若 30 秒內未選擇，系統將照常執行關機。";
			ShowReminder(prompt, countdownTimer, form => form.Show(), () => CompleteReminder(version, target, shouldPostpone, clickedAt ?? DateTime.Now));
		}

		internal static void ShowReminder(Form prompt, System.Windows.Forms.Timer countdownTimer, Action<Form> show, Action onClosed)
		{
			bool shown = false;
			try
			{
				prompt.FormClosed += (_, _) =>
				{
					try { if(shown) onClosed(); }
					finally { CleanupReminder(prompt, countdownTimer); }
				};
				activePrompt = prompt;
				countdownTimer.Start();
				show(prompt);
				shown = true;
			}
			catch
			{
				CleanupReminder(prompt, countdownTimer);
				throw;
			}
		}

		private static void CleanupReminder(Form prompt, System.Windows.Forms.Timer countdownTimer)
		{
			try { countdownTimer.Stop(); } catch { }
			try { countdownTimer.Dispose(); } catch { }
			if(ReferenceEquals(activePrompt, prompt)) activePrompt = null;
			try { if(!prompt.IsDisposed) prompt.Dispose(); } catch { }
		}

		private static void CompleteReminder(long version, DateTime target, bool postpone, DateTime clickedAt)
		{
			if(exiting || config == null || shutdownFlow == null) return;
			if(postpone)
			{
				if(!shutdownFlow.TryPostpone(version, target, clickedAt)) return;
				LogMessage($"使用者選擇順延關機，本次關機延後至 {shutdownFlow.Target(DateTime.Now):yyyy-MM-dd HH:mm:ss}");
				UpdateTrayIconTooltip();
				return;
			}
			ShutdownCommit? commit = shutdownFlow.TryCommit(version, target, DateTime.Now);
			if(commit == null) { LogMessage("提醒期間排程已停用或變更，取消本次關機。"); return; }
			LogMessage("排程已提交；等待關機命令結果。");
			Task.Run(async () =>
			{
				Task<ShutdownResult> started = shutdownFlow.RunQueued(commit, StartShutdownAsync);
				ShutdownResult result = await started.ConfigureAwait(false);
				PostToUi(() => ReportShutdownResult(result));
			});
		}

		private static void PostToUi(Action action)
		{
			if(exiting || uiDispatcher == null || uiDispatcher.IsDisposed || !uiDispatcher.IsHandleCreated) return;
			try { uiDispatcher.BeginInvoke(action); } catch(ObjectDisposedException) { }
		}

		private static void ReportShutdownResult(ShutdownResult result)
		{
			switch(result.Outcome)
			{
				case ShutdownOutcome.Success:
					LogMessage("關機命令成功接受。");
					trayIcon?.ShowBalloonTip(3000, "電腦關機", "關機命令已成功接受。", ToolTipIcon.Info);
					break;
				case ShutdownOutcome.NonzeroExit:
					LogMessage($"關機命令失敗，exit code={result.ExitCode}；下次排程仍保留。");
					trayIcon?.ShowBalloonTip(5000, "關機未執行", $"命令失敗（exit code {result.ExitCode}），將於下次排程再試。", ToolTipIcon.Error);
					break;
				case ShutdownOutcome.UacCancelled:
					LogMessage("使用者取消權限提升；下次排程仍保留。");
					trayIcon?.ShowBalloonTip(5000, "關機已取消", "已取消權限提升；將於下次排程再試。", ToolTipIcon.Warning);
					break;
				case ShutdownOutcome.CancelledBeforeStart:
					LogMessage("關機工作在啟動前已取消。");
					break;
				default:
					LogMessage($"關機程序啟動失敗：{result.Error}；下次排程仍保留。");
					trayIcon?.ShowBalloonTip(5000, "關機未執行", $"無法啟動關機命令：{result.Error}\n若權限不足，請自行以系統管理員身分重新啟動程式。", ToolTipIcon.Error);
					break;
			}
		}
		private static void StartShutdownTimer(ShutdownConfig value)
		{
			if(exiting || !value.EnableAutoShutdown) return;
			if(shutdownTimer == null)
			{
				shutdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
				shutdownTimer.Tick += (_, _) => CheckShutdownTime();
			}
			shutdownTimer.Stop();
			shutdownTimer.Start();
			LogMessage("關機計時器已啟動 - 每秒檢查一次");
		}

		private static void CheckShutdownTime()
		{
			if(exiting || config == null || shutdownFlow == null || activePrompt != null || !config.EnableAutoShutdown) return;
			DateTime now = DateTime.Now;
			DateTime target = shutdownFlow.Target(now);
			TimeSpan remaining = target - now;
			if(remaining.TotalMinutes <= 5)
				LogMessage($"預定關機時間：{target:yyyy-MM-dd HH:mm:ss}，剩餘 {remaining.TotalSeconds:F0} 秒。");
			if(remaining.TotalSeconds <= 10 && remaining.TotalSeconds > 0)
				trayIcon?.ShowBalloonTip(5000, "關機即將執行", $"電腦將在 {Math.Ceiling(remaining.TotalSeconds)} 秒後關機。", ToolTipIcon.Warning);
			if(shutdownFlow.IsDue(now))
			{
				long version = shutdownFlow.Version;
				try { ShowShutdownPrompt(target, version); }
				catch(Exception ex)
				{
					LogMessage($"建立關機提醒失敗，本次排程停止重試：{ex.Message}");
					shutdownFlow.TrySkip(version, target, DateTime.Now);
				}
			}
		}
		// 初始化日誌檔案
		private static void InitializeLogFile()
		{
			try
			{
				string? logDirectory = Path.GetDirectoryName(LogPath);

				if(!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
				{
					Directory.CreateDirectory(logDirectory);
				}
				LogMessage("程式啟動，日誌系統初始化");
				LogMessage($"執行檔路徑: {ExecutablePath}");
				LogMessage($"設定檔路徑: {ConfigPath}");
				LogMessage($"日誌檔案路徑: {LogPath}");
			}
			catch(Exception ex)
			{
				try
				{
					string altPath = Path.Combine(Environment.CurrentDirectory, $"shutdown_log_{DateTime.Now:yyyy-MM-dd}_alt.txt");
					File.AppendAllText(altPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 日誌初始化失敗: {ex.Message}\r\n");
				}
				catch
				{
				}
			}
		}

		// 記錄訊息至每日日誌檔案
		private static void LogMessage(string message)
		{
			string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
			try { Console.WriteLine(logEntry); } catch { }
			try
			{
				lock(logLock) File.AppendAllText(LogPath, logEntry + Environment.NewLine);
			}
			catch
			{
			}
		}

		private static void RestartShutdownTimer(ShutdownConfig config)
		{
			// Simply restart the timer with new config
			StartShutdownTimer(config);
		}

		private static void ExecuteShutdown(ShutdownConfig value)
		{
			bool force = value.ForceShutdown;
			Task.Run(async () =>
			{
				ShutdownResult result = await StartShutdownAsync(force).ConfigureAwait(false);
				PostToUi(() => ReportShutdownResult(result));
			});
		}

		// Process.Start is the synchronous start boundary; process exit is awaited off the UI thread.
		private static Task<ShutdownResult> StartShutdownAsync(bool force)
		{
			LogMessage($"執行關機命令：{ShutdownCommand.ExecutablePath} {ShutdownCommand.Arguments(force)}");
			return ShutdownCommand.Execute(force, StartAndWait);
		}

		private static Task<int> StartAndWait(ProcessStartInfo info)
		{
			Process process = Process.Start(info) ?? throw new InvalidOperationException("Process.Start returned null");
			return Task.Run(() =>
			{
				using(process)
				{
					process.WaitForExit();
					return process.ExitCode;
				}
			});
		}
	}
}
