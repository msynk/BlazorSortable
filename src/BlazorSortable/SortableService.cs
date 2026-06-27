using BlazorSortable.Internal;

namespace BlazorSortable;

/// <summary>
/// Per-circuit coordinator that holds the active drag operation so that
/// multiple <see cref="Sortable{TItem}"/> lists in the same group can exchange items.
/// Register with <c>builder.Services.AddBlazorSortable()</c>.
/// </summary>
public sealed class SortableService
{
    internal DragContext? Context { get; set; }
}
