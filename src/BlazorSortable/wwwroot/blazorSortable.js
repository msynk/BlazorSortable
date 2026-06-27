// Minimal JS helper for BlazorSortable.
// The sorting engine lives in C#; this module adds two things that need the DOM:
//   1. drag "handles" (a row is draggable only while pressing the handle element)
//   2. FLIP animations so items glide to their new positions instead of snapping.

// listEl -> Map(childElement -> DOMRect) captured just before a reorder.
const flipStore = new WeakMap();

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
