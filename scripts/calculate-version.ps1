param (
    [string]$VersionOverride = "",
    [string]$PropsPath = "Directory.Build.props"
)

$ErrorActionPreference = "Stop"

if ($VersionOverride -ne "") {
    $cleanVersion = $VersionOverride.Trim().TrimStart('v')
    Write-Host "Using version override: $cleanVersion"
    $newVersion = $cleanVersion
} else {
    # 1. Try to find the latest git tag matching v*.*.* or v*.*
    $latestTag = $(git tag -l "v*" --sort=-v:refname 2>$null | Select-Object -First 1)

    $baseVersion = ""
    if ($latestTag) {
        $baseVersion = $latestTag.Trim().TrimStart('v')
        Write-Host "Found latest git tag: $latestTag -> $baseVersion"
    } elseif (Test-Path $PropsPath) {
        [xml]$propsXml = Get-Content $PropsPath
        $baseVersion = $propsXml.Project.PropertyGroup.Version
        Write-Host "Found version in ${PropsPath}: $baseVersion"
    }

    if (-not $baseVersion) {
        $baseVersion = "0.0.0"
        Write-Host "No prior version found. Falling back to default: $baseVersion"
    }

    # Parse major and minor
    if ($baseVersion -match '^(\d+)\.(\d+)') {
        $major = [int]$matches[1]
        $minor = [int]$matches[2]
    } else {
        $major = 0
        $minor = 0
    }

    # User rule: bump minor; when minor hits 10, reset to 0 and bump major
    $minor++
    if ($minor -ge 10) {
        $minor = 0
        $major++
    }
    $patch = 0
    $newVersion = "$major.$minor.$patch"
}

$newTag = "v$newVersion"
Write-Host "Calculated Next Version: $newVersion"
Write-Host "Calculated Next Tag: $newTag"

# If running in GitHub Actions, set step outputs and environment variables
if ($env:GITHUB_OUTPUT) {
    "version=$newVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "tag=$newTag" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

return [PSCustomObject]@{
    Version = $newVersion
    Tag     = $newTag
}
