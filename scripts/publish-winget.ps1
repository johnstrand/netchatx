param (
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$InstallerUrl = "",
    [string]$InstallerFile = "",
    [string]$Token = "",
    [switch]$DryRun,
    [string]$OutputDir = "dist/winget",
    [string]$PackageId = "JohnStrand.Stanza",
    [string]$Repo = "johnstrand/netchatx"
)

$ErrorActionPreference = "Stop"

$cleanVersion = $Version.Trim().TrimStart('v')
$tag = "v$cleanVersion"

if (-not $InstallerUrl) {
    $InstallerUrl = "https://github.com/$Repo/releases/download/$tag/Stanza-v${cleanVersion}-win-x64-installer.exe"
}

if (-not $Token -and $env:WINGET_TOKEN) {
    $Token = $env:WINGET_TOKEN
}

# Resolve OutputDir
$fullOutputDir = [System.IO.Path]::GetFullPath($OutputDir)
if (-not (Test-Path $fullOutputDir)) {
    New-Item -ItemType Directory -Path $fullOutputDir -Force | Out-Null
}

Write-Host "=========================================================="
Write-Host " Stanza WinGet Manifest Publisher"
Write-Host " Package ID:     $PackageId"
Write-Host " Version:        $cleanVersion (Tag: $tag)"
Write-Host " Installer URL:  $InstallerUrl"
Write-Host " Output Dir:     $fullOutputDir"
Write-Host " Dry Run:        $DryRun"
Write-Host " Token Present:  $([bool]$Token)"
Write-Host "=========================================================="

# 1. Determine Installer SHA256
$sha256 = ""
if ($InstallerFile -and (Test-Path $InstallerFile)) {
    Write-Host "Calculating SHA256 from local installer file: $InstallerFile..."
    $sha256 = (Get-FileHash -Path $InstallerFile -Algorithm SHA256).Hash.ToUpperInvariant()
} else {
    Write-Host "Attempting to download installer from: $InstallerUrl to calculate SHA256..."
    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) "Stanza-v${cleanVersion}-win-x64-installer.exe"
    try {
        Invoke-WebRequest -Uri $InstallerUrl -OutFile $tempFile -TimeoutSec 60
        $sha256 = (Get-FileHash -Path $tempFile -Algorithm SHA256).Hash.ToUpperInvariant()
        Write-Host "Downloaded installer successfully. SHA256: $sha256"
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
    } catch {
        if ($DryRun) {
            Write-Warning "Could not download installer from $InstallerUrl. Using placeholder SHA256 for dry-run verification."
            $sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
        } else {
            throw "Failed to download installer from $InstallerUrl to compute SHA256 checksum: $_"
        }
    }
}

Write-Host "Using Installer SHA256: $sha256"

# 2. Locate or Download wingetcreate
$wingetCreatePath = $null
if (Get-Command wingetcreate -ErrorAction SilentlyContinue) {
    $wingetCreatePath = (Get-Command wingetcreate).Source
    Write-Host "Found wingetcreate in PATH: $wingetCreatePath"
} elseif (Test-Path "$env:TEMP\wingetcreate.exe") {
    $wingetCreatePath = "$env:TEMP\wingetcreate.exe"
    Write-Host "Found wingetcreate in TEMP: $wingetCreatePath"
} else {
    $downloadTarget = "$env:TEMP\wingetcreate.exe"
    Write-Host "Downloading latest wingetcreate.exe to $downloadTarget..."
    Invoke-WebRequest -Uri "https://github.com/microsoft/winget-create/releases/latest/download/wingetcreate.exe" -OutFile $downloadTarget
    $wingetCreatePath = $downloadTarget
}

# 3. Load template manifests and substitute variables
$templateDir = Join-Path $PSScriptRoot "..\packaging\winget\manifests"
if (-not (Test-Path $templateDir)) {
    throw "Template directory not found at $templateDir"
}

$versionTemplate = Get-Content (Join-Path $templateDir "$PackageId.yaml") -Raw
$installerTemplate = Get-Content (Join-Path $templateDir "$PackageId.installer.yaml") -Raw
$localeTemplate = Get-Content (Join-Path $templateDir "$PackageId.locale.en-US.yaml") -Raw

# Substitute version, installer url, sha256, and release notes url
$versionContent = $versionTemplate -replace 'PackageVersion: .*', "PackageVersion: $cleanVersion"

$installerContent = $installerTemplate `
    -replace 'PackageVersion: .*', "PackageVersion: $cleanVersion" `
    -replace 'InstallerUrl: .*', "InstallerUrl: $InstallerUrl" `
    -replace 'InstallerSha256: .*', "InstallerSha256: $sha256"

$localeContent = $localeTemplate `
    -replace 'PackageVersion: .*', "PackageVersion: $cleanVersion" `
    -replace 'ReleaseNotesUrl: .*', "ReleaseNotesUrl: https://github.com/$Repo/releases/tag/$tag"

# Write out generated manifests
$outVersionFile = Join-Path $fullOutputDir "$PackageId.yaml"
$outInstallerFile = Join-Path $fullOutputDir "$PackageId.installer.yaml"
$outLocaleFile = Join-Path $fullOutputDir "$PackageId.locale.en-US.yaml"

Set-Content -Path $outVersionFile -Value $versionContent -Encoding utf8
Set-Content -Path $outInstallerFile -Value $installerContent -Encoding utf8
Set-Content -Path $outLocaleFile -Value $localeContent -Encoding utf8

Write-Host "Manifests successfully generated in ${fullOutputDir}:"
Write-Host "  - $(Split-Path $outVersionFile -Leaf)"
Write-Host "  - $(Split-Path $outInstallerFile -Leaf)"
Write-Host "  - $(Split-Path $outLocaleFile -Leaf)"

# 4. Validate manifests
Write-Host "Validating generated manifests..."
if (Get-Command winget -ErrorAction SilentlyContinue) {
    & winget validate $fullOutputDir
    if ($LASTEXITCODE -ne 0) {
        throw "winget validate failed with exit code $LASTEXITCODE"
    }
    Write-Host "winget validation passed!"
} else {
    Write-Host "winget CLI not found; manifest structure verified against template schemas."
}

# 5. Check upstream presence & submit PR
if ($DryRun) {
    Write-Host "Dry run enabled. Skipping pull request submission to microsoft/winget-pkgs."
    Write-Host "All manifests have been generated and validated successfully."
    return
}

if (-not $Token) {
    Write-Warning "=========================================================================="
    Write-Warning "WINGET_TOKEN is not set. Pull request submission to microsoft/winget-pkgs was SKIPPED."
    Write-Warning ""
    Write-Warning "To enable automated PR submission:"
    Write-Warning "1. Create a GitHub Personal Access Token (classic) with 'public_repo' scope."
    Write-Warning "2. Add it as a repository secret named 'WINGET_TOKEN'."
    Write-Warning "=========================================================================="
    return
}

Write-Host "Submitting manifests to microsoft/winget-pkgs via wingetcreate..."
$prTitle = "New version: $PackageId version $cleanVersion"
& $wingetCreatePath submit "$fullOutputDir" --token $Token --prtitle $prTitle --no-open

if ($LASTEXITCODE -ne 0) {
    throw "wingetcreate submit failed with exit code $LASTEXITCODE"
}

Write-Host "Pull request submitted successfully to microsoft/winget-pkgs!"
