# Script to install OneSGetDatabases.Web as a Windows Service
param (
    [string]$ServiceName = "OneSGetDatabasesWeb",
    [string]$DisplayName = "1C: Get Databases Web Control Surface",
    [string]$ExePath = "$PSScriptRoot\OneSGetDatabases.Web.exe",
    [switch]$Uninstall,
    [switch]$Start
)

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator privileges are required to manage Windows Services."
    exit 1
}

if ($Uninstall) {
    Write-Host "Stopping and removing service $ServiceName..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName
    Write-Host "Service $ServiceName removed successfully." -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $ExePath)) {
    $possiblePaths = @(
        "$PSScriptRoot\OneSGetDatabases.Web.exe",
        "$PSScriptRoot\bin\Release\net10.0-windows\OneSGetDatabases.Web.exe",
        "$PSScriptRoot\..\..\publish\Web\OneSGetDatabases.Web.exe"
    )
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) { $ExePath = $p; break }
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Executable not found at: $ExePath. Please build the project first (build.ps1)."
    exit 1
}

$fullExePath = (Resolve-Path $ExePath).Path
Write-Host "Registering service $ServiceName..." -ForegroundColor Cyan
Write-Host "Executable path: $fullExePath"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "Service already exists. Reconfiguring..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -ErrorAction SilentlyContinue
    sc.exe config $ServiceName binPath= "`"$fullExePath`"" start= auto
} else {
    New-Service -Name $ServiceName -BinaryPathName "`"$fullExePath`"" -DisplayName $DisplayName -StartupType Automatic -Description "1C:Enterprise database inventory web control surface and DBMS inspector."
}

# Auto recovery on failure (restart in 60s)
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000

Write-Host "Service $ServiceName registered successfully." -ForegroundColor Green

if ($Start) {
    Write-Host "Starting service $ServiceName..." -ForegroundColor Cyan
    Start-Service -Name $ServiceName
    Write-Host "Service $ServiceName started. Web UI available at http://localhost:5070" -ForegroundColor Green
}
