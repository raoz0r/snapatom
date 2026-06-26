# Scripts\build_and_test.ps1
# This script helps to clean, build, publish, run, and package the SnapAtom application locally.

$ErrorActionPreference = "Stop"

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "         SnapAtom Local Build Tool       " -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

# 1. Stop running processes of SnapAtom to prevent file locks
Write-Host "Checking for running instances of SnapAtom.exe..." -ForegroundColor Yellow
$runningProcesses = Get-Process -Name "SnapAtom" -ErrorAction SilentlyContinue
if ($runningProcesses) {
    Write-Host "Stopping running instances of SnapAtom..." -ForegroundColor Yellow
    Stop-Process -Name "SnapAtom" -Force
    Start-Sleep -Seconds 1
}

# 2. Clean previous build outputs
Write-Host "Cleaning build folders..." -ForegroundColor Yellow
$foldersToClean = @("bin", "obj", "publish_output")
foreach ($folder in $foldersToClean) {
    if (Test-Path $folder) {
        Remove-Item -Path $folder -Recurse -Force
    }
}
if (Test-Path "SnapAtom.msi") {
    Remove-Item -Path "SnapAtom.msi" -Force
}

# 3. Publish the C# App as a single-file executable
Write-Host "Publishing SnapAtom as a single-file Win-x64 executable..." -ForegroundColor Yellow
dotnet publish SnapAtom.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o publish_output

if ($LASTEXITCODE -ne 0) {
    Write-Error "Dotnet publish failed!"
    exit $LASTEXITCODE
}
Write-Host "Publish successful! Executable is located in publish_output\" -ForegroundColor Green

# 4. Prompt to run the published app directly (fastest loop)
$runApp = Read-Host "Would you like to run the published application directly now? (Y/N)"
if ($runApp -eq 'y' -or $runApp -eq 'Y') {
    Write-Host "Starting publish_output\SnapAtom.exe..." -ForegroundColor Green
    Start-Process -FilePath "publish_output\SnapAtom.exe" -WorkingDirectory "publish_output"
    Write-Host "SnapAtom is now running from publish_output. Check the system tray!" -ForegroundColor Cyan
}

# 5. Prompt to build the MSI package
$buildMsi = Read-Host "Would you like to compile the WiX MSI package locally? (Y/N)"
if ($buildMsi -eq 'y' -or $buildMsi -eq 'Y') {
    # Check if wix CLI is installed
    $wixCheck = Get-Command wix -ErrorAction SilentlyContinue
    if (-not $wixCheck) {
        Write-Host "WiX CLI not found. Attempting to install it via dotnet tool..." -ForegroundColor Yellow
        dotnet tool install --global wix --ignore-failed-sources
        wix eula accept wix7
    }

    Write-Host "Building MSI Package..." -ForegroundColor Yellow
    dotnet build SnapAtom.wixproj -c Release -p:PublishDir=publish_output -p:Version=1.0.0
    
    if ($LASTEXITCODE -ne 0) {
         Write-Error "MSI build failed!"
         exit $LASTEXITCODE
    }

    if (Test-Path "bin\Release\SnapAtom.msi") {
        Copy-Item -Path "bin\Release\SnapAtom.msi" -Destination "SnapAtom.msi" -Force
        Write-Host "MSI Package created successfully at: .\SnapAtom.msi" -ForegroundColor Green

        $installMsi = Read-Host "Would you like to run/install the local MSI package? (Y/N)"
        if ($installMsi -eq 'y' -or $installMsi -eq 'Y') {
            Write-Host "Launching MSI installer..." -ForegroundColor Green
            Start-Process -FilePath "SnapAtom.msi"
        }
    } else {
        Write-Error "Could not find MSI package at bin\Release\SnapAtom.msi"
    }
}

Write-Host "Done!" -ForegroundColor Green
