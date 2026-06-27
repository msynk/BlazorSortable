namespace BlazorSortable.Internal;

/// <summary>
/// Non-generic surface of a <see cref="Sortable{TItem}"/> list, used by the
/// drag coordinator and sibling zones to manipulate items and raise events
/// without knowing the concrete item type.
/// </summary>
internal interface ISortableZone
{
    /// <summary>Stable DOM id of the list.</summary>
    string Id { get; }

    /// <summary>The group name shared by lists that can exchange items.</summary>
    string? GroupName { get; }

    /// <summary>Whether sorting within this list is permitted.</summary>
    bool AllowSort { get; }

    /// <summary>Whether the list is disabled entirely.</summary>
    bool IsDisabled { get; }

    /// <summary>How items may be pulled out of this list.</summary>
    SortablePull PullMode { get; }

    /// <summary>Whether this list can receive items dragged from <paramref name="source"/>.</summary>
    bool CanReceiveFrom(ISortableZone source);

    int Count { get; }
    int IndexOf(object item);
    object ItemAt(int index);
    void Insert(int index, object item);
    void RemoveAt(int index);

    /// <summary>Creates a copy of <paramref name="item"/> for clone-mode drags.</summary>
    object CloneItem(object item);

    /// <summary>Snapshots item positions before a reorder (for FLIP animation).</summary>
    Task CaptureFlipAsync();

    /// <summary>Requests that this zone play a FLIP animation on its next render.</summary>
    void RequestFlipPlay();

    /// <summary>Removes any leftover inline animation styles.</summary>
    Task ClearFlipAsync();

    /// <summary>Re-renders the list and notifies <c>ItemsChanged</c> for two-way binding.</summary>
    Task RefreshAsync();

    /// <summary>Raises the given lifecycle event on this zone.</summary>
    Task RaiseEventAsync(SortableEventType type, SortableMoveInfo info);
}
