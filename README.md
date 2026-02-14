# Timbermods

Custom mods for Timberborn.

## Mods

- **[PowerZipline](PowerZipline/)** — Power zipline buildings with mechanical power transfer through cables

## How to Install

1. Go to the [Releases](https://github.com/szatmary/timbermods/releases) page and download the `.zip` file for the mod you want
2. Find your Timberborn mods folder:
   - **Windows**: `Documents\Timberborn\Mods\`
   - **macOS**: `~/Documents/Timberborn/Mods/`
   - **Linux**: `~/.config/unity3d/Mechanistry/Timberborn/Mods/`
3. Create a new folder inside `Mods/` (e.g. `PowerZipline`)
4. Extract the zip contents into that folder
5. Launch Timberborn — it will automatically detect the new mod and ask you to enable it on startup

To uninstall, delete the mod's folder and restart the game.

---

## Developer Guide

Everything below is for building mods from source.

### Prerequisites

#### macOS

1. Install the .NET SDK via Homebrew:
   ```bash
   brew install dotnet
   ```
2. Timberborn v1.0+ installed via Steam

#### Windows

> **Note:** Windows build support is untested. Patches welcome!

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ recommended)
2. Timberborn v1.0+ installed via Steam
3. PowerShell 5.1+ (included with Windows 10/11)

#### Linux

> **Note:** Linux build support is untested. Patches welcome!

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ recommended) via your package manager or the official install script
2. Timberborn v1.0+ installed via Steam

### Building

Use the provided build scripts from the repo root. They auto-detect paths and deploy mods to the correct location.

**macOS / Linux:**
```bash
# Build all mods
./build.sh

# Build a specific mod
./build.sh PowerZipline
```

**Windows (PowerShell):**
```powershell
# Build all mods
.\build.ps1

# Build a specific mod
.\build.ps1 -Mods PowerZipline
```

The build scripts compile each mod and deploy files to the Timberborn mods directory automatically.

### Game Version

Built and tested against Timberborn **v1.0.8.0** on macOS.
