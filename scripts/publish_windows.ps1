param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "dist\\publish\\$Runtime"
$zipPath = Join-Path $root "dist\\CherryKeyLayout-$Runtime.zip"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

dotnet publish "$root\\CherryKeyLayout.Gui\\CherryKeyLayout.Gui.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishTrimmed=false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $publishDir

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $zipPath) | Out-Null
Compress-Archive -Path "$publishDir\\*" -DestinationPath $zipPath

Write-Host "Release zip created: $zipPath"
