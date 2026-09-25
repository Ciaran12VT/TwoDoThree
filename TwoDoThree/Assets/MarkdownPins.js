// Only this browser's main document can supply a pin selection. Excerpts are DOM
// range clones of trusted rendered Markdown, never persisted HTML or separate browsers.
const mainDocument = document.getElementById('markdown-main');
const pinSidebar = document.getElementById('pin-sidebar');
const pinList = document.getElementById('pin-list');
const pinToggle = document.getElementById('pin-toggle');
const pinSelectionButton = document.getElementById('pin-selection');
const pinStatus = document.getElementById('pin-status');
const pinDivider = document.getElementById('pin-divider');
const pinWrap = document.getElementById('pin-wrap');
const pinMenu = document.getElementById('pin-menu');
const dragHint = document.getElementById('pin-drag-hint');
const clearPinsButton = document.getElementById('pin-clear');
const cardMenu = document.getElementById('pin-card-menu');
const renameButton = document.getElementById('pin-rename');
const renameDialog = document.getElementById('pin-rename-dialog');
const titleInput = document.getElementById('pin-title-input');
let contextPinId = null, renamingPinId = null;
let pins = pinConfig.pins || [], sidebarOpen = !!pinConfig.open;
let activePinDrag = null, edgeTimer = null, temporaryReveal = false;
let lastPinSelection = null;
let preferredWidth = pinConfig.width || 330, resizeGesture = null, pinGesture = null, suppressPinClick = false;

function widthBounds() { const min = Math.min(200, innerWidth*.45); return { min, max: Math.min(1600, Math.max(min, innerWidth-280)) }; }
function applyPinPreferences(width = preferredWidth, wrap = pinWrap.checked, preserve = true) {
    const anchor = preserve && sidebarOpen && !temporaryReveal && width !== preferredWidth ? captureMarkdownAnchor() : null;
    preferredWidth = width; pinWrap.checked = wrap;
    const bounds = widthBounds(), actual = Math.max(bounds.min, Math.min(bounds.max, preferredWidth));
    document.body.style.setProperty('--pin-width', actual + 'px');
    document.body.classList.toggle('pins-nowrap', !wrap);
    pinDivider.setAttribute('aria-valuemin', Math.round(bounds.min));
    pinDivider.setAttribute('aria-valuemax', Math.round(bounds.max));
    pinDivider.setAttribute('aria-valuenow', Math.round(actual));
    if (anchor) restoreMarkdownAnchor(anchor);
}
function savePinPreferences() {
    window.chrome.webview.postMessage({action:'pinPreferences', documentId, width:Math.max(180,preferredWidth), wrap:pinWrap.checked});
}

function mappedNodes(element) {
    // Block renderers add layout whitespace outside <pre>; only its code text is
    // represented by the source run, not those HTML separators or copy buttons.
    const root = element.querySelector('pre > code') || element;
    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
        acceptNode: n => n.parentElement.closest('button') ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
    });
    const nodes = []; while (walker.nextNode()) nodes.push(walker.currentNode); return nodes;
}
function selectionPayload(range) {
    if (!range || range.collapsed || !mainDocument.contains(range.startContainer) || !mainDocument.contains(range.endContainer)) return null;
    let start = null, end = null, text = '';
    for (const element of mainDocument.querySelectorAll('[data-run]')) {
        const run = sourceRuns[Number(element.dataset.run)]; let offset = 0;
        if (!run) continue;
        for (const node of mappedNodes(element)) {
            const length = node.data.length;
            if (range.intersectsNode(node)) {
                const from = range.startContainer === node ? range.startOffset : range.comparePoint(node, 0) < 0 ? length : 0;
                const to = range.endContainer === node ? range.endOffset : range.comparePoint(node, length) > 0 ? 0 : length;
                if (to > from && offset + to < run.boundaries.length) {
                    start ??= run.boundaries[offset + from];
                    end = run.boundaries[offset + to];
                    text += node.data.slice(from, to);
                }
            }
            offset += length;
        }
    }
    return start !== null && end > start && text.trim() ? { start, end, text } : null;
}
function pointAtSource(source, end) {
    for (const element of mainDocument.querySelectorAll('[data-run]')) {
        const run = sourceRuns[Number(element.dataset.run)];
        if (!run) continue;
        const offsets = run.boundaries.map((v, i) => Math.abs(v-source) < .00001 ? i : -1).filter(i => i >= 0);
        if (!offsets.length) continue;
        let offset = end ? offsets[offsets.length-1] : offsets[0];
        const nodes = mappedNodes(element);
        for (const node of nodes) {
            if (offset <= node.data.length) return { node, offset };
            offset -= node.data.length;
        }
    }
    return null;
}
function pinRange(pin) {
    if (pin.unavailable) return null;
    const start = pointAtSource(pin.start, false), end = pointAtSource(pin.end, true);
    if (!start || !end) return null;
    const range = document.createRange(); range.setStart(start.node, start.offset); range.setEnd(end.node, end.offset);
    return range.collapsed ? null : range;
}
function highlightPins() {
    if (!CSS.highlights) return;
    CSS.highlights.delete('markdown-pins');
    if (sidebarOpen) CSS.highlights.set('markdown-pins', new Highlight(...pins.map(pinRange).filter(Boolean)));
}
function setPinSidebar(open, overlay = false, preserve = true) {
    const anchor = preserve ? captureMarkdownAnchor() : null;
    sidebarOpen = open;
    document.body.classList.toggle('pins-open', open);
    document.body.classList.toggle('pins-overlay', open && overlay);
    pinToggle.setAttribute('aria-expanded', String(open));
    highlightPins();
    if (anchor && !overlay) requestAnimationFrame(() => restoreMarkdownAnchor(anchor));
}
function markdownPinViewState() { return { open: sidebarOpen && !temporaryReveal, scroll: pinSidebar.scrollTop }; }
function excerptFragment(range) {
    let fragment = range.cloneContents();
    let ancestor = range.commonAncestorContainer;
    if (ancestor.nodeType === Node.TEXT_NODE) ancestor = ancestor.parentElement;
    while (ancestor && ancestor !== mainDocument) {
        const shell = ancestor.cloneNode(false); shell.append(fragment); fragment = shell; ancestor = ancestor.parentElement;
    }
    const holder = document.createElement('div'); holder.append(fragment);
    holder.querySelectorAll('button,script,style').forEach(e => e.remove());
    holder.querySelectorAll('[id]').forEach(e => e.removeAttribute('id'));
    // A partial checklist excerpt retains the item's checkbox but never adds unselected text.
    for (const item of holder.querySelectorAll('li[data-source-start]')) {
        if (item.querySelector('.markdown-task')) continue;
        if (!Array.from(item.querySelectorAll('[data-run]')).some(e => e.closest('li') === item)) continue;
        const original = mainDocument.querySelector(`li[data-source-start="${item.dataset.sourceStart}"]`);
        const task = original?.querySelector('.markdown-task');
        if (task && task.closest('li') === original) item.prepend(task.cloneNode(true));
    }
    return holder;
}
function renderPins() {
    const scroll = pinSidebar.scrollTop;
    const horizontal = new Map(Array.from(pinList.children, card => [card.dataset.pinId, card.querySelector('.pin-content')?.scrollLeft || 0]));
    const focusedCard = document.activeElement?.closest('.pin-card');
    const focusedId = focusedCard?.dataset.pinId;
    const focusedControl = document.activeElement?.dataset.pinControl;
    pinList.querySelectorAll('.copy-code').forEach(button => buttons.delete(Number(button.dataset.copyId)));
    pinList.replaceChildren();
    const ranges = new Map(pins.map(pin => [pin.id,pinRange(pin)]));
    // Display order follows live source positions. Creation order only breaks ties
    // (and keeps default names stable); unresolved pins never get invented matches.
    const ordered = [...pins].sort((a,b) => Number(!ranges.get(a.id))-Number(!ranges.get(b.id))
        || a.start-b.start || a.end-b.end || a.order-b.order || a.id.localeCompare(b.id));
    for (const pin of ordered) {
        const card = document.createElement('section'); card.className = 'pin-card'; card.dataset.pinId = pin.id;
        const header = document.createElement('header'); header.className = 'pin-header'; header.tabIndex = 0;
        header.dataset.pinControl = 'header';
        header.title = 'Double-click header to go to passage';
        const title = pin.title || `Pin ${pin.order+1}`;
        const label = document.createElement('span'); label.className = 'pin-title'; label.textContent = title;
        const collapse = document.createElement('button'); collapse.type = 'button'; collapse.className = 'pin-collapse'; collapse.dataset.pinControl = 'collapse';
        collapse.title = pin.collapsed ? 'Expand excerpt' : 'Collapse excerpt'; collapse.setAttribute('aria-label', collapse.title);
        collapse.setAttribute('aria-expanded',String(!pin.collapsed)); collapse.setAttribute('aria-controls','pin-body-'+pin.id);
        collapse.innerHTML = '<svg aria-hidden="true" viewBox="0 0 24 24"><path d="'+(pin.collapsed?'m9 5 7 7-7 7':'m5 9 7 7 7-7')+'"/></svg>';
        collapse.addEventListener('click',e=>{e.stopPropagation();window.chrome.webview.postMessage({action:'collapsePin',documentId,pinId:pin.id,collapsed:!pin.collapsed});});
        collapse.addEventListener('dblclick',e=>e.stopPropagation());
        const unpin = document.createElement('button'); unpin.type = 'button'; unpin.className = 'pin-unpin';
        unpin.dataset.pinControl = 'unpin';
        unpin.title = 'Unpin excerpt'; unpin.setAttribute('aria-label', 'Unpin excerpt');
        unpin.innerHTML = '<svg aria-hidden="true" viewBox="0 0 24 24"><path d="m16 3 5 5-4 2-3 5-2 1-4-4 1-2 5-3zM8 16l-5 5M3 3l18 18"/></svg>';
        unpin.addEventListener('click', e => { e.stopPropagation(); window.chrome.webview.postMessage({action:'unpin', documentId, pinId:pin.id}); });
        unpin.addEventListener('dblclick', e => e.stopPropagation());
        const range = ranges.get(pin.id);
        function jump() { if (range) scrollTo(0, scrollY + range.getBoundingClientRect().top - innerHeight*.2); }
        header.addEventListener('dblclick', jump);
        header.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target === header) { e.preventDefault(); jump(); } });
        header.append(label, collapse, unpin); card.append(header);
        card.addEventListener('contextmenu',e=>{
            e.preventDefault(); e.stopPropagation(); contextPinId=pin.id;
            pinMenu.hidden=true; showPinMenu(cardMenu,renameButton,e.clientX,e.clientY);
        });
        if (range) {
            const content = excerptFragment(range); content.className = 'pin-content';
            content.addEventListener('click', e => { if (e.target.closest('a[href^="#"]')) e.preventDefault(); });
            card.append(content); initializeCodeCopies(content);
        } else {
            const unavailable = document.createElement('div'); unavailable.className = 'pin-unavailable';
            unavailable.textContent = (pin.unavailable || 'Passage is unavailable in this version.') + '\n\n' + pin.selectedText;
            card.append(unavailable); header.title = 'Passage unavailable'; header.setAttribute('aria-label',title+': passage unavailable');
        }
        const body = card.lastElementChild; body.id = 'pin-body-'+pin.id; body.hidden = !!pin.collapsed;
        pinList.append(card);
        const content = card.querySelector('.pin-content');
        if (content) content.scrollLeft = horizontal.get(pin.id) || 0;
    }
    if (focusedId && focusedControl) pinList.querySelector(`[data-pin-id="${focusedId}"] [data-pin-control="${focusedControl}"]`)?.focus({preventScroll:true});
    clearPinsButton.disabled = pins.length === 0;
    pinStatus.textContent = pinConfig.error || (!pinConfig.canPin ? 'Save changes before creating pins.' : pins.length ? 'Double-click a card header to go to its passage.' : 'Select text, then drag it here. Or right-click the selection to pin it (Alt+Shift+P).');
    pinSidebar.scrollTop = scroll;
    highlightPins();
}
function updateMarkdownPins(updated, error = null, created = false, width, wrap) {
    if (updated) pins = updated;
    if (!resizeGesture && !pinGesture && width !== undefined) applyPinPreferences(width, wrap);
    if (created) { temporaryReveal = false; getSelection().removeAllRanges(); setPinSidebar(true); }
    else if (error && temporaryReveal) { temporaryReveal = false; setPinSidebar(false); }
    renderPins();
    if (error) { pinStatus.textContent = error; taskStatus.textContent = error; }
}
function submitPin(payload) {
    if (!payload) { taskStatus.textContent = 'Select text in the main document first.'; return; }
    if (!pinConfig.canPin) { taskStatus.textContent = 'Save the document before pinning this selection.'; return; }
    window.chrome.webview.postMessage({ action:'pinSelection', documentId, ...payload });
}
pinToggle.addEventListener('click', () => { temporaryReveal = false; setPinSidebar(!sidebarOpen); });
pinSelectionButton.disabled = true;
pinSelectionButton.title = pinConfig.canPin ? 'Pin the selected passage' : 'Save changes before creating pins';
pinSelectionButton.addEventListener('mousedown', e => e.preventDefault());
pinSelectionButton.addEventListener('click', () => { pinMenu.hidden = true; submitPin(lastPinSelection); });
function showPinMenu(menu,button,x,y) {
    menu.hidden=false;
    menu.style.left=Math.max(0,Math.min(x,innerWidth-menu.offsetWidth))+'px';
    menu.style.top=Math.max(0,Math.min(y,innerHeight-menu.offsetHeight))+'px';
    button.focus();
}
document.addEventListener('contextmenu', e => {
    if (!mainDocument.contains(e.target) || !lastPinSelection) return;
    e.preventDefault(); cardMenu.hidden=true; showPinMenu(pinMenu,pinSelectionButton,e.clientX,e.clientY);
});
document.addEventListener('pointerdown', e => {
    if (!pinMenu.contains(e.target)) pinMenu.hidden = true;
    if (!cardMenu.contains(e.target)) cardMenu.hidden = true;
});
clearPinsButton.addEventListener('click',()=>window.chrome.webview.postMessage({action:'clearPins',documentId}));
renameButton.addEventListener('click',()=>{
    cardMenu.hidden=true; const pin=pins.find(p=>p.id===contextPinId); if(!pin)return;
    renamingPinId=pin.id; titleInput.value=pin.title || `Pin ${pin.order+1}`;
    document.getElementById('pin-title-error').textContent=''; titleInput.setCustomValidity('');
    renameDialog.showModal(); titleInput.focus(); titleInput.select();
});
document.getElementById('pin-rename-cancel').addEventListener('click',()=>renameDialog.close());
renameDialog.addEventListener('close',()=>{
    pinList.querySelector(`[data-pin-id="${renamingPinId}"] .pin-header`)?.focus({preventScroll:true}); renamingPinId=null;
});
document.getElementById('pin-rename-form').addEventListener('submit',e=>{
    e.preventDefault(); const title=titleInput.value.trim();
    if(!title || /[\u0000-\u001f\u007f-\u009f]/.test(title)) {
        document.getElementById('pin-title-error').textContent='Enter a nonempty title on one line.'; titleInput.focus(); return;
    }
    window.chrome.webview.postMessage({action:'renamePin',documentId,pinId:renamingPinId,title}); renameDialog.close();
});
document.addEventListener('keydown', e => {
    if (renameDialog.open) return;
    if (e.altKey && e.shiftKey && e.code === 'KeyP') { e.preventDefault(); submitPin(lastPinSelection); }
    if (e.key === 'Escape') { pinMenu.hidden = cardMenu.hidden = true; finishPinDrag(false); }
});
document.addEventListener('selectionchange', () => {
    const selection = getSelection(); lastPinSelection = selection.rangeCount ? selectionPayload(selection.getRangeAt(0)) : null;
    pinSelectionButton.disabled = !pinConfig.canPin || !lastPinSelection;
});
function clearEdgeTimer() { clearTimeout(edgeTimer); edgeTimer = null; }
function overSidebar(x,y) {
    const r = pinSidebar.getBoundingClientRect();
    return sidebarOpen && x >= r.left && x < r.right && y >= r.top && y < r.bottom;
}
function trackPinDrag(x,y) {
    if (!activePinDrag) return;
    const atEdge = x >= 0 && x <= 32 && y >= 0 && y < innerHeight;
    if (!sidebarOpen && atEdge) {
        if (!edgeTimer) edgeTimer = setTimeout(() => {
            edgeTimer = null;
            if (!activePinDrag) return;
            temporaryReveal = true; setPinSidebar(true, true, false);
            pinSidebar.classList.add('pin-drop-target');
        }, 950);
    } else clearEdgeTimer();
    pinSidebar.classList.toggle('pin-drop-target',overSidebar(x,y));
    dragHint.hidden = false;
    dragHint.style.left = Math.max(0,Math.min(x+14,innerWidth-190))+'px';
    dragHint.style.top = Math.max(0,Math.min(y+16,innerHeight-36))+'px';
    dragHint.textContent = overSidebar(x,y) ? 'Release to pin excerpt' : 'Drag to the left to pin';
}
function finishPinDrag(commit, x = -1, y = -1) {
    if (!pinGesture) return;
    const gesture = pinGesture, payload = activePinDrag;
    pinGesture = null; activePinDrag = null; clearEdgeTimer();
    dragHint.hidden = true; pinSidebar.classList.remove('pin-drop-target');
    document.body.classList.remove('pin-dragging');
    if (mainDocument.hasPointerCapture(gesture.id)) mainDocument.releasePointerCapture(gesture.id);
    if (commit && payload && overSidebar(x,y)) submitPin(payload);
    else if (temporaryReveal) { temporaryReveal = false; setPinSidebar(false,false,false); }
    // A click without movement should retain normal browser caret placement.
    if (commit && !payload) {
        const caret = document.caretRangeFromPoint(x,y);
        if (caret) { getSelection().removeAllRanges(); getSelection().addRange(caret); }
    }
}
// Keep this gesture inside the browser. HTML selection dragging hands control to
// Windows OLE, which does not deliver target dragover/drop in our no-drop native
// host. Capturing before that handoff also preserves the selection on second down.
mainDocument.addEventListener('pointerdown', e => {
    suppressPinClick = false;
    if (e.button !== 0 || e.shiftKey || e.ctrlKey || e.altKey || e.target.closest('button,input')) return;
    const selection = getSelection(), range = selection.rangeCount ? selection.getRangeAt(0) : null;
    const payload = selectionPayload(range);
    if (!payload || !Array.from(range.getClientRects()).some(r => e.clientX>=r.left && e.clientX<=r.right && e.clientY>=r.top && e.clientY<=r.bottom)) return;
    if (!pinConfig.canPin) { taskStatus.textContent = 'Save the document before pinning this selection.'; return; }
    e.preventDefault();
    suppressPinClick = true;
    pinGesture = { id:e.pointerId, x:e.clientX, y:e.clientY, payload };
    mainDocument.setPointerCapture(e.pointerId);
});
mainDocument.addEventListener('pointermove', e => {
    if (!pinGesture || e.pointerId !== pinGesture.id) return;
    if (!(e.buttons & 1)) { finishPinDrag(false); return; }
    if (!activePinDrag && Math.hypot(e.clientX-pinGesture.x,e.clientY-pinGesture.y) < 5) return;
    activePinDrag = pinGesture.payload; document.body.classList.add('pin-dragging');
    trackPinDrag(e.clientX,e.clientY);
});
mainDocument.addEventListener('pointerup', e => { if (pinGesture?.id===e.pointerId) finishPinDrag(true,e.clientX,e.clientY); });
mainDocument.addEventListener('click', e => { if (suppressPinClick) { suppressPinClick=false; e.preventDefault(); e.stopPropagation(); } }, true);
mainDocument.addEventListener('pointercancel', () => finishPinDrag(false));
mainDocument.addEventListener('lostpointercapture', () => finishPinDrag(false));
window.addEventListener('blur', () => { pinMenu.hidden = cardMenu.hidden = true; finishPinDrag(false); });
mainDocument.addEventListener('dragstart', e => e.preventDefault());
document.addEventListener('drop', e => e.preventDefault()); // Never accept external data.

pinWrap.addEventListener('change', () => { applyPinPreferences(); savePinPreferences(); });
pinDivider.addEventListener('pointerdown', e => {
    if (e.button!==0 || !sidebarOpen || temporaryReveal) return;
    e.preventDefault(); resizeGesture = { id:e.pointerId, anchor:captureMarkdownAnchor() };
    pinDivider.setPointerCapture(e.pointerId); document.body.classList.add('pin-resizing');
});
pinDivider.addEventListener('pointermove', e => {
    if (resizeGesture?.id!==e.pointerId) return;
    const bounds = widthBounds(); applyPinPreferences(Math.max(bounds.min,Math.min(bounds.max,e.clientX)),pinWrap.checked,false);
    restoreMarkdownAnchor(resizeGesture.anchor);
});
function finishResize() {
    if (!resizeGesture) return;
    resizeGesture = null; document.body.classList.remove('pin-resizing'); savePinPreferences();
}
pinDivider.addEventListener('pointerup', finishResize);
pinDivider.addEventListener('lostpointercapture', finishResize);
pinDivider.addEventListener('pointercancel', finishResize);
pinDivider.addEventListener('keydown', e => {
    const bounds = widthBounds(); let width = Number(pinDivider.getAttribute('aria-valuenow'));
    if(e.key==='ArrowLeft') width-=20; else if(e.key==='ArrowRight') width+=20;
    else if(e.key==='Home') width=bounds.min; else if(e.key==='End') width=bounds.max; else return;
    e.preventDefault(); applyPinPreferences(Math.max(bounds.min,Math.min(bounds.max,width))); savePinPreferences();
});
window.addEventListener('resize', () => { finishPinDrag(false); applyPinPreferences(); });
applyPinPreferences(preferredWidth,pinConfig.wrap!==false,false);
renderPins(); setPinSidebar(sidebarOpen, false, false); pinSidebar.scrollTop = pinConfig.scroll || 0;
