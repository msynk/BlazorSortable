using BlazorSortable.Internal;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace BlazorSortable;

/// <summary>
/// A native Blazor drag-and-drop sortable list, inspired by SortableJS.
/// Reorder items within a list or drag them between lists that share a <see cref="Group"/>.
/// </summary>
/// <typeparam name="TItem">The type of the bound items.</typeparam>
public partial class Sortable<TItem> : ISortableZone, IAsyncDisposable
{
    private ElementReference _listElement;
    private IJSObjectReference? _module;
    private bool _handleInitialized;
    private bool _playFlipOnRender;

    [Inject] private SortableService Service { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The list of items to display and reorder. Bind with <c>@bind-Items</c>.</summary>
    [Parameter] public IList<TItem>? Items { get; set; }

    /// <summary>Raised when the bound list changes (supports <c>@bind-Items</c>).</summary>
    [Parameter] public EventCallback<IList<TItem>> ItemsChanged { get; set; }

    /// <summary>Template rendered for each item.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>Optional extra content rendered after the items (e.g. an empty-state placeholder).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Stable DOM id for the list. Auto-generated when not supplied.</summary>
    [Parameter] public string Id { get; set; } = $"sortable-{Guid.NewGuid():N}";

    /// <summary>Lists that share the same group name can exchange items.</summary>
    [Parameter] public string? Group { get; set; }

    /// <summary>Controls how items may be dragged out of this list.</summary>
    [Parameter] public SortablePull Pull { get; set; } = SortablePull.Move;

    /// <summary>Whether items from other lists in the group may be dropped here.</summary>
    [Parameter] public bool Put { get; set; } = true;

    /// <summary>Allow reordering within this list. Set to <c>false</c> to only allow dragging out.</summary>
    [Parameter] public bool Sort { get; set; } = true;

    /// <summary>Disables all drag behaviour when <c>true</c>.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Duration in milliseconds of the slide animation when items move to new
    /// positions, mirroring the SortableJS <c>animation</c> option. Set to <c>0</c>
    /// to disable animations. Default is <c>150</c>.
    /// </summary>
    [Parameter] public int Animation { get; set; } = 150;

    /// <summary>CSS selector for a drag handle within each item. Requires JS interop.</summary>
    [Parameter] public string? Handle { get; set; }

    /// <summary>Predicate returning <c>true</c> for items that must not be dragged.</summary>
    [Parameter] public Func<TItem, bool>? Filter { get; set; }

    /// <summary>Factory used to copy an item when <see cref="Pull"/> is <see cref="SortablePull.Clone"/>.</summary>
    [Parameter] public Func<TItem, TItem>? Clone { get; set; }

    /// <summary>Extra CSS classes for the list container.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Inline styles for the list container.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>Base CSS class applied to every item wrapper.</summary>
    [Parameter] public string? ItemClass { get; set; }

    /// <summary>
    /// Optional per-item class selector. Its result is appended to each item's wrapper
    /// class, letting an item keep its own styling regardless of which list holds it.
    /// </summary>
    [Parameter] public Func<TItem, string?>? ItemClassSelector { get; set; }

    /// <summary>Class applied to the item being dragged (the placeholder). Default <c>sortable-ghost</c>.</summary>
    [Parameter] public string GhostClass { get; set; } = "sortable-ghost";

    /// <summary>Class applied to the chosen item. Default <c>sortable-chosen</c>.</summary>
    [Parameter] public string ChosenClass { get; set; } = "sortable-chosen";

    // Lifecycle events (mirroring SortableJS).
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnStart { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnEnd { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnAdd { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnRemove { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnUpdate { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnSort { get; set; }
    [Parameter] public EventCallback<SortableEventArgs<TItem>> OnChange { get; set; }

    private static readonly EqualityComparer<TItem> Comparer = EqualityComparer<TItem>.Default;

    // Stable @key for the placeholder element so Blazor animates it instead of recreating it.
    private static readonly object PlaceholderKey = new();

    // ----- rendering helpers ---------------------------------------------

    private string ContainerCssClass
    {
        get
        {
            var classes = "sortable";
            if (!string.IsNullOrWhiteSpace(Class)) classes += " " + Class;
            if (Disabled) classes += " sortable-disabled";
            return classes;
        }
    }

    private string ItemCssClass(TItem item)
    {
        var classes = "sortable-item";
        if (!string.IsNullOrWhiteSpace(ItemClass)) classes += " " + ItemClass;

        var extra = ItemClassSelector?.Invoke(item);
        if (!string.IsNullOrWhiteSpace(extra)) classes += " " + extra;

        var ctx = Service.Context;
        if (ctx is not null && ReferenceEquals(ctx.CurrentZone, this) && ctx.Item is TItem dragged && Comparer.Equals(dragged, item))
        {
            if (ctx.PlaceholderZone is not null && !ReferenceEquals(ctx.PlaceholderZone, this))
            {
                // Previewed as the ghost in another list. For a move, hide the original here
                // (same as within-list). For a clone, the original must stay put and visible.
                if (ctx.PullMode != SortablePull.Clone)
                    classes += " sortable-hidden";
            }
            else
            {
                classes += " " + GhostClass + " " + ChosenClass;
            }
        }
        if (Filter is not null && Filter(item)) classes += " sortable-filtered";
        return classes;
    }

    // The preview slot uses the exact same ghost styling as a within-list drag,
    // and keeps the dragged item's own per-item class so its style is preserved.
    private string PlaceholderCssClass()
    {
        var classes = "sortable-item";
        if (!string.IsNullOrWhiteSpace(ItemClass)) classes += " " + ItemClass;

        if (ItemClassSelector is not null && Service.Context?.Item is TItem preview)
        {
            var extra = ItemClassSelector(preview);
            if (!string.IsNullOrWhiteSpace(extra)) classes += " " + extra;
        }

        classes += " " + GhostClass;
        if (!string.IsNullOrWhiteSpace(ChosenClass)) classes += " " + ChosenClass;
        return classes;
    }

    private bool IsDraggable(TItem item)
    {
        if (Disabled) return false;
        if (Filter is not null && Filter(item)) return false;
        // With a handle, the row is not draggable until the JS helper enables it
        // when the pointer goes down on the handle element.
        if (!string.IsNullOrEmpty(Handle)) return false;
        return true;
    }

    // ----- drag event handlers -------------------------------------------

    private async Task OnItemDragStart(TItem item)
    {
        if (Disabled || (Filter is not null && Filter(item))) return;

        // Defensive: clear any context left over from a drag that ended outside a list.
        if (Service.Context is { Finished: false } stale)
        {
            await stale.Source.ClearFlipAsync();
            await stale.CurrentZone.ClearFlipAsync();
        }
        Service.Context = null;

        var ctx = new DragContext
        {
            Item = item!,
            OriginalItem = item!,
            Source = this,
            CurrentZone = this,
            OldIndex = Items?.IndexOf(item) ?? -1,
            PullMode = Pull
        };
        Service.Context = ctx;

        await RaiseEventAsync(SortableEventType.Start, new SortableMoveInfo
        {
            Item = item!,
            OldIndex = ctx.OldIndex,
            NewIndex = ctx.OldIndex,
            From = this,
            To = this
        });
        StateHasChanged();
    }

    private async Task OnItemDragEnter(TItem overItem)
    {
        var ctx = Service.Context;
        if (ctx is null || Disabled || Items is null) return;

        if (ReferenceEquals(ctx.CurrentZone, this))
        {
            // Back over the home list: drop any cross-list placeholder and reorder for real.
            await ClearPlaceholderAsync(ctx);

            if (!AllowSort) return;
            if (ctx.Item is not TItem dragged || Comparer.Equals(dragged, overItem)) return;

            int from = Items.IndexOf(dragged);
            int to = Items.IndexOf(overItem);
            if (from < 0 || to < 0) return;

            await CaptureFlipAsync();
            Items.RemoveAt(from);
            int insertAt = from < to ? Items.IndexOf(overItem) + 1 : Items.IndexOf(overItem);
            Items.Insert(insertAt, dragged);
            RequestFlipPlay();

            await RaiseEventAsync(SortableEventType.Change, new SortableMoveInfo
            {
                Item = dragged!,
                OldIndex = from,
                NewIndex = insertAt,
                From = this,
                To = this
            });
            StateHasChanged();
        }
        else
        {
            // Dragged in from another list: show a placeholder where it would land.
            // The actual item is not moved until drop, so the native drag stays alive.
            if (!CanReceiveFrom(ctx.Source) || ctx.Source.PullMode == SortablePull.None) return;
            int index = Items.IndexOf(overItem);
            if (index < 0) index = Count;
            await ShowPlaceholderAsync(ctx, index);
        }
    }

    private async Task OnContainerDragEnter()
    {
        var ctx = Service.Context;
        if (ctx is null || Disabled || Items is null) return;
        if (ReferenceEquals(ctx.CurrentZone, this)) return; // home list handles its own items
        if (!CanReceiveFrom(ctx.Source) || ctx.Source.PullMode == SortablePull.None) return;

        // Over the empty area / padding: preview an append at the end.
        await ShowPlaceholderAsync(ctx, Count);
    }

    /// <summary>Shows (or moves) the drop placeholder in this zone at <paramref name="index"/>.</summary>
    private async Task ShowPlaceholderAsync(DragContext ctx, int index)
    {
        if (ReferenceEquals(ctx.PlaceholderZone, this) && ctx.PlaceholderIndex == index) return;

        var oldZone = ctx.PlaceholderZone;
        var source = ctx.CurrentZone; // holds the original item (hidden while previewed)

        await CaptureFlipAsync();
        if (oldZone is not null && !ReferenceEquals(oldZone, this)) await oldZone.CaptureFlipAsync();
        if (!ReferenceEquals(source, this) && !ReferenceEquals(source, oldZone)) await source.CaptureFlipAsync();

        ctx.PlaceholderZone = this;
        ctx.PlaceholderIndex = index;

        if (oldZone is not null && !ReferenceEquals(oldZone, this))
        {
            oldZone.RequestFlipPlay();
            await oldZone.RefreshAsync();
        }
        if (!ReferenceEquals(source, this) && !ReferenceEquals(source, oldZone))
        {
            source.RequestFlipPlay();
            await source.RefreshAsync();
        }
        RequestFlipPlay();
        await RefreshAsync();
        StateHasChanged();
    }

    /// <summary>Removes the placeholder from whichever zone is currently showing it.</summary>
    private static async Task ClearPlaceholderAsync(DragContext ctx)
    {
        var ph = ctx.PlaceholderZone;
        if (ph is null) return;
        await ph.CaptureFlipAsync();
        ctx.PlaceholderZone = null;
        ph.RequestFlipPlay();
        await ph.RefreshAsync();
    }

    private bool IsPlaceholderAt(int index)
    {
        var ctx = Service.Context;
        return ctx is not null
            && ReferenceEquals(ctx.PlaceholderZone, this)
            && ctx.PlaceholderIndex == index;
    }

    private Task OnItemDrop(DragEventArgs _) => DropAsync();

    private Task OnPlaceholderDrop(DragEventArgs _) => DropAsync();

    private Task OnContainerDragOver(DragEventArgs _) => Task.CompletedTask;

    private Task OnContainerDrop(DragEventArgs _) => DropAsync();

    private async Task OnItemDragEnd()
    {
        // Fires on the (untouched) source node. Commits a pending cross-list placeholder
        // if the drop landed there, otherwise finalizes the in-list reorder or cancels.
        await DropAsync();
    }

    private async Task DropAsync()
    {
        var ctx = Service.Context;
        if (ctx is null) return;

        if (ReferenceEquals(ctx.PlaceholderZone, this) && !ReferenceEquals(ctx.CurrentZone, this))
            await CommitPlaceholderAsync(ctx);

        await FinalizeAsync(ctx);
    }

    /// <summary>Moves the dragged item from its source list into this list at the placeholder index.</summary>
    private async Task CommitPlaceholderAsync(DragContext ctx)
    {
        if (Items is null) return;

        var source = ctx.CurrentZone;
        int index = ctx.PlaceholderIndex;
        ctx.PlaceholderZone = null;

        await source.CaptureFlipAsync();
        await CaptureFlipAsync();

        object moving;
        if (ctx.PullMode == SortablePull.Clone)
        {
            moving = ctx.Source.CloneItem(ctx.OriginalItem);
            ctx.HasCloned = true;
        }
        else
        {
            moving = ctx.Item;
            int currentIndex = source.IndexOf(moving);
            if (currentIndex >= 0) source.RemoveAt(currentIndex);
            source.RequestFlipPlay();
            await source.RefreshAsync();
        }

        if (index < 0 || index > Count) index = Count;
        Insert(index, moving);

        ctx.Item = moving;
        ctx.CurrentZone = this;
        RequestFlipPlay();
        await RefreshAsync();
    }

    private async Task FinalizeAsync(DragContext ctx)
    {
        if (ctx.Finished) return;
        ctx.Finished = true;

        var to = ctx.CurrentZone;
        var placeholderZone = ctx.PlaceholderZone;
        int newIndex = to.IndexOf(ctx.Item);
        var info = new SortableMoveInfo
        {
            Item = ctx.Item,
            OldIndex = ctx.OldIndex,
            NewIndex = newIndex,
            From = ctx.Source,
            To = to
        };

        if (ReferenceEquals(to, ctx.Source))
        {
            if (newIndex != ctx.OldIndex)
            {
                await ctx.Source.RaiseEventAsync(SortableEventType.Update, info);
                await ctx.Source.RaiseEventAsync(SortableEventType.Sort, info);
            }
        }
        else
        {
            if (ctx.PullMode != SortablePull.Clone)
                await ctx.Source.RaiseEventAsync(SortableEventType.Remove, info);
            await to.RaiseEventAsync(SortableEventType.Add, info);
            await ctx.Source.RaiseEventAsync(SortableEventType.Sort, info);
            await to.RaiseEventAsync(SortableEventType.Sort, info);
        }

        await ctx.Source.RaiseEventAsync(SortableEventType.End, info);

        Service.Context = null;
        ctx.PlaceholderZone = null;

        await ctx.Source.ClearFlipAsync();
        await to.ClearFlipAsync();
        await ctx.Source.RefreshAsync();
        await to.RefreshAsync();

        // A cancelled cross-list drag leaves a placeholder in a third zone: clear it too.
        if (placeholderZone is not null
            && !ReferenceEquals(placeholderZone, ctx.Source)
            && !ReferenceEquals(placeholderZone, to))
        {
            await placeholderZone.ClearFlipAsync();
            await placeholderZone.RefreshAsync();
        }
    }

    // ----- ISortableZone --------------------------------------------------

    string ISortableZone.Id => Id;
    string? ISortableZone.GroupName => Group;
    bool ISortableZone.AllowSort => Sort;
    bool ISortableZone.IsDisabled => Disabled;
    SortablePull ISortableZone.PullMode => Pull;

    private bool AllowSort => Sort;

    public int Count => Items?.Count ?? 0;

    bool ISortableZone.CanReceiveFrom(ISortableZone source) => CanReceiveFrom(source);

    private bool CanReceiveFrom(ISortableZone source)
    {
        if (Disabled || !Put) return false;
        if (ReferenceEquals(source, this)) return true;
        if (Group is null || source.GroupName is null) return false;
        return string.Equals(Group, source.GroupName, StringComparison.Ordinal);
    }

    int ISortableZone.IndexOf(object item) => Items is null ? -1 : Items.IndexOf((TItem)item);

    object ISortableZone.ItemAt(int index) => Items![index]!;

    void ISortableZone.Insert(int index, object item) => Insert(index, item);

    private void Insert(int index, object item) => Items?.Insert(index, (TItem)item);

    void ISortableZone.RemoveAt(int index) => Items?.RemoveAt(index);

    object ISortableZone.CloneItem(object item)
    {
        if (Clone is not null) return Clone((TItem)item)!;
        if (item is ICloneable cloneable) return cloneable.Clone();
        return item; // value types / strings: safe to share
    }

    Task ISortableZone.CaptureFlipAsync() => CaptureFlipAsync();

    private async Task CaptureFlipAsync()
    {
        if (Animation > 0 && _module is not null)
        {
            try { await _module.InvokeVoidAsync("capture", _listElement); }
            catch (JSDisconnectedException) { }
        }
    }

    void ISortableZone.RequestFlipPlay() => RequestFlipPlay();

    private void RequestFlipPlay()
    {
        if (Animation > 0) _playFlipOnRender = true;
    }

    async Task ISortableZone.ClearFlipAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("clear", _listElement); }
            catch (JSDisconnectedException) { }
        }
    }

    async Task ISortableZone.RefreshAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StateHasChanged();
        if (ItemsChanged.HasDelegate)
            await ItemsChanged.InvokeAsync(Items);
    }

    async Task ISortableZone.RaiseEventAsync(SortableEventType type, SortableMoveInfo info)
        => await RaiseEventAsync(type, info);

    private Task RaiseEventAsync(SortableEventType type, SortableMoveInfo info)
    {
        var args = new SortableEventArgs<TItem>
        {
            Item = (TItem)info.Item,
            OldIndex = info.OldIndex,
            NewIndex = info.NewIndex,
            FromId = info.From.Id,
            ToId = info.To.Id,
            FromGroup = info.From.GroupName,
            ToGroup = info.To.GroupName
        };

        return type switch
        {
            SortableEventType.Start => OnStart.InvokeAsync(args),
            SortableEventType.End => OnEnd.InvokeAsync(args),
            SortableEventType.Add => OnAdd.InvokeAsync(args),
            SortableEventType.Remove => OnRemove.InvokeAsync(args),
            SortableEventType.Update => OnUpdate.InvokeAsync(args),
            SortableEventType.Sort => OnSort.InvokeAsync(args),
            SortableEventType.Change => OnChange.InvokeAsync(args),
            _ => Task.CompletedTask
        };
    }

    // ----- JS interop for handle support ---------------------------------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/BlazorSortable/blazorSortable.js");
        }

        if (!string.IsNullOrEmpty(Handle) && !_handleInitialized && _module is not null)
        {
            _handleInitialized = true;
            await _module.InvokeVoidAsync("initHandle", _listElement, Handle);
        }

        if (_playFlipOnRender && _module is not null)
        {
            _playFlipOnRender = false;
            try { await _module.InvokeVoidAsync("play", _listElement, Animation); }
            catch (JSDisconnectedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Service.Context is { } ctx && ReferenceEquals(ctx.CurrentZone, this))
            Service.Context = null;

        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
    }
}
