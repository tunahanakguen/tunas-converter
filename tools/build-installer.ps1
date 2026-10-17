# Baut TUNAS CONVERTER für Windows (x64, ohne vorinstalliertes .NET) und erzeugt den Installer.
# Ergebnis: publish\TUNAS-Converter-Setup-<Version>.exe
# Aufruf:   powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1 [-FfmpegDir <Ordner mit ffmpeg.exe>]
param([string]$FfmpegDir = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\TuNAS.Converter\TuNAS.Converter.csproj'
$app = Join-Path $root 'publish\app'

function Find-Tool($names) {
    foreach ($n in $names) { if (Test-Path $n) { return $n } }
    return $null
}

# Werkzeuge suchen (auch wenn das Terminal den neuen PATH noch nicht kennt)
$dotnet = Find-Tool @("$env:ProgramFiles\dotnet\dotnet.exe", (Get-Command dotnet -ErrorAction SilentlyContinue).Source)
$iscc = Find-Tool @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")
if (-not $dotnet) { throw '.NET SDK nicht gefunden (winget install Microsoft.DotNet.SDK.10).' }
if (-not $iscc) { throw 'Inno Setup nicht gefunden (winget install JRSoftware.InnoSetup).' }

if (-not $FfmpegDir) {
    $FfmpegDir = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages" -Directory -Filter 'Gyan.FFmpeg*' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-ChildItem $_.FullName -Recurse -Filter ffmpeg.exe } | Select-Object -First 1 | ForEach-Object { $_.DirectoryName }
}
if (-not $FfmpegDir -or -not (Test-Path (Join-Path $FfmpegDir 'ffprobe.exe'))) {
    throw 'FFmpeg nicht gefunden. Mit -FfmpegDir den Ordner mit ffmpeg.exe und ffprobe.exe angeben.'
}

[xml]$csproj = Get-Content $project
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
"Version $version"

# 1. Veröffentlichen: eigenständig (mit .NET-Laufzeit), nur 64-Bit
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
& $dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $app
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fehlgeschlagen.' }

# Nur die 64-Bit-Version von VLC wird gebraucht (32-Bit und ARM entfernen)
foreach ($arch in 'win-x86', 'win-arm64') {
    $dir = Join-Path $app "libvlc\$arch"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

# 2. FFmpeg samt Lizenz in den Unterordner "ffmpeg" legen (dort sucht das Programm zuerst)
$ffTarget = Join-Path $app 'ffmpeg'
New-Item -ItemType Directory -Force $ffTarget | Out-Null
Copy-Item (Join-Path $FfmpegDir 'ffmpeg.exe'), (Join-Path $FfmpegDir 'ffprobe.exe') $ffTarget
$ffRoot = Split-Path $FfmpegDir -Parent
foreach ($f in 'LICENSE', 'README.txt') { if (Test-Path (Join-Path $ffRoot $f)) { Copy-Item (Join-Path $ffRoot $f) (Join-Path $ffTarget "FFmpeg-$f.txt") } }

# 3. Lizenzen beilegen
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $app 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $app 'THIRD-PARTY-NOTICES.md')

# 4. Installer bauen
& $iscc "/DAppVersion=$version" "/DSourceDir=$app" (Join-Path $root 'installer\TunasConverter.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup fehlgeschlagen.' }

$setup = Join-Path $root "publish\TUNAS-Converter-Setup-$version.exe"
"Fertig: $setup ($([math]::Round((Get-Item $setup).Length / 1MB)) MB)"
