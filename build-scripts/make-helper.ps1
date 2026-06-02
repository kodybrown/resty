# Helper commands for GNU Make recipes on Windows.

if ($args.Count -lt 1) {
  Write-Error "Usage: make-helper.ps1 command [options]"
  exit 1
}

if ($PSBoundParameters.ContainsKey('Debug')) {
  Write-Host "args:"
  foreach ($a in $args) {
    Write-Host "- $a"
  }
}

switch ($args[0]) {
  "copy-file" {
    if ($args.Count -ne 3) {
      Write-Error "Usage: make-helper.ps1 copy-file [source] [destination]"
      exit 1
    }
    $source = $args[1]
    $destination = $args[2]

    if (-not (Test-Path -Type Leaf -LiteralPath $source)) {
      Write-Error "Source file does not exist: $source (copy-file)"
      exit 1
    }

    Write-Debug "Copying file from $source to $destination"
    Copy-Item -LiteralPath $source -Destination $destination -Force
  }
  "move-file" {
    if ($args.Count -ne 3) {
      Write-Error "Usage: make-helper.ps1 move-file [source] [destination]"
      exit 1
    }
    $source = $args[1]
    $destination = $args[2]

    if (-not (Test-Path -Type Leaf -LiteralPath $source)) {
      Write-Error "Source file does not exist: $source (move-file)"
      exit 1
    }

    Write-Debug "Moving file from $source to $destination"
    Move-Item -LiteralPath $source -Destination $destination -Force
  }
  "delete-file" {
    if ($args.Count -lt 2) {
      Write-Error "Usage: make-helper.ps1 delete-file [file] [...]"
      exit 1
    }
    $files = $args[1..($args.Count - 1)]

    foreach ($f in $files) {
      # If the file contains a wildcard...
      if ($f -like "*`*") {
        Get-ChildItem -Path $f -File -ErrorAction Ignore | ForEach-Object {
          Write-Debug "Deleting file: $($_.FullName)"
          Remove-Item -Force $_.FullName
        }
        continue
      }

      if (Test-Path -Type Leaf -LiteralPath $f) {
        Write-Debug "Deleting file: $f"
        Remove-Item -LiteralPath $f -Force
      }
    }
  }
  "make-dir" {
    if ($args.Count -lt 2) {
      Write-Error "Usage: make-helper.ps1 make-dir [directory] [...]"
      exit 1
    }
    $dirs = $args[1..($args.Count - 1)]

    foreach ($d in $dirs) {
      Write-Debug "Creating directory: $d"
      New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
  }
  "copy-dir" {
    if ($args.Count -ne 3) {
      Write-Error "Usage: make-helper.ps1 copy-dir [source] [destination]"
      exit 1
    }
    $source = $args[1]
    $destination = $args[2]

    if (-not (Test-Path -Type Container -LiteralPath $source)) {
      Write-Error "Source directory does not exist: $source"
      exit 1
    }

    Write-Debug "Copying directory from $source to $destination"
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
  }
  "move-dir" {
    if ($args.Count -ne 3) {
      Write-Error "Usage: make-helper.ps1 move-dir [source] [destination]"
      exit 1
    }
    $source = $args[1]
    $destination = $args[2]

    if (-not (Test-Path -Type Container -LiteralPath $source)) {
      Write-Error "Source directory does not exist: $source"
      exit 1
    }

    Write-Debug "Moving directory from $source to $destination"
    Move-Item -LiteralPath $source -Destination $destination -Force
  }
  "delete-dir" {
    if ($args.Count -lt 2) {
      Write-Error "Usage: make-helper.ps1 delete-dir [directory] [...]"
      exit 1
    }
    $dirs = $args[1..($args.Count - 1)]

    foreach ($d in $dirs) {
      if (Test-Path -Type Container -LiteralPath $d) {
        Write-Debug "Deleting directory: $d"
        Remove-Item -LiteralPath $d -Recurse -Force
      }
    }
  }
  "zip-dir" {
    if ($args.Count -ne 3) {
      Write-Error "Usage: make-helper.ps1 zip-dir [source_dir] [destination.zip]"
      exit 1
    }

    # Remove trailing slashes from the zip file path, so that
    # the directory name is included in the zip instead of just its files.
    $source = $args[1].TrimEnd('\', '/')
    $zipFile = $args[2]

    if (-not (Test-Path -Type Container -LiteralPath $source)) {
      Write-Error "Source directory does not exist: $source"
      exit 1
    }

    Write-Debug "Zipping directory from $source to $zipFile"
    Compress-Archive -Path $source -DestinationPath $zipFile -Force
  }
  default {
    Write-Error "Unknown command: $($args[0])"
    exit 1
  }
}
