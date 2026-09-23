param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$PublishDir = "publish/win-x64",
    [string]$OutputDir = "dist"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$PublishDir = (Resolve-Path $PublishDir).Path
$OutputDir = (Resolve-Path $OutputDir).Path

$zipName = "Stanza-v${Version}-win-x64.zip"
$zipPath = Join-Path $OutputDir $zipName
Write-Host "Creating Windows portable ZIP: $zipPath..."
Compress-Archive -Path "$PublishDir\*" -DestinationPath $zipPath -Force

# Locate Inno Setup compiler
$isccPath = $null
if (Get-Command iscc -ErrorAction SilentlyContinue) {
    $isccPath = "iscc"
} elseif (Test-Path "C:\Program Files (x86)\Inno Setup 6\ISCC.exe") {
    $isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
}

if ($isccPath) {
    $issPath = (Resolve-Path "packaging\windows\installer.iss").Path
    $iconPath = (Resolve-Path "src\Stanza.Gui\Assets\stanza-logo.ico").Path
    Write-Host "Compiling Inno Setup installer using $isccPath..."
    Write-Host "  SourceDir: $PublishDir"
    Write-Host "  OutputDir: $OutputDir"
    Write-Host "  SetupIcon: $iconPath"
    & $isccPath "/DMyAppVersion=$Version" "/DSourceDir=$PublishDir" "/DOutputDir=$OutputDir" "/DSetupIcon=$iconPath" "$issPath"
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Warning "Inno Setup compiler (iscc) not found. Installer exe was not generated."
}

Write-Host "Windows packaging completed."
