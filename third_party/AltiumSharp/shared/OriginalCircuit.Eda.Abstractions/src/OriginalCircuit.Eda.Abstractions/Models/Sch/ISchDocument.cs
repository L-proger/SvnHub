using OriginalCircuit.Eda.Primitives;

namespace OriginalCircuit.Eda.Models.Sch;

/// <summary>
/// Represents a schematic document containing placed components and top-level primitives.
/// </summary>
public interface ISchDocument : IAsyncDisposable
{
    /// <summary>
    /// All components placed in this document.
    /// </summary>
    IReadOnlyList<ISchComponent> Components { get; }

    /// <summary>
    /// All wires in this document (top-level, not owned by components).
    /// </summary>
    IReadOnlyList<ISchWire> Wires { get; }

    /// <summary>
    /// All net labels in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchNetLabel> NetLabels { get; }

    /// <summary>
    /// All junctions in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchJunction> Junctions { get; }

    /// <summary>
    /// All power objects in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchPowerObject> PowerObjects { get; }

    /// <summary>
    /// All labels in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchLabel> Labels { get; }

    /// <summary>
    /// All no-connect markers in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchNoConnect> NoConnects { get; }

    /// <summary>
    /// All buses in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchBus> Buses { get; }

    /// <summary>
    /// All bus entries in this document (top-level).
    /// </summary>
    IReadOnlyList<ISchBusEntry> BusEntries { get; }

    /// <summary>
    /// Gets the bounding box encompassing all primitives.
    /// </summary>
    CoordRect Bounds { get; }

    /// <summary>Adds a component to the document.</summary>
    void AddComponent(ISchComponent component);

    /// <summary>Removes a component from the document.</summary>
    /// <returns><c>true</c> if the component was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveComponent(ISchComponent component);

    /// <summary>Adds a wire to the document.</summary>
    void AddWire(ISchWire wire);

    /// <summary>Removes a wire from the document.</summary>
    /// <returns><c>true</c> if the wire was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveWire(ISchWire wire);

    /// <summary>Adds a net label to the document.</summary>
    void AddNetLabel(ISchNetLabel netLabel);

    /// <summary>Removes a net label from the document.</summary>
    /// <returns><c>true</c> if the net label was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveNetLabel(ISchNetLabel netLabel);

    /// <summary>Adds a junction to the document.</summary>
    void AddJunction(ISchJunction junction);

    /// <summary>Removes a junction from the document.</summary>
    /// <returns><c>true</c> if the junction was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveJunction(ISchJunction junction);

    /// <summary>Adds a power object to the document.</summary>
    void AddPowerObject(ISchPowerObject powerObject);

    /// <summary>Removes a power object from the document.</summary>
    /// <returns><c>true</c> if the power object was found and removed; otherwise <c>false</c>.</returns>
    bool RemovePowerObject(ISchPowerObject powerObject);

    /// <summary>Adds a label to the document.</summary>
    void AddLabel(ISchLabel label);

    /// <summary>Removes a label from the document.</summary>
    /// <returns><c>true</c> if the label was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveLabel(ISchLabel label);

    /// <summary>Adds a no-connect marker to the document.</summary>
    void AddNoConnect(ISchNoConnect noConnect);

    /// <summary>Removes a no-connect marker from the document.</summary>
    /// <returns><c>true</c> if the no-connect was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveNoConnect(ISchNoConnect noConnect);

    /// <summary>Adds a bus to the document.</summary>
    void AddBus(ISchBus bus);

    /// <summary>Removes a bus from the document.</summary>
    /// <returns><c>true</c> if the bus was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveBus(ISchBus bus);

    /// <summary>Adds a bus entry to the document.</summary>
    void AddBusEntry(ISchBusEntry busEntry);

    /// <summary>Removes a bus entry from the document.</summary>
    /// <returns><c>true</c> if the bus entry was found and removed; otherwise <c>false</c>.</returns>
    bool RemoveBusEntry(ISchBusEntry busEntry);

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
