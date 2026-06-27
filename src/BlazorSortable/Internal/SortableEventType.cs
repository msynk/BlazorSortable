namespace BlazorSortable.Internal;

/// <summary>Identifies which lifecycle callback a zone should raise.</summary>
internal enum SortableEventType
{
    Start,
    End,
    Add,
    Remove,
    Update,
    Sort,
    Change
}
