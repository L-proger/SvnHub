using System.Globalization;
using System.Runtime.CompilerServices;

namespace OriginalCircuit.Eda.Primitives;

/// <summary>
/// Internal coordinate value, stored as a fixed-point integer.
/// Uses 10,000 units per mil (1/1000 inch) for sub-mil precision.
/// </summary>
public readonly struct Coord : IEquatable<Coord>, IComparable<Coord>, IFormattable
{
    /// <summary>
    /// Internal units per mil (1/1000 inch).
    /// </summary>
    public const int UnitsPerMil = 10000;

    /// <summary>
    /// Internal units per millimeter.
    /// </summary>
    public const double UnitsPerMm = UnitsPerMil * 1000.0 / 25.4;

    /// <summary>
    /// Internal units per inch.
    /// </summary>
    public const int UnitsPerInch = UnitsPerMil * 1000;

    /// <summary>
    /// Length of 1 inch as a coordinate value.
    /// </summary>
    public static readonly Coord OneInch = new(UnitsPerInch);

    /// <summary>
    /// Length of 1 mil (1/1000 inch) as a coordinate value.
    /// </summary>
    public static readonly Coord OneMil = new(UnitsPerMil);

    /// <summary>
    /// Length of 1 millimeter as a coordinate value.
    /// </summary>
    public static readonly Coord OneMm = FromMm(1.0);

    /// <summary>
    /// Zero coordinate.
    /// </summary>
    public static readonly Coord Zero = new(0);

    private readonly int _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Coord(int value) => _value = value;

    /// <summary>
    /// Creates a coordinate from a value in mils (1/1000 inch).
    /// </summary>
    /// <param name="mils">The value in mils.</param>
    /// <returns>A new <see cref="Coord"/> representing the specified distance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord FromMils(double mils) => new(checked((int)(mils * UnitsPerMil)));

    /// <summary>
    /// Gets the value in mils (1/1000 inch).
    /// </summary>
    /// <returns>The coordinate value expressed in mils.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToMils() => _value / (double)UnitsPerMil;

    /// <summary>
    /// Creates a coordinate from a value in millimeters.
    /// </summary>
    /// <param name="mm">The value in millimeters.</param>
    /// <returns>A new <see cref="Coord"/> representing the specified distance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord FromMm(double mm) => new(checked((int)(mm * UnitsPerMm)));

    /// <summary>
    /// Gets the value in millimeters.
    /// </summary>
    /// <returns>The coordinate value expressed in millimeters.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToMm() => _value / UnitsPerMm;

    /// <summary>
    /// Creates a coordinate from a value in inches.
    /// </summary>
    /// <param name="inches">The value in inches.</param>
    /// <returns>A new <see cref="Coord"/> representing the specified distance.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord FromInches(double inches) => new(checked((int)(inches * UnitsPerInch)));

    /// <summary>
    /// Gets the value in inches.
    /// </summary>
    /// <returns>The coordinate value expressed in inches.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ToInches() => _value / (double)UnitsPerInch;

    /// <summary>
    /// Creates a coordinate from the raw internal integer value.
    /// </summary>
    /// <param name="value">The raw internal integer value.</param>
    /// <returns>A new <see cref="Coord"/> wrapping the raw value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord FromRaw(int value) => new(value);

    /// <summary>
    /// Gets the raw internal integer value.
    /// </summary>
    /// <returns>The underlying integer value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ToRaw() => _value;

    /// <summary>
    /// Parses a coordinate from a span, handling unit suffixes (mil, mm, in).
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <param name="result">The parsed coordinate, or <see cref="Zero"/> on failure.</param>
    /// <returns><see langword="true"/> if the span was successfully parsed.</returns>
    public static bool TryParse(ReadOnlySpan<char> span, out Coord result)
    {
        span = span.Trim();

        if (span.EndsWith("mil", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(span[..^3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mils))
            {
                result = FromMils(mils);
                return true;
            }
        }
        else if (span.EndsWith("mm", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(span[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var mm))
            {
                result = FromMm(mm);
                return true;
            }
        }
        else if (span.EndsWith("in", StringComparison.OrdinalIgnoreCase))
        {
            if (double.TryParse(span[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var inches))
            {
                result = FromInches(inches);
                return true;
            }
        }
        else if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var mils))
        {
            // Default to mils if no suffix
            result = FromMils(mils);
            return true;
        }

        result = Zero;
        return false;
    }

    /// <summary>
    /// Parses a coordinate from a span, handling unit suffixes (mil, mm, in).
    /// </summary>
    /// <param name="span">The text to parse.</param>
    /// <returns>The parsed coordinate value.</returns>
    /// <exception cref="FormatException">Thrown when the span cannot be parsed as a coordinate.</exception>
    public static Coord Parse(ReadOnlySpan<char> span)
    {
        if (!TryParse(span, out var result))
            throw new FormatException($"Cannot parse '{span.ToString()}' as a coordinate");
        return result;
    }

    /// <summary>
    /// Returns the coordinate formatted as mils with invariant culture.
    /// </summary>
    /// <returns>A string representation of the coordinate in mils.</returns>
    public override string ToString() => ToString("mil", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats the coordinate using the specified format and provider.
    /// </summary>
    /// <param name="format">Format specifier: "mil", "mm", "in", or "raw".</param>
    /// <param name="formatProvider">The format provider for numeric formatting.</param>
    /// <returns>The formatted coordinate string.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        format ??= "mil";
        formatProvider ??= CultureInfo.InvariantCulture;

        return format.ToLowerInvariant() switch
        {
            "mil" or "mils" => $"{ToMils().ToString("0.#####", formatProvider)}mil",
            "mm" => $"{ToMm().ToString("0.#####", formatProvider)}mm",
            "in" or "inch" or "inches" => $"{ToInches().ToString("0.######", formatProvider)}in",
            "raw" => _value.ToString(formatProvider),
            _ => throw new FormatException($"Unknown format specifier: {format}")
        };
    }

    /// <summary>
    /// Adds two coordinates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator +(Coord a, Coord b) => new(a._value + b._value);

    /// <summary>
    /// Subtracts one coordinate from another.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator -(Coord a, Coord b) => new(a._value - b._value);

    /// <summary>
    /// Negates a coordinate value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator -(Coord a) => new(-a._value);

    /// <summary>
    /// Multiplies a coordinate by a scalar value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator *(Coord a, double scalar) => new(checked((int)(a._value * scalar)));

    /// <summary>
    /// Multiplies a scalar value by a coordinate.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator *(double scalar, Coord a) => new(checked((int)(a._value * scalar)));

    /// <summary>
    /// Divides a coordinate by a scalar value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord operator /(Coord a, double scalar) => new(checked((int)(a._value / scalar)));

    /// <summary>
    /// Divides one coordinate by another, returning the ratio as a double.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double operator /(Coord a, Coord b) => (double)a._value / b._value;

    /// <inheritdoc />
    public bool Equals(Coord other) => _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Coord other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _value;

    /// <inheritdoc />
    public int CompareTo(Coord other) => _value.CompareTo(other._value);

    /// <summary>
    /// Returns <see langword="true"/> if two coordinates are equal.
    /// </summary>
    public static bool operator ==(Coord left, Coord right) => left._value == right._value;

    /// <summary>
    /// Returns <see langword="true"/> if two coordinates are not equal.
    /// </summary>
    public static bool operator !=(Coord left, Coord right) => left._value != right._value;

    /// <summary>
    /// Returns <see langword="true"/> if the left coordinate is less than the right.
    /// </summary>
    public static bool operator <(Coord left, Coord right) => left._value < right._value;

    /// <summary>
    /// Returns <see langword="true"/> if the left coordinate is less than or equal to the right.
    /// </summary>
    public static bool operator <=(Coord left, Coord right) => left._value <= right._value;

    /// <summary>
    /// Returns <see langword="true"/> if the left coordinate is greater than the right.
    /// </summary>
    public static bool operator >(Coord left, Coord right) => left._value > right._value;

    /// <summary>
    /// Returns <see langword="true"/> if the left coordinate is greater than or equal to the right.
    /// </summary>
    public static bool operator >=(Coord left, Coord right) => left._value >= right._value;

    /// <summary>
    /// Returns the absolute value of a coordinate.
    /// </summary>
    /// <param name="value">The coordinate value.</param>
    /// <returns>The absolute value.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord Abs(Coord value) => new(Math.Abs(value._value));

    /// <summary>
    /// Returns the smaller of two coordinates.
    /// </summary>
    /// <param name="a">The first coordinate.</param>
    /// <param name="b">The second coordinate.</param>
    /// <returns>The smaller of the two values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord Min(Coord a, Coord b) => new(Math.Min(a._value, b._value));

    /// <summary>
    /// Returns the larger of two coordinates.
    /// </summary>
    /// <param name="a">The first coordinate.</param>
    /// <param name="b">The second coordinate.</param>
    /// <returns>The larger of the two values.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Coord Max(Coord a, Coord b) => new(Math.Max(a._value, b._value));
}
