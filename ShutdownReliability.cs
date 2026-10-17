using System;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Showdown;

internal sealed class ShutdownSchedule
{
	private DateTime target;
	private readonly TimeSpan dailyTime;
	public DateTime Target => target;

	public ShutdownSchedule(DateTime now, int hour, int minute, int second)
	{
		ValidateTime(hour, minute, second);
		dailyTime = new TimeSpan(hour, minute, second);
		Reset(now);
	}

	public void Reset(DateTime now)
	{
		target = now.Date + dailyTime;
		if(target <= now) target = target.AddDays(1);
	}

	public bool IsDue(DateTime now)
	{
		NormalizeDate(now);
		return target <= now;
	}

	public void NormalizeDate(DateTime now)
	{
		if(target.Date < now.Date) target = now.Date + dailyTime;
	}

	public void PostponeAt(DateTime clickedAt) => target = clickedAt.AddMinutes(5);

	public void Complete(DateTime now)
	{
		target = now.Date.AddDays(1) + dailyTime;
	}

	public static void ValidateTime(int hour, int minute, int second)
	{
		if(hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
			throw new ArgumentOutOfRangeException(nameof(hour), "關機時間必須介於 00:00:00 到 23:59:59。");
	}
}

internal sealed record ShutdownCommit(long Version, DateTime Target, bool Force);

internal enum ShutdownOutcome { Success, NonzeroExit, StartFailure, UacCancelled, CancelledBeforeStart }

internal sealed record ShutdownResult(ShutdownOutcome Outcome, int? ExitCode = null, string? Error = null);

internal sealed class ShutdownFlow
{
	private readonly object gate = new();
	private ShutdownSchedule schedule;
	private bool enabled;
	private bool force;
	private bool exiting;
	private long version;

	public ShutdownFlow(DateTime now, int hour, int minute, int second, bool enabled, bool force)
	{
		schedule = new ShutdownSchedule(now, hour, minute, second);
		this.enabled = enabled;
		this.force = force;
	}

	public DateTime Target(DateTime now)
	{
		lock(gate) { schedule.IsDue(now); return schedule.Target; }
	}

	public bool IsDue(DateTime now)
	{
		lock(gate) return enabled && !exiting && schedule.IsDue(now);
	}

	public long Update(bool autoEnabled, bool forceEnabled, DateTime now, int hour, int minute, int second, bool resetTarget)
	{
		lock(gate)
		{
			ShutdownSchedule.ValidateTime(hour, minute, second);
			if(resetTarget || enabled != autoEnabled) version++;
			enabled = autoEnabled;
			force = forceEnabled;
			if(resetTarget) schedule = new ShutdownSchedule(now, hour, minute, second);
			return version;
		}
	}

	public bool TryPostpone(long expectedVersion, DateTime expectedTarget, DateTime clickedAt)
	{
		lock(gate)
		{
			schedule.NormalizeDate(clickedAt);
			if(!Current(expectedVersion, expectedTarget) || !enabled || !schedule.IsDue(clickedAt)) return false;
			schedule.PostponeAt(clickedAt);
			return true;
		}
	}

	public ShutdownCommit? TryCommit(long expectedVersion, DateTime expectedTarget, DateTime now)
	{
		lock(gate)
		{
			schedule.NormalizeDate(now);
			if(!Current(expectedVersion, expectedTarget) || !enabled || !schedule.IsDue(now)) return null;
			var commit = new ShutdownCommit(version, schedule.Target, force);
			schedule.Complete(now);
			return commit;
		}
	}

	public bool TrySkip(long expectedVersion, DateTime expectedTarget, DateTime now)
	{
		lock(gate)
		{
			schedule.NormalizeDate(now);
			if(!Current(expectedVersion, expectedTarget) || !enabled || !schedule.IsDue(now)) return false;
			schedule.Complete(now);
			return true;
		}
	}

	// Invoking executor inside this short gate is the process-start commit point; executor must return promptly with a task.
	public Task<ShutdownResult> RunQueued(ShutdownCommit commit, Func<bool, Task<ShutdownResult>> executor)
	{
		lock(gate)
		{
			if(exiting || !enabled || version != commit.Version)
				return Task.FromResult(new ShutdownResult(ShutdownOutcome.CancelledBeforeStart));
			try { return executor(commit.Force); }
			catch(Exception ex) { return Task.FromResult(new ShutdownResult(ShutdownOutcome.StartFailure, Error: ex.Message)); }
		}
	}

	public void Exit()
	{
		lock(gate) { exiting = true; version++; }
	}

	public long Version { get { lock(gate) return version; } }

	private bool Current(long expectedVersion, DateTime expectedTarget)
		=> !exiting && version == expectedVersion && schedule.Target == expectedTarget;
}

internal sealed record ConfigLoadResult(ShutdownConfig Config, bool IsValid, string? Warning);

internal static class ShutdownConfigStore
{
	public static ConfigLoadResult Load(string path)
	{
		bool missing;
		try { File.GetAttributes(path); missing = false; }
		catch(FileNotFoundException) { missing = true; }
		catch(DirectoryNotFoundException) { missing = true; }
		catch(Exception ex) { return new(new ShutdownConfig { EnableAutoShutdown = false }, false, $"無法檢查設定檔，已停用自動關機且不覆寫原檔：{ex.Message}"); }
		if(missing)
		{
			var defaults = new ShutdownConfig();
			try { Save(path, defaults); return new(defaults, true, null); }
			catch(Exception ex) { return new(defaults, true, $"預設設定僅本次有效，無法保存：{ex.Message}"); }
		}
		try
		{
			var loaded = JsonSerializer.Deserialize<ShutdownConfig>(File.ReadAllText(path));
			if(loaded == null) throw new InvalidDataException("設定內容為空。");
			ShutdownSchedule.ValidateTime(loaded.Hour, loaded.Minute, loaded.Second);
			return new(loaded, true, null);
		}
		catch(Exception ex)
		{
			return new(new ShutdownConfig { EnableAutoShutdown = false }, false, $"設定檔讀取或驗證失敗，已保留原檔並停用自動關機：{ex.Message}");
		}
	}

	public static void Save(string path, ShutdownConfig config)
	{
		ShutdownSchedule.ValidateTime(config.Hour, config.Minute, config.Second);
		string directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException("設定路徑缺少目錄。");
		Directory.CreateDirectory(directory);
		string temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		try
		{
			File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
			File.Move(temp, path, true);
		}
		finally { if(File.Exists(temp)) File.Delete(temp); }
	}
}

internal static class ShutdownCommand
{
	public static string ExecutablePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
	public static string Arguments(bool force) => force ? "/s /f /t 0" : "/s /t 0";
	public static ProcessStartInfo BuildStartInfo(bool force) => new(ExecutablePath, Arguments(force)) { UseShellExecute = false, CreateNoWindow = true };
	public static async Task<ShutdownResult> Execute(bool force, Func<ProcessStartInfo, Task<int>> startAndWait)
	{
		try { return FromExitCode(await startAndWait(BuildStartInfo(force)).ConfigureAwait(false)); }
		catch(Win32Exception ex) { return FromStartError(ex.NativeErrorCode, ex.Message); }
		catch(Exception ex) { return new ShutdownResult(ShutdownOutcome.StartFailure, Error: ex.Message); }
	}
	public static ShutdownResult FromExitCode(int exitCode) => exitCode == 0
		? new(ShutdownOutcome.Success, exitCode)
		: new(ShutdownOutcome.NonzeroExit, exitCode);
	public static ShutdownResult FromStartError(int errorCode, string message) => errorCode == 1223
		? new(ShutdownOutcome.UacCancelled, errorCode, message)
		: new(ShutdownOutcome.StartFailure, errorCode, message);
}

internal static class SafeCliOutput
{
	private static readonly UTF8Encoding Utf8NoBom = new(false);
	public static bool WriteUtf8(Stream? stream, string text)
	{
		if(stream == null) return false;
		try
		{
			using var writer = new StreamWriter(stream, Utf8NoBom, 1024, true);
			writer.WriteLine(text);
			writer.Flush();
			return true;
		}
		catch { return false; }
	}
	public static void WriteStandardOutputLine(string text)
	{
		try { WriteUtf8(Console.OpenStandardOutput(), text); } catch { }
	}
	public static void WriteStandardErrorLine(string text)
	{
		try { WriteUtf8(Console.OpenStandardError(), text); } catch { }
	}
}

internal static class ShutdownStatus
{
	public static string Tooltip(bool enabled, DateTime target)
		=> enabled ? $"自動關機工具 - 下次關機：{target:HH:mm:ss}" : "自動關機工具 - 自動關機已停用";
}

internal static class ReliabilitySelfTest
{
	private static int cases;
	private static int failures;
	private static Action<string> writeLine = _ => { };
	public static void Run(Action<string> output)
	{
		writeLine = output;
		cases = failures = 0;
		Test("逾時首啟跳過歷史", () => Equal(new DateTime(2026,10,18,17,30,0), new ShutdownSchedule(new DateTime(2026,10,17,17,30,3),17,30,0).Target));
		Test("同日延遲仍到期", () => True(new ShutdownSchedule(new DateTime(2026,10,17,17,29,0),17,30,0).IsDue(new DateTime(2026,10,17,17,30,8))));
		Test("昨日目標跨日丟棄", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,23,58,0),23,59,0); False(s.IsDue(new DateTime(2026,10,18,0,1,0))); Equal(new DateTime(2026,10,18,23,59,0),s.Target); });
		Test("多日睡眠只判定今日", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,23,58,0),23,59,0); True(s.IsDue(new DateTime(2026,10,20,23,59,1))); Equal(new DateTime(2026,10,20,23,59,0),s.Target); });
		Test("連續兩日各自觸發", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,17,0,0),17,30,0); True(s.IsDue(new DateTime(2026,10,17,17,30,1))); s.Complete(new DateTime(2026,10,17,17,30,1)); True(s.IsDue(new DateTime(2026,10,18,17,30,1))); });
		Test("未觸發重複每日最多一次", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,17,0,0),17,30,0); True(s.IsDue(new DateTime(2026,10,17,17,30,1))); s.Complete(new DateTime(2026,10,17,17,30,1)); False(s.IsDue(new DateTime(2026,10,17,18,0,0))); });
		Test("倒數末段順延錨定點擊", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,10,0,0),10,0,0); var clicked = new DateTime(2026,10,17,10,0,29); s.PostponeAt(clicked); Equal(clicked.AddMinutes(5),s.Target); });
		Test("提醒關閉晚醒仍從點擊加五分", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,10,0,0),10,0,0); var clicked = new DateTime(2026,10,17,10,0,29); s.PostponeAt(clicked); False(s.IsDue(new DateTime(2026,10,17,10,5,28))); });
		Test("順延跨日保留明日目標", () => { var s = new ShutdownSchedule(new DateTime(2026,10,17,23,59,0),23,59,0); s.PostponeAt(new DateTime(2026,10,17,23,59,59)); Equal(new DateTime(2026,10,18,0,4,59),s.Target); });
		Test("停用期間舊提醒不提交", () => { var f = NewFlow(); var target=f.Target(new DateTime(2026,10,17,17,30,0)); var v=f.Version; f.Update(false,false,new DateTime(2026,10,17,17,30,0),17,30,0,false); Null(f.TryCommit(v,target,new DateTime(2026,10,17,17,31,0))); });
		Test("改設定期間舊提醒不提交", () => { var f=NewFlow(); var t=f.Target(new DateTime(2026,10,17,17,30,0)); var v=f.Version; f.Update(true,false,new DateTime(2026,10,17,17,31,0),18,0,0,true); Null(f.TryCommit(v,t,new DateTime(2026,10,17,17,32,0))); });
		Test("重新啟用重置為下次排程", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,17,31,0),17,30,0,false,false); f.Update(true,false,new DateTime(2026,10,17,17,31,0),17,30,0,true); Equal(new DateTime(2026,10,18,17,30,0),f.Target(new DateTime(2026,10,17,17,31,0))); });
		Test("退出期間舊提醒不提交", () => { var f=NewFlow(); var t=f.Target(new DateTime(2026,10,17,17,30,0)); var v=f.Version; f.Exit(); Null(f.TryCommit(v,t,new DateTime(2026,10,17,17,31,0))); });
		Test("Force 由commit快照決定", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var c=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; f.Update(true,false,new DateTime(2026,10,17,17,31,1),17,30,0,false); bool? force=null; await f.RunQueued(c,x=>{force=x;return Task.FromResult(new ShutdownResult(ShutdownOutcome.Success));}); True(force == true); });
		Test("排隊後停用則執行器零次", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var c=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; f.Update(false,true,new DateTime(2026,10,17,17,31,1),17,30,0,false); int calls=0; var r=await f.RunQueued(c,_=>{calls++;return Task.FromResult(new ShutdownResult(ShutdownOutcome.Success));}); Equal(0,calls); Equal(ShutdownOutcome.CancelledBeforeStart,r.Outcome); });
		Test("排隊後退出則執行器零次", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var c=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; f.Exit(); int calls=0; await f.RunQueued(c,_=>{calls++;return Task.FromResult(new ShutdownResult(ShutdownOutcome.Success));}); Equal(0,calls); });
		Test("UAC取消不再次呼叫執行器", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var c=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; int calls=0; var result=await f.RunQueued(c,_=>{calls++;return Task.FromResult(ShutdownCommand.FromStartError(1223,"cancel"));}); Equal(1,calls); Equal(ShutdownOutcome.UacCancelled,result.Outcome); True(f.IsDue(new DateTime(2026,10,18,17,30,1))); });
		Test("順延舊版本不改新目標", () => { var f=NewFlow(); var t=f.Target(new DateTime(2026,10,17,17,30,0)); var v=f.Version; f.Update(true,false,new DateTime(2026,10,17,17,31,0),18,0,0,true); DateTime before=f.Target(new DateTime(2026,10,17,17,31,0)); False(f.TryPostpone(v,t,new DateTime(2026,10,17,17,31,0))); Equal(before,f.Target(new DateTime(2026,10,17,17,31,0))); });
		Test("舊提醒失敗不能跳過新目標", () => { var f=NewFlow(); var t=f.Target(new DateTime(2026,10,17,17,30,0)); var v=f.Version; f.Update(true,false,new DateTime(2026,10,17,17,31,0),18,0,0,true); False(f.TrySkip(v,t,new DateTime(2026,10,17,17,32,0))); Equal(new DateTime(2026,10,17,18,0,0),f.Target(new DateTime(2026,10,17,17,32,0))); });
		Test("昨日提醒不能提交今日排程", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,7,0,0),8,0,0,true,false); var old=f.Target(new DateTime(2026,10,17,8,0,0)); Null(f.TryCommit(f.Version,old,new DateTime(2026,10,18,9,0,0))); Equal(new DateTime(2026,10,18,8,0,0),f.Target(new DateTime(2026,10,18,9,0,0))); True(f.IsDue(new DateTime(2026,10,18,9,0,0))); });
		Test("昨日提醒不能順延今日排程", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,7,0,0),8,0,0,true,false); var old=f.Target(new DateTime(2026,10,17,8,0,0)); False(f.TryPostpone(f.Version,old,new DateTime(2026,10,18,9,0,0))); Equal(new DateTime(2026,10,18,8,0,0),f.Target(new DateTime(2026,10,18,9,0,0))); True(f.IsDue(new DateTime(2026,10,18,9,0,0))); });
		Test("昨日提醒不能略過今日排程", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,7,0,0),8,0,0,true,false); var old=f.Target(new DateTime(2026,10,17,8,0,0)); False(f.TrySkip(f.Version,old,new DateTime(2026,10,18,9,0,0))); Equal(new DateTime(2026,10,18,8,0,0),f.Target(new DateTime(2026,10,18,9,0,0))); True(f.IsDue(new DateTime(2026,10,18,9,0,0))); });
		Test("昨日提醒在今日排程未到時取消", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,7,0,0),8,0,0,true,false); var old=f.Target(new DateTime(2026,10,17,8,0,0)); Null(f.TryCommit(f.Version,old,new DateTime(2026,10,18,7,0,0))); Equal(new DateTime(2026,10,18,8,0,0),f.Target(new DateTime(2026,10,18,7,0,0))); False(f.IsDue(new DateTime(2026,10,18,7,0,0))); });
		Test("順延跨日目標不被昨日提醒清除", () => { var f=new ShutdownFlow(new DateTime(2026,10,17,23,58,0),23,59,0,true,false); var old=f.Target(new DateTime(2026,10,17,23,59,0)); True(f.TryPostpone(f.Version,old,new DateTime(2026,10,17,23,59,50))); Equal(new DateTime(2026,10,18,0,4,50),f.Target(new DateTime(2026,10,18,0,1,0))); });
		Test("失敗結果保留下一日", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var c=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; var r=await f.RunQueued(c,_=>Task.FromResult(ShutdownCommand.FromExitCode(5))); Equal(ShutdownOutcome.NonzeroExit,r.Outcome); True(f.IsDue(new DateTime(2026,10,18,17,30,1))); });
		Test("UAC取消結果分類", () => Equal(ShutdownOutcome.UacCancelled,ShutdownCommand.FromStartError(1223,"cancel").Outcome));
		Test("Force命令參數差異", () => { Equal("/s /t 0",ShutdownCommand.Arguments(false)); Equal("/s /f /t 0",ShutdownCommand.Arguments(true)); });
		Test("程序啟動資訊不提權且Force參數正確", () => { var normal=ShutdownCommand.BuildStartInfo(false); var force=ShutdownCommand.BuildStartInfo(true); Equal("/s /t 0",normal.Arguments); Equal("/s /f /t 0",force.Arguments); False(normal.UseShellExecute); False(force.UseShellExecute); True(normal.CreateNoWindow && force.CreateNoWindow); Equal("",normal.Verb); Equal(ShutdownCommand.ExecutablePath,normal.FileName); });
		Test("AccessDenied只呼叫一次且不提權fallback", async () => { int calls=0; ProcessStartInfo? seen=null; var result=await ShutdownCommand.Execute(false,info=>{calls++;seen=info;throw new Win32Exception(5);}); Equal(1,calls); Equal(ShutdownOutcome.StartFailure,result.Outcome); False(seen!.UseShellExecute); Equal("",seen.Verb); });
		Test("UAC取消分類且不重試", async () => { int calls=0; var result=await ShutdownCommand.Execute(true,info=>{calls++;throw new Win32Exception(1223);}); Equal(1,calls); Equal(ShutdownOutcome.UacCancelled,result.Outcome); });
		Test("非零exit code由共用adapter消費", async () => { int calls=0; var result=await ShutdownCommand.Execute(false,info=>{calls++;return Task.FromResult(42);}); Equal(1,calls); Equal(ShutdownOutcome.NonzeroExit,result.Outcome); Equal(42,result.ExitCode); });
		Test("共用gate不等待程序退出", async () => { var f=DueFlow(); var t=f.Target(new DateTime(2026,10,17,17,31,0)); var commit=f.TryCommit(f.Version,t,new DateTime(2026,10,17,17,31,0))!; var pending=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously); int starts=0; var resultTask=f.RunQueued(commit,force=>ShutdownCommand.Execute(force,info=>{starts++;return pending.Task;})); f.Update(true,false,new DateTime(2026,10,17,17,31,1),17,30,0,false); Equal(1,starts); pending.SetResult(0); Equal(ShutdownOutcome.Success,(await resultTask).Outcome); });
		Test("安全CLI輸出UTF8且無handle不崩潰", () => { using var stream=new MemoryStream(); True(SafeCliOutput.WriteUtf8(stream,"繁體中文")); string bytes=Encoding.UTF8.GetString(stream.ToArray()); True(bytes.StartsWith("繁體中文",StringComparison.Ordinal)); False(SafeCliOutput.WriteUtf8(null,"safe")); using var bad=new ThrowingStream(); False(SafeCliOutput.WriteUtf8(bad,"safe")); });
		Test("非法時間被拒", () => Throws(()=>ShutdownSchedule.ValidateTime(24,0,0)));
		Test("壞設定保留原文且停用", () => { string dir=Path.Combine(Path.GetTempPath(),"ontime-selftest-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string path=Path.Combine(dir,"config.json"); File.WriteAllText(path,"{ bad"); var r=ShutdownConfigStore.Load(path); False(r.IsValid); False(r.Config.EnableAutoShutdown); Equal("{ bad",File.ReadAllText(path)); Directory.Delete(dir,true); });
		Test("值域外設定保留原文且停用", () => { string dir=Path.Combine(Path.GetTempPath(),"ontime-selftest-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir); string path=Path.Combine(dir,"config.json"); const string json="{\"Hour\":25,\"Minute\":0,\"Second\":0}"; File.WriteAllText(path,json); var r=ShutdownConfigStore.Load(path); False(r.IsValid); False(r.Config.EnableAutoShutdown); Equal(json,File.ReadAllText(path)); Directory.Delete(dir,true); });
		Test("儲存失敗回報且不改runtime", () => { var c=new ShutdownConfig { Hour=21, EnableAutoShutdown=true }; var before=c.Hour; string blocker=Path.Combine(Path.GetTempPath(),"blocker-"+Guid.NewGuid().ToString("N")); File.WriteAllText(blocker,"x"); try { Throws(()=>ShutdownConfigStore.Save(Path.Combine(blocker,"c.json"),c)); Equal(before,c.Hour); } finally { File.Delete(blocker); } });
		Test("儲存失敗後tooltip反映runtime", () => { var c=new ShutdownConfig { EnableAutoShutdown=false }; Equal("自動關機工具 - 自動關機已停用",ShutdownStatus.Tooltip(c.EnableAutoShutdown,new DateTime(2026,10,17,17,30,0))); c.EnableAutoShutdown=true; Equal("自動關機工具 - 下次關機：17:30:00",ShutdownStatus.Tooltip(c.EnableAutoShutdown,new DateTime(2026,10,17,17,30,0))); });
		writeLine($"GREEN：{cases - failures}/{cases} cases passed.");
		if(failures != 0) throw new InvalidOperationException($"{failures} of {cases} regression cases failed.");
	}
	private static ShutdownFlow NewFlow() => new(new DateTime(2026,10,17,17,0,0),17,30,0,true,false);
	private static ShutdownFlow DueFlow() => new(new DateTime(2026,10,17,17,0,0),17,30,0,true,true);
	private static void Test(string name, Action check) { cases++; try { check(); writeLine($"PASS {name}"); } catch(Exception ex) { failures++; writeLine($"FAIL {name}: {ex.Message}"); } }
	private static void Test(string name, Func<Task> check) { cases++; try { check().GetAwaiter().GetResult(); writeLine($"PASS {name}"); } catch(Exception ex) { failures++; writeLine($"FAIL {name}: {ex.Message}"); } }
	private static void True(bool x) { if(!x) throw new Exception("預期 true"); }
	private static void False(bool x) => True(!x);
	private static void Null(object? x) { if(x != null) throw new Exception("預期 null"); }
	private static void Equal<T>(T a,T b) { if(!Equals(a,b)) throw new Exception($"預期 {a}，實際 {b}"); }
	private static void Throws(Action x) { try { x(); } catch { return; } throw new Exception("預期例外"); }
	private sealed class ThrowingStream : MemoryStream { public override void Write(byte[] buffer,int offset,int count) => throw new IOException("no handle"); public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("no handle"); }
}
