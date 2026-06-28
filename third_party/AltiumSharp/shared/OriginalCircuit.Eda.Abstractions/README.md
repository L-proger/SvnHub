# OriginalCircuit.Eda.Abstractions

Shared interfaces and value types for format-agnostic Electronic Design Automation (EDA) tooling in .NET.

This library defines the **common subset** of concepts shared between EDA file formats — schematic symbols, PCB footprints, documents, and rendering primitives — without tying consumers to any specific format.

## Implementations

This abstractions library is consumed by:

- [**AltiumSharp**](https://github.com/issus/AltiumSharp) — Altium Designer file format reader/writer
- **KiCadSharp** — KiCad file format reader/writer

## Installation

```
dotnet add package OriginalCircuit.Eda.Abstractions
```

Requires **.NET 10.0** or later.

## Project Structure

```
src/OriginalCircuit.Eda.Abstractions/
├── Enums/           Shared enumerations (PinElectricalType, PadShape, LineStyle, …)
├── Models/          Base interfaces: IPrimitive, IComponent, ILibrary<T>
│   ├── Sch/         Schematic interfaces (ISchPin, ISchComponent, ISchDocument, …)
│   └── Pcb/         PCB interfaces (IPcbPad, IPcbComponent, IPcbDocument, …)
├── Primitives/      Value types: Coord, CoordPoint, CoordRect, EdaColor
└── Rendering/       IRenderContext, IRenderer, visitor pattern, RenderOptions
```

## Core Concepts

### Coordinate System

All spatial values use a fixed-point coordinate system built on three value types:

| Type | Description |
|------|-------------|
| `Coord` | Fixed-point integer (10,000 units per mil). Converts to/from mils, mm, and inches. |
| `CoordPoint` | Immutable 2D point (`X`, `Y` as `Coord`). Supports offset, rotation, and distance. |
| `CoordRect` | Axis-aligned bounding rectangle. Supports contains, intersects, union, and inflate. |

```csharp
// Create coordinates from real-world units
var x = Coord.FromMils(100);
var y = Coord.FromMm(2.54);

var point = new CoordPoint(x, y);
var rotated = point.Rotate(90);

var rect = CoordRect.FromCenterAndSize(point, Coord.FromMils(200), Coord.FromMils(100));
bool hit = rect.Contains(rotated);
```

Raw `double` or `int` values are never used for positions or dimensions — `Coord` eliminates floating-point precision issues common in CAD software.

### Color

`EdaColor` is a `readonly record struct` with `byte R, G, B, A` fields, providing a format-neutral color representation that both Altium (packed BGR int) and KiCad (RGBA float tuple) can convert to and from.

```csharp
var color = EdaColor.FromRgb(255, 0, 0);       // opaque red
var semi  = new EdaColor(0, 128, 255, 128);     // semi-transparent blue
```

## Interfaces

### Base

| Interface | Description |
|-----------|-------------|
| `IPrimitive` | Base for all drawable elements. Exposes `Bounds`. |
| `IComponent` | Named component with description and bounds. Base for symbols and footprints. |
| `ILibrary<T>` | Generic collection of components. Async-disposable with save support. |

### Schematic

**Primitives** — `ISchPin`, `ISchLine`, `ISchRectangle`, `ISchCircle`, `ISchArc`, `ISchPolyline`, `ISchPolygon`, `ISchBezier`, `ISchLabel`, `ISchNetLabel`, `ISchWire`, `ISchJunction`, `ISchNoConnect`, `ISchPowerObject`, `ISchBus`, `ISchBusEntry`, `ISchSheet`, `ISchSheetPin`, `ISchImage`, `ISchParameter`

**Containers:**

| Interface | Description |
|-----------|-------------|
| `ISchComponent` | Symbol definition — pins, lines, rectangles, arcs, labels, and other graphical primitives. |
| `ISchDocument` | Top-level schematic — components, wires, net labels, junctions, buses, power objects. |
| `ISchLibrary` | Collection of `ISchComponent`. |

### PCB

**Primitives** — `IPcbPad`, `IPcbTrack`, `IPcbVia`, `IPcbArc`, `IPcbText`, `IPcbRegion`

**Containers:**

| Interface | Description |
|-----------|-------------|
| `IPcbComponent` | Footprint definition — pads, tracks, vias, arcs, texts, regions. |
| `IPcbDocument` | Top-level PCB — components, pads, vias, tracks, arcs, texts, regions, board bounds. |
| `IPcbLibrary` | Collection of `IPcbComponent`. |

### Rendering

| Interface | Description |
|-----------|-------------|
| `IRenderContext` | Abstract drawing surface (lines, arcs, text, transforms, clipping). |
| `IRenderer` | Renders a component to a stream or file. |
| `ISchLibRenderer` | Schematic-specific renderer. |
| `IPcbLibRenderer` | PCB-specific renderer. |
| `IPrimitiveVisitor<T>` | Visitor pattern for traversing primitives. |
| `ISchPrimitiveVisitor<T>` | Typed visitor for all schematic primitive types. |
| `IPcbPrimitiveVisitor<T>` | Typed visitor for all PCB primitive types. |

## Enums

| Enum | Values |
|------|--------|
| `PinElectricalType` | Input, Output, Bidirectional, Passive, TriState, PowerIn, PowerOut, OpenCollector, OpenEmitter, Unspecified, NoConnect, Free |
| `PinOrientation` | Right, Up, Left, Down |
| `PadShape` | Circle, Rect, Oval, RoundRect, Trapezoid, Custom |
| `PadType` | ThruHole, Smd, NpThruHole, Connect |
| `PadHoleType` | Round, Slot |
| `LineStyle` | Solid, Dash, Dot, DashDot, DashDotDot, DefaultStyle |
| `TextJustification` | BottomLeft, BottomCenter, BottomRight, MiddleLeft, MiddleCenter, MiddleRight, TopLeft, TopCenter, TopRight |
| `PowerPortStyle` | Circle, Arrow, Bar, Wave, PowerGround, SignalGround, Earth, and others |
| `SchFillType` | None, Filled, Background |

## Design Principles

### Interfaces Only

This library contains **no concrete implementations** — only interfaces, enums, value types, and configuration records. Readers, writers, and renderers live in the consuming projects.

### Common Subset

Only concepts that are meaningfully shared between Altium and KiCad are included. A member belongs here if:

1. Both formats have the concept
2. The semantics are equivalent enough for format-agnostic code
3. Both implementations can populate it without fabricating data

Format-specific features (Altium's `ISchEllipse`, KiCad's zone fill settings, etc.) remain on the concrete types in each consuming project.

### Async I/O

All I/O-adjacent methods use `ValueTask` and accept `CancellationToken`:

```csharp
ValueTask SaveAsync(string path, SaveOptions? options = null, CancellationToken ct = default);
```

Document and library types implement `IAsyncDisposable` for proper resource cleanup.

### Immutable Value Types

`Coord`, `CoordPoint`, `CoordRect`, and `EdaColor` are all `readonly record struct` — immutable and allocation-free. Collections are exposed as `IReadOnlyList<T>`.

## Building

```bash
dotnet build
```

## License

[MIT](https://opensource.org/licenses/MIT)
