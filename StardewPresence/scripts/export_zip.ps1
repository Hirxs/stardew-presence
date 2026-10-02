param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$projectDir = (Resolve-Path "$PSScriptRoot\..").Path
$manifestPath = Join-Path $projectDir "manifest.json"
$manifestJson = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $manifestJson.Version
}

$stagingDir = Join-Path $projectDir "bin\Release\package\StardewPresence"

if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# Copy release files
Copy-Item (Join-Path $projectDir "bin\Release\net6.0\StardewPresence.dll") -Destination $stagingDir -Force
Copy-Item $manifestPath -Destination $stagingDir -Force
Copy-Item (Join-Path $projectDir "ui_layout.json") -Destination $stagingDir -Force
if (Test-Path (Join-Path $projectDir "assets")) {
    Copy-Item (Join-Path $projectDir "assets") -Destination $stagingDir -Recurse -Force
}
Copy-Item (Join-Path $projectDir "i18n") -Destination $stagingDir -Recurse -Force

# Unblock files
Get-ChildItem -Path $stagingDir -Recurse | Unblock-File

# Output zip paths
$zipName = "StardewPresence-v$Version.zip"
$zipDestinations = @(
    (Join-Path $projectDir "..\$zipName"),
    "C:\Users\ale_y\Downloads\$zipName"
)

foreach ($zipFile in $zipDestinations) {
    if (Test-Path $zipFile) {
        Remove-Item $zipFile -Force
    }
    Compress-Archive -Path $stagingDir -DestinationPath $zipFile -Force
    Write-Host "[ZIP EXPORTED] -> $zipFile" -ForegroundColor Green
}
