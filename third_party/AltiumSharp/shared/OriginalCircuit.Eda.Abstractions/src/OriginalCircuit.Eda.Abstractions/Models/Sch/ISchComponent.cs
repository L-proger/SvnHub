namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic symbol/component containing pins and graphical primitives.
/// </summary>
public interface ISchComponent : IComponent
{
    /// <summary>
    /// Number of parts (units) in this component. Multi-part components have PartCount &gt; 1.
    /// </summary>
    int PartCount { get; }

    /// <summary>
    /// All pins belonging to this component.
    /// </summary>
    IReadOnlyList<ISchPin> Pins { get; }

    /// <summary>
    /// All line primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchLine> Lines { get; }

    /// <summary>
    /// All rectangle primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchRectangle> Rectangles { get; }

    /// <summary>
    /// All text label primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchLabel> Labels { get; }

    /// <summary>
    /// All wire primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchWire> Wires { get; }

    /// <summary>
    /// All polyline primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchPolyline> Polylines { get; }

    /// <summary>
    /// All polygon primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchPolygon> Polygons { get; }

    /// <summary>
    /// All arc primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchArc> Arcs { get; }

    /// <summary>
    /// All circle primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchCircle> Circles { get; }

    /// <summary>
    /// All bezier curve primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchBezier> Beziers { get; }

    /// <summary>
    /// All net label primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchNetLabel> NetLabels { get; }

    /// <summary>
    /// All junction primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchJunction> Junctions { get; }

    /// <summary>
    /// All image primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchImage> Images { get; }

    /// <summary>
    /// All parameter primitives belonging to this component.
    /// </summary>
    IReadOnlyList<ISchParameter> Parameters { get; }

    /// <summary>Adds a pin to the component.</summary>
    void AddPin(ISchPin pin);

    /// <summary>Removes a pin from the component.</summary>
    /// <returns><c>true</c> if the pin was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePin(ISchPin pin);

    /// <summary>Adds a line to the component.</summary>
    void AddLine(ISchLine line);

    /// <summary>Removes a line from the component.</summary>
    /// <returns><c>true</c> if the line was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveLine(ISchLine line);

    /// <summary>Adds a rectangle to the component.</summary>
    void AddRectangle(ISchRectangle rectangle);

    /// <summary>Removes a rectangle from the component.</summary>
    /// <returns><c>true</c> if the rectangle was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveRectangle(ISchRectangle rectangle);

    /// <summary>Adds a label to the component.</summary>
    void AddLabel(ISchLabel label);

    /// <summary>Removes a label from the component.</summary>
    /// <returns><c>true</c> if the label was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveLabel(ISchLabel label);

    /// <summary>Adds a wire to the component.</summary>
    void AddWire(ISchWire wire);

    /// <summary>Removes a wire from the component.</summary>
    /// <returns><c>true</c> if the wire was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveWire(ISchWire wire);

    /// <summary>Adds a polyline to the component.</summary>
    void AddPolyline(ISchPolyline polyline);

    /// <summary>Removes a polyline from the component.</summary>
    /// <returns><c>true</c> if the polyline was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePolyline(ISchPolyline polyline);

    /// <summary>Adds a polygon to the component.</summary>
    void AddPolygon(ISchPolygon polygon);

    /// <summary>Removes a polygon from the component.</summary>
    /// <returns><c>true</c> if the polygon was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePolygon(ISchPolygon polygon);

    /// <summary>Adds an arc to the component.</summary>
    void AddArc(ISchArc arc);

    /// <summary>Removes an arc from the component.</summary>
    /// <returns><c>true</c> if the arc was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveArc(ISchArc arc);

    /// <summary>Adds a circle to the component.</summary>
    void AddCircle(ISchCircle circle);

    /// <summary>Removes a circle from the component.</summary>
    /// <returns><c>true</c> if the circle was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveCircle(ISchCircle circle);

    /// <summary>Adds a bezier curve to the component.</summary>
    void AddBezier(ISchBezier bezier);

    /// <summary>Removes a bezier curve from the component.</summary>
    /// <returns><c>true</c> if the bezier was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveBezier(ISchBezier bezier);

    /// <summary>Adds a net label to the component.</summary>
    void AddNetLabel(ISchNetLabel netLabel);

    /// <summary>Removes a net label from the component.</summary>
    /// <returns><c>true</c> if the net label was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveNetLabel(ISchNetLabel netLabel);

    /// <summary>Adds a junction to the component.</summary>
    void AddJunction(ISchJunction junction);

    /// <summary>Removes a junction from the component.</summary>
    /// <returns><c>true</c> if the junction was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveJunction(ISchJunction junction);

    /// <summary>Adds an image to the component.</summary>
    void AddImage(ISchImage image);

    /// <summary>Removes an image from the component.</summary>
    /// <returns><c>true</c> if the image was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveImage(ISchImage image);

    /// <summary>Adds a parameter to the component.</summary>
    void AddParameter(ISchParameter parameter);

    /// <summary>Removes a parameter from the component.</summary>
    /// <returns><c>true</c> if the parameter was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveParameter(ISchParameter parameter);
}
