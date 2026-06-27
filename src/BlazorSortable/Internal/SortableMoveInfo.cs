namespace BlazorSortable.Internal;

/// <summary>
/// Carries the details of a move between zones so a zone can build a typed
/// <see cref="SortableEventArgs{TItem}"/> for its own item type.
/// </summary>
internal sealed class SortableMoveInfo
{
    public required object Item { get; init; }
    public int OldIndex { get; init; }
    public int NewIndex { get; init; }
    public required ISortableZone From { get; init; }
    public required ISortableZone To { get; init; }
}
