using BlazorSortable.Internal;

namespace BlazorSortable;

/// <summary>
/// Per-circuit coordinator that holds the active drag operation so that
/// multiple <see cref="BlazorSortable{TItem}"/> lists in the same group can exchange items.
/// Register with <c>builder.Services.AddBlazorSortable()</c>.
/// </summary>
public sealed class BlazorSortableService
{
    internal BlazorSortableDragContext? Context { get; set; }

    /// <summary>
    /// Serializes the drag handlers of all lists. Each handler awaits JS interop, so
    /// without it a <c>dragover</c> could interleave with another one (or with the
    /// <c>drop</c>/<c>dragend</c> that finishes the drag) and act on stale indices.
    /// </summary>
    internal SemaphoreSlim Gate { get; } = new(1, 1);
}
