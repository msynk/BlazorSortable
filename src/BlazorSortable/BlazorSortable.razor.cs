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
public partial class BlazorSortable<TItem> : IBlazorSortableZone, IAsyncDisposable
{
    private ElementReference _listElement;
    private IJSObjectReference? _module;
    private bool _handleInitialized;
    private bool _playFlipOnRender;
    private bool _disposed;

    [Inject] private BlazorSortableService Service { get; set; } = default!;
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
    [Parameter] public BlazorSortablePull Pull { get; set; } = BlazorSortablePull.Move;

    /// <summary>Whether items from other lists in the group may be dropped here.</summary>
    [Parameter] public bool Put { get; set; } = true;

    /// <summary>Allow reordering within this list. Set to <c>false</c> to only allow dragging out.</summary>
    [Parameter] public bool Sort { get; set; } = true;

    /// <summary>
    /// The fraction (0–1) of an item that acts as its swap zone, mirroring the SortableJS
    /// <c>swapThreshold</c> option. With the default <c>1</c> the whole item swaps; a smaller
    /// value (e.g. <c>0.65</c>) leaves a dead zone near each edge so swaps feel less twitchy.
    /// </summary>
    [Parameter] public double SwapThreshold { get; set; } = 1;

    /// <summary>
    /// When <c>true</c> the swap zones sit at the item's edges instead of its centre, giving a
    /// "sort between items" feel. Mirrors the SortableJS <c>invertSwap</c> option.
    /// </summary>
    [Parameter] public bool InvertSwap { get; set; }

    /// <summary>
    /// The fraction (0–1) of an item used for the inverted swap zone. Defaults to
    /// <see cref="SwapThreshold"/> when left at <c>0</c>. Mirrors <c>invertedSwapThreshold</c>.
    /// </summary>
    [Parameter] public double InvertedSwapThreshold { get; set; }

    /// <summary>
    /// The axis to sort along. <see cref="BlazorSortableDirection.Auto"/> detects it from the
    /// layout; set it explicitly for grids. Mirrors the SortableJS <c>direction</c> option.
    /// </summary>
    [Parameter] public BlazorSortableDirection Direction { get; set; } = BlazorSortableDirection.Auto;

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

    /// <summary>Factory used to copy an item when <see cref="Pull"/> is <see cref="BlazorSortablePull.Clone"/>.</summary>
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
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnStart { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnEnd { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnAdd { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnRemove { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnUpdate { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnSort { get; set; }
    [Parameter] public EventCallback<BlazorSortableEventArgs<TItem>> OnChange { get; set; }

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
                if (ctx.PullMode != BlazorSortablePull.Clone)
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

        var ctx = new BlazorSortableDragContext
        {
            Item = item!,
            OriginalItem = item!,
            Source = this,
            CurrentZone = this,
            OldIndex = Items?.IndexOf(item) ?? -1,
            PullMode = Pull
        };
        Service.Context = ctx;

        await RaiseEventAsync(BlazorSortableEventType.Start, new BlazorSortableMoveInfo
        {
            Item = item!,
            OldIndex = ctx.OldIndex,
            NewIndex = ctx.OldIndex,
            From = this,
            To = this
        });
        StateHasChanged();
    }

    /// <summary>
    /// Positions the drag preview as the pointer moves. Within the home list it
    /// reorders the items live; over another list it shows a drop placeholder.
    /// Driven by <c>dragover</c> (which bubbles up from the items) so the pointer
    /// coordinates are available for swap-threshold hit testing.
    /// </summary>
    private async Task OnContainerDragOver(DragEventArgs e)
    {
        var ctx = Service.Context;
        if (ctx is null || Disabled || Items is null) return;

        if (ReferenceEquals(ctx.CurrentZone, this))
            await ReorderWithinAsync(ctx, e);
        else
            await PreviewFromOtherListAsync(ctx, e);
    }

    /// <summary>Reorders the home list as the dragged item moves over it.</summary>
    private async Task ReorderWithinAsync(BlazorSortableDragContext ctx, DragEventArgs e)
    {
        // Returning to the home list: drop any cross-list placeholder first.
        if (ctx.PlaceholderZone is not null)
            await ClearPlaceholderAsync(ctx);

        if (!AllowSort || ctx.Item is not TItem dragged || Items is null) return;

        int from = Items.IndexOf(dragged);
        if (from < 0) return;

        int target = await GetDropIndexAsync(e.ClientX, e.ClientY);
        if (target < 0) return; // dead zone: keep the current order

        int insertAt = target > from ? target - 1 : target;
        if (insertAt == from) return;

        await CaptureFlipAsync();
        Items.RemoveAt(from);
        Items.Insert(insertAt, dragged);
        RequestFlipPlay();

        await RaiseEventAsync(BlazorSortableEventType.Change, new BlazorSortableMoveInfo
        {
            Item = dragged!,
            OldIndex = from,
            NewIndex = insertAt,
            From = this,
            To = this
        });
        StateHasChanged();
    }

    /// <summary>
    /// Previews where an item dragged in from another list would land. The actual
    /// item is not moved until drop, so the native drag stays alive. Hovering past
    /// the last item's swap zone targets the end, making it reachable across lists.
    /// </summary>
    private async Task PreviewFromOtherListAsync(BlazorSortableDragContext ctx, DragEventArgs e)
    {
        if (!CanReceiveFrom(ctx.Source) || ctx.Source.PullMode == BlazorSortablePull.None) return;

        // Refuse to receive an item into its own descendant list, which would
        // detach the dragged subtree from the render tree (data corruption).
        if (await WouldNestAsync()) return;

        int index = await GetDropIndexAsync(e.ClientX, e.ClientY);
        if (index < 0)
        {
            // Dead zone: keep an existing preview, or default to the end when first entering.
            if (ReferenceEquals(ctx.PlaceholderZone, this)) return;
            index = Count;
        }
        if (index > Count) index = Count;
        await ShowPlaceholderAsync(ctx, index);
    }

    private async Task<int> GetDropIndexAsync(double clientX, double clientY)
    {
        if (_disposed || _module is null) return -1;
        try
        {
            return await _module.InvokeAsync<int>("dropIndex", _listElement, clientX, clientY,
                SwapThreshold, InvertSwap, InvertedSwapThreshold, DirectionValue);
        }
        catch (JSDisconnectedException) { return -1; }
        catch (ObjectDisposedException) { return -1; }
    }

    /// <summary>
    /// True when this list lives inside the DOM subtree of the item currently
    /// being dragged, so receiving it here would nest the item inside itself.
    /// </summary>
    private async Task<bool> WouldNestAsync()
    {
        if (_disposed || _module is null) return false;
        try { return await _module.InvokeAsync<bool>("wouldNest", _listElement); }
        catch (JSDisconnectedException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    private string DirectionValue => Direction switch
    {
        BlazorSortableDirection.Horizontal => "horizontal",
        BlazorSortableDirection.Vertical => "vertical",
        _ => "auto"
    };

    /// <summary>Shows (or moves) the drop placeholder in this zone at <paramref name="index"/>.</summary>
    private async Task ShowPlaceholderAsync(BlazorSortableDragContext ctx, int index)
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
    private static async Task ClearPlaceholderAsync(BlazorSortableDragContext ctx)
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
    private async Task CommitPlaceholderAsync(BlazorSortableDragContext ctx)
    {
        if (Items is null) return;

        var source = ctx.CurrentZone;
        int index = ctx.PlaceholderIndex;
        ctx.PlaceholderZone = null;

        await source.CaptureFlipAsync();
        await CaptureFlipAsync();

        object moving;
        if (ctx.PullMode == BlazorSortablePull.Clone)
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

    private async Task FinalizeAsync(BlazorSortableDragContext ctx)
    {
        if (ctx.Finished) return;
        ctx.Finished = true;

        var to = ctx.CurrentZone;
        var placeholderZone = ctx.PlaceholderZone;
        int newIndex = to.IndexOf(ctx.Item);
        var info = new BlazorSortableMoveInfo
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
                await ctx.Source.RaiseEventAsync(BlazorSortableEventType.Update, info);
                await ctx.Source.RaiseEventAsync(BlazorSortableEventType.Sort, info);
            }
        }
        else
        {
            if (ctx.PullMode != BlazorSortablePull.Clone)
                await ctx.Source.RaiseEventAsync(BlazorSortableEventType.Remove, info);
            await to.RaiseEventAsync(BlazorSortableEventType.Add, info);
            await ctx.Source.RaiseEventAsync(BlazorSortableEventType.Sort, info);
            await to.RaiseEventAsync(BlazorSortableEventType.Sort, info);
        }

        await ctx.Source.RaiseEventAsync(BlazorSortableEventType.End, info);

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

    // ----- IBlazorSortableZone --------------------------------------------

    string IBlazorSortableZone.Id => Id;
    string? IBlazorSortableZone.GroupName => Group;
    bool IBlazorSortableZone.AllowSort => Sort;
    bool IBlazorSortableZone.IsDisabled => Disabled;
    BlazorSortablePull IBlazorSortableZone.PullMode => Pull;

    private bool AllowSort => Sort;

    public int Count => Items?.Count ?? 0;

    bool IBlazorSortableZone.CanReceiveFrom(IBlazorSortableZone source) => CanReceiveFrom(source);

    private bool CanReceiveFrom(IBlazorSortableZone source)
    {
        if (Disabled || !Put) return false;
        if (ReferenceEquals(source, this)) return true;
        if (Group is null || source.GroupName is null) return false;
        return string.Equals(Group, source.GroupName, StringComparison.Ordinal);
    }

    int IBlazorSortableZone.IndexOf(object item) => Items is null ? -1 : Items.IndexOf((TItem)item);

    object IBlazorSortableZone.ItemAt(int index) => Items![index]!;

    void IBlazorSortableZone.Insert(int index, object item) => Insert(index, item);

    private void Insert(int index, object item) => Items?.Insert(index, (TItem)item);

    void IBlazorSortableZone.RemoveAt(int index) => Items?.RemoveAt(index);

    object IBlazorSortableZone.CloneItem(object item)
    {
        if (Clone is not null) return Clone((TItem)item)!;
        if (item is ICloneable cloneable) return cloneable.Clone();
        return item; // value types / strings: safe to share
    }

    Task IBlazorSortableZone.CaptureFlipAsync() => CaptureFlipAsync();

    private async Task CaptureFlipAsync()
    {
        if (_disposed || _module is null || Animation <= 0) return;
        try { await _module.InvokeVoidAsync("capture", _listElement); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    void IBlazorSortableZone.RequestFlipPlay() => RequestFlipPlay();

    private void RequestFlipPlay()
    {
        if (Animation > 0) _playFlipOnRender = true;
    }

    async Task IBlazorSortableZone.ClearFlipAsync()
    {
        if (_disposed || _module is null) return;
        try { await _module.InvokeVoidAsync("clear", _listElement); }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }

    async Task IBlazorSortableZone.RefreshAsync() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        StateHasChanged();
        if (ItemsChanged.HasDelegate)
            await ItemsChanged.InvokeAsync(Items);
    }

    async Task IBlazorSortableZone.RaiseEventAsync(BlazorSortableEventType type, BlazorSortableMoveInfo info)
        => await RaiseEventAsync(type, info);

    private Task RaiseEventAsync(BlazorSortableEventType type, BlazorSortableMoveInfo info)
    {
        var args = new BlazorSortableEventArgs<TItem>
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
            BlazorSortableEventType.Start => OnStart.InvokeAsync(args),
            BlazorSortableEventType.End => OnEnd.InvokeAsync(args),
            BlazorSortableEventType.Add => OnAdd.InvokeAsync(args),
            BlazorSortableEventType.Remove => OnRemove.InvokeAsync(args),
            BlazorSortableEventType.Update => OnUpdate.InvokeAsync(args),
            BlazorSortableEventType.Sort => OnSort.InvokeAsync(args),
            BlazorSortableEventType.Change => OnChange.InvokeAsync(args),
            _ => Task.CompletedTask
        };
    }

    // ----- JS interop for handle support ---------------------------------

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;

        if (firstRender)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/BlazorSortable/blazorSortable.js");
        }

        if (!string.IsNullOrEmpty(Handle) && !_handleInitialized && _module is not null)
        {
            _handleInitialized = true;
            try { await _module.InvokeVoidAsync("initHandle", _listElement, Handle); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }

        if (_playFlipOnRender && _module is not null)
        {
            _playFlipOnRender = false;
            try { await _module.InvokeVoidAsync("play", _listElement, Animation); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (Service.Context is { } ctx && ReferenceEquals(ctx.CurrentZone, this))
            Service.Context = null;

        if (_module is not null)
        {
            try { await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }
}
