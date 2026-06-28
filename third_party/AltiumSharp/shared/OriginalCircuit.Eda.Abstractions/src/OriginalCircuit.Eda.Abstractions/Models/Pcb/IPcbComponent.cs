using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB footprint/component containing pads, tracks, and other primitives.
/// </summary>
public interface IPcbComponent : IComponent
{
    /// <summary>
    /// The component height (for 3D clearance calculations).
    /// </summary>
    Coord Height { get; set; }

    /// <summary>
    /// The layer the component is placed on.
    /// </summary>
    int Layer { get; }

    /// <summary>
    /// All pads belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbPad> Pads { get; }

    /// <summary>
    /// All tracks belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbTrack> Tracks { get; }

    /// <summary>
    /// All vias belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbVia> Vias { get; }

    /// <summary>
    /// All arcs belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbArc> Arcs { get; }

    /// <summary>
    /// All text objects belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbText> Texts { get; }

    /// <summary>
    /// All regions belonging to this component.
    /// </summary>
    IReadOnlyList<IPcbRegion> Regions { get; }

    /// <summary>Adds a pad to the component.</summary>
    void AddPad(IPcbPad pad);

    /// <summary>Removes a pad from the component.</summary>
    /// <returns><c>true</c> if the pad was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePad(IPcbPad pad);

    /// <summary>Adds a track to the component.</summary>
    void AddTrack(IPcbTrack track);

    /// <summary>Removes a track from the component.</summary>
    /// <returns><c>true</c> if the track was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveTrack(IPcbTrack track);

    /// <summary>Adds a via to the component.</summary>
    void AddVia(IPcbVia via);

    /// <summary>Removes a via from the component.</summary>
    /// <returns><c>true</c> if the via was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveVia(IPcbVia via);

    /// <summary>Adds an arc to the component.</summary>
    void AddArc(IPcbArc arc);

    /// <summary>Removes an arc from the component.</summary>
    /// <returns><c>true</c> if the arc was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveArc(IPcbArc arc);

    /// <summary>Adds a text object to the component.</summary>
    void AddText(IPcbText text);

    /// <summary>Removes a text object from the component.</summary>
    /// <returns><c>true</c> if the text was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveText(IPcbText text);

    /// <summary>Adds a region to the component.</summary>
    void AddRegion(IPcbRegion region);

    /// <summary>Removes a region from the component.</summary>
    /// <returns><c>true</c> if the region was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveRegion(IPcbRegion region);
}
