using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class StartupDiagnostics
{
	public const string PlatformUpdateNeeded = "โฟลเดอร์เกมนี้ยังไม่ได้ติดตั้งอัปเดต V1.01 ของ Cloud23333: ไม่พบ App\\FGO_Runtime.dll ซึ่งสคริปต์เริ่มเกมและสคริปต์เซิร์ฟเวอร์ของแพตช์นี้จำเป็นต้องใช้ ให้ติดตั้งอัปเดต V1.01 หรือ V1.02 ของเขาลงในโฟลเดอร์เกมก่อน แล้วจึงเปิด FGOAC scooby อีกครั้ง";

	public const string PlatformHalfUpdated = "โฟลเดอร์เกมนี้มีไฟล์อัปเดตของ Cloud23333 อยู่ แต่ App\\fgohook.dll ยังเป็นไฟล์เดิมของเวอร์ชัน 11.00 แสดงว่าการอัปเดตยังไม่สมบูรณ์ และเกมจะหยุดทำงานตอนเริ่ม ให้แตกไฟล์อัปเดต V1.02 ของเขาทับลงในโฟลเดอร์เกมอีกครั้งโดยให้เขียนทับทุกไฟล์ แล้วจึงเปิด FGOAC scooby อีกครั้ง";

	/// <summary>App\fgohook.dll as shipped in the original 11.00 package, before Cloud23333's V1.01.</summary>
	private const string OriginalHookSha256 = "75cb6a90b6bf36286ad69f177dc4fae39072fea6cdab3e562778768ef6071077";

	public const string GameErrors = "ERROR 4102 - The game stopped. Most often it could not reach the local server, because the server is not running, its ports are already in use, or the server address in Settings is wrong: start the server from the launcher, then click Run Environment Check on this page. If the computer name is the same as the Windows user name, or the game log names NamedPipe.cpp or an invalid named-account owner, the game files are older than Cloud23333's V1.02 update, which fixes that crash: apply his latest update in full. Renaming the Windows account or moving the game to drive C: is not needed.\n\nERROR 8404 - The game's own Startup Mode was saved as Satellite (Sub Unit), so it waits for a main unit that does not exist. Turn on Operator menu keys (F1, F2) on the Controls page and press Play, then on the error screen press F1 to open the Game Test Menu; F2 moves the arrow and F1 confirms. Open Game Settings, set Startup Mode to Main Unit, then choose Exit. The next boot goes to the title screen.\n\nCannot use Aime card, at the title screen - the game's first message to the local server timed out on that boot. Close the game, check that the server shows ready on the Play page, and press Play again.\n\n0x80131515 at Play, or the server failing with a message about FGO_Runtime.dll - Windows marked App\\FGO_Runtime.dll as downloaded from the internet, and PowerShell refuses to load a file with that mark. The launcher clears the mark itself when it starts; if it comes back, right-click the file, open Properties and tick Unblock.\n\nERROR 4104 - The game is installed on drive E: or Y:, and the game's own file hook redirects every E: path to its data mount, so it cannot open its resource files. Move the whole install folder to another drive, such as D:, and start it from there.\n\nERROR 4105 - The game was not started as administrator, so it could not create the registry keys it needs and the server rejected its first play record about 90 seconds after boot. Close the game and start the launcher as administrator.\n\n0xC0000005, the game closing a few seconds after launch - Windows Defender Controlled Folder Access is almost always blocking the game from writing its files. Open Windows Security > Virus & threat protection > Ransomware protection and allow the game's executable, or turn Controlled Folder Access off.\n\nNo sound, or 0x88890004 / 0x88890026 - Windows dropped the game's audio stream, usually because the default output device changed or is muted. Set the headphones or speakers you want as the Windows default output and unmute them in the volume mixer, then start the game again.\n\n0x88890010 - The Windows Audio service is not running. Start the Windows Audio service, then launch the game again.\n\nA black screen or a display driver error - the game is built for NVIDIA cards and needs a current driver. Update your graphics driver, then launch the game again.\n\nERROR 6401 - usually seen with a controller or other USB device plugged in. Turn off USB selective suspend in the Windows power plan, then start the game again.\n\nExit code 22 with 0xC000001D - the CPU lacks the F16C instruction set the game uses (Pentium and Celeron before the 12th generation, Intel Core before the 3rd). The game cannot run on that CPU.\n\nThe game crashes when the first battle loads on an AMD card - the graphics layer earlier FGOAC scooby versions installed fails to build the game's shaders on RX 500, RX 6000 and RX 7600 cards. Cloud23333's latest update brings his own layer for AMD and Intel cards: apply it in full, then press Play.";

	/// <summary>The meaning of a launch script exit code, as the launch script of this game folder uses it.</summary>
	public static string Explain(int code)
	{
		return Explain(code, GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."))));
	}

	/// <summary>The meaning of a launch script exit code: on <paramref name="game1100"/> FGOAC_Launcher.ps1's, else Cloud23333's FGO_Launcher.ps1's.</summary>
	public static string Explain(int code, bool game1100)
	{
		return code switch
		{
			2 => "ไฟล์ที่เกมต้องใช้บางไฟล์หายไป ให้แตกแพ็กเกจ FGOAC scooby ลงในโฟลเดอร์เกมโดยตรง คือโฟลเดอร์ที่มี App และ Server เพื่อให้ FGOAC scooby.exe อยู่ข้างกัน ตรวจสอบว่าได้ติดตั้งอัปเดตของ Cloud23333 แล้ว และดูว่าโปรแกรมป้องกันไวรัสกักกันไฟล์ใดไว้หรือไม่",
			3 => "ตัวเรียกเกมหาการ์ดเครือข่ายสำหรับเครือข่ายตู้เกมไม่พบ ให้ตั้งที่อยู่เซิร์ฟเวอร์เป็น auto ในการตั้งค่า และติดตั้งโฟลเดอร์ App จากแพ็กเกจใหม่อีกครั้ง เพื่อให้ FGO_Launcher.ps1 กับ fgohook.dll ตรงรุ่นกัน โหมด auto ไม่ต้องใช้การเชื่อมต่ออินเทอร์เน็ต", 
			4 => "โฟลเดอร์ที่เกมต้องเขียนข้อมูลถูกล็อกอยู่ ให้เปิดตัวเรียกเกมในฐานะผู้ดูแลระบบ แล้วตรวจสอบพาธที่แสดงด้านล่างว่ามีพื้นที่ว่างพอหรือไม่ ถูกตั้งเป็นอ่านอย่างเดียวหรือไม่ หรือถูกโปรแกรมป้องกันไวรัสบล็อกโฟลเดอร์ไว้หรือไม่", 
			5 => "Windows ไม่มีอุปกรณ์ส่งออกเสียงที่เปิดใช้งานอยู่ ให้เปิดใช้งานลำโพงหรือหูฟังใน Windows และตั้งเป็นอุปกรณ์ส่งออกเสียงเริ่มต้น แล้วเริ่มเกมอีกครั้ง", 
			10 => "เซิร์ฟเวอร์ในเครื่องเริ่มทำงานไม่ได้ มักเกิดจากพอร์ตถูกใช้งานอยู่แล้ว ฐานข้อมูลล้มเหลว หรือมีเซิร์ฟเวอร์รุ่นเก่ายังทำงานค้างอยู่ ให้อ่านข้อผิดพลาดด้านล่าง แล้วตรวจสอบ logs/server-control.log, artemis-stderr.log และ mariadb.log", 
			11 => "เชื่อมต่อพอร์ตของเซิร์ฟเวอร์ไม่ได้ หากใช้เครื่องเดียวให้ตั้งที่อยู่เซิร์ฟเวอร์เป็น auto แล้วเริ่มเซิร์ฟเวอร์ในเครื่อง หากใช้เซิร์ฟเวอร์ระยะไกลให้ตรวจสอบที่อยู่และพอร์ตที่กรอก หากใช้เซิร์ฟเวอร์ชุมชนให้เริ่มเกมด้วย Play Online ในตัวเรียกเกม ซึ่งจะจัดการการเชื่อมต่อให้", 
			12 => "ไม่พบ FGOAudio.dll ซึ่งเป็นฮุกเสียงแบบเลือกติดตั้ง สคริปต์เริ่มเกมถูกเรียกด้วย -EnableExperimentalAudio ให้เริ่มเกมด้วยปุ่มเล่นแทน เพราะเกมมีเสียงแบบแชร์ของตัวเองอยู่แล้วและไม่ต้องใช้ไฟล์นั้น", 
			13 => "ไฟล์ตั้งค่าไฟล์ใดไฟล์หนึ่งไม่ใช่ JSON ที่ถูกต้อง หรือ gameVersion ใน App\\fgo-launcher.json ไม่ใช่หมายเลขเวอร์ชัน ให้ตรวจสอบ App\\fgo-launcher.json และ App\\config.json ว่าพิมพ์ผิดหรือไม่ แล้วกดเล่นอีกครั้ง", 
			14 => "จอภาพหรือค่าความละเอียดไม่ถูกต้อง ให้เลือกจอภาพและความกว้างกับความสูงที่ถูกต้องอีกครั้งที่การตั้งค่า > การแสดงผล แล้วบันทึก", 
			15 => "การตรวจสอบสภาพแวดล้อมไม่ผ่าน ให้แก้รายการที่แสดงด้านล่าง แล้วกดตรวจสอบสภาพแวดล้อมที่การตั้งค่า > การวินิจฉัยและความช่วยเหลือ เพื่อดูรายงานฉบับเต็ม", 
			16 => game1100 ? "เครื่องนี้ไม่มีการ์ดสำหรับเซิร์ฟเวอร์ที่ปุ่มเล่นใช้ หากใช้เซิร์ฟเวอร์บนเครื่องนี้ ให้เลือกบัญชีในเมนู Master ในหน้าเล่น หากใช้เซิร์ฟเวอร์ชุมชน ให้เลือก New account ในหน้า Online หรือ Recover account หากมีคีย์กู้คืน แล้วกดเล่นอีกครั้ง" : "ใช้การ์ดจอที่เลือกไว้สำหรับเกมไม่ได้ จึงหยุดเกมแทนที่จะรันบนการ์ดใบอื่น ที่การตั้งค่า > การแสดงผล ให้เลือกการ์ดใบอื่นหรืออัตโนมัติ แล้วกดบันทึกการตั้งค่าการแสดงผล หากเปิดเลเยอร์ความเข้ากันได้ของ AMD และ Intel อยู่ การบันทึกตรงนั้นจะล้างการเลือกการ์ด เพราะเลือกการ์ดควบคู่กันไม่ได้",
			22 => "เกมหยุดทำงานหรือปิดตัวเองไม่นานหลังเริ่มทำงาน ให้ตรวจสอบโฟลเดอร์ logs เพื่อดูความล้มเหลวครั้งล่าสุด รหัสออก และไฟล์ crash dump ถ้าหน้าต่างเกมเปิดแล้วปิดอีกครั้งพร้อมรหัส 0xC0000005 ทั้งที่การตรวจสอบสภาพแวดล้อมผ่าน ให้ลองใช้โหมดหน้าต่างที่ 1280x720 บนจอหลัก และเมื่อรายงานปัญหาให้แนบ logs\\ago-crash-*.dmp พร้อมรุ่นการ์ดจอและเวอร์ชันไดรเวอร์ของคุณ",
			_ => "สคริปต์เริ่มเกมทำงานไม่จบ ให้อ่านข้อผิดพลาดด้านล่างและ logs/fgo-last-launch.log ตัวเลขรหัสออกเพียงอย่างเดียวไม่ได้บอกสาเหตุ", 
		};
	}

	public static void CheckLayout()
	{
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		string runtime = Path.Combine(GamePaths.GameRoot, "FGO_Runtime.dll");
		if (!File.Exists(runtime))
		{
			throw new FileNotFoundException(PlatformUpdateNeeded, runtime);
		}
		string hook = Path.Combine(GamePaths.GameRoot, "fgohook.dll");
		if (File.Exists(hook) && string.Equals(Updater.HashFile(hook), OriginalHookSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException(PlatformHalfUpdated);
		}
		string text = Path.Combine(GamePaths.GameRoot, "FGOAC_StartupChecks.ps1");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException(Explain(2), text);
		}
		CheckFolders(text, fullPath, CommunityServer.OnlineSession(GamePaths.GameRoot));
	}

	/// <summary>
	/// Runs the folder check of FGOAC_StartupChecks.ps1 on the game folder; a failure throws IOException with the Status
	/// sentence, a blank line, and the check's own output.
	/// </summary>
	public static void CheckFolders(string checkScript, string installRoot, bool online)
	{
		// The check's message alone, unwrapped: the host's error view would fold it and add the script's own path.
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(installRoot, redirectOutput: true, new string[2] { "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; try { . $env:FGO_CHECK_SCRIPT; Test-FgoWritableLayout -InstallRoot $env:FGO_CHECK_ROOT" + (online ? " -CommunityServer" : "") + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }" });
		processStartInfo.Environment["FGO_CHECK_SCRIPT"] = checkScript;
		processStartInfo.Environment["FGO_CHECK_ROOT"] = installRoot;
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("เริ่มการตรวจสอบโฟลเดอร์ไม่ได้");
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(20.0)).GetAwaiter().GetResult();
		Task.WhenAll<string>(task, task2).WaitAsync(TimeSpan.FromSeconds(2.0)).GetAwaiter()
			.GetResult();
		if (process.ExitCode != 0)
		{
			string output = (task2.GetAwaiter().GetResult() + task.GetAwaiter().GetResult()).Trim();
			throw new IOException(FolderCheckProblem(output) + "\n\n" + output);
		}
	}

	/// <summary>
	/// The Status sentence for a failed folder check: the first path in its output, as the check names it or as a
	/// quoted or bare path, is the locked folder or file; without one the check itself failed.
	/// </summary>
	public static string FolderCheckProblem(string output)
	{
		Match path = Regex.Match(output, @"Cannot write to (?<path>[A-Za-z]:\\.+?)\. Run the launcher|'(?<path>[A-Za-z]:\\[^']+)'|(?<path>[A-Za-z]:\\[^\s'""]*)");
		return path.Success ? ("A folder or file the game must write to is locked: " + path.Groups["path"].Value.TrimEnd('.') + ". Start the launcher as administrator, then check it for free space, a read-only flag, or a block from your antivirus.") : "The check of the game folder did not finish. Open the logs for the reason, then start the launcher again.";
	}
}
