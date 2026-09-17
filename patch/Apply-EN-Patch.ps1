<#
Applies the FGOAC scooby English patch to an FGO Arcade local-platform install.

The script is shipped at the root of the release package, next to "FGOAC scooby.exe",
manifest.json and the payload folder. Unzipping the package into the game folder already puts
every file beside the install, so a normal run copies the payload into place, checks every file
against manifest.json and writes the marker App\zh\th-patch.json. Re-running the same version
does nothing, unless a platform update has since put its own scripts and text over the patched
ones; then the files that differ are copied back.

Usage
  .\Apply-EN-Patch.ps1
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -NonInteractive
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -Rollback

The script asks Windows for administrator rights only when the game folder cannot be written to
as it stands, so a normal install on a data drive applies the patch without a prompt.

What it never touches: accounts, decks, Server\state, the database, and App\fgo-launcher.json
apart from setting chineseEnabled to true, which is the switch that makes the game load the
App\zh folder the patch fills with English.

Exit codes
  0  the patch was applied, was already up to date, or the rollback finished
  1  an unexpected error; the message says what failed
  2  no FGO Arcade install was found, or the folder given is not one
  3  the install is on drive E: or Y:, where the game cannot run
  4  the game, the server or a launcher is still running from that folder
  5  the patch package is incomplete, or lists a file the patch is not allowed to write
  6  a file did not match its checksum after it was copied
  7  -Rollback found no backup to restore
  8  cancelled: no folder was chosen
  9  the install has not had Cloud23333's V1.01 update, which the English scripts need
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = '',
    [string]$PackageRoot = '',
    [switch]$Rollback,
    [switch]$Force,
    [switch]$NonInteractive,
    [int]$IgnoreProcessId = 0
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptHost = [IO.Path]::Combine($PSHOME, 'pwsh.exe')
if (!(Test-Path -LiteralPath $scriptHost -PathType Leaf)) { $scriptHost = [IO.Path]::Combine($PSHOME, 'powershell.exe') }
if ([string]::IsNullOrWhiteSpace($PackageRoot)) { $PackageRoot = $PSScriptRoot }

$ProtectedPrefixes = @('Server\state\', 'Server\data\', 'DEVICE\', 'AMFS\', 'GameData\', '_th-patch-backup\', '_update-backup\', 'App\deck-loadouts\', 'Server\artemis\config\summon-presets\')
$ProtectedFiles = @('App\fgo-launcher.json', 'App\deck.json', 'App\deck.json.bak')
# "FGOA scooby.exe" is the name this launcher shipped under before 1.1.0. It stays in the list so an
# upgrade still refuses to run while the old build is open.
$RunningNames = @('ago.exe', 'amdaemon.exe', 'inject.exe', 'FGOLocalPlatform.exe', 'FGOAC scooby.exe', 'FGOA scooby.exe')
$RetiredLauncherName = 'FGOA scooby.exe'

function Test-FgoInstallRoot {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ([IO.File]::Exists([IO.Path]::Combine($Path, 'App\ago.exe')) -and
        [IO.File]::Exists([IO.Path]::Combine($Path, 'App\fgo-launcher.json')) -and
        [IO.Directory]::Exists([IO.Path]::Combine($Path, 'Server')))
}

function Find-FgoInstallRoot {
    foreach ($candidate in @($PackageRoot, (Split-Path -Parent $PackageRoot))) {
        if (Test-FgoInstallRoot -Path $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $skip = @('Windows', 'Program Files', 'Program Files (x86)', 'ProgramData', '$Recycle.Bin', 'System Volume Information')
    foreach ($drive in (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object -ExpandProperty DeviceID)) {
        $root = $drive + '\'
        if (Test-FgoInstallRoot -Path $root) { return $root }
        $first = @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $skip -notcontains $_.Name })
        foreach ($level1 in $first) {
            if (Test-FgoInstallRoot -Path $level1.FullName) { return $level1.FullName }
        }
        foreach ($level1 in $first) {
            foreach ($level2 in @(Get-ChildItem -LiteralPath $level1.FullName -Directory -Force -ErrorAction SilentlyContinue)) {
                if (Test-FgoInstallRoot -Path $level2.FullName) { return $level2.FullName }
            }
        }
    }
    return ''
}

function Select-FgoInstallRoot {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $picker = New-Object Windows.Forms.FolderBrowserDialog
    $picker.Description = 'เลือกโฟลเดอร์ FGO Arcade ของคุณ - โฟลเดอร์ที่มีโฟลเดอร์ App และ Server อยู่ข้างใน'
    $picker.ShowNewFolderButton = $false
    try {
        while ($picker.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            if (Test-FgoInstallRoot -Path $picker.SelectedPath) { return $picker.SelectedPath }
            [void][Windows.Forms.MessageBox]::Show(
                "โฟลเดอร์นั้นไม่ใช่การติดตั้ง FGO Arcade เลือกโฟลเดอร์ที่มี App\ago.exe และโฟลเดอร์ Server" + [Environment]::NewLine + [Environment]::NewLine + "คุณเลือก: " + $picker.SelectedPath,
                'FGOAC scooby - แพตช์ภาษาไทย', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Warning)
        }
        return ''
    } finally { $picker.Dispose() }
}

function Get-FgoDestinationPath {
    param([string]$Root, [string]$Relative)
    if ($Relative -match '^[A-Za-z]:' -or $Relative.StartsWith('\') -or $Relative.Split('\') -contains '..') {
        throw "แพ็กเกจแพตช์ระบุพาธที่ไม่ถูกต้อง: $Relative"
    }
    foreach ($prefix in $ProtectedPrefixes) {
        if ($Relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "แพ็กเกจแพตช์ระบุไฟล์ที่แพตช์ไม่ได้รับอนุญาตให้เขียน: $Relative"
        }
    }
    foreach ($protected in $ProtectedFiles) {
        if ($Relative.Equals($protected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "แพ็กเกจแพตช์ระบุไฟล์ที่แพตช์ไม่ได้รับอนุญาตให้เขียน: $Relative"
        }
    }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    if (!$full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "แพ็กเกจแพตช์ระบุพาธที่อยู่นอกโฟลเดอร์เกม: $Relative"
    }
    return $full
}

function Get-FgoFileHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Clear-FgoReadOnly {
    param([string]$Path)
    if ([IO.File]::Exists($Path)) {
        $attributes = [IO.File]::GetAttributes($Path)
        if ($attributes -band [IO.FileAttributes]::ReadOnly) {
            [IO.File]::SetAttributes($Path, $attributes -band (-bnot [IO.FileAttributes]::ReadOnly))
        }
    }
}

function Copy-FgoFile {
    param([string]$Source, [string]$Destination)
    $parent = Split-Path -Parent $Destination
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    Clear-FgoReadOnly -Path $Destination
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    Clear-FgoReadOnly -Path $Destination
}

# Windows Defender keeps reading a file this large for some seconds after it is written, and a rename
# during that time fails. Waits until the file can be opened with no sharing, or gives up quietly.
function Wait-FgoFileReleased {
    param([string]$Path, [int]$Seconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ($true) {
        try {
            $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $stream.Dispose()
            return
        } catch {
            if ((Get-Date) -gt $deadline) { return }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Write-FgoJson {
    param([string]$Path, $Value)
    $parent = Split-Path -Parent $Path
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
}

function Test-FgoWritable {
    param([string]$Path)
    if (!([IO.Directory]::Exists($Path))) { return $false }
    $probe = [IO.Path]::Combine($Path, '.fgoa-scooby-' + [guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($probe, 'write check')
        [IO.File]::Delete($probe)
        return $true
    } catch { return $false }
}

function Stop-WithMessage {
    param([string]$Message, [int]$Code)
    Write-Host $Message
    exit $Code
}

# The folder picker needs an STA thread, the same way the author's updater gets one.
if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive -and
    [Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    $arguments = @('-NoLogo', '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PackageRoot -ne $PSScriptRoot) { $arguments += @('-PackageRoot', $PackageRoot) }
    if ($Rollback) { $arguments += '-Rollback' }
    if ($Force) { $arguments += '-Force' }
    & $scriptHost @arguments
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Write-Host 'กำลังค้นหาโฟลเดอร์ FGO Arcade ของคุณ...'
    $InstallRoot = Find-FgoInstallRoot
    if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive) { $InstallRoot = Select-FgoInstallRoot }
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        Stop-WithMessage 'ไม่ได้เลือกโฟลเดอร์ FGO Arcade จึงไม่มีการเปลี่ยนแปลงใด ๆ' 8
    }
}
if (!(Test-FgoInstallRoot -Path $InstallRoot)) {
    Stop-WithMessage "โฟลเดอร์นั้นไม่ใช่การติดตั้ง FGO Arcade: $InstallRoot เลือกโฟลเดอร์ที่มี App\ago.exe, App\fgo-launcher.json และโฟลเดอร์ Server" 2
}
$InstallRoot = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\')
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
$rootPrefix = $InstallRoot + '\'
$driveLetter = $InstallRoot.Substring(0, 1).ToUpperInvariant()
if ($driveLetter -eq 'E' -or $driveLetter -eq 'Y') {
    Stop-WithMessage "เกมไม่สามารถทำงานจากไดรฟ์ ${driveLetter}: ได้ - ฮุกไฟล์ของเกมเองจะส่งทุกพาธ ${driveLetter}: ไปยังจุดเชื่อมต่อข้อมูลตู้เกม เกมจึงหยุดด้วย ERROR 4104 ให้ย้ายโฟลเดอร์ $InstallRoot ทั้งโฟลเดอร์ไปไว้ที่ไดรฟ์อื่น เช่น D: แล้วรันแพตช์อีกครั้ง" 3
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevationNeeded = !(Test-FgoWritable -Path $InstallRoot) -or !(Test-FgoWritable -Path ([IO.Path]::Combine($InstallRoot, 'App')))
if ($elevationNeeded -and ![Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $command = "& '" + $PSCommandPath.Replace("'", "''") + "' -InstallRoot '" + $InstallRoot.Replace("'", "''") + "' -PackageRoot '" + $PackageRoot.Replace("'", "''") + "' -NonInteractive"
    if ($Rollback) { $command += ' -Rollback' }
    if ($Force) { $command += ' -Force' }
    $transcript = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'fgoa-scooby-patch-' + [guid]::NewGuid().ToString('N') + '.log')
    $quoted = $transcript.Replace("'", "''")
    $command = "try { $command *> '$quoted' } catch { " + '$_' + " | Out-String | Out-File -LiteralPath '$quoted' -Append; exit 1 }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $elevated = Start-Process -FilePath $scriptHost -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    if (Test-Path -LiteralPath $transcript) {
        Get-Content -LiteralPath $transcript
        Remove-Item -LiteralPath $transcript -Force
    }
    exit $elevated.ExitCode
}

$running = @(Get-CimInstance Win32_Process | Where-Object {
    $RunningNames -contains $_.Name -and $_.ProcessId -ne $IgnoreProcessId -and
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
})
if ($running) {
    Stop-WithMessage ("ปิดเกมและตัวเรียกเกมใน $InstallRoot ก่อน แล้วรันแพตช์อีกครั้ง ยังทำงานอยู่: " + (($running | ForEach-Object { $_.Name }) -join ', ')) 4
}

$backupRoot = [IO.Path]::Combine($InstallRoot, '_th-patch-backup')
$markerPath = [IO.Path]::Combine($InstallRoot, 'App\zh\th-patch.json')

if ($Rollback) {
    if (!([IO.Directory]::Exists($backupRoot))) { Stop-WithMessage "ไม่มีข้อมูลสำรองของแพตช์ให้คืนค่าใน $backupRoot" 7 }
    $backup = Get-ChildItem -LiteralPath $backupRoot -Directory | Sort-Object -Property Name | Select-Object -Last 1
    if (!$backup) { Stop-WithMessage "ไม่มีข้อมูลสำรองของแพตช์ให้คืนค่าใน $backupRoot" 7 }
    $recordPath = [IO.Path]::Combine($backup.FullName, 'th-patch-restore.json')
    if (!([IO.File]::Exists($recordPath))) {
        Stop-WithMessage "ข้อมูลสำรองใน $($backup.FullName) ไม่มีไฟล์ th-patch-restore.json จึงย้อนกลับอัตโนมัติไม่ได้" 7
    }
    $record = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "กำลังคืนค่าไฟล์ที่บันทึกไว้ใน $($backup.FullName)..."
    $restored = 0
    foreach ($relative in @($record.replaced)) {
        $saved = [IO.Path]::Combine($backup.FullName, $relative)
        if ([IO.File]::Exists($saved)) {
            Copy-FgoFile -Source $saved -Destination ([IO.Path]::Combine($InstallRoot, $relative))
            $restored++
        }
    }
    $removed = 0
    foreach ($relative in @($record.added)) {
        $added = [IO.Path]::Combine($InstallRoot, $relative)
        if ([IO.File]::Exists($added)) {
            Clear-FgoReadOnly -Path $added
            [IO.File]::Delete($added)
            $removed++
        }
    }
    $savedMarker = [IO.Path]::Combine($backup.FullName, 'App\zh\th-patch.json')
    if ([IO.File]::Exists($savedMarker)) { Copy-FgoFile -Source $savedMarker -Destination $markerPath }
    elseif ([IO.File]::Exists($markerPath)) { [IO.File]::Delete($markerPath) }
    Write-Host "ย้อนกลับเสร็จสิ้น: คืนค่า $restored ไฟล์ ลบ $removed ไฟล์"
    Write-Host 'การตั้งค่าตัวเรียกเกมถูกปล่อยไว้ตามเดิม หากต้องการชุดข้อความภาษาจีนกลับคืน ให้ปิด ข้อความแปล ในตัวเรียกเกม'
    exit 0
}

# App\FGO_Runtime.dll came with Cloud23333's V1.01 update, and the English launch and server
# scripts in this package load it. On the original 11.00 package they would leave the server
# unable to start, so the patch stops here instead.
if (!([IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'App\FGO_Runtime.dll')))) {
    Stop-WithMessage "โฟลเดอร์เกมนี้ยังไม่ได้อัปเดต V1.01 ของ Cloud23333: ไม่พบ App\FGO_Runtime.dll ซึ่งแพตช์ภาษาไทยจำเป็นต้องใช้ ให้ติดตั้งอัปเดต V1.01 หรือ V1.02 ของเขาลงใน $InstallRoot ก่อน แล้วเปิด FGOAC scooby อีกครั้ง" 9
}

$manifestPath = [IO.Path]::Combine($PackageRoot, 'manifest.json')
if (!([IO.File]::Exists($manifestPath))) {
    Stop-WithMessage "แพ็กเกจแพตช์ไม่สมบูรณ์: ไม่พบ manifest.json ใน $PackageRoot ให้แตกไฟล์แพ็กเกจทั้งหมดอีกครั้ง" 5
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version
$manifestHash = [string]$manifest.manifestHash
$entries = @($manifest.files.PSObject.Properties)
if ($entries.Count -eq 0) { Stop-WithMessage 'แพ็กเกจแพตช์ไม่สมบูรณ์: manifest.json ไม่ได้ระบุไฟล์ใดเลย' 5 }

if (!$Force -and [IO.File]::Exists($markerPath)) {
    try {
        $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$marker.version -eq $version -and [string]$marker.manifestHash -eq $manifestHash) {
            # A platform update copies its own launch scripts, server tools and Chinese text over
            # the patched ones and leaves the marker alone, so the marker on its own is not proof.
            # The files outside App\zh\rom are few and small, and an update overwrites those too,
            # so checking them on every run is cheap and enough.
            $replaced = ''
            foreach ($entry in $entries) {
                if ($entry.Name.StartsWith('App\zh\rom\', [StringComparison]::OrdinalIgnoreCase) -or $entry.Name -eq 'FGOAC scooby.exe') { continue }
                $installed = [IO.Path]::Combine($InstallRoot, $entry.Name)
                if (!([IO.File]::Exists($installed)) -or (Get-FgoFileHash -Path $installed) -ne [string]$entry.Value) { $replaced = $entry.Name; break }
            }
            if ($replaced -eq '') {
                Write-Host "แพตช์ภาษาไทยเวอร์ชัน $version ติดตั้งอยู่แล้วใน $InstallRoot ไม่มีการเปลี่ยนแปลงใด ๆ"
                exit 0
            }
            Write-Host "มีบันทึกว่าแพตช์ภาษาไทยเวอร์ชัน $version ติดตั้งแล้ว แต่ $replaced ถูกแทนที่ไปหลังจากนั้น ซึ่งมักเกิดจากการอัปเดตแพลตฟอร์ม จึงจะติดตั้งแพตช์ใหม่อีกครั้ง"
        }
    } catch {
        Write-Host 'อ่านไฟล์เครื่องหมายแพตช์ที่มีอยู่ไม่ได้ จึงจะติดตั้งแพตช์ใหม่อีกครั้ง'
    }
}

Write-Host "กำลังติดตั้งแพตช์ภาษาไทยเวอร์ชัน $version ลงใน $InstallRoot"
$payloadRoot = [IO.Path]::Combine($PackageRoot, 'payload')
$plan = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in $entries) {
    $relative = $entry.Name
    try { $destination = Get-FgoDestinationPath -Root $InstallRoot -Relative $relative }
    catch { Stop-WithMessage $_.Exception.Message 5 }
    $source = [IO.Path]::Combine($payloadRoot, $relative)
    if (!([IO.File]::Exists($source))) { $source = [IO.Path]::Combine($PackageRoot, $relative) }
    if (!([IO.File]::Exists($source))) {
        Stop-WithMessage "แพ็กเกจแพตช์ไม่สมบูรณ์: ไม่พบ $relative ให้แตกไฟล์แพ็กเกจทั้งหมดอีกครั้ง" 5
    }
    $plan.Add([pscustomobject]@{
        Relative    = $relative
        Source      = $source
        Destination = $destination
        Expected    = [string]$entry.Value
        InPlace     = $source.Equals($destination, [StringComparison]::OrdinalIgnoreCase)
        IsLauncher  = [IO.Path]::GetFileName($destination) -eq 'FGOAC scooby.exe'
    })
}

# When the package sits somewhere else, as it does when a launcher's updater unpacks it into the
# temporary folder, the game folder gets its own copy of the package first - installer, manifest,
# payload, the rest - the way an unzip by hand leaves it, so the next start finds the version it is
# on and a later re-apply or -Rollback has its files. The launcher is left to its manifest entry,
# which stages it as .new while it runs; manifest.json goes last, so a copy that stops half way
# still describes the previous package.
if (!$PackageRoot.Equals($InstallRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'กำลังคัดลอกแพ็กเกจไปยังโฟลเดอร์เกม...'
    $packagePrefix = $PackageRoot + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $PackageRoot -File -Recurse)) {
        if ($file.Name -eq 'FGOAC scooby.exe' -or $file.FullName.Equals($manifestPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        Copy-FgoFile -Source $file.FullName -Destination ([IO.Path]::Combine($InstallRoot, $file.FullName.Substring($packagePrefix.Length)))
    }
    Copy-FgoFile -Source $manifestPath -Destination ([IO.Path]::Combine($InstallRoot, 'manifest.json'))
}

Write-Host "กำลังตรวจสอบไฟล์ $($plan.Count) ไฟล์เทียบกับแพ็กเกจ..."
$toCopy = New-Object 'System.Collections.Generic.List[object]'
$checked = 0
foreach ($item in $plan) {
    $checked++
    if (($checked % 400) -eq 0) { Write-Host "  ตรวจสอบแล้ว $checked จาก $($plan.Count) ไฟล์" }
    if ($item.InPlace) { continue }
    if ([IO.File]::Exists($item.Destination) -and (Get-FgoFileHash -Path $item.Destination) -eq $item.Expected) { continue }
    $toCopy.Add($item)
}

if ($toCopy.Count -eq 0) {
    Write-Host 'ไฟล์ทุกไฟล์อยู่ในตำแหน่งอยู่แล้ว จึงเขียนเฉพาะไฟล์เครื่องหมายเท่านั้น'
} else {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = [IO.Path]::Combine($backupRoot, $stamp)
    [void][IO.Directory]::CreateDirectory($backup)
    $replaced = New-Object 'System.Collections.Generic.List[string]'
    $added = New-Object 'System.Collections.Generic.List[string]'
    Write-Host "กำลังสำรองไฟล์ที่จะถูกแทนที่ไปยัง $backup"
    foreach ($item in $toCopy) {
        if ([IO.File]::Exists($item.Destination)) {
            Copy-FgoFile -Source $item.Destination -Destination ([IO.Path]::Combine($backup, $item.Relative))
            $replaced.Add($item.Relative)
        } else {
            $added.Add($item.Relative)
        }
    }
    if ([IO.File]::Exists($markerPath)) {
        Copy-FgoFile -Source $markerPath -Destination ([IO.Path]::Combine($backup, 'App\zh\th-patch.json'))
    }
    Write-FgoJson -Path ([IO.Path]::Combine($backup, 'th-patch-restore.json')) -Value ([ordered]@{
        version  = $version
        savedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        replaced = @($replaced)
        added    = @($added)
    })

    Write-Host "กำลังคัดลอกไฟล์ $($toCopy.Count) ไฟล์..."
    $copied = 0
    foreach ($item in $toCopy) {
        try {
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
        } catch {
            if (!$item.IsLauncher) { throw }
            # A running program cannot overwrite its own file, so the new launcher is left beside the
            # old one. The launcher swaps it in and restarts itself when it closes.
            $item.Destination = $item.Destination + '.new'
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
            Write-Host 'ตัวเรียกเกมเปิดอยู่ ไฟล์ใหม่จึงถูกวางพักไว้ข้าง ๆ และจะสลับเข้ามาแทนเมื่อปิดตัวเรียกเกม'
        }
        $copied++
        if (($copied % 200) -eq 0) { Write-Host "  คัดลอกแล้ว $copied จาก $($toCopy.Count) ไฟล์" }
    }

    Write-Host 'กำลังตรวจสอบไฟล์ที่คัดลอก...'
    $bad = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $toCopy) {
        if ((Get-FgoFileHash -Path $item.Destination) -ne $item.Expected) { $bad.Add($item.Relative) }
    }
    if ($bad.Count -gt 0) {
        Write-Host 'ไฟล์เหล่านี้ไม่ตรงกับเช็คซัมหลังคัดลอก แพตช์จึงไม่สมบูรณ์:'
        foreach ($relative in $bad) { Write-Host "  $relative" }
        Write-Host "ให้รันแพตช์อีกครั้ง หากยังล้มเหลว ให้ตรวจสอบโปรแกรมป้องกันไวรัสและพื้นที่ว่างบนไดรฟ์ ${driveLetter}: ไฟล์ที่ถูกแทนที่อยู่ใน $backup และ -Rollback จะคืนค่ากลับให้"
        exit 6
    }
    Write-Host "ข้อมูลสำรองของไฟล์ที่ถูกแทนที่: $backup"
    # The launcher swaps the staged build in the moment it closes; that has to find the file free.
    foreach ($item in $toCopy) {
        if ($item.IsLauncher -and $item.Destination.EndsWith('.new')) { Wait-FgoFileReleased -Path $item.Destination -Seconds 90 }
    }
}

# Before 1.1.0 the launcher was called "FGOA scooby.exe". Once the new one is in place the old file is
# only a second icon that starts an out-of-date build, so it goes.
$retiredLauncher = [IO.Path]::Combine($InstallRoot, $RetiredLauncherName)
if ([IO.File]::Exists($retiredLauncher) -and [IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'FGOAC scooby.exe'))) {
    try {
        Clear-FgoReadOnly -Path $retiredLauncher
        Remove-Item -LiteralPath $retiredLauncher -Force
        Write-Host "ลบ $RetiredLauncherName แล้ว เนื่องจากเวอร์ชันนี้มาแทนที่"
    } catch {
        Write-Host "$RetiredLauncherName ยังอยู่และลบไม่ได้: $($_.Exception.Message) ให้ลบเองเพื่อไม่ให้เผลอเปิดบิลด์เก่า"
    }
}

# chineseEnabled is the switch that makes the game read App\zh, which now holds the English set.
$configurationPath = [IO.Path]::Combine($InstallRoot, 'App\fgo-launcher.json')
try {
    $configuration = Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($configuration.chineseEnabled -ne $true) {
        $configuration | Add-Member -NotePropertyName 'chineseEnabled' -NotePropertyValue $true -Force
        Write-FgoJson -Path $configurationPath -Value $configuration
        Write-Host 'เปิด ข้อความแปล ในการตั้งค่าตัวเรียกเกมแล้ว'
    }
} catch {
    Write-Host "ตั้งค่าสวิตช์ ข้อความแปล ใน App\fgo-launcher.json ไม่ได้: $($_.Exception.Message)"
    Write-Host 'ให้เปิดตัวเรียกเกมแล้วเปิด ข้อความแปล ด้วยตัวเอง'
}

# Windows marks every file that came out of a downloaded zip, and Windows PowerShell then refuses
# to load App\FGO_Runtime.dll (0x80131515). A platform update puts the mark back, so this runs on
# every pass, not only when files were copied.
$marked = @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'App')) -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.dll', '.exe', '.ps1' }) +
    @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'Server')) -Filter '*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($file in $marked) { Unblock-File -LiteralPath $file.FullName -ErrorAction SilentlyContinue }

Write-FgoJson -Path $markerPath -Value ([ordered]@{
    version      = $version
    appliedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    manifestHash = $manifestHash
})
Write-Host "ติดตั้งแพตช์ภาษาไทยเวอร์ชัน $version ใน $InstallRoot แล้ว"
exit 0
