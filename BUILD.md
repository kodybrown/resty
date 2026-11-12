# Building Resty

Resty provides multiple build scripts for different platforms and preferences.

## Publish and package with Make

```powershell
make publish RID=win-x64
make package RID=win-x64
```

`make publish` lets MSBuild choose the publish directory and prints the path.
This respects external output paths set in `Directory.Build.props`. Without
custom settings, the .NET SDK uses the project's build output directory.
`make package` writes the archive under `artifacts/packages/`, and
`make install-user` uses the executable from the same publish directory.

## Linux Makefile

For Linux, the simplest install path is the repo-level Makefile:

```bash
# Publish and install to ~/.local/bin/resty
make install-user

# Publish and install to /usr/local/bin/resty
sudo make install
```

Key variables:

- `RID`: target runtime identifier. Defaults to the detected Linux architecture.
- `PREFIX`: installation root. Defaults to `/usr/local`.
- `DESTDIR`: staging prefix for distro packaging.
- `CONFIGURATION`: build configuration. Defaults to `Release`.

Examples:

```bash
# Package into a staging directory
make install DESTDIR="$PWD/package-root" PREFIX=/usr

# Build for ARM64
make publish RID=linux-arm64

# Remove an installed binary
sudo make uninstall
```

## Build Scripts

### Windows Batch Script (`publish.bat`)

Simple batch script that builds for Windows x64 and copies to your `%USERPROFILE%\Bin` directory.

```cmd
publish.bat
```

### PowerShell Script (`publish.ps1`)

Cross-platform PowerShell script with advanced features and platform detection.

```powershell
# Basic usage (auto-detects platform)
./publish.ps1

# Cross-platform builds
./publish.ps1 -Runtime win-x64
./publish.ps1 -Runtime linux-x64
./publish.ps1 -Runtime osx-arm64

# Additional options
./publish.ps1 -Configuration Debug -Verbose
./publish.ps1 -SkipCopy -OutputPath ./dist
./publish.ps1 -Help
```

### Bash Script (`publish.sh`)

Unix/Linux shell script with cross-platform support and colored output.

```bash
# Basic usage (auto-detects platform)
./publish.sh

# Cross-platform builds
./publish.sh --runtime win-x64
./publish.sh --runtime linux-x64
./publish.sh --runtime osx-arm64

# Additional options
./publish.sh --configuration Debug --verbose
./publish.sh --skip-copy --output ./dist
./publish.sh --help
```

## Supported Platforms

| Runtime ID    | Platform           | Architecture |
|---------------|--------------------|--------------|
| `win-x64`     | Windows            | x64          |
| `win-x86`     | Windows            | x86          |
| `win-arm64`   | Windows            | ARM64        |
| `linux-x64`   | Linux              | x64          |
| `linux-arm64` | Linux              | ARM64        |
| `osx-x64`     | macOS              | x64 (Intel)  |
| `osx-arm64`   | macOS              | ARM64 (M1/M2)|

## Output

All scripts create a single-file, self-contained executable that includes:

- ✅ .NET runtime (no installation required on target machine)
- ✅ All dependencies bundled
- ✅ Trimming disabled by default for compatibility with current serializers
- ✅ Optimized for Release builds

## Installation

The scripts automatically copy the built executable to your `bin` directory:

- **Windows**: `%USERPROFILE%\Bin\resty.exe`
- **Unix/Linux**: `$HOME/bin/resty`

To skip this automatic copy, use:
- PowerShell: `-SkipCopy`
- Bash: `--skip-copy`

## Manual Build

If you prefer to build manually:

```bash
dotnet publish Resty.Cli/Resty.Cli.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:PublishTrimmed=false
```

Replace `win-x64` with your target runtime identifier.
