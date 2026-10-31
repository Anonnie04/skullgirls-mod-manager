param([switch]$Test)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.build'
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.packages' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
if ($Test) {
    dotnet run --project Tests/Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
dotnet publish App/App.csproj -c Release -r win-x64 --self-contained false -o dist/windows-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
dotnet publish Desktop/Desktop.csproj -c Release -r linux-x64 --self-contained true -o dist/linux-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Linux build failed.' }
Write-Host 'Ready: dist/windows-x64/SkullgirlsModManager.exe and dist/linux-x64/SkullgirlsModManager'
