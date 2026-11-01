using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool serverConfiguring;

	private bool stoppingServer;

	private CancellationTokenSource? serverCommandCancellation;

	private Task<int>? serverCommandTask;

	private Task? stopServerTask;

	private async void ServerSettings_OnConfigure(object? sender, EventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		if (Process.GetProcessesByName("ago").Length != 0 || launcherProcessRunning)
		{
			ThemedMessageBox.Show("ปิดเกมก่อนเปลี่ยนการตั้งค่าเซิร์ฟเวอร์", "การตั้งค่าเซิร์ฟเวอร์");
			return;
		}
		serverConfiguring = true;
		ServerSettingsPanel.IsEnabled = false;
		StartGameButton.IsEnabled = false;
		try
		{
			_ = 5;
			try
			{
				string[] arguments = ServerSettingsPanel.ApplyArguments();
				string[] obj = (string[])arguments.Clone();
				obj[0] = "validate";
				await ServerSettingsView.RunTool(obj);
				Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"));
				if (await RunServerCommandAsync(start: false) != 0)
				{
					throw new IOException("เซิร์ฟเวอร์ไม่ได้หยุดทำงาน การตั้งค่าจึงไม่ถูกเปลี่ยน - ดูที่ logs/server-control.log");
				}
				if (windowClosing)
				{
					goto end_IL_00b4;
				}
				await ServerSettingsView.RunTool(arguments);
				await ServerSettingsPanel.ReloadAsync();
				await RefreshRuntimeStatusAsync();
				await RefreshAccountsAsync(showErrors: false);
				ThemedMessageBox.Show("บันทึกการตั้งค่าเซิร์ฟเวอร์แล้ว เซิร์ฟเวอร์จะเริ่มทำงานเมื่อคุณคลิกปุ่มเริ่มเซิร์ฟเวอร์หรือปุ่มเล่น", "การตั้งค่าเซิร์ฟเวอร์");
				goto end_IL_0091;
				end_IL_00b4:;
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "การตั้งค่าเซิร์ฟเวอร์", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				goto end_IL_0091;
			}
			end_IL_0091:;
		}
		finally
		{
			serverConfiguring = false;
			ServerSettingsPanel.IsEnabled = true;
			await RefreshRuntimeStatusAsync();
		}
	}

	private async void StartServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "Start-FGOLocalServer.ps1"));
		if (!File.Exists(fullPath))
		{
			ThemedMessageBox.Show("ไม่พบสคริปต์เริ่มเซิร์ฟเวอร์:\n" + fullPath, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		serverConfiguring = true;
		RuntimeStatusText.Text = "กำลังเริ่มและตรวจสอบเซิร์ฟเวอร์ในเครื่อง...";
		try
		{
			if (await RunServerCommandAsync(start: true) != 0 && !windowClosing)
			{
				ThemedMessageBox.Show(StartupDiagnostics.Explain(10) + "\n\n" + ReadTail(Path.Combine(GamePaths.LogsRoot, "server-control.log")), "เซิร์ฟเวอร์ไม่เริ่มทำงาน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!windowClosing)
			{
				ThemedMessageBox.Show(ex2.Message, "เซิร์ฟเวอร์ไม่เริ่มทำงาน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			serverConfiguring = false;
			await RefreshRuntimeStatusAsync();
			if (!windowClosing)
			{
				await RefreshAccountsAsync(showErrors: false);
			}
		}
	}

	private async Task<int> RunServerCommandAsync(bool start)
	{
		if (serverCommandTask != null)
		{
			throw new InvalidOperationException("คำสั่งควบคุมเซิร์ฟเวอร์ก่อนหน้ายังทำงานไม่เสร็จ");
		}
		using CancellationTokenSource cancellation = new CancellationTokenSource();
		serverCommandCancellation = cancellation;
		Task<int> task = (serverCommandTask = ExecuteServerCommandAsync(start, cancellation.Token));
		try
		{
			return await task;
		}
		finally
		{
			serverCommandTask = null;
			serverCommandCancellation = null;
		}
	}

	private static async Task<int> ExecuteServerCommandAsync(bool start, CancellationToken cancellation)
	{
		string script = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", start ? "Start-FGOLocalServer.ps1" : "Stop-FGOLocalServer.ps1"));
		if (!File.Exists(script))
		{
			throw new FileNotFoundException("ไม่พบสคริปต์ควบคุมเซิร์ฟเวอร์", script);
		}
		await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0), cancellation);
		cancellation.ThrowIfCancellationRequested();
		CapturedProcess captured = StartCapturedPowerShellScript(script, delegate(string line, bool error)
		{
			AppendServerControlLog(line);
		});
		using (captured.Process)
		{
			try
			{
				await ProcessCompletion.WaitAsync(captured.Process, TimeSpan.FromSeconds(start ? 120 : 50), cancellation);
				await Task.WhenAny(captured.StreamsCompleted, Task.Delay(1500));
				return captured.Process.ExitCode;
			}
			catch (Exception ex)
			{
				AppendServerControlLog(ex.Message);
				throw;
			}
		}
	}

	private async Task CancelServerCommandAsync()
	{
		Task<int> task = serverCommandTask;
		if (task != null)
		{
			serverCommandCancellation?.Cancel();
			try
			{
				await task;
			}
			catch (Exception ex)
			{
				AppendServerControlLog("Server control cancelled: " + ex.Message);
			}
			await Task.Yield();
		}
	}

	private async Task StopLocalServerAsync()
	{
		// One stop at a time: a second caller joins the stop already under way. The shared task is
		// released here, after it settles, so a stop that fails at once cannot stay pinned.
		Task pending = stopServerTask;
		if (pending == null)
		{
			pending = StopLocalServerCoreAsync();
			stopServerTask = pending;
		}
		try
		{
			await pending;
		}
		finally
		{
			if (stopServerTask == pending)
			{
				stopServerTask = null;
			}
		}
	}

	private async Task StopLocalServerCoreAsync()
	{
		await CancelServerCommandAsync();
		if (await RunServerCommandAsync(start: false) != 0)
		{
			throw new IOException("เซิร์ฟเวอร์ในเครื่องหยุดทำงานไม่สมบูรณ์ - ดูที่ logs/server-control.log ระบบปล่อยให้ฐานข้อมูลทำงานต่อไว้ เพื่อไม่ให้การเขียนข้อมูลถูกตัดกลางคัน");
		}
	}

	private async void StopServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (stoppingServer || windowClosing)
		{
			return;
		}
		if (IsThisGameRunning() || launcherProcessRunning)
		{
			ThemedMessageBox.Show("หยุดเกมก่อนหยุดเซิร์ฟเวอร์ในเครื่อง", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (serverConfiguring && serverCommandTask == null)
			{
				return;
			}
			stoppingServer = true;
			RuntimeStatusText.Text = "กำลังหยุดเซิร์ฟเวอร์ในเครื่อง...";
			StartGameButton.IsEnabled = false;
			try
			{
				await StopLocalServerAsync();
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "เซิร์ฟเวอร์ไม่หยุดทำงาน", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
			finally
			{
				stoppingServer = false;
				await RefreshRuntimeStatusAsync();
				if (!windowClosing)
				{
					await RefreshAccountsAsync(showErrors: false);
				}
			}
		}
	}
}
