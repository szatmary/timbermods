# Graphs

A Timberborn mod that opens a full-screen window with line graphs of all
settlement metrics (goods, population, science, wellbeing) over time, with
weather cycles rendered as background bands. Toggle with **Shift+G**.

This mod is a work in progress — current scaffold is an empty mod stub.

## Build

From the repo root:

```bash
./build.sh Graphs
```

The build script sets `DOTNET_ROOT` and publishes the mod to
`~/Documents/Timberborn/Mods/Graphs/` using the flat mod layout.

## Requirements

- Timberborn 1.0.0.0 or later
- macOS (Apple Silicon) uses the bundled `AppleSiliconHarmony` shim
