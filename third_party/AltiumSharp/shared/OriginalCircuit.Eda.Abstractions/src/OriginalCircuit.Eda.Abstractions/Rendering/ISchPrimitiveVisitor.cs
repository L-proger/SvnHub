using OriginalCircuit.Eda.Models.Sch;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Visitor for schematic primitives, providing typed visit methods for each primitive type.
/// </summary>
/// <typeparam name="TContext">The rendering context type.</typeparam>
public interface ISchPrimitiveVisitor<in TContext> : IPrimitiveVisitor<TContext>
{
    /// <summary>Visits a schematic pin.</summary>
    /// <param name="pin">The pin to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchPin pin, TContext context);

    /// <summary>Visits a schematic line.</summary>
    /// <param name="line">The line to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchLine line, TContext context);

    /// <summary>Visits a schematic label.</summary>
    /// <param name="label">The label to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchLabel label, TContext context);

    /// <summary>Visits a schematic rectangle.</summary>
    /// <param name="rectangle">The rectangle to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchRectangle rectangle, TContext context);

    /// <summary>Visits a schematic wire.</summary>
    /// <param name="wire">The wire to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchWire wire, TContext context);

    /// <summary>Visits a schematic polygon.</summary>
    /// <param name="polygon">The polygon to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchPolygon polygon, TContext context);

    /// <summary>Visits a schematic polyline.</summary>
    /// <param name="polyline">The polyline to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchPolyline polyline, TContext context);

    /// <summary>Visits a schematic arc.</summary>
    /// <param name="arc">The arc to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchArc arc, TContext context);

    /// <summary>Visits a schematic circle.</summary>
    /// <param name="circle">The circle to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchCircle circle, TContext context);

    /// <summary>Visits a schematic bezier curve.</summary>
    /// <param name="bezier">The bezier to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchBezier bezier, TContext context);

    /// <summary>Visits a schematic net label.</summary>
    /// <param name="netLabel">The net label to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchNetLabel netLabel, TContext context);

    /// <summary>Visits a schematic junction.</summary>
    /// <param name="junction">The junction to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchJunction junction, TContext context);

    /// <summary>Visits a schematic parameter.</summary>
    /// <param name="parameter">The parameter to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchParameter parameter, TContext context);

    /// <summary>Visits a schematic image.</summary>
    /// <param name="image">The image to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchImage image, TContext context);

    /// <summary>Visits a schematic power object.</summary>
    /// <param name="powerObject">The power object to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchPowerObject powerObject, TContext context);

    /// <summary>Visits a schematic no-connect marker.</summary>
    /// <param name="noConnect">The no-connect marker to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchNoConnect noConnect, TContext context);

    /// <summary>Visits a schematic bus entry.</summary>
    /// <param name="busEntry">The bus entry to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchBusEntry busEntry, TContext context);

    /// <summary>Visits a schematic bus.</summary>
    /// <param name="bus">The bus to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchBus bus, TContext context);

    /// <summary>Visits a schematic sheet symbol.</summary>
    /// <param name="sheet">The sheet symbol to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchSheet sheet, TContext context);

    /// <summary>Visits a schematic sheet pin.</summary>
    /// <param name="sheetPin">The sheet pin to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(ISchSheetPin sheetPin, TContext context);
}
