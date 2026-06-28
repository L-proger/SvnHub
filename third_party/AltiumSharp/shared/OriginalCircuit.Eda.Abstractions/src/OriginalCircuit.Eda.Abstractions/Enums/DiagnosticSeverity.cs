namespace OriginalCircuit.Eda.Enums;

/// <summary>
/// Severity level of a diagnostic message produced during parsing or validation.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message.</summary>
    Info = 0,

    /// <summary>Warning that may indicate a problem.</summary>
    Warning = 1,

    /// <summary>Error that prevents correct processing.</summary>
    Error = 2
}
