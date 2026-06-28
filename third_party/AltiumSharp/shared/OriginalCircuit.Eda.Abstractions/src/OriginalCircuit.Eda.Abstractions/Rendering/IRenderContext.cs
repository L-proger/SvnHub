using OriginalCircuit.Eda.Enums;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Abstract drawing operations for rendering primitives.
/// Implemented by SkiaSharp, SVG, etc. backends.
/// </summary>
public interface IRenderContext
{
    /// <summary>Clears the entire canvas with the specified background color.</summary>
    /// <param name="argbColor">The background color as a packed ARGB value.</param>
    void Clear(uint argbColor);

    /// <summary>Draws a line between two points.</summary>
    /// <param name="x1">Start X coordinate.</param>
    /// <param name="y1">Start Y coordinate.</param>
    /// <param name="x2">End X coordinate.</param>
    /// <param name="y2">End Y coordinate.</param>
    /// <param name="color">Line color as packed ARGB.</param>
    /// <param name="width">Line width.</param>
    /// <param name="style">Line dash style.</param>
    void DrawLine(double x1, double y1, double x2, double y2, uint color, double width,
        LineStyle style = LineStyle.Solid);

    /// <summary>Draws an elliptical arc.</summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">X radius.</param>
    /// <param name="ry">Y radius.</param>
    /// <param name="startAngle">Start angle in degrees.</param>
    /// <param name="sweepAngle">Sweep angle in degrees.</param>
    /// <param name="color">Arc color as packed ARGB.</param>
    /// <param name="width">Arc stroke width.</param>
    void DrawArc(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color, double width);

    /// <summary>Draws a rectangle outline.</summary>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate.</param>
    /// <param name="width">Rectangle width.</param>
    /// <param name="height">Rectangle height.</param>
    /// <param name="color">Border color as packed ARGB.</param>
    /// <param name="lineWidth">Border line width.</param>
    void DrawRectangle(double x, double y, double width, double height, uint color, double lineWidth);

    /// <summary>Fills a rectangle.</summary>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate.</param>
    /// <param name="width">Rectangle width.</param>
    /// <param name="height">Rectangle height.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillRectangle(double x, double y, double width, double height, uint color);

    /// <summary>Draws a rounded rectangle outline with independent corner radii.</summary>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate.</param>
    /// <param name="width">Rectangle width.</param>
    /// <param name="height">Rectangle height.</param>
    /// <param name="cornerRadiusX">Horizontal corner radius.</param>
    /// <param name="cornerRadiusY">Vertical corner radius.</param>
    /// <param name="color">Border color as packed ARGB.</param>
    /// <param name="lineWidth">Border line width.</param>
    void DrawRoundedRectangle(double x, double y, double width, double height,
        double cornerRadiusX, double cornerRadiusY, uint color, double lineWidth);

    /// <summary>Fills a rounded rectangle.</summary>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate.</param>
    /// <param name="width">Rectangle width.</param>
    /// <param name="height">Rectangle height.</param>
    /// <param name="cornerRadius">Corner radius.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillRoundedRectangle(double x, double y, double width, double height,
        double cornerRadius, uint color);

    /// <summary>Draws an ellipse outline.</summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">X radius.</param>
    /// <param name="ry">Y radius.</param>
    /// <param name="color">Stroke color as packed ARGB.</param>
    /// <param name="width">Stroke width.</param>
    void DrawEllipse(double cx, double cy, double rx, double ry, uint color, double width);

    /// <summary>Fills an ellipse.</summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">X radius.</param>
    /// <param name="ry">Y radius.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillEllipse(double cx, double cy, double rx, double ry, uint color);

    /// <summary>Draws a closed polygon outline.</summary>
    /// <param name="xPoints">X coordinates of the polygon vertices.</param>
    /// <param name="yPoints">Y coordinates of the polygon vertices.</param>
    /// <param name="color">Stroke color as packed ARGB.</param>
    /// <param name="width">Stroke width.</param>
    void DrawPolygon(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color, double width);

    /// <summary>Fills a closed polygon.</summary>
    /// <param name="xPoints">X coordinates of the polygon vertices.</param>
    /// <param name="yPoints">Y coordinates of the polygon vertices.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillPolygon(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color);

    /// <summary>
    /// Fills one or more closed contours as a single path using the even-odd fill rule, so inner
    /// contours (holes) are subtracted from the outer one. Used for regions/pours with cutouts.
    /// </summary>
    /// <param name="contours">Contours as (X, Y) coordinate arrays; the first is the outer boundary,
    /// the rest are holes. Each contour needs at least three points.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillContours(IReadOnlyList<(double[] X, double[] Y)> contours, uint color);

    /// <summary>Draws an open polyline through the given points.</summary>
    /// <param name="xPoints">X coordinates of the polyline vertices.</param>
    /// <param name="yPoints">Y coordinates of the polyline vertices.</param>
    /// <param name="color">Stroke color as packed ARGB.</param>
    /// <param name="width">Stroke width.</param>
    /// <param name="style">Line dash style.</param>
    void DrawPolyline(ReadOnlySpan<double> xPoints, ReadOnlySpan<double> yPoints, uint color, double width,
        LineStyle style = LineStyle.Solid);

    /// <summary>Draws a cubic bezier curve.</summary>
    /// <param name="x0">Start point X.</param>
    /// <param name="y0">Start point Y.</param>
    /// <param name="x1">First control point X.</param>
    /// <param name="y1">First control point Y.</param>
    /// <param name="x2">Second control point X.</param>
    /// <param name="y2">Second control point Y.</param>
    /// <param name="x3">End point X.</param>
    /// <param name="y3">End point Y.</param>
    /// <param name="color">Stroke color as packed ARGB.</param>
    /// <param name="width">Stroke width.</param>
    void DrawBezier(double x0, double y0, double x1, double y1, double x2, double y2,
        double x3, double y3, uint color, double width);

    /// <summary>Fills a pie (arc sector) shape.</summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">X radius.</param>
    /// <param name="ry">Y radius.</param>
    /// <param name="startAngle">Start angle in degrees.</param>
    /// <param name="sweepAngle">Sweep angle in degrees.</param>
    /// <param name="color">Fill color as packed ARGB.</param>
    void FillPie(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color);

    /// <summary>Draws a pie (arc sector) outline.</summary>
    /// <param name="cx">Center X coordinate.</param>
    /// <param name="cy">Center Y coordinate.</param>
    /// <param name="rx">X radius.</param>
    /// <param name="ry">Y radius.</param>
    /// <param name="startAngle">Start angle in degrees.</param>
    /// <param name="sweepAngle">Sweep angle in degrees.</param>
    /// <param name="color">Stroke color as packed ARGB.</param>
    /// <param name="lineWidth">Stroke width.</param>
    void DrawPie(double cx, double cy, double rx, double ry, double startAngle, double sweepAngle,
        uint color, double lineWidth);

    /// <summary>Draws text at the specified position using the given font family.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">X position.</param>
    /// <param name="y">Y position.</param>
    /// <param name="fontSize">Font size.</param>
    /// <param name="color">Text color as packed ARGB.</param>
    /// <param name="fontFamily">Font family name.</param>
    void DrawText(string text, double x, double y, double fontSize, uint color,
        string fontFamily = "Arial");

    /// <summary>Draws text at the specified position using detailed text rendering options.</summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">X position.</param>
    /// <param name="y">Y position.</param>
    /// <param name="fontSize">Font size.</param>
    /// <param name="color">Text color as packed ARGB.</param>
    /// <param name="options">Detailed text rendering options.</param>
    void DrawText(string text, double x, double y, double fontSize, uint color,
        TextRenderOptions options);

    /// <summary>Measures the dimensions of the specified text without rendering it.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="fontSize">Font size.</param>
    /// <param name="options">Optional text rendering options.</param>
    /// <returns>The measured text dimensions.</returns>
    TextMetrics MeasureText(string text, double fontSize, TextRenderOptions? options = null);

    /// <summary>Draws a raster image from raw byte data into the specified rectangle.</summary>
    /// <param name="imageData">The raw image bytes.</param>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate.</param>
    /// <param name="width">Image display width.</param>
    /// <param name="height">Image display height.</param>
    void DrawImage(ReadOnlySpan<byte> imageData, double x, double y, double width, double height);

    /// <summary>Sets a rectangular clipping region.</summary>
    /// <param name="x">Clip region left X.</param>
    /// <param name="y">Clip region top Y.</param>
    /// <param name="w">Clip region width.</param>
    /// <param name="h">Clip region height.</param>
    void SetClipRect(double x, double y, double w, double h);

    /// <summary>
    /// Restricts subsequent drawing to the union of the given closed contours (non-zero winding, so
    /// overlapping contours union rather than cancel). The clip applies until the enclosing
    /// <see cref="SaveState"/> is restored via <see cref="RestoreState"/>.
    /// </summary>
    /// <param name="contours">Closed contours as (X, Y) coordinate arrays; each needs at least three points.</param>
    void SetClipPath(IReadOnlyList<(double[] X, double[] Y)> contours);

    /// <summary>Resets the clipping region to the full canvas.</summary>
    void ResetClip();

    /// <summary>Saves the current graphics state (transform, clip) onto a stack.</summary>
    void SaveState();

    /// <summary>Restores the most recently saved graphics state from the stack.</summary>
    void RestoreState();

    /// <summary>
    /// Begins a named group of drawing operations. Vector backends (SVG) emit an addressable container
    /// (e.g. <c>&lt;g id="…"&gt;</c>) so the group can be styled or toggled after export; raster backends
    /// treat it as a plain state save. Pair with <see cref="EndGroup"/>.
    /// </summary>
    /// <param name="id">An identifier for the group (used as the element id by vector backends).</param>
    void BeginGroup(string id);

    /// <summary>Ends the most recently started <see cref="BeginGroup"/>.</summary>
    void EndGroup();

    /// <summary>Applies a translation to the current transform.</summary>
    /// <param name="dx">X translation.</param>
    /// <param name="dy">Y translation.</param>
    void Translate(double dx, double dy);

    /// <summary>Applies a rotation (in degrees) to the current transform.</summary>
    /// <param name="angleDegrees">Rotation angle in degrees.</param>
    void Rotate(double angleDegrees);

    /// <summary>Applies a scale to the current transform.</summary>
    /// <param name="sx">X scale factor.</param>
    /// <param name="sy">Y scale factor.</param>
    void Scale(double sx, double sy);
}
