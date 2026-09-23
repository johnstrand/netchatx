<#
.SYNOPSIS
    Alias wrapper for cleanup-workspaces.ps1.
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

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$targetScript = Join-Path $scriptDir "cleanup-workspaces.ps1"

& $targetScript @PSBoundParameters
