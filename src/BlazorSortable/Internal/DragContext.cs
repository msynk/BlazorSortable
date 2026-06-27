namespace BlazorSortable.Internal;

/// <summary>
/// Tracks the in-flight drag operation across all participating zones.
/// A single instance lives on <see cref="SortableService"/> for the duration of one drag.
/// </summary>
internal sealed class DragContext
{
    /// <summary>The data item currently being dragged (a clone in clone-mode).</summary>
    public required object Item { get; set; }

    /// <summary>The original item where the drag started (equals <see cref="Item"/> unless cloned).</summary>
    public required object OriginalItem { get; init; }

    /// <summary>The zone where the drag began.</summary>
    public required ISortableZone Source { get; init; }

    /// <summary>The zone that currently contains <see cref="Item"/>.</summary>
    public required ISortableZone CurrentZone { get; set; }

    /// <summary>Index of the item in the source list when the drag began.</summary>
    public int OldIndex { get; init; }

    /// <summary>The effective pull mode resolved from the source list.</summary>
    public SortablePull PullMode { get; init; }

    /// <summary>True once a clone has been spun off into another list.</summary>
    public bool HasCloned { get; set; }

    /// <summary>The zone currently showing the drop placeholder (a target preview), if any.</summary>
    public ISortableZone? PlaceholderZone { get; set; }

    /// <summary>Index within <see cref="PlaceholderZone"/> where the item would be inserted.</summary>
    public int PlaceholderIndex { get; set; }

    /// <summary>Guards against finalizing the same drag twice (drop + dragend).</summary>
    public bool Finished { get; set; }
}
