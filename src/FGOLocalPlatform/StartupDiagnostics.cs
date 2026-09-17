using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class StartupDiagnostics
{
	public const string PlatformUpdateNeeded = "โฟลเดอร์เกมนี้ยังไม่ได้ติดตั้งอัปเดต V1.01 ของ Cloud23333: ไม่พบ App\\FGO_Runtime.dll ซึ่งสคริปต์เริ่มเกมและสคริปต์เซิร์ฟเวอร์ของแพตช์นี้จำเป็นต้องใช้ ให้ติดตั้งอัปเดต V1.01 หรือ V1.02 ของเขาลงในโฟลเดอร์เกมก่อน แล้วจึงเปิด FGOAC scooby อีกครั้ง";

	public const string PlatformHalfUpdated = "โฟลเดอร์เกมนี้มีไฟล์อัปเดตของ Cloud23333 อยู่ แต่ App\\fgohook.dll ยังเป็นไฟล์เดิมของเวอร์ชัน 11.00 แสดงว่าการอัปเดตยังไม่สมบูรณ์ และเกมจะหยุดทำงานตอนเริ่ม ให้แตกไฟล์อัปเดต V1.02 ของเขาทับลงในโฟลเดอร์เกมอีกครั้งโดยให้เขียนทับทุกไฟล์ แล้วจึงเปิด FGOAC scooby อีกครั้ง";

	/// <summary>App\fgohook.dll as shipped in the original 11.00 package, before Cloud23333's V1.01.</summary>
	private const string OriginalHookSha256 = "75cb6a90b6bf36286ad69f177dc4fae39072fea6cdab3e562778768ef6071077";

	public const string GameErrors = "ERROR 4102 - เกมติดต่อเซิร์ฟเวอร์ในเครื่องไม่ได้ เพราะเซิร์ฟเวอร์ไม่ได้ทำงาน พอร์ตถูกใช้งานอยู่แล้ว หรือที่อยู่เซิร์ฟเวอร์ในการตั้งค่าไม่ถูกต้อง ให้เริ่มเซิร์ฟเวอร์จากตัวเรียกเกม แล้วคลิกเรียกใช้การตรวจสอบสภาพแวดล้อมในหน้านี้ บน V1.01 ของ Cloud23333 ข้อผิดพลาดเดียวกันนี้ยังเกิดขึ้นเมื่อชื่อคอมพิวเตอร์ตรงกับชื่อผู้ใช้ด้วย อัปเดต V1.02 ของเขาแก้ปัญหานี้แล้ว จึงควรอัปเดตเป็น V1.02\n\nERROR 8404 - ค่า Startup Mode ของเกมถูกบันทึกไว้เป็น Satellite (Sub Unit) เกมจึงรอเครื่องหลักที่ไม่มีอยู่จริง ที่หน้าจอข้อผิดพลาดให้กด F1 เพื่อเปิด Game Test Menu โดย F2 ใช้เลื่อนลูกศรและ F1 ใช้ยืนยัน จากนั้นเปิด Game Settings ตั้ง Startup Mode เป็น Main Unit แล้วเลือก Exit การเปิดครั้งถัดไปจะเข้าสู่หน้าจอไตเติล\n\nCannot use Aime card ที่หน้าจอไตเติล - ข้อความแรกที่เกมส่งไปยังเซิร์ฟเวอร์ในเครื่องหมดเวลาในการเปิดครั้งนั้น ให้ปิดเกม ตรวจดูว่าเซิร์ฟเวอร์ขึ้นสถานะพร้อมในหน้าเล่นเกม แล้วกดเล่นอีกครั้ง\n\n0x80131515 ตอนกดเล่น หรือเซิร์ฟเวอร์ล้มเหลวพร้อมข้อความเกี่ยวกับ FGO_Runtime.dll - Windows ทำเครื่องหมายว่า App\\FGO_Runtime.dll ถูกดาวน์โหลดมาจากอินเทอร์เน็ต และ PowerShell จะไม่ยอมโหลดไฟล์ที่มีเครื่องหมายนั้น ตัวเรียกเกมจะล้างเครื่องหมายนี้ให้เองตอนเริ่มทำงาน หากยังกลับมาอีก ให้คลิกขวาที่ไฟล์ เปิด Properties แล้วติ๊ก Unblock\n\nERROR 4104 - เกมถูกติดตั้งไว้บนไดรฟ์ E: หรือ Y: และฮุกไฟล์ของเกมเองจะเปลี่ยนเส้นทางทุกพาธที่ขึ้นต้นด้วย E: ไปยังจุดเมาต์ข้อมูลของมัน เกมจึงเปิดไฟล์ทรัพยากรไม่ได้ ให้ย้ายโฟลเดอร์ติดตั้งทั้งหมดไปไว้ที่ไดรฟ์อื่น เช่น D: แล้วเริ่มจากที่นั่น\n\nERROR 4105 - เกมไม่ได้ถูกเปิดในฐานะผู้ดูแลระบบ จึงสร้างคีย์รีจิสทรีที่จำเป็นไม่ได้ และเซิร์ฟเวอร์ปฏิเสธบันทึกการเล่นครั้งแรกหลังเปิดเครื่องประมาณ 90 วินาที ให้ปิดเกมแล้วเปิดตัวเรียกเกมในฐานะผู้ดูแลระบบ\n\n0xC0000005 เกมปิดตัวเองหลังเปิดไม่กี่วินาที - เกือบทุกครั้งเกิดจาก Controlled Folder Access ของ Windows Defender บล็อกไม่ให้เกมเขียนไฟล์ ให้เปิด Windows Security > Virus & threat protection > Ransomware protection แล้วอนุญาตไฟล์เรียกทำงานของเกม หรือปิด Controlled Folder Access\n\nไม่มีเสียง หรือ 0x88890004 / 0x88890026 - Windows ตัดสตรีมเสียงของเกม มักเกิดจากอุปกรณ์ส่งออกเสียงเริ่มต้นถูกเปลี่ยนหรือถูกปิดเสียง ให้ตั้งหูฟังหรือลำโพงที่ต้องการเป็นอุปกรณ์ส่งออกเสียงเริ่มต้นของ Windows และเปิดเสียงในตัวผสมเสียง หากยังเกิดขึ้นอีกให้ปิดฮุกเสียงในหน้าการตั้งค่า > เสียง เพื่อให้เกมใช้ WASAPI แบบแชร์โหมด\n\n0x88890010 - บริการ Windows Audio ไม่ได้ทำงาน ให้เริ่มบริการ Windows Audio แล้วเปิดเกมอีกครั้ง\n\nจอดำหรือข้อผิดพลาดของไดรเวอร์การแสดงผล - เกมถูกสร้างมาสำหรับการ์ด NVIDIA และต้องใช้ไดรเวอร์รุ่นปัจจุบัน ให้อัปเดตไดรเวอร์การ์ดจอ แล้วเปิดเกมอีกครั้ง\n\nERROR 6401 - มีผู้เล่นที่เสียบคอนโทรลเลอร์หรืออุปกรณ์ USB ไว้รายงานเข้ามา การปิด USB selective suspend ในแผนการใช้พลังงานของ Windows ช่วยแก้ปัญหานี้ให้พวกเขาได้\n\nรหัสออก 22 พร้อม 0xC000001D - ซีพียูไม่มีชุดคำสั่ง F16C ที่เกมใช้ (Pentium และ Celeron ก่อนเจเนอเรชัน 12, Intel Core ก่อนเจเนอเรชัน 3) เกมไม่สามารถทำงานบนซีพียูนั้นได้\n\nเกมหยุดทำงานตอนโหลดการต่อสู้ครั้งแรกบนการ์ด AMD - เลเยอร์กราฟิกรุ่นใหม่กว่าสร้างเชเดอร์ของเกมบนการ์ด RX 500, RX 6000 และ RX 7600 ไม่สำเร็จ ให้เปิดการตั้งค่า > การแสดงผล แล้วคลิกย้อนกลับไปใช้เลเยอร์รุ่นเก่า";

	public static string Explain(int code)
	{
		return code switch
		{
			2 => "ไฟล์บางไฟล์ที่เกมต้องใช้หายไป ให้แตกแพ็กเกจ FGOAC scooby ลงในโฟลเดอร์เกมโดยตรง ซึ่งเป็นโฟลเดอร์ที่มี App และ Server อยู่ เพื่อให้ FGOAC scooby.exe อยู่ข้าง ๆ กัน ตรวจสอบว่าได้ติดตั้งอัปเดต V1.01 หรือ V1.02 ของ Cloud23333 แล้ว และตรวจดูว่าโปรแกรมป้องกันไวรัสกักกันไฟล์ใดไว้หรือไม่",
			3 => "ตัวเรียกเกมหาการ์ดเครือข่ายสำหรับเครือข่ายตู้เกมไม่พบ ให้ตั้งที่อยู่เซิร์ฟเวอร์เป็น auto ในการตั้งค่า และติดตั้งโฟลเดอร์ App จากแพ็กเกจใหม่อีกครั้ง เพื่อให้ FGO_Launcher.ps1 กับ fgohook.dll ตรงรุ่นกัน โหมด auto ไม่ต้องใช้การเชื่อมต่ออินเทอร์เน็ต", 
			4 => "โฟลเดอร์ที่เกมต้องเขียนข้อมูลถูกล็อกอยู่ ให้เปิดตัวเรียกเกมในฐานะผู้ดูแลระบบ แล้วตรวจสอบพาธที่แสดงด้านล่างว่ามีพื้นที่ว่างพอหรือไม่ ถูกตั้งเป็นอ่านอย่างเดียวหรือไม่ หรือถูกโปรแกรมป้องกันไวรัสบล็อกโฟลเดอร์ไว้หรือไม่", 
			5 => "Windows ไม่มีอุปกรณ์ส่งออกเสียงที่เปิดใช้งานอยู่ ให้เปิดใช้งานลำโพงหรือหูฟังใน Windows และตั้งเป็นอุปกรณ์ส่งออกเสียงเริ่มต้น แล้วเริ่มเกมอีกครั้ง", 
			10 => "เซิร์ฟเวอร์ในเครื่องเริ่มทำงานไม่ได้ มักเกิดจากพอร์ตถูกใช้งานอยู่แล้ว ฐานข้อมูลล้มเหลว หรือมีเซิร์ฟเวอร์รุ่นเก่ายังทำงานค้างอยู่ ให้อ่านข้อผิดพลาดด้านล่าง แล้วตรวจสอบ logs/server-control.log, artemis-stderr.log และ mariadb.log", 
			11 => "ติดต่อพอร์ตของเซิร์ฟเวอร์ไม่ได้ หากเล่นบนเครื่องเดียวให้ตั้งที่อยู่เซิร์ฟเวอร์เป็น auto แล้วเริ่มเซิร์ฟเวอร์ในเครื่อง หากใช้เซิร์ฟเวอร์ระยะไกลให้ตรวจสอบที่อยู่และพอร์ตที่กรอกไว้", 
			12 => "ไม่พบฮุกเสียงเสริม FGOAudio.dll ให้ปิดฮุกเสียงแบบทดลองในหน้าการตั้งค่า > เสียง - เวอร์ชัน 11.00 มีระบบเสียงแบบแชร์โหมดของตัวเองอยู่แล้วและไม่ต้องใช้ไฟล์นั้น", 
			13 => "ไฟล์ตั้งค่าไฟล์หนึ่งไม่ใช่ JSON ที่ถูกต้อง หรือเวอร์ชันเกมในไฟล์ไม่ถูกต้อง ให้ตรวจสอบ fgo-launcher.json และ config.json ว่าพิมพ์ผิดหรือไม่ และตรวจดูว่าเวอร์ชันเกมระบุเป็น 11.00", 
			14 => "การตั้งค่าจอภาพหรือความละเอียดไม่ถูกต้อง ให้เลือกจอภาพและกำหนดความกว้างกับความสูงที่ถูกต้องอีกครั้งในหน้าการแสดงผล แล้วบันทึก", 
			15 => "การตรวจสอบสภาพแวดล้อมไม่ผ่าน ให้แก้ไขรายการที่แสดงด้านล่าง แล้วคลิกเรียกใช้การตรวจสอบสภาพแวดล้อมในหน้าการวินิจฉัยและความช่วยเหลือ เพื่อดูรายงานฉบับเต็ม", 
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
		string text = Path.Combine(GamePaths.GameRoot, "FGO_StartupChecks.ps1");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException(Explain(2), text);
		}
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(fullPath, redirectOutput: true, new string[2] { "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; . $env:FGO_CHECK_SCRIPT; Test-FgoWritableLayout -InstallRoot $env:FGO_CHECK_ROOT" });
		processStartInfo.Environment["FGO_CHECK_SCRIPT"] = text;
		processStartInfo.Environment["FGO_CHECK_ROOT"] = fullPath;
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("เริ่มการตรวจสอบโฟลเดอร์ไม่ได้");
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(20.0)).GetAwaiter().GetResult();
		Task.WhenAll<string>(task, task2).WaitAsync(TimeSpan.FromSeconds(2.0)).GetAwaiter()
			.GetResult();
		if (process.ExitCode != 0)
		{
			throw new IOException(Explain(4) + "\n\n" + task2.GetAwaiter().GetResult() + task.GetAwaiter().GetResult());
		}
	}
}
