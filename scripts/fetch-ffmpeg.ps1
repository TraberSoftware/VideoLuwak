<#
.SYNOPSIS
    Downloads a static Windows ffmpeg/ffprobe build into assets/ffmpeg/win-x64 so it can be
    embedded into the portable VideoLuwak.exe during a Windows publish.

.DESCRIPTION
    The App csproj embeds assets/ffmpeg/win-x64/ffmpeg.exe and ffprobe.exe as resources when
    they exist (see the EMBEDDED_FFMPEG item group). Run this script once before publishing
    for win-x64 on Windows. Linux builds intentionally do NOT embed ffmpeg and use the
    system-wide ffmpeg from PATH instead.

    The default download is a pinned ffmpeg 8.1.2 build and its SHA-256 is verified before
    extraction.

.EXAMPLE
    pwsh ./scripts/fetch-ffmpeg.ps1
#>
[CmdletBinding()]
param(
    [string] $Url = 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-8.1.2-essentials_build.zip',
    [string] $Sha256 = 'db580001caa24ac104c8cb856cd113a87b0a443f7bdf47d8c12b1d740584a2ec',
    [string] $Destination = (Join-Path $PSScriptRoot '..\assets\ffmpeg\win-x64')
)

$ErrorActionPreference = 'Stop'

function Get-RepoRoot {
    (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}

function Resolve-Destination([string] $path) {
    if ([System.IO.Path]::IsPathRooted($path)) { return [System.IO.Path]::GetFullPath($path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-RepoRoot) $path))
}

$dest = Resolve-Destination $Destination
New-Item -ItemType Directory -Force -Path $dest | Out-Null

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("videoluwak-ffmpeg-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

try {
    $zip = Join-Path $tmp 'ffmpeg.zip'
    Write-Host "Downloading ffmpeg from $Url ..."
    Invoke-WebRequest -Uri $Url -OutFile $zip -UseBasicParsing

    $actualHash = (Get-FileHash -Path $zip -Algorithm SHA256).Hash
    if ($actualHash -ne $Sha256.ToUpperInvariant()) {
        throw "ffmpeg archive checksum mismatch. Expected $Sha256 but received $actualHash."
    }

    Write-Host "Extracting ..."
    Expand-Archive -Path $zip -DestinationPath $tmp -Force

    $ffmpeg = Get-ChildItem -Path $tmp -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $ffmpeg) { throw "ffmpeg.exe was not found inside the downloaded archive." }
    $ffprobe = Join-Path $ffmpeg.DirectoryName 'ffprobe.exe'
    if (-not (Test-Path $ffprobe)) { throw "ffprobe.exe was not found next to ffmpeg.exe." }

    Copy-Item $ffmpeg.FullName (Join-Path $dest 'ffmpeg.exe') -Force
    Copy-Item $ffprobe (Join-Path $dest 'ffprobe.exe') -Force

    $hash = (Get-FileHash (Join-Path $dest 'ffmpeg.exe') -Algorithm SHA256).Hash
    Set-Content -Path (Join-Path $dest 'SHA256SUMS.txt') -Value ("{0}  ffmpeg.exe`n{1}  ffprobe.exe" -f $hash, (Get-FileHash (Join-Path $dest 'ffprobe.exe') -Algorithm SHA256).Hash)

    Write-Host "Done. Embedded binaries written to $dest" -ForegroundColor Green
}
finally {
    Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
}
