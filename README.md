# Timbermods

Custom mods for Timberborn.

## Mods

- **[PowerZipline](PowerZipline/)** — Power zipline buildings with mechanical power transfer through cables

## Prerequisites

### macOS

1. Install the .NET SDK via Homebrew:
   ```bash
   brew install dotnet
   ```
2. Timberborn v1.0+ installed via Steam

### Windows

> **Note:** Windows build support is untested. Patches welcome!

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ recommended)
2. Timberborn v1.0+ installed via Steam
3. PowerShell 5.1+ (included with Windows 10/11)

### Linux

> **Note:** Linux build support is untested. Patches welcome!

1. Install the [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ recommended) via your package manager or the official install script
2. Timberborn v1.0+ installed via Steam

## Building

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

## Mod Installation (Pre-built)

If you want to install a pre-built mod manually:

1. Find your Timberborn mods folder:
   - **macOS**: `~/Documents/Timberborn/Mods/`
   - **Windows**: `%USERPROFILE%\Documents\Timberborn\Mods\`
   - **Linux**: `~/.config/unity3d/Mechanistry/Timberborn/Mods/`

2. Create a folder for the mod (e.g. `PowerZipline/`)

3. Copy the mod files into it with a flat structure:
   ```
   Mods/
   └── ModName/
       ├── manifest.json
       ├── ModName.dll
       ├── 0Harmony.dll          (if the mod uses Harmony)
       ├── Localizations/
       │   └── enUS.csv
       ├── Buildings/
       │   └── ...
       └── TemplateCollections/
           └── ...
   ```

4. Launch Timberborn — the mod appears in the built-in mod manager (main menu > Mods). Enable it and set load order if needed.

## Uninstalling

Delete the mod's folder from the `Mods/` directory and restart the game.

## Game Version

Built and tested against Timberborn **v1.0.8.0** on macOS.
