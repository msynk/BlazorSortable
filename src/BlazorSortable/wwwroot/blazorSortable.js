// Minimal JS helper for BlazorSortable.
// The sorting engine lives in C#; this module adds two things that need the DOM:
//   1. drag "handles" (a row is draggable only while pressing the handle element)
//   2. FLIP animations so items glide to their new positions instead of snapping.

// listEl -> Map(childElement -> DOMRect) captured just before a reorder.
const flipStore = new WeakMap();

// --- Self-nesting guard --------------------------------------------------

// Track the item element currently being dragged. ES modules are evaluated
// once (the import is cached), so these document listeners are installed a
// single time no matter how many lists are on the page.
let draggedItemEl = null;
document.addEventListener('dragstart', (e) => {
    const t = e.target;
    draggedItemEl = (t && t.closest) ? t.closest('[data-sortable-item]') : null;
}, true);
document.addEventListener('dragend', () => { draggedItemEl = null; }, true);

// True when the given list lives inside the element being dragged — dropping
// there would nest an item inside itself and detach its subtree from the tree.
export function wouldNest(listEl) {
    return !!(draggedItemEl && listEl && draggedItemEl !== listEl && draggedItemEl.contains(listEl));
}

// --- Handles -------------------------------------------------------------

export function initHandle(listEl, handleSelector) {
    if (!listEl || !handleSelector) return;

    const onPointerDown = (e) => {
        const item = e.target.closest('[data-sortable-item]');
        if (!item || !listEl.contains(item)) return;
        const onHandle = e.target.closest(handleSelector);
        item.setAttribute('draggable', onHandle ? 'true' : 'false');
    };

    const reset = (e) => {
        const item = e.target.closest('[data-sortable-item]');
        if (item) item.setAttribute('draggable', 'false');
    };

    listEl.addEventListener('pointerdown', onPointerDown, true);
    listEl.addEventListener('pointerup', reset, true);
    listEl.addEventListener('dragend', reset, true);
}

// --- Drop position -------------------------------------------------------

// Given a pointer position, return the index (0..count) at which the dragged
// item should be inserted, or -1 when the pointer is in a "dead zone" and the
// current position should be kept.
//
// Mirrors the SortableJS swap-threshold model:
//   - swapThreshold (0..1): the centred fraction of an item that is an active
//     swap zone. Its upper/left half inserts before the item, its lower/right
//     half inserts after it. The rest is a dead zone.
//   - invertSwap + invertedSwapThreshold: instead of a centred zone, the active
//     zones sit at the item's edges (used for "sort between items" behaviour).
//
// The target item is found by 2D hit-testing (the item whose box is under the
// pointer), so grids work in every direction — not just along one row. The
// `direction` axis is then used only to decide whether to land before or after
// that item. When the pointer is not over any item (gaps / trailing padding),
// it clamps to the nearest slot so the end of the list stays reachable.
export function dropIndex(listEl, x, y, swapThreshold, invertSwap, invertedSwapThreshold, direction) {
    if (!listEl) return -1;

    swapThreshold = swapThreshold > 0 ? swapThreshold : 1;
    invertedSwapThreshold = invertedSwapThreshold > 0 ? invertedSwapThreshold : swapThreshold;

    const rects = [];
    for (const child of listEl.children) {
        if (!child.hasAttribute('data-sortable-item')) continue;
        if (child.hasAttribute('data-sortable-placeholder')) continue;
        const r = child.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue; // hidden (e.g. the source ghost)
        rects.push(r);
    }

    const n = rects.length;
    if (n === 0) return 0;

    let horizontal;
    if (direction === 'horizontal') horizontal = true;
    else if (direction === 'vertical') horizontal = false;
    else horizontal = n >= 2 &&
        Math.abs(rects[1].left - rects[0].left) > Math.abs(rects[1].top - rects[0].top);

    const axisPos = (r) => {
        const s1 = horizontal ? r.left : r.top;
        const s2 = horizontal ? r.right : r.bottom;
        return { s1, s2, center: s1 + (s2 - s1) / 2, p: horizontal ? x : y };
    };

    // Item directly under the pointer (both axes) — this is what makes grids work.
    let i = -1;
    for (let k = 0; k < n; k++) {
        const r = rects[k];
        if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) { i = k; break; }
    }

    if (i === -1) {
        // Not over any item (a gap or the trailing padding): clamp to the nearest
        // slot in reading order. Always decisive so the list end stays reachable.
        let best = Infinity;
        for (let k = 0; k < n; k++) {
            const r = rects[k];
            const cx = Math.max(r.left, Math.min(x, r.right));
            const cy = Math.max(r.top, Math.min(y, r.bottom));
            const dx = x - cx, dy = y - cy;
            const d = dx * dx + dy * dy;
            if (d < best) { best = d; i = k; }
        }
        const { center, p } = axisPos(rects[i]);
        return p < center ? i : i + 1;
    }

    const { s1, s2, center, p } = axisPos(rects[i]);
    const len = s2 - s1;

    if (!invertSwap) {
        const margin = len * (1 - swapThreshold) / 2;
        if (p > s1 + margin && p < s2 - margin) {
            return p < center ? i : i + 1;
        }
        return -1; // edge dead zone
    }

    const edge = len * invertedSwapThreshold / 2;
    if (p < s1 + edge || p > s2 - edge) {
        return p > center ? i + 1 : i;
    }
    return -1; // inverted (centre) dead zone
}

// Index (0-based, counting only visible sortable items) of the item whose box
// is directly under the pointer, or -1 when the pointer is over no item. Used by
// swap mode to pick the item the dragged one will trade places with.
export function itemIndexAt(listEl, x, y) {
    if (!listEl) return -1;
    let i = 0;
    for (const child of listEl.children) {
        if (!child.hasAttribute('data-sortable-item')) continue;
        if (child.hasAttribute('data-sortable-placeholder')) continue;
        const r = child.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue; // hidden (e.g. the source ghost)
        if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return i;
        i++;
    }
    return -1;
}

// --- FLIP animation ------------------------------------------------------

// Snapshot the current on-screen position of every item. Reading
// getBoundingClientRect includes any in-flight transform, so animations
// chain smoothly when the user keeps dragging.
export function capture(listEl) {
    if (!listEl) return;
    const map = new Map();
    for (const child of listEl.children) {
        if (!child.hasAttribute('data-sortable-item')) continue;
        map.set(child, child.getBoundingClientRect());
    }
    flipStore.set(listEl, map);
}

// After Blazor has reordered the DOM, move each item back to where it was
// (Invert) then transition the offset away to zero (Play).
export function play(listEl, duration) {
    if (!listEl || duration <= 0) return;
    const prev = flipStore.get(listEl);
    if (!prev) return;
    flipStore.delete(listEl);

    const moved = [];
    for (const child of listEl.children) {
        if (!child.hasAttribute('data-sortable-item')) continue;
        const oldRect = prev.get(child);
        if (!oldRect) continue; // newly added item

        const newRect = child.getBoundingClientRect();

        // Skip items that are hidden before or after the change (e.g. the original
        // item while it is shown as a ghost in another list). Animating those would
        // make them fly in from the corner.
        const wasHidden = oldRect.width === 0 && oldRect.height === 0;
        const isHidden = newRect.width === 0 && newRect.height === 0;
        if (wasHidden || isHidden) continue;

        const dx = oldRect.left - newRect.left;
        const dy = oldRect.top - newRect.top;
        if (dx === 0 && dy === 0) continue;

        child.style.transition = 'none';
        child.style.transform = `translate(${dx}px, ${dy}px)`;
        moved.push(child);
    }

    if (moved.length === 0) return;

    // Force a reflow so the inverted transforms are applied before we animate.
    void listEl.offsetWidth;

    for (const child of moved) {
        child.style.transition = `transform ${duration}ms cubic-bezier(0.22, 1, 0.36, 1)`;
        child.style.transform = '';

        const onEnd = (e) => {
            if (e.propertyName !== 'transform') return;
            child.style.transition = '';
            child.style.transform = '';
            child.removeEventListener('transitionend', onEnd);
        };
        child.addEventListener('transitionend', onEnd);
    }
}

// Strip any leftover inline animation styles (called when a drag ends).
export function clear(listEl) {
    if (!listEl) return;
    flipStore.delete(listEl);
    for (const child of listEl.children) {
        if (!child.hasAttribute('data-sortable-item')) continue;
        child.style.transition = '';
        child.style.transform = '';
    }
}
