# SvnHub Integration Notes

This is a source snapshot of the `issus/AltiumSharp` v2 code used for server-side Altium previews.

SvnHub imports only the projects required for SVG rendering:

- `OriginalCircuit.Altium`
- `OriginalCircuit.Altium.Generators`
- `OriginalCircuit.Altium.Rendering.Core`
- `OriginalCircuit.Altium.Rendering.Svg`
- `OriginalCircuit.Eda.Abstractions`
- `OriginalCircuit.Eda.Rendering.Core`
- `OriginalCircuit.Eda.Rendering.Svg`

Raster, glTF, STEP, and mechanical rendering projects are intentionally omitted to avoid native or heavier dependencies. When official v2 NuGet packages for `OriginalCircuit.Altium.Rendering.Svg` become available, prefer replacing this source snapshot with package references.

`Directory.Build.props` is intentionally simplified for SvnHub build usage: packaging, SourceLink, documentation XML generation, and NuGet audit settings from the upstream repository are not needed for this vendored source dependency.

SvnHub adds small public facade files over existing rendering internals so the iBOM exporter can reuse upstream geometry without reflection:

- `src/OriginalCircuit.Altium.Rendering.Core/AltiumStrokeTextGeometry.cs`
- `src/OriginalCircuit.Altium.Rendering.Core/AltiumBarcodeTextGeometry.cs`

When refreshing the upstream snapshot, keep or reapply these facade files unless upstream exposes equivalent public APIs.
