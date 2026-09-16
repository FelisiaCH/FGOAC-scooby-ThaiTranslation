[CmdletBinding()]
param([switch]$AsJson)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$root=Split-Path -Parent $PSScriptRoot
$checks=[Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail) {
    $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
}
Add-Check 'ระบบปฏิบัติการ' ([Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Version.Major -ge 10) ([Environment]::OSVersion.VersionString+'; อัปเดตนี้รองรับ Windows 10/11 x64')
Add-Check 'PowerShell' ([IntPtr]::Size -eq 8 -and $PSVersionTable.PSVersion -ge [version]'5.1' -and $PSVersionTable.PSVersion.Major -ne 6) ("พบเวอร์ชัน $($PSVersionTable.PSVersion) แบบ $([IntPtr]::Size*8)-bit รองรับทั้ง Windows PowerShell 5.1 และ PowerShell 7")
Add-Check '.NET ของตัวเรียกเกม' $true 'ไฟล์ EXE ส่วนหน้าทั้งสองตัวมี .NET desktop runtime มาให้ในตัว จึงไม่ต้องติดตั้ง .NET แยกต่างหาก'
$criticalPaths=@('App\ago.exe','App\fgohook.dll','App\am\amdaemon.exe',
    'App\Tools\Locale_Remulator\LRHookx64.dll','DEVICE\runtime\segatools.runtime.ini',
    'DEVICE\runtime\amdaemon_main.json','Server\mariadb-10.11.16-winx64\bin\mariadbd.exe') |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root $_)) }
$tooLong=@($criticalPaths | Where-Object { $_.Length -ge 260 })
Add-Check 'ความยาวพาธที่ใช้ตอนเริ่มเกม' ($tooLong.Count -eq 0) $(if($tooLong.Count){
    'พาธเหล่านี้ยาวเกินไปสำหรับโมดูลเนทีฟ: '+($tooLong -join '; ')+' กรุณาย้ายการติดตั้งไปไว้ในโฟลเดอร์ที่ซ้อนกันน้อยลง ส่วนอักษรไดรฟ์ใช้ค่าเดิมได้'
}else{'พาธสำคัญที่ใช้ตอนเริ่มเกมสั้นพอสำหรับโมดูลเนทีฟแล้ว แต่โฟลเดอร์ย่อยของทรัพยากรที่ซ้อนกันลึกมากยังอาจชนขีดจำกัดความยาวพาธของเนทีฟได้'})
try {
    if (!('FgoEnvironmentNative' -as [type])) {
        # A DLL still marked as downloaded cannot be loaded by Windows PowerShell (0x80131515).
        Unblock-File -LiteralPath (Join-Path $PSScriptRoot 'FGO_Runtime.dll') -ErrorAction SilentlyContinue
        [void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'FGO_Runtime.dll'))
    }
    function Test-Library([string]$Folder,[string]$Name) {
        $path=Join-Path $Folder $Name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
        $module=[FgoEnvironmentNative]::LoadLibraryEx($path,[IntPtr]::Zero,0x900)
        if ($module -eq [IntPtr]::Zero) { return $false }
        [void][FgoEnvironmentNative]::FreeLibrary($module)
        return $true
    }
    foreach ($runtime in @(
        @{Name='Visual C++ 2010 x64'; Files=@('msvcr100.dll','msvcp100.dll')},
        @{Name='Visual C++ 2012 x64'; Files=@('msvcr110.dll','msvcp110.dll')},
        @{Name='Visual C++ v14 x64'; Files=@('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')}
    )) {
        $missing=@()
        foreach ($folder in @($PSScriptRoot,(Join-Path $PSScriptRoot 'am'))) {
            foreach ($name in $runtime.Files) {
                $searchFolder=if (Test-Path -LiteralPath (Join-Path $folder $name)) {$folder} else {[Environment]::SystemDirectory}
                if (!(Test-Library $searchFolder $name)) { $missing+="$searchFolder\$name" }
            }
        }
        $detail=if ($missing.Count -eq 0) {'ไฟล์ DLL ที่จำเป็นโหลดได้ แพ็กเกจนี้มีไลบรารีรันไทม์พื้นฐานมาให้แล้ว'} else {"โหลดไม่สำเร็จ: $($missing -join '; ') กรุณาติดตั้งแพ็กเกจฉบับเต็มใหม่อีกครั้ง หรือติดตั้ง Microsoft x64 redistributable รุ่นที่ตรงกัน"}
        Add-Check $runtime.Name ($missing.Count -eq 0) $detail
    }
    $mediaMissing=@(@('mfplat.dll','mfreadwrite.dll') | Where-Object { !(Test-Library ([Environment]::SystemDirectory) $_) })
    Add-Check 'คอมโพเนนต์ Windows Media' ($mediaMissing.Count -eq 0) $(if($mediaMissing.Count){"โหลด $($mediaMissing -join ', ') ไม่สำเร็จ กรุณาติดตั้ง Media Feature Pack บน Windows N/KN หรือคืนค่าคอมโพเนนต์สื่อบน Windows ที่ถูกตัดทอน"}else{'ไลบรารี Media Foundation โหลดได้'})
} catch { Add-Check 'การตรวจสอบไลบรารีรันไทม์' $false $_.Exception.Message }
try {
    $service=Get-Service -Name Audiosrv
    Add-Check 'บริการเสียงของ Windows' ($service.Status -eq 'Running') ("สถานะปัจจุบัน: $($service.Status) หากบริการ Windows Audio ไม่ทำงาน กรุณาเริ่มบริการนี้")
    $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    $render=$registry.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render')
    $active=0
    try {
        if ($render) { foreach ($name in $render.GetSubKeyNames()) {
            $endpoint=$render.OpenSubKey($name)
            try { if (([long]$endpoint.GetValue('DeviceState',0) -band 1) -ne 0) { $active++ } } finally { $endpoint.Dispose() }
        } }
    } finally { if ($render) {$render.Dispose()}; $registry.Dispose() }
    Add-Check 'อุปกรณ์ส่งออกเสียง' ($active -gt 0) ("พบอุปกรณ์ส่งออกเสียงที่เปิดใช้งานอยู่ $active รายการ เกมใช้โหมดแชร์ หากไม่พบเลย กรุณาเชื่อมต่อหรือเปิดใช้งานลำโพงหรือหูฟัง แล้วตั้งเป็นอุปกรณ์ส่งออกเริ่มต้นใน Windows")
} catch { Add-Check 'การตรวจสอบเสียง' $false $_.Exception.Message }
$required=@('App\ago.exe','App\am\amdaemon.exe','App\fgohook.dll','App\FGO_Runtime.dll','App\inject.exe','App\Tools\Locale_Remulator\LRHookx64.dll','App\config.json','App\segatools.ini','AMFS\ICF1','AMFS\ICF2','Server\mariadb-10.11.16-winx64\bin\mariadbd.exe')
$missing=@($required | Where-Object {!(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)})
Add-Check 'ไฟล์เกมและเซิร์ฟเวอร์' ($missing.Count -eq 0) $(if($missing -contains 'App\FGO_Runtime.dll'){'ไม่พบ: '+($missing -join ', ')+' โดยไฟล์ App\FGO_Runtime.dll มาพร้อมอัปเดต V1.01 ของ Cloud23333 กรุณาติดตั้งอัปเดต V1.01 หรือ V1.02 ของเขาลงในโฟลเดอร์เกม แล้วเรียกใช้ FGOAC scooby อีกครั้ง'}elseif($missing.Count){'ไม่พบ: '+($missing -join ', ')+' กรุณาติดตั้งแพ็กเกจฉบับเต็มใหม่ เนื่องจากอัปเดตแบบเพิ่มส่วนไม่ได้รวมตัวเกมมาด้วย'}else{'ไฟล์โปรแกรมหลักครบถ้วน การตรวจสอบนี้ไม่ได้ตรวจทรัพยากร ROM ทุกไฟล์'})
try {
    $python=Join-Path $root 'Server\python\python.exe'
    $result=& $python -I -c "import sys,yaml,sqlalchemy,aiomysql,uvicorn,starlette,Crypto; print(sys.version.split()[0])" 2>&1
    if ($LASTEXITCODE -ne 0) {throw ($result -join "`n")}
    Add-Check 'Python และไลบรารีเซิร์ฟเวอร์ที่มาพร้อมแพ็กเกจ' $true ("Python $result และไลบรารีหลักของเซิร์ฟเวอร์โหลดได้ ไม่ต้องใช้ Python ของระบบหรือติดตั้งไลบรารีเพิ่มทางออนไลน์")
} catch { Add-Check 'Python และไลบรารีเซิร์ฟเวอร์ที่มาพร้อมแพ็กเกจ' $false ("สภาพแวดล้อมที่มาพร้อมแพ็กเกจไม่สมบูรณ์: $($_.Exception.Message) กรุณาคืนค่า Server/python และ Server/venv และอย่าเขียนทับด้วย venv ของโปรเจกต์อื่น") }
foreach ($folder in @('App','AMFS','GameData','DEVICE','Server\state','Server\data\mariadb','logs')) {
    $probe=Join-Path (Join-Path $root $folder) ('.fgo-env-'+[guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe,'check'); [IO.File]::Delete($probe); Add-Check ("สิทธิ์โฟลเดอร์: $folder") $true 'สร้างและลบไฟล์ชั่วคราวได้' }
    catch { Add-Check ("สิทธิ์โฟลเดอร์: $folder") $false ("เขียนไม่สำเร็จ: $($_.Exception.Message) กรุณาเรียกใช้ตัวเรียกเกมในฐานะผู้ดูแลระบบ และตรวจสอบว่าไดรฟ์ไม่เต็ม ไม่ได้ถูกป้องกันการเขียน และไม่ถูกบล็อกโดยโปรแกรมความปลอดภัย") }
}
Add-Check 'โหมดเครือข่าย' $true 'ในโหมดอัตโนมัติ เกมจะเข้าถึงบริการในเครื่องผ่านอะแดปเตอร์เสมือนภายในโปรเซสและ 127.0.0.1 จึงไม่จำเป็นต้องมีอะแดปเตอร์จริง เกตเวย์ หรือการเชื่อมต่ออินเทอร์เน็ต การตรวจสอบนี้ไม่ได้เริ่มเซิร์ฟเวอร์'
if ($AsJson) { ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 4 }
else { foreach ($check in $checks) { $status=if($check.Passed){'PASS'}else{'ACTION NEEDED'}; Write-Output "[$status] $($check.Name): $($check.Detail)`n" } }
