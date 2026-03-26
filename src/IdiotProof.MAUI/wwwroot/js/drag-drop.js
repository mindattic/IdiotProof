// Drag-and-drop helper for Blazor — handles visual feedback that Blazor can't do fast enough
window.DragDrop = {
    _dotnetRef: null,
    _dragType: null,   // 'toolbox' or 'segment'
    _dragData: null,   // key string or index number

    init: function (dotnetRef) {
        this._dotnetRef = dotnetRef;
    },

    startToolboxDrag: function (key) {
        this._dragType = 'toolbox';
        this._dragData = key;
        document.body.classList.add('is-dragging');
    },

    startSegmentDrag: function (index) {
        this._dragType = 'segment';
        this._dragData = index;
        document.body.classList.add('is-dragging');
    },

    endDrag: function () {
        this._dragType = null;
        this._dragData = null;
        document.body.classList.remove('is-dragging');
        // Clear all active drop zones
        document.querySelectorAll('.drop-zone').forEach(el => el.classList.remove('drop-active'));
    },

    // Called from JS event listeners on drop zones
    _setupDropZones: function () {
        // Handled via Blazor events + CSS now
    },

    getDragType: function () { return this._dragType; },
    getDragData: function () { return this._dragData; }
};

// Make all elements with [data-drag-toolbox] work with native drag
document.addEventListener('dragstart', function (e) {
    var toolboxKey = e.target.getAttribute('data-drag-toolbox');
    if (toolboxKey) {
        e.dataTransfer.effectAllowed = 'copy';
        e.dataTransfer.setData('text/plain', 'toolbox:' + toolboxKey);
        DragDrop.startToolboxDrag(toolboxKey);
        return;
    }

    var segIndex = e.target.getAttribute('data-drag-segment');
    if (segIndex !== null) {
        e.dataTransfer.effectAllowed = 'move';
        e.dataTransfer.setData('text/plain', 'segment:' + segIndex);
        DragDrop.startSegmentDrag(parseInt(segIndex));
        return;
    }
});

document.addEventListener('dragend', function (e) {
    DragDrop.endDrag();
});

// Handle dragover on drop zones — pure CSS class toggle, no Blazor round-trip
document.addEventListener('dragover', function (e) {
    var zone = e.target.closest('.drop-zone');
    if (zone) {
        e.preventDefault();
        e.dataTransfer.dropEffect = DragDrop._dragType === 'segment' ? 'move' : 'copy';
        // Remove active from all others, add to this one
        document.querySelectorAll('.drop-zone.drop-active').forEach(el => {
            if (el !== zone) el.classList.remove('drop-active');
        });
        zone.classList.add('drop-active');
    }
});

document.addEventListener('dragleave', function (e) {
    var zone = e.target.closest('.drop-zone');
    if (zone && !zone.contains(e.relatedTarget)) {
        zone.classList.remove('drop-active');
    }
});

document.addEventListener('drop', function (e) {
    var zone = e.target.closest('.drop-zone');
    if (zone && DragDrop._dotnetRef) {
        e.preventDefault();
        zone.classList.remove('drop-active');

        var dropIndex = parseInt(zone.getAttribute('data-drop-index'));
        if (isNaN(dropIndex)) return;

        if (DragDrop._dragType === 'toolbox') {
            DragDrop._dotnetRef.invokeMethodAsync('JsDropToolbox', DragDrop._dragData, dropIndex);
        } else if (DragDrop._dragType === 'segment') {
            DragDrop._dotnetRef.invokeMethodAsync('JsDropSegment', DragDrop._dragData, dropIndex);
        }

        DragDrop.endDrag();
    }
});
