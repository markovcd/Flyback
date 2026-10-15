# Installs an MSIX from pack-msix.ps1 the way the Store would, asks it how it is doing, and
# removes it again (ADR-0199). It signs a copy with a throwaway certificate named for the
# manifest's publisher and trusts that certificate for the run, since Windows installs no
# unsigned package.
#
#   pwsh scripts/store-smoke.ps1 -Msix dist/flyback-1.4.0-win-x64.msix -Version 1.4.0
#
# Checks that the command line answers through its alias with the release's version, and
# that it installs plugins under %AppData%\Flyback, the Store copy's own folder being
# read-only. Needs an administrator, for the machine's TrustedPeople store, and
# signtool.exe from the Windows SDK. Exit 0 when every check holds; otherwise it throws,
# naming the first that did not.

param(
    [Parameter(Mandatory)] [string] $Msix,
    [Parameter(Mandatory)] [string] $Version
)

$ErrorActionPreference = 'Stop'

$manifest = [xml] (Get-Content -Raw (Join-Path $PSScriptRoot '..\src\Flyback.Editor.Desktop\Store\AppxManifest.xml'))
$identity = $manifest.Package.Identity

$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" |
    Sort-Object { [version] $_.Directory.Parent.Name } | Select-Object -Last 1

if (-not $signtool) { throw 'signtool.exe is not installed; it comes with the Windows SDK.' }

$work = Join-Path ([IO.Path]::GetTempPath()) "flyback-smoke-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null

$certificate = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher `
    -KeyUsage DigitalSignature -FriendlyName 'Flyback Store smoke' `
    -CertStoreLocation Cert:\CurrentUser\My `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

$trusted = $null

try {
    $public = Join-Path $work 'smoke.cer'
    Export-Certificate -Cert $certificate -FilePath $public | Out-Null
    $trusted = Import-Certificate -FilePath $public -CertStoreLocation Cert:\LocalMachine\TrustedPeople

    $signed = Join-Path $work (Split-Path -Leaf $Msix)
    Copy-Item $Msix $signed
    & $signtool.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint $signed
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with $LASTEXITCODE." }

    Add-AppxPackage -Path $signed
    $package = Get-AppxPackage -Name $identity.Name
    if (-not $package) { throw "$($identity.Name) did not install." }
    if ($package.Version -ne "$Version.0") { throw "Installed $($package.Version), not $Version.0." }

    $cli = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\flyback-cli.exe'
    if (-not (Test-Path $cli)) { throw 'The flyback-cli alias was not made.' }

    $said = (& $cli --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw "flyback-cli --version failed with $LASTEXITCODE." }
    if (-not $said.StartsWith($Version)) { throw "flyback-cli says it is '$said', not $Version." }

    $plugins = (& $cli plugin list --json | Out-String | ConvertFrom-Json).plugins
    $expected = Join-Path $env:APPDATA 'Flyback\plugins'
    if ($plugins -ne $expected) { throw "A Store copy installs plugins in '$plugins', not '$expected'." }

    Write-Output "The Store package installs, answers as $said and installs plugins in $plugins."
}
finally {
    Get-AppxPackage -Name $identity.Name | Remove-AppxPackage -ErrorAction SilentlyContinue
    if ($trusted) { Remove-Item -Path "Cert:\LocalMachine\TrustedPeople\$($trusted.Thumbprint)" -ErrorAction SilentlyContinue }
    Remove-Item -Path "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
