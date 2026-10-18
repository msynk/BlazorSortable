namespace BlazorSortable.Internal;

/// <summary>
/// Tracks the in-flight drag operation across all participating zones.
/// A single instance lives on <see cref="BlazorSortableService"/> for the duration of one drag.
/// </summary>
internal sealed class BlazorSortableDragContext
{
    /// <summary>The data item currently being dragged (the clone once a clone has been dropped).</summary>
    public required object Item { get; set; }

    /// <summary>The original item where the drag started (equals <see cref="Item"/> unless cloned).</summary>
    public required object OriginalItem { get; init; }

    /// <summary>The zone where the drag began.</summary>
    public required IBlazorSortableZone Source { get; init; }

    /// <summary>The zone that currently contains <see cref="Item"/>.</summary>
    public required IBlazorSortableZone CurrentZone { get; set; }

    /// <summary>Index of the item in the source list when the drag began.</summary>
    public int OldIndex { get; init; }

    /// <summary>The effective pull mode resolved from the source list.</summary>
    public BlazorSortablePull PullMode { get; init; }

    /// <summary>The source list's items when the drag began, to tell whether it really changed.</summary>
    public required IReadOnlyList<object?> StartOrder { get; init; }

    /// <summary>The zone currently showing the drop placeholder (a target preview), if any.</summary>
    public IBlazorSortableZone? PlaceholderZone { get; set; }

    /// <summary>Index within <see cref="PlaceholderZone"/> where the item would be inserted.</summary>
    public int PlaceholderIndex { get; set; }

    /// <summary>
    /// In swap mode, the zone that holds the item currently highlighted as the swap
    /// target (the item the dragged one will trade places with on drop), if any.
    /// </summary>
    public IBlazorSortableZone? SwapTargetZone { get; set; }

    /// <summary>In swap mode, the item highlighted as the swap target, if any.</summary>
    public object? SwapTargetItem { get; set; }

    /// <summary>
    /// In multi-drag mode, the full ordered set of items being dragged together
    /// (including the primary <see cref="Item"/>), in source-list order. <c>null</c>
    /// for a single-item drag.
    /// </summary>
    public List<object>? MultiItems { get; set; }

    /// <summary>Zones whose bound list was modified by this drag.</summary>
    public HashSet<IBlazorSortableZone> ChangedZones { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>Guards against finalizing the same drag twice (drop + dragend).</summary>
    public bool Finished { get; set; }
}
