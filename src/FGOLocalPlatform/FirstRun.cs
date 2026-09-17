using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

/// <summary>
/// Everything that has to happen once before a fresh install can be played: the English patch,
/// the folder and environment checks, a first account with a full roster, and the launch
/// defaults. Each step is skipped when it has already been done, so later starts do nothing.
/// </summary>
internal sealed class FirstRun
{
	internal sealed record ToolResult(bool Ok, string Message, JsonObject? Root);

	private static readonly string[] UpgradeActions = new string[7] { "servants", "craft-essences", "materials", "levels", "bond", "costumes", "quests" };

	/// <summary>
	/// The three programs that listen or connect on the cabinet network. Without a rule for each,
	/// Windows asks about them the first time the game runs, and the question can open behind the
	/// game window.
	/// </summary>
	private static readonly (string Name, string Program, string Description)[] FirewallPrograms = new (string, string, string)[3]
	{
		("FGOAC scooby server", "Server\\python\\python.exe", "เซิร์ฟเวอร์ในเครื่อง"),
		("FGOAC scooby game", "App\\ago.exe", "เกม"),
		("FGOAC scooby service", "App\\am\\amdaemon.exe", "บริการตู้เกม")
	};

	/// <summary>
	/// Rule names used before the launcher was renamed. Deleting them keeps an install that ran
	/// 1.0.1 from carrying two rules per program.
	/// </summary>
	private static readonly string[] RetiredFirewallRules = new string[3] { "FGOA scooby server", "FGOA scooby game", "FGOA scooby service" };

	private readonly Window owner;

	private readonly Func<string[], Task<ToolResult>> accountTool;

	private readonly Action<string> report;

	private readonly Action<string> log;

	private readonly List<string> summary = new List<string>();

	private readonly string installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));

	internal FirstRun(Window owner, Func<string[], Task<ToolResult>> accountTool, Action<string> report, Action<string> log)
	{
		this.owner = owner;
		this.accountTool = accountTool;
		this.report = report;
		this.log = log;
	}

	/// <summary>
	/// True when this start found no account, which is what a brand-new install looks like.
	/// </summary>
	internal bool FreshInstall { get; private set; }

	private string MarkerPath => Path.Combine(GamePaths.GameRoot, "zh", "th-patch.json");

	private string ScriptPath => Path.Combine(installRoot, "Apply-EN-Patch.ps1");

	private string ManifestPath => Path.Combine(installRoot, "manifest.json");

	private string LauncherSettingsPath => Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");

	/// <summary>
	/// Returns true when something was changed, so the caller can reload what it shows.
	/// </summary>
	internal async Task<bool> RunAsync()
	{
		bool patched = await ApplyPatchAsync();
		report("กำลังตรวจสอบบัญชีของคุณ...");
		ToolResult accounts = await accountTool(new string[2] { "list", "--json" });
		bool needsAccount = NeedsFirstAccount(accounts);
		FreshInstall = needsAccount;
		bool firstTime = patched || needsAccount;
		if (firstTime)
		{
			await CheckEnvironmentAsync();
		}
		await EnsureFirewallRulesAsync(firstTime);
		bool accountCreated = needsAccount && await CreateFirstAccountAsync(accounts);
		bool defaultsWritten = ApplyMissingDefaults();
		if (summary.Count == 0)
		{
			return patched || accountCreated || defaultsWritten;
		}
		report("ทุกอย่างพร้อมแล้ว - กดเล่นได้เลยเมื่อคุณพร้อม");
		// The setup can take a few minutes, so the player may well be looking at something else:
		// this one has to come to the front and be findable in the task bar.
		ThemedMessageBox.Show(owner, "FGOAC scooby พร้อมใช้งานแล้ว" + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, summary) + Environment.NewLine + Environment.NewLine + "กดเล่นเพื่อเริ่มเกม Windows จะขอสิทธิ์หนึ่งครั้ง เพราะเกมต้องทำงานด้วยสิทธิ์ผู้ดูแลระบบ", "FGOAC scooby - เริ่มใช้งานครั้งแรก", MessageBoxButton.OK, MessageBoxImage.Asterisk, MessageBoxResult.OK, foreground: true);
		return true;
	}

	private async Task<bool> ApplyPatchAsync()
	{
		if (!File.Exists(ScriptPath) || !File.Exists(ManifestPath))
		{
			return false;
		}
		string version = "";
		string manifestHash = "";
		JsonObject files = new JsonObject();
		try
		{
			if (JsonNode.Parse(File.ReadAllText(ManifestPath)) is JsonObject manifest)
			{
				version = manifest["version"]?.GetValue<string>() ?? "";
				manifestHash = manifest["manifestHash"]?.GetValue<string>() ?? "";
				files = manifest["files"] as JsonObject ?? files;
			}
		}
		catch (Exception ex)
		{
			log("The English patch manifest could not be read: " + ex.Message);
			return false;
		}
		if (version.Length == 0 || manifestHash.Length == 0)
		{
			log("The English patch manifest has no version, so the patch was skipped.");
			return false;
		}
		if (IsPatchInstalled(version, manifestHash, files))
		{
			return false;
		}
		report("กำลังติดตั้งแพตช์ภาษาไทย - ครั้งแรกจะใช้เวลาสักครู่");
		log("Applying the English patch version " + version);
		StringBuilder transcript = new StringBuilder();
		int exitCode;
		try
		{
			exitCode = await RunPatchScriptAsync(transcript);
		}
		catch (Exception ex2)
		{
			log("The English patch could not be started: " + ex2.Message);
			summary.Add("- เริ่มติดตั้งแพตช์ภาษาไทยไม่ได้: " + ex2.Message);
			return false;
		}
		log(transcript.ToString());
		string lastLine = LastLine(transcript.ToString());
		if (exitCode != 0)
		{
			report("ไม่ได้ติดตั้งแพตช์ภาษาไทย");
			summary.Add("- ไม่ได้ติดตั้งแพตช์ภาษาไทย " + lastLine);
			return false;
		}
		summary.Add("- ติดตั้งแพตช์ภาษาไทยเวอร์ชัน " + version + " แล้ว");
		return true;
	}

	private bool IsPatchInstalled(string version, string manifestHash, JsonObject files)
	{
		try
		{
			if (!(File.Exists(MarkerPath) && JsonNode.Parse(File.ReadAllText(MarkerPath)) is JsonObject marker))
			{
				return false;
			}
			if (marker["version"]?.GetValue<string>() != version || marker["manifestHash"]?.GetValue<string>() != manifestHash)
			{
				return false;
			}
		}
		catch (Exception ex)
		{
			log("The patch marker could not be read, so the patch is applied again: " + ex.Message);
			return false;
		}
		// A platform update copies its own launch scripts, server tools and Chinese text over the
		// patched ones and leaves the marker alone, so the marker on its own is not proof. The
		// files outside App\zh\rom are few and small, and an update overwrites those too, so
		// checking them on every start is cheap and enough.
		foreach (KeyValuePair<string, JsonNode?> file in files)
		{
			if (file.Key.StartsWith("App\\zh\\rom\\", StringComparison.OrdinalIgnoreCase) || file.Key.Equals("FGOAC scooby.exe", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string path = Path.Combine(installRoot, file.Key);
			if (!File.Exists(path) || !string.Equals(Updater.HashFile(path), file.Value?.GetValue<string>(), StringComparison.OrdinalIgnoreCase))
			{
				log("The English patch version " + version + " is recorded as installed, but " + file.Key + " has been replaced since, usually by a platform update, so it is applied again.");
				return false;
			}
		}
		return true;
	}

	private async Task<int> RunPatchScriptAsync(StringBuilder transcript)
	{
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(installRoot, redirectOutput: true, new string[]
		{
			"-File", ScriptPath, "-InstallRoot", installRoot, "-PackageRoot", installRoot, "-NonInteractive",
			"-IgnoreProcessId", Environment.ProcessId.ToString()
		});
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("ตัวติดตั้งแพตช์ไม่เริ่มทำงาน");
		Task<string> errorTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30.0));
		// The output loop below cannot take the token, so the deadline ends the process instead,
		// which closes the pipe and lets the loop finish.
		using CancellationTokenRegistration killOnTimeout = timeout.Token.Register(delegate
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
		});
		string? line;
		while ((line = await process.StandardOutput.ReadLineAsync()) != null)
		{
			transcript.AppendLine(line);
			if (!string.IsNullOrWhiteSpace(line))
			{
				report(line.Trim());
			}
		}
		string text = await errorTask;
		if (!string.IsNullOrWhiteSpace(text))
		{
			transcript.AppendLine(text);
		}
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw new IOException("ตัวติดตั้งแพตช์ใช้เวลานานเกิน 30 นาที จึงถูกหยุดการทำงาน");
		}
		return process.ExitCode;
	}

	private async Task CheckEnvironmentAsync()
	{
		report("กำลังตรวจสอบว่าเกมเขียนข้อมูลลงโฟลเดอร์ของตัวเองได้หรือไม่...");
		try
		{
			await Task.Run(StartupDiagnostics.CheckLayout);
		}
		catch (Exception ex)
		{
			log("The folder check failed: " + ex.Message);
			summary.Add("- โฟลเดอร์ที่เกมต้องเขียนข้อมูลยังถูกล็อกอยู่ " + StartupDiagnostics.Explain(4));
			return;
		}
		report("กำลังเรียกใช้การตรวจสอบสภาพแวดล้อม...");
		string text;
		try
		{
			text = await RuntimeDiagnostics.CheckAsync();
		}
		catch (Exception ex2)
		{
			log("The environment check could not run: " + ex2.Message);
			summary.Add("- เรียกใช้การตรวจสอบสภาพแวดล้อมไม่ได้: " + ex2.Message);
			return;
		}
		log(text);
		string[] array = text.Split('\n').Select((string value) => value.Trim()).Where((string value) => value.StartsWith("[ACTION NEEDED]", StringComparison.Ordinal)).ToArray();
		if (array.Length == 0)
		{
			summary.Add("- การตรวจสอบสภาพแวดล้อม: ผ่านทั้งหมด");
			return;
		}
		string text2 = array[0].Substring("[ACTION NEEDED]".Length).Trim();
		int num = text2.IndexOf(':');
		if (num > 0)
		{
			text2 = text2.Substring(0, num);
		}
		summary.Add($"- การตรวจสอบสภาพแวดล้อม: มี {array.Length} รายการที่ต้องแก้ไข เริ่มจาก {text2} รายงานฉบับเต็มอยู่ที่หน้าขั้นสูง > การวินิจฉัยและความช่วยเหลือ");
	}

	/// <summary>
	/// Adds one inbound allow rule per program, so Windows never has to ask. Runs on every start,
	/// because a rule can be removed later; a rule that is already there is left alone, and one
	/// that points somewhere else is replaced, which is what happens when the game folder moves.
	/// </summary>
	private async Task EnsureFirewallRulesAsync(bool announce)
	{
		List<string> created = new List<string>();
		List<string> failed = new List<string>();
		foreach (string retired in RetiredFirewallRules)
		{
			try
			{
				if ((await RunNetshAsync("advfirewall", "firewall", "show", "rule", "name=" + retired)).ExitCode == 0)
				{
					await RunNetshAsync("advfirewall", "firewall", "delete", "rule", "name=" + retired);
					log("Removed the Windows Firewall rule " + retired + ", left over from an earlier version.");
				}
			}
			catch (Exception ex)
			{
				log("The Windows Firewall rule " + retired + " could not be removed: " + ex.Message);
			}
		}
		foreach ((string Name, string Program, string Description) firewallProgram in FirewallPrograms)
		{
			string path = Path.Combine(installRoot, firewallProgram.Program);
			if (!File.Exists(path))
			{
				continue;
			}
			try
			{
				(int ExitCode, string Output) tuple = await RunNetshAsync("advfirewall", "firewall", "show", "rule", "name=" + firewallProgram.Name, "verbose");
				if (tuple.ExitCode == 0 && tuple.Output.Contains(path, StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				report("กำลังอนุญาต" + firewallProgram.Description + "ผ่าน Windows Firewall...");
				if (tuple.ExitCode == 0)
				{
					await RunNetshAsync("advfirewall", "firewall", "delete", "rule", "name=" + firewallProgram.Name);
				}
				(int ExitCode, string Output) tuple2 = await RunNetshAsync("advfirewall", "firewall", "add", "rule", "name=" + firewallProgram.Name, "dir=in", "action=allow", "program=" + path, "enable=yes", "profile=any");
				if (tuple2.ExitCode == 0)
				{
					created.Add(firewallProgram.Description);
					log("Added the Windows Firewall rule " + firewallProgram.Name + " for " + path);
				}
				else
				{
					failed.Add(firewallProgram.Description);
					log("The Windows Firewall rule " + firewallProgram.Name + " could not be added: " + tuple2.Output.Trim());
				}
			}
			catch (Exception ex)
			{
				failed.Add(firewallProgram.Description);
				log("The Windows Firewall rule " + firewallProgram.Name + " could not be added: " + ex.Message);
			}
		}
		if (!announce)
		{
			return;
		}
		if (failed.Count > 0)
		{
			summary.Add("- ตั้งค่า Windows Firewall สำหรับ" + string.Join("และ", failed) + "ไม่สำเร็จ ครั้งแรกที่คุณกดเล่น Windows จะถามว่าจะอนุญาตให้ผ่านหรือไม่ - ให้ตอบตกลง คำถามนั้นอาจเปิดขึ้นด้านหลังหน้าต่างเกม จึงควรมองหาในแถบงานด้วย");
		}
		else if (created.Count > 0)
		{
			summary.Add("- อนุญาตให้ " + string.Join(", ", created) + " ผ่าน Windows Firewall แล้ว Windows จะได้ไม่ขัดจังหวะตอนคุณกดเล่น");
		}
	}

	private static async Task<(int ExitCode, string Output)> RunNetshAsync(params string[] arguments)
	{
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "netsh.exe"))
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		foreach (string item in arguments)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Windows Firewall could not be reached.");
		Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
		Task<string> errorTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			throw new IOException("Windows Firewall did not answer within 20 seconds.");
		}
		return (process.ExitCode, await outputTask + await errorTask);
	}

	private bool NeedsFirstAccount(ToolResult accounts)
	{
		if (!accounts.Ok || accounts.Root == null)
		{
			log("The account list could not be read during the first run: " + accounts.Message);
			return false;
		}
		return !(accounts.Root["accounts"] is JsonArray existing) || existing.Count == 0;
	}

	private async Task<bool> CreateFirstAccountAsync(ToolResult accounts)
	{
		if (accounts.Root == null)
		{
			return false;
		}
		if (accounts.Root["server_running"]?.GetValue<bool>() == true)
		{
			summary.Add("- ยังไม่ได้สร้างบัญชี เพราะเซิร์ฟเวอร์ในเครื่องกำลังทำงานอยู่ ให้หยุดเซิร์ฟเวอร์ในหน้าเล่น แล้วใช้ปุ่มบัญชีใหม่ในหน้าบัญชี");
			return false;
		}
		report("กำลังสร้างบัญชี Master...");
		ToolResult toolResult2 = await accountTool(new string[6] { "create", "--name", "Master", "--mode", "normal", "--json" });
		if (!toolResult2.Ok || toolResult2.Root == null)
		{
			log("The first account could not be created: " + toolResult2.Message);
			summary.Add("- ยังไม่ได้สร้างบัญชี: " + toolResult2.Message + " ให้ใช้ปุ่มบัญชีใหม่ในหน้าบัญชี");
			return false;
		}
		int aimeId = (toolResult2.Root["aime_id"]?.GetValue<int>()).GetValueOrDefault();
		string text = toolResult2.Root["access_code"]?.GetValue<string>() ?? "";
		log($"Created the first account Master (ID {aimeId}), access code {text}");
		List<string> list = new List<string>();
		foreach (string action in UpgradeActions)
		{
			report("กำลังเติมข้อมูลบัญชี: " + action.Replace('-', ' ') + "...");
			ToolResult toolResult3 = await accountTool(new string[6] { "upgrade", "--aime-id", aimeId.ToString(), "--action", action, "--json" });
			if (!toolResult3.Ok)
			{
				list.Add(action);
				log($"The {action} step of the one-click inventory failed: {toolResult3.Message}");
			}
		}
		report("กำลังเลือกบัญชี...");
		ToolResult toolResult4 = await accountTool(new string[4] { "use", "--aime-id", aimeId.ToString(), "--json" });
		if (!toolResult4.Ok)
		{
			log("The first account could not be selected: " + toolResult4.Message);
			summary.Add($"- สร้างบัญชี Master (ID {aimeId}) แล้ว แต่ยังไม่ได้เลือกใช้ ให้เลือกบัญชีนี้ในหน้าบัญชี แล้วคลิกใช้บัญชีที่เลือก");
			return true;
		}
		string text2 = ((list.Count == 0) ? "" : $" ขั้นตอน {string.Join(", ", list)} ของคลังไอเทมและการพัฒนาแบบคลิกเดียวยังทำไม่สำเร็จ - คุณเรียกใช้อีกครั้งได้ในหน้าบัญชี");
		summary.Add($"- สร้างบัญชี Master (ID {aimeId}) พร้อมชุดเซอร์แวนต์และ Craft Essence ครบทั้งหมด และเลือกใช้บัญชีนี้แล้ว รหัสเข้าใช้งานคือ {text}{text2}");
		return true;
	}

	private bool ApplyMissingDefaults()
	{
		try
		{
			JsonObject jsonObject = ((File.Exists(LauncherSettingsPath) && JsonNode.Parse(File.ReadAllText(LauncherSettingsPath)) is JsonObject settings) ? settings : new JsonObject());
			List<string> list = new List<string>();
			if (jsonObject["displayMode"] == null && jsonObject["windowed"] == null)
			{
				jsonObject["displayMode"] = "windowed";
				jsonObject["windowed"] = true;
				list.Add("windowed");
			}
			if (jsonObject["resolutionWidth"] == null && jsonObject["resolutionHeight"] == null)
			{
				jsonObject["resolutionWidth"] = 1280;
				jsonObject["resolutionHeight"] = 720;
				list.Add("1280x720");
			}
			if (jsonObject["inputMode"] == null)
			{
				jsonObject["inputMode"] = "keyboard";
				list.Add("การควบคุมด้วยคีย์บอร์ด");
			}
			if (string.IsNullOrWhiteSpace(jsonObject["monitorDevice"]?.GetValue<string>()))
			{
				string primaryMonitorDevice = GetPrimaryMonitorDevice();
				if (primaryMonitorDevice.Length > 0)
				{
					jsonObject["monitorDevice"] = primaryMonitorDevice;
					list.Add("จอหลักของคุณ");
				}
			}
			if (jsonObject["gpuCompat"] == null && GpuCompat.SourceAvailable && !GpuCompat.HasNvidiaAdapter())
			{
				GpuCompat.Apply(enabled: true);
				jsonObject["gpuCompat"] = true;
				list.Add("เลเยอร์ความเข้ากันได้สำหรับ AMD และ Intel");
			}
			if (list.Count == 0)
			{
				return false;
			}
			AtomicFile.WriteAllText(LauncherSettingsPath, jsonObject.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
			log("First-run defaults written: " + string.Join(", ", list));
			summary.Add("- ตั้งค่าส่วนที่คุณยังไม่ได้เลือกไว้ให้แล้ว: " + string.Join(", ", list) + " เปลี่ยนได้ทุกเมื่อในหน้าการตั้งค่า");
			return true;
		}
		catch (Exception ex)
		{
			log("The first-run defaults could not be written: " + ex.Message);
			return false;
		}
	}

	private static string GetPrimaryMonitorDevice()
	{
		IReadOnlyList<DisplayMonitor.Entry> connected = DisplayMonitor.GetConnected();
		DisplayMonitor.Entry entry = connected.FirstOrDefault((DisplayMonitor.Entry candidate) => candidate.Label.Contains("(primary)", StringComparison.OrdinalIgnoreCase));
		if (entry != null)
		{
			return entry.Device;
		}
		if (connected.Count <= 0)
		{
			return "";
		}
		return connected[0].Device;
	}

	private static string LastLine(string text)
	{
		string[] source = text.Split('\n').Select((string value) => value.Trim()).Where((string value) => value.Length > 0).ToArray();
		if (source.Length == 0)
		{
			return "";
		}
		return source[^1];
	}
}
