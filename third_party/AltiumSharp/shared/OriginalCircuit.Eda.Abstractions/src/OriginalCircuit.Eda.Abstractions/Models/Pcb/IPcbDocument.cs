using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Pcb;

/// <summary>
/// Represents a PCB document containing placed components and board-level primitives.
/// </summary>
public interface IPcbDocument : IAsyncDisposable
{
    /// <summary>
    /// All placed component instances on the board.
    /// </summary>
    IReadOnlyList<IPcbComponent> Components { get; }

    /// <summary>
    /// All pads on the board.
    /// </summary>
    IReadOnlyList<IPcbPad> Pads { get; }

    /// <summary>
    /// All vias on the board.
    /// </summary>
    IReadOnlyList<IPcbVia> Vias { get; }

    /// <summary>
    /// All tracks on the board.
    /// </summary>
    IReadOnlyList<IPcbTrack> Tracks { get; }

    /// <summary>
    /// All arcs on the board.
    /// </summary>
    IReadOnlyList<IPcbArc> Arcs { get; }

    /// <summary>
    /// All text objects on the board.
    /// </summary>
    IReadOnlyList<IPcbText> Texts { get; }

    /// <summary>
    /// All regions on the board.
    /// </summary>
    IReadOnlyList<IPcbRegion> Regions { get; }

    /// <summary>
    /// Gets the bounding box encompassing all board primitives.
    /// </summary>
    CoordRect Bounds { get; }

    /// <summary>Adds a component to the document.</summary>
    void AddComponent(IPcbComponent component);

    /// <summary>Removes a component from the document.</summary>
    /// <returns><c>true</c> if the component was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveComponent(IPcbComponent component);

    /// <summary>Adds a pad to the document.</summary>
    void AddPad(IPcbPad pad);

    /// <summary>Removes a pad from the document.</summary>
    /// <returns><c>true</c> if the pad was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePad(IPcbPad pad);

    /// <summary>Adds a via to the document.</summary>
    void AddVia(IPcbVia via);

    /// <summary>Removes a via from the document.</summary>
    /// <returns><c>true</c> if the via was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveVia(IPcbVia via);

    /// <summary>Adds a track to the document.</summary>
    void AddTrack(IPcbTrack track);

    /// <summary>Removes a track from the document.</summary>
    /// <returns><c>true</c> if the track was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveTrack(IPcbTrack track);

    /// <summary>Adds an arc to the document.</summary>
    void AddArc(IPcbArc arc);

    /// <summary>Removes an arc from the document.</summary>
    /// <returns><c>true</c> if the arc was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveArc(IPcbArc arc);

    /// <summary>Adds a text object to the document.</summary>
    void AddText(IPcbText text);

    /// <summary>Removes a text object from the document.</summary>
    /// <returns><c>true</c> if the text was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveText(IPcbText text);

    /// <summary>Adds a region to the document.</summary>
    void AddRegion(IPcbRegion region);

    /// <summary>Removes a region from the document.</summary>
    /// <returns><c>true</c> if the region was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveRegion(IPcbRegion region);

    /// <summary>
    /// Saves the document to a file.
    /// </summary>
    /// <param name="path">The file path to save to.</param>
    /// <param name="options">Optional save options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SaveAsync(string path, SaveOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Saves the document to a stream.
    /// </summary>
    /// <param name="stream">The stream to save to.</param>
    /// <param name="options">Optional save options.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SaveAsync(Stream stream, SaveOptions? options = null, CancellationToken ct = default);
}
