using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class RuntimeDiagnostics
{
	public static async Task<string> CheckAsync()
	{
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(GamePaths.GameRoot, redirectOutput: true, new string[2] { "-File", Path.Combine(GamePaths.GameRoot, "FGO_EnvironmentCheck.ps1") });
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("เริ่มการตรวจสอบสภาพแวดล้อมไม่ได้");
		Task<string> output = process.StandardOutput.ReadToEndAsync();
		Task<string> error = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			process.Kill(entireProcessTree: true);
			throw new IOException("การตรวจสอบสภาพแวดล้อมใช้เวลาเกิน 45 วินาที ตรวจดูว่าโปรแกรมป้องกันไวรัสบล็อก PowerShell หรือ Python ที่มากับแพ็กเกจอยู่หรือไม่ แล้วลองใหม่อีกครั้ง");
		}
		string text = $"Checked at {DateTime.Now:yyyy-MM-dd HH:mm:ss}\nScript host: {PowerShellHost.Executable}\n\n";
		string result = text + await output;
		string text2 = await error;
		if (!string.IsNullOrWhiteSpace(text2))
		{
			result = result + "\n" + text2;
		}
		Directory.CreateDirectory(GamePaths.LogsRoot);
		File.WriteAllText(Path.Combine(GamePaths.LogsRoot, "environment-check.txt"), result, Encoding.UTF8);
		return result;
	}
}
