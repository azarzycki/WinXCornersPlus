<#
.SYNOPSIS
  Builds the WinXCornersPlus MSI for one architecture from a release payload (installer/*.wixproj, WiX restored
  from NuGet) and checks the package it produced: name, publisher, version, platform, UpgradeCode, files.

.DESCRIPTION
  Windows only. The default output is the release asset WinXCornersPlus-<version>-<arch>.msi. The installer
  tests build two more packages from the same payload with -OutputName and:
    -ProductVersion      a higher version, to test the upgrade (and, installed first, the downgrade refusal);
    -DotNetCheckVersion  a .NET version that cannot exist, to simulate a machine without the runtime.
  Those test packages are never release assets.

.EXAMPLE
  ./build/release/New-ReleaseInstaller.ps1 -PayloadDirectory payload -OutputDirectory dist -Version 1.5.0 -Architecture x64 -ProjectUrl https://github.com/owner/repo
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $PayloadDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [ValidateSet('x64', 'arm64')] [string] $Architecture,
    [Parameter(Mandatory)] [string] $ProjectUrl,
    [string] $OutputName,
    [string] $ProductVersion,
    [string] $DotNetCheckVersion = '10.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0
Import-Module (Join-Path $PSScriptRoot 'ReleaseTools.psm1') -Force

# The values below reach MSBuild as /p: arguments and the WiX preprocessor, so they are validated strictly.
if ($ProjectUrl -notmatch '\Ahttps://[A-Za-z0-9.-]+(:[0-9]+)?/[A-Za-z0-9-]+/[A-Za-z0-9._-]+\z') { throw "ProjectUrl '$ProjectUrl' is not https://host/owner/repo." }
if ($DotNetCheckVersion -notmatch '\A[0-9]+\.[0-9]+\.[0-9]+\z') { throw "DotNetCheckVersion '$DotNetCheckVersion' is not X.Y.Z." }
if (-not $ProductVersion) { $ProductVersion = ConvertTo-MsiProductVersion $Version }
$ProductVersion = ConvertTo-MsiProductVersion $ProductVersion
$assetName = Get-ReleaseAssetName -Kind msi -Architecture $Architecture -Version $Version
if (-not $OutputName) { $OutputName = [System.IO.Path]::GetFileNameWithoutExtension($assetName) }
if ($OutputName -notmatch '\A[0-9A-Za-z._-]+\z') { throw "OutputName '$OutputName' is not a plain file name." }

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $root 'installer/WinXCornersPlus.Installer.wixproj'
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
$buildOutput = Join-Path ([System.IO.Path]::GetTempPath()) ("wxc-msi-" + [guid]::NewGuid().ToString('N'))

Write-Host "Building $OutputName.msi: $Architecture, ProductVersion $ProductVersion, .NET check $DotNetCheckVersion, payload $payload"
& dotnet build $project -c Release --no-incremental -nologo `
    "-p:InstallerPlatform=$Architecture" "-p:ProductVersion=$ProductVersion" "-p:PayloadDir=$payload" `
    "-p:ProjectUrl=$ProjectUrl" "-p:DotNetCheckVersion=$DotNetCheckVersion" "-p:OutputName=$OutputName" `
    "-p:OutputPath=$buildOutput\"
if ($LASTEXITCODE -ne 0) { throw "The installer build failed (exit code $LASTEXITCODE)." }

$built = Join-Path $buildOutput "$OutputName.msi"
if (-not (Test-Path -LiteralPath $built)) {
    $found = @(Get-ChildItem -LiteralPath $buildOutput -Recurse -Filter *.msi | ForEach-Object FullName)
    throw "Expected $built; the build produced: $($found -join ', ')"
}
$msiPath = Join-Path $output "$OutputName.msi"
Copy-Item -LiteralPath $built -Destination $msiPath -Force
Remove-Item -LiteralPath $buildOutput -Recurse -Force

# --- Read the package back through the Windows Installer API -------------------------------------------
# PowerShell's COM adapter calls the methods and parameterized properties (StringData, Property) directly.
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase([string] $msiPath, [int] 0) # msiOpenDatabaseModeReadOnly
$facts = [ordered]@{}
$files = [System.Collections.Generic.List[string]]::new()
try {
    foreach ($name in 'ProductName', 'Manufacturer', 'ProductVersion', 'UpgradeCode', 'ALLUSERS', 'ARPURLINFOABOUT') {
        $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$name'")
        $view.Execute()
        $record = $view.Fetch()
        $facts[$name] = if ($null -eq $record) { $null } else { $record.StringData(1) }
        $view.Close()
    }
    $view = $database.OpenView('SELECT `FileName` FROM `File`')
    $view.Execute()
    while ($null -ne ($record = $view.Fetch())) {
        # FileName is "SHORT~1.EXT|LongName.ext" when a short name exists.
        $files.Add(($record.StringData(1) -split '\|')[-1])
    }
    $view.Close()
    $facts['Template'] = $database.SummaryInformation(0).Property(7)
}
finally {
    foreach ($com in @($database, $installer)) { if ($com) { [void] [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) } }
    [System.GC]::Collect(); [System.GC]::WaitForPendingFinalizers()
}

$facts.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-16} {1}" -f $_.Key, $_.Value) }
Write-Host "  Files            $($files -join ', ')"
$expectedTemplate = if ($Architecture -eq 'arm64') { 'Arm64;1033' } else { 'x64;1033' }
$problems = @(
    if ($facts.ProductName -cne 'WinXCornersPlus') { "ProductName is '$($facts.ProductName)'." }
    if ($facts.Manufacturer -cne 'Asteski') { "Manufacturer is '$($facts.Manufacturer)'." }
    if ($facts.ProductVersion -cne $ProductVersion) { "ProductVersion is '$($facts.ProductVersion)', expected '$ProductVersion'." }
    if ($facts.UpgradeCode -cne '{791AA4DB-70E0-473F-983F-779AF25323D4}') { "UpgradeCode is '$($facts.UpgradeCode)'." }
    if ($facts.ALLUSERS -cne '1') { "ALLUSERS is '$($facts.ALLUSERS)': not a per-machine package." }
    if ($facts.ARPURLINFOABOUT -cne $ProjectUrl) { "ARPURLINFOABOUT is '$($facts.ARPURLINFOABOUT)'." }
    if ($facts.Template -cne $expectedTemplate) { "Platform (summary Template) is '$($facts.Template)', expected '$expectedTemplate'." }
    $payloadFiles = @(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object Name)
    foreach ($name in $payloadFiles) { if ($name -cnotin $files) { "$name from the payload is not in the package." } }
    foreach ($name in $files) { if ($name -cnotin $payloadFiles) { "$name is in the package but not in the payload." } }
)
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "::error::$_" }
    throw "The installer package failed its checks ($($problems.Count) problem(s))."
}

$size = (Get-Item -LiteralPath $msiPath).Length
Write-Host "Installer OK: $msiPath ($size bytes)"
if ($env:GITHUB_STEP_SUMMARY) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value @(
        "## ``$OutputName.msi``", '',
        "ProductVersion ``$($facts.ProductVersion)``, platform ``$($facts.Template)``, per-machine, $($files.Count) files, $size bytes.", '')
}
