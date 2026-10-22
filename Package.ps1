param([switch]$SkipBuild, [string]$PublishDirectory = 'dist')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
git diff --quiet
if ($LASTEXITCODE -ne 0) { throw 'Stage all source changes before packaging so the source ZIP matches the build.' }
$untracked = @(git ls-files --others --exclude-standard)
if ($LASTEXITCODE -ne 0 -or $untracked.Count -gt 0) { throw 'Stage intended source files or ignore local files before packaging.' }
if (-not $SkipBuild) {
    & "$PSScriptRoot\Build.ps1" -Test
}
if (-not (Test-Path (Join-Path $PublishDirectory 'SkullgirlsModManager.exe'))) { throw 'Build the app first.' }
$version = ([xml](Get-Content 'App\App.csproj' -Raw)).Project.PropertyGroup.Version
$output = Join-Path $PSScriptRoot 'artifacts'
New-Item -ItemType Directory -Path $output -Force | Out-Null
# Explicit files only: never bundle the user's mod library, settings or backups.
$binaryFiles = Get-ChildItem $PublishDirectory -File | Where-Object { $_.Extension -in '.exe', '.dll', '.json', '.md' -or $_.Name -eq 'LICENSE' }
$binaryZip = Join-Path $output "SkullgirlsModManager-$version-windows.zip"
Compress-Archive -Path @($binaryFiles.FullName) -DestinationPath $binaryZip -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($binaryZip, [System.IO.Compression.ZipArchiveMode]::Update)
try {
    foreach ($file in Get-ChildItem (Join-Path $PublishDirectory 'licenses') -File) {
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "licenses/$($file.Name)") | Out-Null
    }
    $docsDirectory = Join-Path $PublishDirectory 'docs'
    $publishRoot = (Resolve-Path $PublishDirectory).Path.TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem $docsDirectory -Filter '*.md' -File -Recurse) {
        $relative = $file.FullName.Substring($publishRoot.Length).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $relative) | Out-Null
    }
} finally { $archive.Dispose() }
# Git's index is an explicit source allowlist, including staged changes before the first commit.
$sourceTree = git write-tree
if ($LASTEXITCODE -ne 0) { throw 'Stage the intended source files in Git before packaging.' }
git archive --format=zip --output="$output\SkullgirlsModManager-$version-source.zip" $sourceTree
if ($LASTEXITCODE -ne 0) { throw 'Source packaging failed.' }
Write-Host "Packages ready in $output. Distribute the matching source ZIP with the Windows ZIP."
