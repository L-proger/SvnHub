using OriginalCircuit.Eda.Models;

namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Visitor pattern for rendering primitives.
/// </summary>
/// <typeparam name="TContext">The rendering context type (e.g., Graphics, SVG builder).</typeparam>
public interface IPrimitiveVisitor<in TContext>
{
    /// <summary>
    /// Visits and renders a primitive to the context.
    /// </summary>
    /// <param name="primitive">The primitive to visit.</param>
    /// <param name="context">The rendering context.</param>
    void Visit(IPrimitive primitive, TContext context);
}
