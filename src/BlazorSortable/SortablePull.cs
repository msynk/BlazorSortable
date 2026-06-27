namespace BlazorSortable;

/// <summary>
/// Controls whether (and how) items may be dragged out of a <see cref="Sortable{TItem}"/> list,
/// mirroring the <c>group.pull</c> option of SortableJS.
/// </summary>
public enum SortablePull
{
    /// <summary>Items can be moved out of the list (default).</summary>
    Move,

    /// <summary>Items cannot be dragged out of the list.</summary>
    None,

    /// <summary>A copy of the item is placed in the target list; the original stays put.</summary>
    Clone
}
