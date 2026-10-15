# Packs a published win-x64 folder into the MSIX the Microsoft Store is handed (ADR-0199).
#
#   pwsh scripts/pack-msix.ps1 -Folder win-x64 -Version 1.4.0 -Out dist/flyback-1.4.0-win-x64.msix
#
# The manifest and its logos come from src/Flyback.Editor.Desktop/Store; the version is
# the release's with a fourth part of 0, which the Store requires. The package is not
# signed: the Store signs it, and store-smoke.ps1 signs its own copy to install.
# Needs makeappx.exe from the Windows SDK, which GitHub's Windows runners carry.

param(
    [Parameter(Mandatory)] [string] $Folder,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Out
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version is major.minor.patch, not '$Version'." }
if (-not (Test-Path (Join-Path $Folder 'Flyback.exe'))) { throw "$Folder holds no Flyback.exe." }

$store = Join-Path $PSScriptRoot '..\src\Flyback.Editor.Desktop\Store'
$staging = Join-Path ([IO.Path]::GetTempPath()) "flyback-msix-$([Guid]::NewGuid().ToString('N'))"

try {
    Copy-Item -Recurse $Folder $staging
    New-Item -ItemType Directory (Join-Path $staging 'Store') | Out-Null
    Copy-Item (Join-Path $store '*.png') (Join-Path $staging 'Store')

    [xml] $manifest = Get-Content -Raw (Join-Path $store 'AppxManifest.xml')
    $manifest.Package.Identity.Version = "$Version.0"
    $manifest.Save((Join-Path $staging 'AppxManifest.xml'))

    $makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" |
        Sort-Object { [version] $_.Directory.Parent.Name } | Select-Object -Last 1

    if (-not $makeappx) { throw 'makeappx.exe is not installed; it comes with the Windows SDK.' }

    New-Item -ItemType Directory -Force (Split-Path -Parent $Out) | Out-Null
    & $makeappx.FullName pack /o /h SHA256 /d $staging /p $Out
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed with $LASTEXITCODE." }

    Write-Output "Packed $Out"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
