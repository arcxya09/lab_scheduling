<#
  Builds both release downloads into dist\release\:
    LabSchedule-<version>-win-x64-portable.zip   解压即用的便携版（整个程序文件夹）
    LabSchedule-<version>-win-x64-setup.msi      安装版（默认装到当前用户，无需管理员权限）
#>
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
[xml]$csproj = Get-Content (Join-Path $root "src\LabSchedule.App\LabSchedule.App.csproj") -Encoding UTF8
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$productName = ($csproj.Project.PropertyGroup | Where-Object { $_.Product } | Select-Object -First 1).Product
$shortVersion = ($version -split '\.')[0..1] -join '.'
$publishDir = Join-Path $root "dist\$shortVersion\$productName-$Runtime"
$releaseDir = Join-Path $root "dist\release"

& (Join-Path $PSScriptRoot "publish-win.ps1") -Runtime $Runtime
if (Test-Path $releaseDir) { Remove-Item -Recurse -Force $releaseDir }
New-Item -ItemType Directory -Force $releaseDir | Out-Null

# Portable: the published folder, zipped with the folder itself at the top level.
# Entries are added one by one so paths use "/" (Windows PowerShell's CreateFromDirectory writes "\").
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipPath = Join-Path $releaseDir "LabSchedule-$version-$Runtime-portable.zip"
$folderName = Split-Path -Leaf $publishDir
$zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $publishDir -Recurse -File) {
        $entryName = "$folderName/" + $file.FullName.Substring($publishDir.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $entryName, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally {
    $zip.Dispose()
}

# Installer: WiX v5 MSI built from the same folder.
dotnet build (Join-Path $root "installer\LabSchedule.Installer.wixproj") -c Release `
    "-p:PublishDir=$publishDir\" "-p:ProductVersion=$version" "-p:OutputPath=$releaseDir\"
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
Get-ChildItem $releaseDir -Filter *.wixpdb | Remove-Item -Force

Write-Host "Release files:"
Get-ChildItem $releaseDir | ForEach-Object { Write-Host ("  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
