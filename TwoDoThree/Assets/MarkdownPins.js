// Only this browser's main document can supply a pin selection. Excerpts are DOM
// range clones of trusted rendered Markdown, never persisted HTML or separate browsers.
const mainDocument = document.getElementById('markdown-main');
const pinSidebar = document.getElementById('pin-sidebar');
const pinList = document.getElementById('pin-list');
const pinToggle = document.getElementById('pin-toggle');
const pinSelectionButton = document.getElementById('pin-selection');
const pinStatus = document.getElementById('pin-status');
let pins = pinConfig.pins || [], sidebarOpen = !!pinConfig.open;
let activePinDrag = null, edgeTimer = null, temporaryReveal = false, dropPending = false;
let lastPinSelection = null;

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
    pinList.querySelectorAll('.copy-code').forEach(button => buttons.delete(Number(button.dataset.copyId)));
    pinList.replaceChildren();
    for (const pin of [...pins].sort((a,b) => a.order-b.order)) {
        const card = document.createElement('section'); card.className = 'pin-card'; card.dataset.pinId = pin.id;
        const header = document.createElement('header'); header.className = 'pin-header'; header.tabIndex = 0;
        header.title = 'Double-click header to go to passage';
        const label = document.createElement('span'); label.className = 'pin-title'; label.textContent = `Pin ${pin.order+1}`;
        const unpin = document.createElement('button'); unpin.textContent = 'Unpin'; unpin.type = 'button';
        unpin.addEventListener('click', e => { e.stopPropagation(); window.chrome.webview.postMessage({action:'unpin', documentId, pinId:pin.id}); });
        unpin.addEventListener('dblclick', e => e.stopPropagation());
        const range = pinRange(pin);
        function jump() { if (range) scrollTo(0, scrollY + range.getBoundingClientRect().top - innerHeight*.2); }
        header.addEventListener('dblclick', jump);
        header.addEventListener('keydown', e => { if (e.key === 'Enter' && e.target === header) { e.preventDefault(); jump(); } });
        header.append(label, unpin); card.append(header);
        if (range) {
            const content = excerptFragment(range); content.className = 'pin-content';
            content.addEventListener('click', e => { if (e.target.closest('a[href^="#"]')) e.preventDefault(); });
            card.append(content); initializeCodeCopies(content);
        } else {
            const unavailable = document.createElement('div'); unavailable.className = 'pin-unavailable';
            unavailable.textContent = (pin.unavailable || 'Passage is unavailable in this version.') + '\n\n' + pin.selectedText;
            card.append(unavailable); header.removeAttribute('tabindex'); header.setAttribute('aria-disabled','true'); header.title = 'Passage unavailable';
        }
        pinList.append(card);
    }
    pinStatus.textContent = pinConfig.error || (pins.length ? 'Double-click a card header to go to its passage.' : 'Select text and drag it here, or use Pin selection.');
    pinSidebar.scrollTop = scroll;
    highlightPins();
}
function updateMarkdownPins(updated, error = null, created = false) {
    if (updated) pins = updated;
    if (created) { temporaryReveal = false; getSelection().removeAllRanges(); setPinSidebar(true); }
    else if (error && temporaryReveal) { temporaryReveal = false; setPinSidebar(false); }
    dropPending = false;
    renderPins();
    if (error) { pinStatus.textContent = error; taskStatus.textContent = error; }
}
function submitPin(payload) {
    if (!payload) { taskStatus.textContent = 'Select text in the main document first.'; return; }
    if (!pinConfig.canPin) { taskStatus.textContent = 'Save the document before pinning this selection.'; return; }
    dropPending = true;
    window.chrome.webview.postMessage({ action:'pinSelection', documentId, ...payload });
}
pinToggle.addEventListener('click', () => { temporaryReveal = false; setPinSidebar(!sidebarOpen); });
pinSelectionButton.disabled = true;
pinSelectionButton.title = pinConfig.canPin ? 'Pin the selected passage' : 'Save changes before creating pins';
pinSelectionButton.addEventListener('mousedown', e => e.preventDefault());
pinSelectionButton.addEventListener('click', () => submitPin(lastPinSelection));
document.addEventListener('selectionchange', () => {
    const selection = getSelection(); lastPinSelection = selection.rangeCount ? selectionPayload(selection.getRangeAt(0)) : null;
    pinSelectionButton.disabled = !pinConfig.canPin || !lastPinSelection;
});
function clearEdgeTimer() { clearTimeout(edgeTimer); edgeTimer = null; }
document.addEventListener('dragstart', e => {
    if (!pinConfig.canPin || !mainDocument.contains(e.target)) return;
    const selection = getSelection();
    activePinDrag = selection.rangeCount ? selectionPayload(selection.getRangeAt(0)) : null;
    if (!activePinDrag) return;
    e.dataTransfer.setData('application/x-2do3-markdown-pin', documentId); e.dataTransfer.effectAllowed = 'copy';
});
function handlePinDragOver(e) {
    if (!activePinDrag) return; // External file/text drops never create pins.
    if (!sidebarOpen && e.clientX <= 32) {
        if (!edgeTimer) edgeTimer = setTimeout(() => { temporaryReveal = true; setPinSidebar(true, true, false); edgeTimer = null; }, 950);
    } else clearEdgeTimer();
    if (pinSidebar.contains(e.target) || e.clientX <= 32) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; }
}
document.addEventListener('dragover', handlePinDragOver);
document.addEventListener('dragenter', handlePinDragOver);
document.addEventListener('dragleave', e => { if (!e.relatedTarget) clearEdgeTimer(); });
pinSidebar.addEventListener('drop', e => {
    if (!activePinDrag || e.dataTransfer.getData('application/x-2do3-markdown-pin') !== documentId) return;
    e.preventDefault(); clearEdgeTimer(); submitPin(activePinDrag);
});
document.addEventListener('drop', e => { if (activePinDrag) e.preventDefault(); });
document.addEventListener('dragend', () => {
    clearEdgeTimer(); activePinDrag = null;
    if (temporaryReveal && !dropPending) { temporaryReveal = false; setPinSidebar(false, false, false); }
});
renderPins(); setPinSidebar(sidebarOpen, false, false); pinSidebar.scrollTop = pinConfig.scroll || 0;
