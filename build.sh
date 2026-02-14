#!/usr/bin/env bash
# Build script for Timberborn mods (macOS / Linux)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Detect platform and set paths
case "$(uname -s)" in
    Darwin)
        STEAM_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Timberborn"
        GAME_MANAGED="$STEAM_DIR/Timberborn.app/Contents/Resources/Data/Managed"
        MODS_DIR="$HOME/Documents/Timberborn/Mods"
        # macOS brew dotnet
        if [ -d "/opt/homebrew/opt/dotnet/libexec" ]; then
            export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
            export PATH="$DOTNET_ROOT:$PATH"
        fi
        ;;
    Linux)
        STEAM_DIR="$HOME/.steam/steam/steamapps/common/Timberborn"
        GAME_MANAGED="$STEAM_DIR/Timberborn_Data/Managed"
        MODS_DIR="$HOME/.config/unity3d/Mechanistry/Timberborn/Mods"
        ;;
    *)
        echo "ERROR: Unsupported platform $(uname -s). Use build.ps1 for Windows."
        exit 1
        ;;
esac

# Check prerequisites
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: dotnet SDK not found."
    case "$(uname -s)" in
        Darwin) echo "Install with: brew install dotnet" ;;
        Linux)  echo "Install from: https://dotnet.microsoft.com/download" ;;
    esac
    exit 1
fi

# Allow env var overrides
GAME_MANAGED="${GAME_MANAGED_DIR:-$GAME_MANAGED}"
MODS_DIR="${TIMBERBORN_MODS_DIR:-$MODS_DIR}"

# Verify game install exists
if [ ! -d "$GAME_MANAGED" ]; then
    echo "ERROR: Game not found at $GAME_MANAGED"
    echo "Set GAME_MANAGED_DIR env var to your Timberborn Managed directory."
    exit 1
fi

# Build specific mod or all mods
if [ $# -gt 0 ]; then
    MODS=("$@")
else
    MODS=()
    for dir in "$SCRIPT_DIR"/*/; do
        if compgen -G "$dir"*.csproj > /dev/null 2>&1; then
            MODS+=("$(basename "$dir")")
        fi
    done
fi

if [ ${#MODS[@]} -eq 0 ]; then
    echo "No mods found to build."
    exit 1
fi

for mod in "${MODS[@]}"; do
    MOD_DIR="$SCRIPT_DIR/$mod"
    if [ ! -d "$MOD_DIR" ]; then
        echo "ERROR: $mod directory not found"
        exit 1
    fi

    DEPLOY_DIR="$MODS_DIR/$mod"

    echo "=== Building $mod ==="
    dotnet build "$MOD_DIR" \
        -p:GameManagedDir="$GAME_MANAGED" \
        -p:DeployDir="$DEPLOY_DIR" \
        || { echo "FAILED: $mod"; exit 1; }

    echo "  Deployed to $DEPLOY_DIR"
    echo ""
done

echo "All mods built and deployed successfully."
echo ""
echo "Deployed mods:"
for mod in "${MODS[@]}"; do
    echo "  $MODS_DIR/$mod/"
done

# Create zip artifacts in dist/
DIST_DIR="$SCRIPT_DIR/dist"
mkdir -p "$DIST_DIR"
for mod in "${MODS[@]}"; do
    DEPLOY_DIR="$MODS_DIR/$mod"
    ZIP_FILE="$DIST_DIR/$mod.zip"
    rm -f "$ZIP_FILE"
    (cd "$DEPLOY_DIR" && zip -r "$ZIP_FILE" .)
    echo "  Artifact: $ZIP_FILE"
done
echo ""
echo "Zip artifacts in dist/"
