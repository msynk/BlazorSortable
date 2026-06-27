# BlazorSortable

A native Blazor drag-and-drop sortable list, inspired by [SortableJS](https://github.com/SortableJS/Sortable).

BlazorSortable is built on the browser's native HTML5 drag-and-drop events wired through Blazor event handlers — it is **not** a JavaScript-interop wrapper around the SortableJS library. The sorting engine lives in C#; a tiny JavaScript module is used only for drag handles and FLIP animations.

## Features

- Reorder items within a list, or drag them between lists that share a group
- Live reordering as you drag, with smooth FLIP slide animations
- Drag between lists with a ghost preview that shows exactly where the item will land
- `clone` and `move` pull modes, plus per-list `put`/`sort`/`disabled` control
- Drag handles, item filters, and per-item styling
- Works with any layout (vertical lists, grids, nested lists)
- Two-way binding with `@bind-Items`
- SortableJS-style lifecycle events (`OnStart`, `OnEnd`, `OnAdd`, `OnRemove`, `OnUpdate`, `OnSort`, `OnChange`)
- Generic and strongly typed: `Sortable<TItem>`

## Project structure

```
src/
├─ BlazorSortable.slnx
├─ BlazorSortable/            # the Razor Class Library (the component)
└─ BlazorSortable.Demo/       # Blazor WebAssembly demo app
```

## Getting started

### 1. Reference the library

Add a project (or package) reference to `BlazorSortable`.

```xml
<ProjectReference Include="..\src\BlazorSortable\BlazorSortable.csproj" />
```

### 2. Register the service

`Sortable<TItem>` needs a scoped coordinator that tracks the active drag across lists.

```csharp
using BlazorSortable;

builder.Services.AddBlazorSortable();
```

### 3. Add the stylesheet

Reference the bundled CSS from your host page (`index.html` or `App.razor`).

```html
<link rel="stylesheet" href="_content/BlazorSortable/blazorSortable.css" />
```

The JavaScript helper is loaded automatically on demand — no script tag required.

### 4. Make the component available

Add the namespace to `_Imports.razor`:

```razor
@using BlazorSortable
```

## Usage

### Basic list

```razor
<Sortable Items="items" TItem="string">
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>

@code {
    private List<string> items = new() { "Item 1", "Item 2", "Item 3" };
}
```

### Two-way binding

Use `@bind-Items` to keep your own field in sync as items are reordered or moved.

```razor
<Sortable @bind-Items="items" TItem="string">
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>

@code {
    private IList<string> items = new List<string> { "A", "B", "C" };
}
```

> When using `@bind-Items`, declare the field as `IList<TItem>` so its type matches the parameter.

### Shared lists (groups)

Lists with the same `Group` can exchange items.

```razor
<Sortable @bind-Items="left" TItem="string" Group="shared">
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>

<Sortable @bind-Items="right" TItem="string" Group="shared">
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>
```

Restrict the exchange per list with `Pull` and `Put`:

```razor
<!-- Items can be dragged out, but nothing can be dropped in -->
<Sortable @bind-Items="left" TItem="string" Group="shared" Put="false" Sort="false">
    ...
</Sortable>
```

### Cloning

Set `Pull="SortablePull.Clone"` to copy items into the target instead of moving them. Provide a `Clone` factory that returns a **distinct object** for each copy (Blazor requires unique `@key`s, so value-equal copies such as duplicate strings are not allowed).

```razor
<Sortable @bind-Items="source" TItem="Card" Group="cards"
          Pull="SortablePull.Clone" Clone="c => new Card { Name = c.Name }">
    <ItemTemplate Context="card">@card.Name</ItemTemplate>
</Sortable>

<Sortable @bind-Items="target" TItem="Card" Group="cards">
    <ItemTemplate Context="card">@card.Name</ItemTemplate>
</Sortable>
```

### Drag handle

Limit dragging to a handle element by passing a CSS selector.

```razor
<Sortable @bind-Items="items" TItem="string" Handle=".handle">
    <ItemTemplate Context="item">
        <span class="handle">☰</span> @item
    </ItemTemplate>
</Sortable>
```

### Filter (locked items)

Return `true` for items that must not be dragged.

```razor
<Sortable @bind-Items="items" TItem="string" Filter='i => i.StartsWith("Locked")'>
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>
```

### Per-item styling

`ItemClassSelector` appends a class based on the item itself, so styling follows the item even when it moves to another list.

```razor
<Sortable @bind-Items="left" TItem="string" Group="shared"
          ItemClass="card" ItemClassSelector='i => i.StartsWith("B") ? "tinted" : null'>
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>
```

### Events

```razor
<Sortable @bind-Items="items" TItem="string"
          OnUpdate='e => Console.WriteLine($"moved {e.Item}: {e.OldIndex} -> {e.NewIndex}")'
          OnAdd='e => Console.WriteLine($"added {e.Item} from {e.FromId}")'>
    <ItemTemplate Context="item">@item</ItemTemplate>
</Sortable>
```

## API reference

### `Sortable<TItem>` parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Items` | `IList<TItem>?` | `null` | The items to display and reorder. Bind with `@bind-Items`. |
| `ItemTemplate` | `RenderFragment<TItem>?` | `null` | Template rendered for each item. |
| `ChildContent` | `RenderFragment?` | `null` | Extra content rendered after the items (e.g. an empty-state). |
| `Id` | `string` | auto | Stable DOM id for the list. |
| `Group` | `string?` | `null` | Lists sharing a group name can exchange items. |
| `Pull` | `SortablePull` | `Move` | How items leave this list: `Move`, `None`, or `Clone`. |
| `Put` | `bool` | `true` | Whether items from the group may be dropped here. |
| `Sort` | `bool` | `true` | Allow reordering within this list. |
| `Disabled` | `bool` | `false` | Disable all drag behaviour. |
| `Animation` | `int` | `150` | Slide animation duration in ms (`0` disables). |
| `Handle` | `string?` | `null` | CSS selector for a drag handle within each item. |
| `Filter` | `Func<TItem, bool>?` | `null` | Returns `true` for items that cannot be dragged. |
| `Clone` | `Func<TItem, TItem>?` | `null` | Factory used to copy an item in clone mode. |
| `Class` | `string?` | `null` | Extra CSS classes for the list container. |
| `Style` | `string?` | `null` | Inline styles for the list container. |
| `ItemClass` | `string?` | `null` | Base CSS class for every item wrapper. |
| `ItemClassSelector` | `Func<TItem, string?>?` | `null` | Per-item class appended to the wrapper. |
| `GhostClass` | `string` | `sortable-ghost` | Class on the dragged item / preview. |
| `ChosenClass` | `string` | `sortable-chosen` | Class on the chosen item. |

### Events

All events are `EventCallback<SortableEventArgs<TItem>>`.

| Event | Raised when |
|---|---|
| `OnStart` | A drag begins. |
| `OnEnd` | A drag ends (always, on the source list). |
| `OnAdd` | An item is dropped in from another list. |
| `OnRemove` | An item is moved out to another list. |
| `OnUpdate` | An item's position changes within the same list. |
| `OnSort` | Any change to a list (add / remove / update). |
| `OnChange` | The dragged item changes position during the drag. |

### `SortableEventArgs<TItem>`

| Member | Type | Description |
|---|---|---|
| `Item` | `TItem` | The dragged item. |
| `OldIndex` | `int` | Index in the source list. |
| `NewIndex` | `int` | Index in the destination list. |
| `FromId` | `string` | `Id` of the source list. |
| `ToId` | `string` | `Id` of the destination list. |
| `FromGroup` | `string?` | Group of the source list. |
| `ToGroup` | `string?` | Group of the destination list. |
| `CrossedLists` | `bool` | `true` when the item moved between lists. |

### `SortablePull`

| Value | Meaning |
|---|---|
| `Move` | Items can be moved out (default). |
| `None` | Items cannot be dragged out. |
| `Clone` | A copy is placed in the target; the original stays. |

## How it works

- **Native drag events.** Items are `draggable` and the component handles `dragstart`, `dragenter`, `dragover`, `drop`, and `dragend` in C#.
- **Live reordering within a list.** Items are keyed with `@key`, so when the list reorders Blazor *moves* the existing DOM node rather than recreating it. This keeps the native drag alive and lets the reorder happen live.
- **Cross-list preview via a placeholder.** Moving a node between two components would destroy and recreate it, which can cancel the native drag. Instead, the dragged item's data stays in its source list during the drag, and the target renders a ghost **placeholder** showing where the item will land. The actual data move happens on drop. For a move, the source original is hidden (`display:none`, the same technique SortableJS uses) so a single ghost is visible; for a clone, the original stays visible.
- **FLIP animations.** Before each reorder the component records item positions, lets Blazor update the DOM, then transitions each item from its old position to the new one. A small JS module performs the measure/animate step.

## Running the demo

```bash
dotnet run --project src/BlazorSortable.Demo
```

Then open the URL shown in the console. The demo mirrors the SortableJS demo site: simple list, shared lists, cloning, disabling sorting, handle, filter, grid, nested sortables, and an events log.

## Notes and limitations

- Item identity must be unique within a list (Blazor `@key`). For clone mode, use a reference-type model and a `Clone` factory that returns distinct objects.
- Cross-list movement is committed on drop (with a live placeholder preview), rather than physically moving the node between lists mid-drag.
- Not yet implemented: swap thresholds / inverted swap, the MultiDrag and Swap plugins, custom easing, and the `toArray`/`sort`/store helpers.
- Targets .NET 9. Tested with Blazor WebAssembly; the design also works under Blazor Server (the drag state is scoped per circuit).

## License

MIT.
