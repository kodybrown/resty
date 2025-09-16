#!/usr/bin/env pwsh
#
# Resty Build and Publish Script (PowerShell)
#
# This script builds Resty as a single-file, self-contained binary
# and optionally copies it to the user's Bin directory.
#

param(
  [string]$Configuration = 'Release',
  [string]$Runtime = '',
  [string]$OutputPath = './publish',
  [switch]$SkipCopy,
  [switch]$Help,
  [switch]$Verbose
)

function Show-Help {
  Write-Host 'Resty Build and Publish Script' -ForegroundColor Green
  Write-Host ''
  Write-Host 'USAGE:'
  Write-Host '  ./publish.ps1 [OPTIONS]'
  Write-Host ''
  Write-Host 'OPTIONS:'
  Write-Host '  -Configuration <config>  Build configuration (Debug/Release, default: Release)'
  Write-Host '  -Runtime <rid>          Target runtime identifier (auto-detected if not specified)'
  Write-Host '  -OutputPath <path>      Output directory (default: ./publish)'
  Write-Host '  -SkipCopy              Skip copying to ~/Bin directory'
  Write-Host '  -Verbose               Show detailed build output'
  Write-Host '  -Help                  Show this help message'
  Write-Host ''
  Write-Host 'EXAMPLES:'
  Write-Host '  ./publish.ps1                           # Build for current platform'
  Write-Host '  ./publish.ps1 -Runtime win-x64          # Build for Windows x64'
  Write-Host '  ./publish.ps1 -Runtime linux-x64        # Build for Linux x64'
  Write-Host '  ./publish.ps1 -Runtime osx-x64          # Build for macOS x64'
  Write-Host "  ./publish.ps1 -SkipCopy                 # Don't copy to ~/Bin"
  Write-Host '  ./publish.ps1 -Configuration Debug      # Debug build'
  Write-Host ''
  Write-Host 'SUPPORTED RUNTIMES:'
  Write-Host '  win-x64, win-x86, win-arm64'
  Write-Host '  linux-x64, linux-arm64'
  Write-Host '  osx-x64, osx-arm64'
}

function Get-DefaultRuntime {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if ([Environment]::Is64BitOperatingSystem) {
      return 'win-x64'
    } else {
      return 'win-x86'
    }
  } elseif ($IsLinux) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'linux-arm64'
    } else {
      return 'linux-x64'
    }
  } elseif ($IsMacOS) {
    if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
      return 'osx-arm64'
    } else {
      return 'osx-x64'
    }
  } else {
    return 'win-x64'  # Fallback
  }
}

function Get-ExecutableName {
  param([string]$Runtime)

  if ($Runtime.StartsWith('win')) {
    return 'resty.exe'
  } else {
    return 'resty'
  }
}

function Get-BinDirectory {
  if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    return "$env:USERPROFILE/Bin"
  } else {
    return "$env:HOME/bin"
  }
}

# Show help if requested
if ($Help) {
  Show-Help
  exit 0
}

# Auto-detect runtime if not specified
if ([string]::IsNullOrEmpty($Runtime)) {
  $Runtime = Get-DefaultRuntime
  Write-Host "Auto-detected runtime: $Runtime" -ForegroundColor Cyan
}

# Get executable name based on runtime
$ExecutableName = Get-ExecutableName -Runtime $Runtime

# Set script location as working directory
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $ScriptDir

try {
  Write-Host "Building Resty ($Configuration, $Runtime)..." -ForegroundColor Yellow
  Write-Host ''

  # Build command arguments
  $BuildArgs = @(
    'publish'
    'Resty.Cli/Resty.Cli.csproj'
    '-c', $Configuration
    '-r', $Runtime
    '--self-contained', 'true'
    '/p:PublishSingleFile=true'
    '/p:PublishTrimmed=true'
    '-o', $OutputPath
  )

  if ($Verbose) {
    $BuildArgs += '--verbosity', 'normal'
  } else {
    $BuildArgs += '--verbosity', 'minimal'
  }

  # Execute build
  $Process = Start-Process -FilePath 'dotnet' -ArgumentList $BuildArgs -Wait -PassThru -NoNewWindow

  if ($Process.ExitCode -eq 0) {
    Write-Host ''
    Write-Host '✅ Publish succeeded!' -ForegroundColor Green

    $OutputFile = Join-Path $OutputPath $ExecutableName
    if (Test-Path $OutputFile) {
      $FileInfo = Get-Item $OutputFile
      $FileSizeMB = [math]::Round($FileInfo.Length / 1MB, 2)
      Write-Host "   📦 Output: $OutputFile ($FileSizeMB MB)" -ForegroundColor Gray
    }

    # Copy to Bin directory if not skipped
    if (-not $SkipCopy) {
      $BinDir = Get-BinDirectory
      $BinFile = Join-Path $BinDir $ExecutableName

      if (Test-Path $BinDir) {
        Write-Host ''
        Write-Host "📋 Copying to $BinFile..." -ForegroundColor Cyan
        try {
          Copy-Item -Path $OutputFile -Destination $BinFile -Force
          Write-Host '✅ Copy succeeded!' -ForegroundColor Green

          # Make executable on Unix systems
          if (-not ($IsWindows -or $env:OS -eq 'Windows_NT')) {
            & chmod +x $BinFile
          }
        } catch {
          Write-Host "⚠️  Copy failed: $($_.Exception.Message)" -ForegroundColor Yellow
          Write-Host "   You can manually copy: $OutputFile -> $BinFile" -ForegroundColor Gray
        }
      } else {
        Write-Host ''
        Write-Host "⚠️  Bin directory not found: $BinDir" -ForegroundColor Yellow
        Write-Host '   Create the directory and add it to your PATH, then copy manually:' -ForegroundColor Gray
        Write-Host "   $OutputFile -> $BinFile" -ForegroundColor Gray
      }
    }

    Write-Host ''
    Write-Host '🚀 Build completed successfully!' -ForegroundColor Green
    exit 0
  } else {
    Write-Host ''
    Write-Host '❌ Publish failed!' -ForegroundColor Red
    Write-Host '   Check the build output above for errors.' -ForegroundColor Gray

    if (-not $Verbose) {
      Write-Host '   Try running with -Verbose for more details.' -ForegroundColor Gray
    }

    Read-Host 'Press Enter to exit'
    exit 1
  }
} finally {
  Pop-Location
}
