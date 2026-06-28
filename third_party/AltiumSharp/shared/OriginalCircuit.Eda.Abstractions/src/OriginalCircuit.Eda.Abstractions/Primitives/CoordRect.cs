using System.Runtime.CompilerServices;

namespace OriginalCircuit.Eda.Primitives;

/// <summary>
/// Represents an axis-aligned bounding rectangle in internal coordinate units.
/// </summary>
public readonly struct CoordRect : IEquatable<CoordRect>
{
    /// <summary>
    /// An empty rectangle at the origin.
    /// </summary>
    public static readonly CoordRect Empty = new(CoordPoint.Zero, CoordPoint.Zero);

    /// <summary>
    /// Bottom-left corner (minimum X, minimum Y).
    /// </summary>
    public CoordPoint Min { get; }

    /// <summary>
    /// Top-right corner (maximum X, maximum Y).
    /// </summary>
    public CoordPoint Max { get; }

    /// <summary>
    /// Creates a rectangle from two corner points (will be normalized).
    /// </summary>
    /// <param name="p1">The first corner point.</param>
    /// <param name="p2">The second corner point.</param>
    public CoordRect(CoordPoint p1, CoordPoint p2)
    {
        Min = new CoordPoint(Coord.Min(p1.X, p2.X), Coord.Min(p1.Y, p2.Y));
        Max = new CoordPoint(Coord.Max(p1.X, p2.X), Coord.Max(p1.Y, p2.Y));
    }

    /// <summary>
    /// Creates a rectangle from explicit coordinates.
    /// </summary>
    /// <param name="minX">The minimum X coordinate.</param>
    /// <param name="minY">The minimum Y coordinate.</param>
    /// <param name="maxX">The maximum X coordinate.</param>
    /// <param name="maxY">The maximum Y coordinate.</param>
    public CoordRect(Coord minX, Coord minY, Coord maxX, Coord maxY)
        : this(new CoordPoint(minX, minY), new CoordPoint(maxX, maxY))
    {
    }

    /// <summary>
    /// Creates a rectangle from center point and size.
    /// </summary>
    /// <param name="center">The center point.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>A new rectangle centered on the given point.</returns>
    public static CoordRect FromCenterAndSize(CoordPoint center, Coord width, Coord height)
    {
        var halfWidth = width / 2;
        var halfHeight = height / 2;
        return new CoordRect(
            new CoordPoint(center.X - halfWidth, center.Y - halfHeight),
            new CoordPoint(center.X + halfWidth, center.Y + halfHeight));
    }

    /// <summary>
    /// Creates a rectangle from center point and size (alias for <see cref="FromCenterAndSize"/>).
    /// </summary>
    /// <param name="center">The center point.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>A new rectangle centered on the given point.</returns>
    public static CoordRect FromCenter(CoordPoint center, Coord width, Coord height) =>
        FromCenterAndSize(center, width, height);

    /// <summary>
    /// Width of the rectangle.
    /// </summary>
    public Coord Width => Max.X - Min.X;

    /// <summary>
    /// Height of the rectangle.
    /// </summary>
    public Coord Height => Max.Y - Min.Y;

    /// <summary>
    /// Center point of the rectangle.
    /// </summary>
    public CoordPoint Center => new(
        Min.X + Width / 2,
        Min.Y + Height / 2);

    /// <summary>
    /// Returns <see langword="true"/> if this rectangle has zero area.
    /// </summary>
    public bool IsEmpty => Width.ToRaw() == 0 && Height.ToRaw() == 0;

    /// <summary>
    /// Returns <see langword="true"/> if the given point is inside this rectangle.
    /// </summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the point is within the rectangle bounds.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(CoordPoint point) =>
        point.X >= Min.X && point.X <= Max.X &&
        point.Y >= Min.Y && point.Y <= Max.Y;

    /// <summary>
    /// Returns <see langword="true"/> if this rectangle intersects with another.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles overlap.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(CoordRect other) =>
        Min.X <= other.Max.X && Max.X >= other.Min.X &&
        Min.Y <= other.Max.Y && Max.Y >= other.Min.Y;

    /// <summary>
    /// Returns a rectangle expanded by the specified amount on all sides.
    /// </summary>
    /// <param name="amount">The amount to expand by.</param>
    /// <returns>A new inflated rectangle.</returns>
    public CoordRect Inflate(Coord amount) => new(
        new CoordPoint(Min.X - amount, Min.Y - amount),
        new CoordPoint(Max.X + amount, Max.Y + amount));

    /// <summary>
    /// Returns the union of this rectangle with another.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The smallest rectangle containing both.</returns>
    public CoordRect Union(CoordRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        return new CoordRect(
            new CoordPoint(Coord.Min(Min.X, other.Min.X), Coord.Min(Min.Y, other.Min.Y)),
            new CoordPoint(Coord.Max(Max.X, other.Max.X), Coord.Max(Max.Y, other.Max.Y)));
    }

    /// <summary>
    /// Returns the union of multiple rectangles.
    /// </summary>
    /// <param name="rects">The rectangles to union.</param>
    /// <returns>The smallest rectangle containing all input rectangles.</returns>
    public static CoordRect Union(IEnumerable<CoordRect> rects)
    {
        // Seed from the first rect rather than Empty, and combine with a plain min/max, so a
        // zero-area rect (a single-point bound at a real location) is included instead of being
        // dropped as "empty" or pulling the union back to the origin sentinel.
        CoordRect? result = null;
        foreach (var rect in rects)
        {
            result = result is null
                ? rect
                : new CoordRect(
                    new CoordPoint(Coord.Min(result.Value.Min.X, rect.Min.X), Coord.Min(result.Value.Min.Y, rect.Min.Y)),
                    new CoordPoint(Coord.Max(result.Value.Max.X, rect.Max.X), Coord.Max(result.Value.Max.Y, rect.Max.Y)));
        }
        return result ?? Empty;
    }

    /// <summary>
    /// Returns the intersection of this rectangle with another, or <see cref="Empty"/> if they don't intersect.
    /// </summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The intersection rectangle, or <see cref="Empty"/>.</returns>
    public CoordRect Intersect(CoordRect other)
    {
        if (!Intersects(other)) return Empty;

        return new CoordRect(
            new CoordPoint(Coord.Max(Min.X, other.Min.X), Coord.Max(Min.Y, other.Min.Y)),
            new CoordPoint(Coord.Min(Max.X, other.Max.X), Coord.Min(Max.Y, other.Max.Y)));
    }

    /// <inheritdoc />
    public bool Equals(CoordRect other) => Min == other.Min && Max == other.Max;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is CoordRect other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    /// <summary>
    /// Returns <see langword="true"/> if two rectangles have equal corners.
    /// </summary>
    public static bool operator ==(CoordRect left, CoordRect right) => left.Equals(right);

    /// <summary>
    /// Returns <see langword="true"/> if two rectangles have different corners.
    /// </summary>
    public static bool operator !=(CoordRect left, CoordRect right) => !left.Equals(right);

    /// <summary>
    /// Returns a string representation in the form "[Min - Max]".
    /// </summary>
    public override string ToString() => $"[{Min} - {Max}]";
}
