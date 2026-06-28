namespace BlazorSortable;

/// <summary>
/// The axis a <see cref="BlazorSortable{TItem}"/> list sorts along, mirroring the
/// <c>direction</c> option of SortableJS. Determines whether swap zones span the
/// width or the height of an item.
/// </summary>
public enum BlazorSortableDirection
{
    /// <summary>Detect the axis automatically from the layout of the items.</summary>
    Auto,

    /// <summary>Sort along the horizontal (x) axis.</summary>
    Horizontal,

    /// <summary>Sort along the vertical (y) axis.</summary>
    Vertical
}
