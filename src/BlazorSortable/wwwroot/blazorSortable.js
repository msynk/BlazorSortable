// Minimal JS helper for BlazorSortable.
// The sorting engine lives in C#; this module adds the parts that need the DOM:
//   1. drag "handles" (an item is draggable only while pressing its handle element)
//   2. drop-position hit testing, using the SortableJS swap-threshold model
//   3. FLIP animations so items glide to their new positions instead of snapping.

const ITEM = 'data-sortable-item';
const PLACEHOLDER = 'data-sortable-placeholder';
const NO_DRAG = 'data-sortable-disabled';
const EASING = 'cubic-bezier(0.22, 1, 0.36, 1)';

// listEl -> Map(item element -> DOMRect) captured just before a reorder.
const flipStore = new WeakMap();

// item element -> { dx, dy } of the FLIP animation currently running on it.
const animations = new WeakMap();

// listEl -> pointerdown listener installed for its drag handle.
const handleStore = new WeakMap();

// --- Drag tracking -------------------------------------------------------

// The item element currently being dragged, plus the SortableJS swap state.
// ES modules are evaluated once (the import is cached), so these document
// listeners are installed a single time no matter how many lists are on the page.
let draggedItemEl = null;
let lastTarget = null;
let lastDirection = 0;
let targetBeforeFirstSwap = 0;
let pastFirstInvertThresh = false;
let isCircumstantialInvert = false;
let ignoreWhileAnimating = null;

function resetDragState() {
    draggedItemEl = null;
    lastTarget = null;
    lastDirection = 0;
    targetBeforeFirstSwap = 0;
    pastFirstInvertThresh = false;
    isCircumstantialInvert = false;
    ignoreWhileAnimating = null;
}

document.addEventListener('dragstart', (e) => {
    resetDragState();
    const item = e.target instanceof Element ? e.target.closest(`[${ITEM}]`) : null;
    if (!item) return;

    if (item.getAttribute('draggable') !== 'true') {
        // A native drag of something inside an item that is not itself draggable
        // right now (e.g. a link or image outside the handle, or a filtered item).
        // Keep it a plain browser drag: don't let the list treat it as a sort.
        e.stopPropagation();
        return;
    }

    draggedItemEl = item;
    if (e.dataTransfer) {
        // Some browsers (older Firefox) only start a drag once some data has been set.
        if (e.dataTransfer.types.length === 0) e.dataTransfer.setData('application/x-blazor-sortable', item.parentElement?.id || 'item');
        e.dataTransfer.effectAllowed = 'move';
    }
}, true);

// The state is deliberately kept after dragend and only reset by the next dragstart:
// with Blazor Server, the last dragover events of a drag can reach .NET (and call
// back into this module) after the browser has already fired dragend.

// True when the given list must not receive the dragged item: it lives inside
// the element being dragged (dropping there would nest an item inside itself and
// detach its subtree), or inside the drop placeholder (a preview copy of the item).
export function wouldNest(listEl) {
    if (!listEl) return false;
    if (listEl.closest(`[${PLACEHOLDER}]`)) return true;
    return !!(draggedItemEl && draggedItemEl !== listEl && draggedItemEl.contains(listEl));
}

// --- Handles -------------------------------------------------------------

// Installs (or replaces, or with a null selector removes) the drag handle of a
// list. Items render as non-draggable; pressing a handle makes its item draggable.
export function setHandle(listEl, handleSelector) {
    if (!listEl) return;

    const previous = handleStore.get(listEl);
    if (previous) {
        listEl.removeEventListener('pointerdown', previous, true);
        listEl.removeEventListener('dragend', resetHandle, true);
        handleStore.delete(listEl);
    }
    if (!handleSelector) return;

    const onPointerDown = (e) => {
        const item = ownItem(listEl, e.target);
        if (!item) return;
        const handle = e.target.closest(handleSelector);
        const onHandle = !!handle && handle.closest(`[${ITEM}]`) === item && !item.hasAttribute(NO_DRAG);
        item.setAttribute('draggable', onHandle ? 'true' : 'false');
    };

    handleStore.set(listEl, onPointerDown);
    listEl.addEventListener('pointerdown', onPointerDown, true);
    listEl.addEventListener('dragend', resetHandle, true);
}

function resetHandle(e) {
    const item = e.target instanceof Element ? e.target.closest(`[${ITEM}]`) : null;
    if (item) item.setAttribute('draggable', 'false');
}

// The item of `listEl` (a direct child) that contains `node`, ignoring items of
// lists nested inside it.
function ownItem(listEl, node) {
    let el = node instanceof Element ? node : node?.parentElement;
    while (el && el !== listEl) {
        if (el.parentElement === listEl) return el.hasAttribute(ITEM) && !el.hasAttribute(PLACEHOLDER) ? el : null;
        el = el.parentElement;
    }
    return null;
}

// --- Drop position -------------------------------------------------------

// Given a pointer position, return the index (0..count, counting the list's
// items but not its placeholder) at which the dragged item should be inserted,
// or -1 when the current position should be kept.
//
// This is a port of SortableJS's swap logic (_getSwapDirection):
//   - swapThreshold (0..1): the centred fraction of an item that is an active
//     swap zone. Entering it moves the dragged item past that item.
//   - invertSwap + invertedSwapThreshold: the active zones sit at the item's
//     edges instead, and the pointer's side of the item decides before/after
//     ("sort between items"). This mode is always used when the item enters a
//     list it is not in yet.
//   - after a swap, the area the dragged item just vacated (its "shadow") swaps
//     it back, which keeps items of different sizes from oscillating.
//
// The target item is found by 2D hit testing, so grids work in every
// direction. When the pointer is not over any item (gaps / trailing padding),
// it clamps to the nearest slot so the end of the list stays reachable.
export function dropIndex(listEl, x, y, swapThreshold, invertSwap, invertedSwapThreshold, direction) {
    if (!listEl) return -1;

    swapThreshold = swapThreshold > 0 && swapThreshold <= 1 ? swapThreshold : 1;
    invertedSwapThreshold = invertedSwapThreshold > 0 && invertedSwapThreshold <= 1 ? invertedSwapThreshold : swapThreshold;

    // Visible items in DOM order, with their index among all of the list's items
    // (hidden ones included) so the result maps straight onto the bound list.
    const entries = [];
    let count = 0;
    let slot = null; // where the dragged item currently sits in this list, if it does
    for (const child of listEl.children) {
        if (!child.hasAttribute(ITEM)) continue;
        if (child.hasAttribute(PLACEHOLDER)) {
            slot = { el: child, gap: count, rect: child.getBoundingClientRect() };
            continue;
        }
        const index = count++;
        const rect = child.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) continue; // hidden (e.g. the original of a cross-list preview)
        const entry = { el: child, index, rect };
        if (child === draggedItemEl) slot = entry;
        entries.push(entry);
    }

    if (entries.length === 0) return count;

    const vertical = isVertical(direction, entries);
    const over = (r) => x >= r.left && x <= r.right && y >= r.top && y <= r.bottom;

    // Over the dragged item (or its placeholder) itself: nothing to do.
    if (slot && over(slot.rect)) {
        if (!animations.has(slot.el)) lastTarget = null;
        return -1;
    }

    const target = entries.find(e => e !== slot && over(e.rect));
    if (!target) {
        lastTarget = null;
        return nearestSlot(entries, slot, x, y, vertical);
    }

    const anim = animations.get(target.el);
    if (anim && ((anim.dx !== 0 && anim.dy !== 0) || target.el === ignoreWhileAnimating)) return -1;

    const differentLevel = !slot;
    const differentRowCol = !!slot && !inRowColumn(layoutBox(slot.el), layoutBox(target.el), vertical);

    if (lastTarget !== target.el) {
        targetBeforeFirstSwap = layoutStart(target.el, vertical);
        pastFirstInvertThresh = false;
        isCircumstantialInvert = (!differentRowCol && invertSwap) || differentLevel;
    }

    const isLastTarget = lastTarget === target.el;
    const targetMoveDistance = isLastTarget ? Math.abs(targetBeforeFirstSwap - layoutStart(target.el, vertical)) : 0;
    const insertDirection = () => {
        const before = slot.gap !== undefined ? slot.gap <= target.index : slot.index < target.index;
        return before ? 1 : -1;
    };

    const dir = swapDirection(vertical ? y : x, target.rect, vertical,
        differentRowCol ? 1 : swapThreshold, invertedSwapThreshold,
        isCircumstantialInvert, isLastTarget, targetMoveDistance, insertDirection);
    if (dir === 0) return -1;

    // Already beside the target on that side: nothing to do.
    if (slot) {
        if (slot.gap !== undefined) {
            if (slot.gap === (dir === 1 ? target.index + 1 : target.index)) return -1;
        } else {
            const at = entries.indexOf(slot);
            if (entries[at - dir] === target) return -1;
        }
    }

    lastTarget = target.el;
    lastDirection = dir;
    ignoreWhileAnimating = target.el;
    return dir === 1 ? target.index + 1 : target.index;
}

// SortableJS's _getSwapDirection: 1 = after the target, -1 = before it, 0 = no move.
function swapDirection(mouse, rect, vertical, swapThreshold, invertedSwapThreshold, invertSwap,
    isLastTarget, targetMoveDistance, insertDirection) {
    const length = vertical ? rect.height : rect.width;
    const s1 = vertical ? rect.top : rect.left;
    const s2 = vertical ? rect.bottom : rect.right;
    let invert = false;

    if (!invertSwap) {
        if (isLastTarget && targetMoveDistance < length * swapThreshold) {
            // Past the first inverted threshold on the side opposite the last move?
            if (!pastFirstInvertThresh &&
                (lastDirection === 1
                    ? mouse > s1 + length * invertedSwapThreshold / 2
                    : mouse < s2 - length * invertedSwapThreshold / 2)) {
                pastFirstInvertThresh = true;
            }

            if (!pastFirstInvertThresh) {
                // Over the dragged item's shadow (where it was before the last move): move back.
                if (lastDirection === 1 ? mouse < s1 + targetMoveDistance : mouse > s2 - targetMoveDistance) {
                    return -lastDirection;
                }
            } else {
                invert = true;
            }
        } else if (mouse > s1 + length * (1 - swapThreshold) / 2 &&
                   mouse < s2 - length * (1 - swapThreshold) / 2) {
            return insertDirection();
        }
    }

    if (invert || invertSwap) {
        if (mouse < s1 + length * invertedSwapThreshold / 2 ||
            mouse > s2 - length * invertedSwapThreshold / 2) {
            return mouse > s1 + length / 2 ? 1 : -1;
        }
    }

    return 0;
}

// Not over any item: insert next to the nearest one, on the pointer's side.
function nearestSlot(entries, slot, x, y, vertical) {
    let best = Infinity, nearest = null;
    for (const e of entries) {
        const r = e.rect;
        const dx = x - Math.max(r.left, Math.min(x, r.right));
        const dy = y - Math.max(r.top, Math.min(y, r.bottom));
        const d = dx * dx + dy * dy;
        if (d < best) { best = d; nearest = e; }
    }
    if (slot && slot.gap !== undefined) {
        const r = slot.rect;
        const dx = x - Math.max(r.left, Math.min(x, r.right));
        const dy = y - Math.max(r.top, Math.min(y, r.bottom));
        if (dx * dx + dy * dy < best) return -1; // nearest to the placeholder: keep it
    }
    if (nearest === slot) return -1;
    const r = nearest.rect;
    const p = vertical ? y : x;
    const center = vertical ? r.top + r.height / 2 : r.left + r.width / 2;
    return p < center ? nearest.index : nearest.index + 1;
}

function isVertical(direction, entries) {
    if (direction === 'horizontal') return false;
    if (direction === 'vertical') return true;
    if (entries.length < 2) return true;
    const a = entries[0].rect, b = entries[1].rect;
    return !(Math.abs(b.left - a.left) > Math.abs(b.top - a.top));
}

// Layout position/size (ignoring the transforms used by animations).
function layoutBox(el) {
    return { left: el.offsetLeft, top: el.offsetTop, width: el.offsetWidth, height: el.offsetHeight };
}

function layoutStart(el, vertical) {
    return vertical ? el.offsetTop : el.offsetLeft;
}

// SortableJS's _dragElInRowColumn: do the two boxes share a row (horizontal) or
// a column (vertical)?
function inRowColumn(a, b, vertical) {
    const near = (p, q) => Math.abs(p - q) < 1;
    const a1 = vertical ? a.left : a.top, aLen = vertical ? a.width : a.height;
    const b1 = vertical ? b.left : b.top, bLen = vertical ? b.width : b.height;
    return near(a1, b1) || near(a1 + aLen, b1 + bLen) || near(a1 + aLen / 2, b1 + bLen / 2);
}

// Index (counting all of the list's items, hidden ones included) of the visible
// item whose box is directly under the pointer, or -1 when the pointer is over
// no item. Used by swap mode to pick the item the dragged one will trade places with.
export function itemIndexAt(listEl, x, y) {
    if (!listEl) return -1;
    let i = 0;
    for (const child of listEl.children) {
        if (!child.hasAttribute(ITEM) || child.hasAttribute(PLACEHOLDER)) continue;
        const index = i++;
        const r = child.getBoundingClientRect();
        if (r.width === 0 && r.height === 0) continue;
        if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom) return index;
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
        if (!child.hasAttribute(ITEM)) continue;
        map.set(child, child.getBoundingClientRect());
    }
    flipStore.set(listEl, map);
}

// After Blazor has reordered the DOM, move each item back to where it was
// (Invert) then transition the offset away to zero (Play).
export function play(listEl, duration) {
    if (!listEl) return;
    const prev = flipStore.get(listEl);
    flipStore.delete(listEl);
    if (!prev || !(duration > 0)) return;

    const items = [];
    for (const child of listEl.children) {
        if (child.hasAttribute(ITEM) && prev.has(child)) items.push(child); // new items are not animated
    }

    // Stop any animation still running so the measurement below sees pure layout.
    // Its starting point was captured mid-flight, so the new animation picks up
    // exactly where the old one was.
    const interrupted = [];
    for (const child of items) {
        if (!animations.has(child)) continue;
        animations.delete(child);
        child.style.transition = 'none';
        child.style.transform = '';
        interrupted.push(child);
    }

    const moves = [];
    for (const child of items) {
        const first = prev.get(child);
        const last = child.getBoundingClientRect();
        // Skip items hidden before or after the change (e.g. the original item while
        // it is previewed in another list): animating those makes them fly in from a corner.
        if ((first.width === 0 && first.height === 0) || (last.width === 0 && last.height === 0)) continue;
        const dx = first.left - last.left;
        const dy = first.top - last.top;
        if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) continue;
        moves.push({ child, dx, dy });
    }

    for (const child of interrupted) {
        if (!moves.some(m => m.child === child)) child.style.transition = '';
    }
    if (moves.length === 0) return;

    for (const { child, dx, dy } of moves) {
        child.style.transition = 'none';
        child.style.transform = `translate(${dx}px, ${dy}px)`;
    }

    // Force a reflow so the inverted transforms are applied before we animate.
    void listEl.offsetWidth;

    for (const { child, dx, dy } of moves) {
        const token = { dx, dy };
        animations.set(child, token);
        child.style.transition = `transform ${duration}ms ${EASING}`;
        child.style.transform = '';

        let timer = 0;
        const done = (e) => {
            // transitionend bubbles: ignore the ones from nested items.
            if (e && (e.target !== child || e.propertyName !== 'transform')) return;
            child.removeEventListener('transitionend', done);
            child.removeEventListener('transitioncancel', done);
            clearTimeout(timer);
            if (animations.get(child) !== token) return; // superseded by a newer animation
            animations.delete(child);
            child.style.transition = '';
        };
        child.addEventListener('transitionend', done);
        child.addEventListener('transitioncancel', done);
        // Fallback for transitions that never report back (e.g. element removed).
        timer = setTimeout(done, duration + 100);
    }
}

// Forget a snapshot that will not be played (called when a drag ends).
export function clear(listEl) {
    if (listEl) flipStore.delete(listEl);
}
