<#
  Builds the distributable folder version of the WinUI 3 app.

  Output: dist\<major.minor>\实验排班生成器-win-x64\LabSchedule.exe
  The folder is self-contained (.NET runtime and Windows App SDK included), so it runs on
  Windows 10 1809+ / Windows 11 x64 without installing anything. Distribute the whole folder.
#>
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\LabSchedule.App\LabSchedule.App.csproj"
[xml]$csproj = Get-Content $project -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$shortVersion = ($version -split '\.')[0..1] -join '.'
$productName = ($csproj.Project.PropertyGroup | Where-Object { $_.Product } | Select-Object -First 1).Product
$platform = if ($Runtime -like "*arm64") { "ARM64" } else { "x64" }
$outDir = Join-Path $root "dist\$shortVersion\$productName-$Runtime"

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }

dotnet publish $project `
    -c Release `
    -r $Runtime `
    -p:Platform=$platform `
    --self-contained true `
    -p:WindowsAppSDKSelfContained=true `
    -o $outDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Windows package created:"
Write-Host "Version directory: $(Split-Path -Parent $outDir)"
Write-Host (Join-Path $outDir "LabSchedule.exe")
