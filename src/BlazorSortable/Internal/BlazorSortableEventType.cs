namespace BlazorSortable.Internal;

/// <summary>Identifies which lifecycle callback a zone should raise.</summary>
internal enum BlazorSortableEventType
{
    Start,
    End,
    Add,
    Remove,
    Update,
    Sort,
    Change
}
