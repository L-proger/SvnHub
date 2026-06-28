# OriginalCircuit.Eda.Rendering

Shared rendering infrastructure for EDA (Electronic Design Automation) file visualization. Provides coordinate transforms, color utilities, and rendering backends (SkiaSharp raster and SVG vector) that are used by format-specific renderers in [AltiumSharp](https://github.com/originalcircuit/altiumsharp) and [KiCadSharp](https://github.com/originalcircuit/kicadsharp).

## Architecture

This library implements the `IRenderContext` and `IRenderer` interfaces defined in [OriginalCircuit.Eda.Abstractions](https://github.com/originalcircuit/eda-abstractions).

```
OriginalCircuit.Eda.Rendering.Core      CoordTransform, ColorHelper
OriginalCircuit.Eda.Rendering.Raster    SkiaRenderContext, RasterRendererBase (PNG output)
OriginalCircuit.Eda.Rendering.Svg       SvgRenderContext, SvgRendererBase (SVG output)
```

Format-specific projects (not in this repo) inherit from the base renderers:

```
AltiumSharp                              KiCadSharp
  OriginalCircuit.Altium.Rendering         OriginalCircuit.KiCad.Rendering
    SchComponentRenderer                     KiCadSchRenderer
    PcbComponentRenderer                     KiCadPcbRenderer
    AltiumRasterRenderer                     KiCadRasterRenderer
    AltiumSvgRenderer                        KiCadSvgRenderer
```

## Packages

| Package | Description | Dependencies |
|---------|-------------|--------------|
| `OriginalCircuit.Eda.Rendering.Core` | CoordTransform and ColorHelper utilities | Eda.Abstractions |
| `OriginalCircuit.Eda.Rendering.Raster` | SkiaSharp raster backend (PNG/JPG) | Core, SkiaSharp |
| `OriginalCircuit.Eda.Rendering.Svg` | SVG vector backend (System.Xml.Linq) | Core |

## Usage

### CoordTransform

Maps between EDA world coordinates (`Coord` values) and screen pixel positions. Handles scaling, centering, and Y-axis inversion.

```csharp
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

var transform = new CoordTransform
{
    ScreenWidth = 1024,
    ScreenHeight = 768
};

// Auto-fit a bounding box to the screen with 5% margin
var bounds = new CoordRect(
    Coord.FromMm(0), Coord.FromMm(0),
    Coord.FromMm(10), Coord.FromMm(8));
transform.AutoZoom(bounds);

// Convert world coordinates to screen pixels
var (sx, sy) = transform.WorldToScreen(Coord.FromMm(5), Coord.FromMm(4));
```

### ColorHelper

Converts between `EdaColor` and packed ARGB values used by the rendering backends.

```csharp
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;

var argb = ColorHelper.EdaColorToArgb(new EdaColor(255, 0, 0, 255)); // 0xFFFF0000
var color = ColorHelper.ArgbToEdaColor(0xFF00FF00); // EdaColor(0, 255, 0, 255)
```

### Implementing a Renderer

Subclass `RasterRendererBase` or `SvgRendererBase` and implement `RenderComponent`:

```csharp
using OriginalCircuit.Eda.Rendering.Raster;

public class MyRasterRenderer : RasterRendererBase
{
    protected override void RenderComponent(
        IComponent component, IRenderContext context, CoordTransform transform)
    {
        // Draw primitives using context.DrawLine(), context.FillRectangle(), etc.
        // Use transform.WorldToScreen() to convert coordinates
    }
}
```

Then render to a stream or file:

```csharp
var renderer = new MyRasterRenderer();
using var stream = File.Create("output.png");
await renderer.RenderAsync(component, stream, new RenderOptions
{
    Width = 512,
    Height = 512,
    AutoZoom = true
});
```

### SVG Rendering

SVG output uses `System.Xml.Linq` with no external dependencies:

```csharp
using OriginalCircuit.Eda.Rendering.Svg;

public class MySvgRenderer : SvgRendererBase
{
    protected override void RenderComponent(
        IComponent component, IRenderContext context, CoordTransform transform)
    {
        // Same IRenderContext API as raster - draw calls produce SVG elements
    }
}
```

## IRenderContext API

Both `SkiaRenderContext` and `SvgRenderContext` implement `IRenderContext`, which provides:

| Method | SVG Element | Description |
|--------|-------------|-------------|
| `DrawLine` | `<line>` | Stroke a line segment |
| `DrawRectangle` | `<rect>` (no fill) | Stroke a rectangle outline |
| `FillRectangle` | `<rect>` (filled) | Fill a rectangle |
| `DrawEllipse` | `<ellipse>` (no fill) | Stroke an ellipse outline |
| `FillEllipse` | `<ellipse>` (filled) | Fill an ellipse |
| `DrawPolygon` | `<polygon>` (no fill) | Stroke a closed polygon |
| `FillPolygon` | `<polygon>` (filled) | Fill a polygon |
| `DrawPolyline` | `<polyline>` | Stroke an open polyline |
| `DrawBezier` | `<path>` (C command) | Cubic bezier curve |
| `DrawArc` | `<path>` (A command) | Elliptical arc segment |
| `FillPie` / `DrawPie` | `<path>` (L/A/Z) | Pie/wedge shape |
| `DrawText` | `<text>` | Text rendering |
| `DrawImage` | `<image>` | Embedded image |
| `Clear` | `<rect>` (background) | Fill background |
| `SaveState` / `RestoreState` | `<g>` | Transform grouping |

## Building

```bash
dotnet build OriginalCircuit.Eda.Rendering.sln
```

Requires .NET 10.0 SDK or later.

## Dependencies

- [OriginalCircuit.Eda.Abstractions](https://github.com/originalcircuit/eda-abstractions) — shared EDA interfaces (`Coord`, `IRenderContext`, `IRenderer`, `IComponent`)
- [SkiaSharp](https://github.com/mono/SkiaSharp) 3.116.1 — raster rendering backend (Raster package only)

## License

MIT
