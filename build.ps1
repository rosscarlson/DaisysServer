<#
  Builds a release: Daisy's Server self-contained (the .NET runtime inside, nothing else to install), with every module
  in src\Modules in modules\<name>\ beside it, and its installer. -> artifacts\DaisysServer-Setup-<version>.exe
  Usage:  .\build.ps1                 # version from src\Directory.Build.props
          .\build.ps1 -Version 0.2.0  # override
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$proj = Join-Path $root 'src\DaisysServer\DaisysServer.csproj'
if (-not $Version) {
    $Version = [regex]::Match((Get-Content (Join-Path $root 'src\Directory.Build.props') -Raw), '<Version>(.+?)</Version>').Groups[1].Value
}

$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish'
$built = Join-Path $artifacts 'modules'
foreach ($d in @($publish, $built)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory -Force $artifacts | Out-Null
Get-ChildItem $artifacts -File -Filter '*-Setup-*.exe' | Remove-Item -Force

# ---- the server
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

# ---- the modules: each built into artifacts\modules\<name>, without what the server already carries
dotnet build $proj -c Release -p:Version=$Version | Out-Null
if ($LASTEXITCODE) { throw 'dotnet build of the server failed' }
$appHas = Get-ChildItem (Join-Path $root 'src\DaisysServer\bin\Release\net8.0-windows') -Filter *.dll | ForEach-Object Name
New-Item -ItemType Directory -Force (Join-Path $publish 'modules') | Out-Null
foreach ($module in Get-ChildItem (Join-Path $root 'src\Modules') -Filter *.csproj -Recurse) {
    $name = $module.Directory.Name
    dotnet build $module.FullName -c Release -p:Version=$Version -p:DebugType=none "-p:ModulesOut=$built"
    if ($LASTEXITCODE) { throw "dotnet build of $name failed" }
    $dir = Join-Path $built $name
    Get-ChildItem $dir -File | Where-Object { $appHas -contains $_.Name -or $_.Name -like '*.runtimeconfig.json' -or $_.Extension -eq '.pdb' } | Remove-Item -Force
    Copy-Item $dir (Join-Path $publish 'modules') -Recurse
}

# ---- the installer
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found (winget install JRSoftware.InnoSetup)' }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$artifacts" (Join-Path $root 'installer\DaisysServer.iss')
if ($LASTEXITCODE) { throw 'Inno Setup compile failed' }
Get-Item (Join-Path $artifacts "DaisysServer-Setup-$Version.exe")
