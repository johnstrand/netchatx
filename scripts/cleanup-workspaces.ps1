<#
.SYNOPSIS
    Cleans up Git worktrees/workspaces and local branches whose remote branch no longer exists on origin.

.DESCRIPTION
    1. Runs 'git fetch --prune origin' to refresh remote tracking refs.
    2. Inspects all Git worktrees (excluding the primary repository root).
    3. Identifies worktrees whose corresponding remote branch has been deleted on origin (e.g. merged PRs).
    4. Safely removes the worktrees, prunes references, and optionally deletes the local branch.
    5. Cleans up any orphaned directories in the .worktree/ folder.

.PARAMETER WhatIf
    Previews the worktrees and branches that would be cleaned up without making changes.

.PARAMETER DryRun
    Alias for -WhatIf.

.PARAMETER Force
    Removes worktrees and deletes branches without prompting for confirmation.

.PARAMETER DeleteLocalBranch
    Deletes the local Git branch associated with the removed worktree. Defaults to $true.

.PARAMETER DeleteMerged
    Also cleans up worktrees whose branch is fully merged into 'main', even if the remote tracking branch still exists. Defaults to $false.

.PARAMETER Remote
    The name of the Git remote to check against. Defaults to 'origin'.

.EXAMPLE
    pwsh ./scripts/cleanup-workspaces.ps1 -WhatIf
    Previews all workspaces whose remote branch no longer exists.

.EXAMPLE
    pwsh ./scripts/cleanup-workspaces.ps1
    Cleans up dead workspaces with confirmation prompts.

.EXAMPLE
    pwsh ./scripts/cleanup-workspaces.ps1 -Force
    Cleans up all dead workspaces and branches non-interactively.
#>

[CmdletBinding()]
param(
    [switch]$WhatIf,
    [switch]$DryRun,
    [switch]$Force,
    [bool]$DeleteLocalBranch = $true,
    [switch]$DeleteMerged,
    [string]$Remote = "origin"
)

$isDryRun = $WhatIf -or $DryRun

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "  Stanza Workspace & Worktree Cleanup    " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Determine primary repository root and current working tree
try {
    $currentWorktree = [System.IO.Path]::GetFullPath((git rev-parse --show-toplevel 2>$null).Trim())
    $gitCommonDir = (git rev-parse --git-common-dir 2>$null).Trim()
    if ($gitCommonDir) {
        if (-not [System.IO.Path]::IsPathRooted($gitCommonDir)) {
            $gitCommonDir = [System.IO.Path]::GetFullPath((Join-Path $currentWorktree $gitCommonDir))
        }
        $repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $gitCommonDir))
    }
    else {
        $repoRoot = $currentWorktree
    }
}
catch {
    Write-Error "Not inside a Git repository or Git is not in PATH."
    exit 1
}

Write-Host "Primary repository root: $repoRoot" -ForegroundColor Gray
if ($currentWorktree.TrimEnd('\', '/') -ne $repoRoot.TrimEnd('\', '/')) {
    Write-Host "Current working directory is inside worktree: $currentWorktree" -ForegroundColor DarkGray
}

# 2. Prune remote tracking references
Write-Host "Fetching and pruning remote '$Remote'..." -ForegroundColor Yellow
if (-not $isDryRun) {
    git fetch --prune $Remote 2>$null
}

# 3. Parse worktrees via git worktree list --porcelain
$worktreeOutput = git worktree list --porcelain
if (-not $worktreeOutput) {
    Write-Host "No worktrees found." -ForegroundColor Green
    exit 0
}

$worktrees = @()
$currentWt = @{}

foreach ($line in $worktreeOutput) {
    $line = $line.Trim()
    if ([string]::IsNullOrWhiteSpace($line)) {
        if ($currentWt.ContainsKey("worktree")) {
            $worktrees += [PSCustomObject]$currentWt
            $currentWt = @{}
        }
        continue
    }

    if ($line -match '^worktree\s+(.+)$') {
        $currentWt["worktree"] = [System.IO.Path]::GetFullPath($Matches[1].Trim())
    }
    elseif ($line -match '^HEAD\s+(.+)$') {
        $currentWt["HEAD"] = $Matches[1].Trim()
    }
    elseif ($line -match '^branch\s+refs/heads/(.+)$') {
        $currentWt["branch"] = $Matches[1].Trim()
    }
    elseif ($line -match '^detached') {
        $currentWt["detached"] = $true
    }
}

if ($currentWt.ContainsKey("worktree")) {
    $worktrees += [PSCustomObject]$currentWt
}

# Filter out the primary root worktree
$secondaryWorktrees = $worktrees | Where-Object {
    $_.worktree.TrimEnd('\', '/') -ne $repoRoot.TrimEnd('\', '/')
}

if ($secondaryWorktrees.Count -eq 0) {
    Write-Host "No secondary worktrees found to inspect." -ForegroundColor Green
}
else {
    Write-Host "Found $($secondaryWorktrees.Count) secondary worktree(s). Checking remote status..." -ForegroundColor Cyan

    $toRemove = @()

    foreach ($wt in $secondaryWorktrees) {
        $branch = $wt.branch
        $path = $wt.worktree
        $reason = ""
        $shouldClean = $false

        if (-not $branch) {
            # Detached HEAD worktree
            $reason = "Detached HEAD worktree"
            $shouldClean = $true
        }
        else {
            # Check if remote branch exists on $Remote
            $remoteRefExists = git rev-parse --verify --quiet "refs/remotes/$Remote/$branch"
            if (-not $remoteRefExists) {
                $reason = "Remote branch '$Remote/$branch' does not exist (deleted or merged)"
                $shouldClean = $true
            }
            elseif ($DeleteMerged) {
                # Check if fully merged into main
                git merge-base --is-ancestor "refs/heads/$branch" "refs/remotes/$Remote/main" 2>$null
                if ($LASTEXITCODE -eq 0) {
                    $reason = "Branch '$branch' is merged into 'main'"
                    $shouldClean = $true
                }
            }
        }

        if ($shouldClean) {
            if ($path.TrimEnd('\', '/') -eq $currentWorktree.TrimEnd('\', '/')) {
                Write-Host "  [SKIP] $path is the current active workspace. (Switch to repository root before removing)." -ForegroundColor Magenta
                continue
            }

            $toRemove += [PSCustomObject]@{
                Path   = $path
                Branch = $branch
                Reason = $reason
            }
        }
        else {
            Write-Host "  [KEEP] $path (Branch: $branch - active on $Remote)" -ForegroundColor Green
        }
    }

    if ($toRemove.Count -eq 0) {
        Write-Host "All existing worktrees have active remotes. Nothing to clean up." -ForegroundColor Green
    }
    else {
        Write-Host ""
        Write-Host "Worktrees to clean up ($($toRemove.Count)):" -ForegroundColor Yellow
        foreach ($item in $toRemove) {
            Write-Host "  - Path: $($item.Path)" -ForegroundColor White
            if ($item.Branch) {
                Write-Host "    Branch: $($item.Branch)" -ForegroundColor Gray
            }
            Write-Host "    Reason: $($item.Reason)" -ForegroundColor Yellow
        }
        Write-Host ""

        if ($isDryRun) {
            Write-Host "[DRY RUN] No changes were made." -ForegroundColor Cyan
        }
        else {
            if (-not $Force) {
                $confirm = Read-Host "Proceed with cleanup of $($toRemove.Count) worktree(s)? (y/N)"
                if ($confirm -ne 'y' -and $confirm -ne 'Y') {
                    Write-Host "Cleanup cancelled by user." -ForegroundColor Yellow
                    exit 0
                }
            }

            foreach ($item in $toRemove) {
                Write-Host "Removing worktree: $($item.Path)..." -ForegroundColor Yellow
                git worktree remove --force "$($item.Path)" 2>&1 | Out-Null

                if (Test-Path $item.Path) {
                    try {
                        Remove-Item -Path $item.Path -Recurse -Force -ErrorAction SilentlyContinue
                    }
                    catch {}
                }

                if ($DeleteLocalBranch -and $item.Branch) {
                    Write-Host "Deleting local branch: $($item.Branch)..." -ForegroundColor Yellow
                    git branch -D "$($item.Branch)" 2>&1 | Out-Null
                }

                Write-Host "  Cleaned: $($item.Path)" -ForegroundColor Green
            }
        }
    }
}

# 4. Check for untracked/orphaned directories in .worktree
$worktreeDir = Join-Path $repoRoot ".worktree"
if (Test-Path $worktreeDir) {
    $existingDirs = Get-ChildItem -Path $worktreeDir -Directory
    $registeredPaths = $worktrees | ForEach-Object { [System.IO.Path]::GetFullPath($_.worktree) }

    $orphanedDirs = $existingDirs | Where-Object {
        $dirPath = [System.IO.Path]::GetFullPath($_.FullName).TrimEnd('\', '/')
        ($dirPath -notin $registeredPaths) -and
        (-not ($registeredPaths | Where-Object { $_.StartsWith($dirPath + [System.IO.Path]::DirectorySeparatorChar) }))
    }

    if ($orphanedDirs.Count -gt 0) {
        Write-Host ""
        Write-Host "Found $($orphanedDirs.Count) orphaned directory/directories in .worktree:" -ForegroundColor Yellow
        foreach ($dir in $orphanedDirs) {
            Write-Host "  - $($dir.FullName)" -ForegroundColor White
        }

        if (-not $isDryRun) {
            if ($Force -or ((Read-Host "Remove orphaned directories? (y/N)") -match '^[yY]')) {
                foreach ($dir in $orphanedDirs) {
                    Remove-Item -Path $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
                    Write-Host "  Removed: $($dir.FullName)" -ForegroundColor Green
                }
            }
        }
    }
}

# 5. Prune worktrees in Git
if (-not $isDryRun) {
    git worktree prune 2>$null
}

Write-Host ""
Write-Host "Cleanup check complete." -ForegroundColor Cyan
