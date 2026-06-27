namespace BlazorSortable.Internal;

/// <summary>
/// Carries the details of a move between zones so a zone can build a typed
/// <see cref="BlazorSortableEventArgs{TItem}"/> for its own item type.
/// </summary>
internal sealed class BlazorSortableMoveInfo
{
    public required object Item { get; init; }
    public int OldIndex { get; init; }
    public int NewIndex { get; init; }
    public required IBlazorSortableZone From { get; init; }
    public required IBlazorSortableZone To { get; init; }
}
