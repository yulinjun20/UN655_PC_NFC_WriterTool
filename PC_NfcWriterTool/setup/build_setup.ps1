#Requires -Version 5.0
<#
.SYNOPSIS
  MSBuild Release → stage files → compile Inno Setup → setup\output\*.exe
#>
param(
  [string]$SolutionRoot = (Split-Path -Parent $PSScriptRoot),
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$SetupDir = $PSScriptRoot
$Staging = Join-Path $SetupDir "staging"
$Iss = Join-Path $SetupDir "PC_NfcWriterTool.iss"
$Sln = Join-Path $SolutionRoot "PC_NfcWriterTool.sln"
$ProjDir = Join-Path $SolutionRoot "PC_NfcWriterTool"
$OutDir = Join-Path $ProjDir "bin\$Configuration"

$msbuild = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuild)) {
  $msbuild = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuild)) {
  throw "MSBuild not found. Install VS 2022 with .NET desktop development."
}

$isccCandidates = @(
  "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
  "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe",
  "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "ISCC.exe not found. Install Inno Setup 6." }

Write-Host "==> MSBuild $Configuration"
& $msbuild $Sln /p:Configuration=$Configuration /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw "MSBuild failed: $LASTEXITCODE" }

$exe = Join-Path $OutDir "PC_NfcWriterTool.exe"
if (-not (Test-Path $exe)) { throw "Missing $exe" }

Write-Host "==> Stage $Staging"
if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item -ItemType Directory -Path $Staging | Out-Null

$required = @(
  "PC_NfcWriterTool.exe",
  "PC_NfcWriterTool.exe.config",
  "System.Data.SQLite.dll",
  "ExcelDataReader.dll"
)
foreach ($name in $required) {
  $src = Join-Path $OutDir $name
  if (-not (Test-Path $src)) { throw "Missing required: $src" }
  Copy-Item -Force $src (Join-Path $Staging $name)
}

foreach ($arch in @("x86", "x64")) {
  $interop = Join-Path $OutDir "$arch\SQLite.Interop.dll"
  if (-not (Test-Path $interop)) { throw "Missing $interop" }
  $destArch = Join-Path $Staging $arch
  New-Item -ItemType Directory -Force -Path $destArch | Out-Null
  Copy-Item -Force $interop (Join-Path $destArch "SQLite.Interop.dll")
}

# docs + templates from solution root (not bin data/)
$docsSrc = Join-Path $SolutionRoot "docs"
if (Test-Path $docsSrc) {
  Copy-Item -Recurse -Force $docsSrc (Join-Path $Staging "docs")
}
$tplSrc = Join-Path $SolutionRoot "templates"
$tplDst = Join-Path $Staging "templates"
New-Item -ItemType Directory -Force -Path $tplDst | Out-Null
$tplNames = @(
  "README.txt",
  "发卡导入模板.csv","发卡导入模板.txt","发卡导入模板.xlsx",
  "import_template.csv","import_template.txt","import_template.xlsx"
)
foreach ($n in $tplNames) {
  $f = Join-Path $tplSrc $n
  if (Test-Path $f) { Copy-Item -Force $f (Join-Path $tplDst $n) }
}

# Never stage test DB
$badDb = Join-Path $Staging "data"
if (Test-Path $badDb) { Remove-Item $badDb -Recurse -Force }

Write-Host "==> ISCC $Iss"
Push-Location $SetupDir
try {
  & $iscc $Iss
  if ($LASTEXITCODE -ne 0) { throw "ISCC failed: $LASTEXITCODE" }
} finally {
  Pop-Location
}

Get-ChildItem (Join-Path $SetupDir "output\*.exe") | ForEach-Object {
  Write-Host ("Setup: {0} ({1:N0} bytes)" -f $_.FullName, $_.Length)
}

