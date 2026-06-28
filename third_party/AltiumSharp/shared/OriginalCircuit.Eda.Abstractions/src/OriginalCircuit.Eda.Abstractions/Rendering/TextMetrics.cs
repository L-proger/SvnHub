namespace OriginalCircuit.Eda.Rendering;

/// <summary>
/// Result of measuring text dimensions.
/// </summary>
/// <param name="Width">The measured text width.</param>
/// <param name="Height">The measured text height.</param>
public readonly record struct TextMetrics(double Width, double Height);
