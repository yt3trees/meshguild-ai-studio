window.workAgentsGraphEditor = {
    attach: function (element, dotNetReference) {
        if (!element) return;
        element.addEventListener('pointerdown', function (event) {
            element.setPointerCapture?.(event.pointerId);
        });
        return { element: element, reference: dotNetReference };
    },

    // canvasEl: ノードとエッジを配置している座標系の基準要素 (wa-canvas-inner)
    // previewLineEl: ドラッグ中に追従させる <line> 要素
    // dotNetRef: OnEdgeDropped(fromId, toId) を持つ .NET 側の参照
    attachEdgeDrag: function (canvasEl, previewLineEl, dotNetRef) {
        if (!canvasEl || previewLineEl == null || canvasEl.__edgeDragAttached) {
            return;
        }
        canvasEl.__edgeDragAttached = true;

        var dragging = false;
        var fromId = null;
        var hoveredNodeEl = null;

        function pointIn(evt) {
            var rect = canvasEl.getBoundingClientRect();
            return { x: evt.clientX - rect.left, y: evt.clientY - rect.top };
        }

        function setHovered(nodeEl) {
            if (hoveredNodeEl === nodeEl) {
                return;
            }
            if (hoveredNodeEl) {
                hoveredNodeEl.classList.remove('wa-node-drop-target');
            }
            hoveredNodeEl = nodeEl;
            if (hoveredNodeEl) {
                hoveredNodeEl.classList.add('wa-node-drop-target');
            }
        }

        function beginDrag(evt) {
            var handle = evt.target.closest('[data-edge-handle]');
            if (!handle) {
                return;
            }
            dragging = true;
            fromId = handle.getAttribute('data-node-id');
            var p = pointIn(evt);
            previewLineEl.setAttribute('x1', p.x);
            previewLineEl.setAttribute('y1', p.y);
            previewLineEl.setAttribute('x2', p.x);
            previewLineEl.setAttribute('y2', p.y);
            previewLineEl.classList.add('active');
            canvasEl.classList.add('wa-edge-dragging');
            evt.preventDefault();
        }

        function updateDrag(evt) {
            if (!dragging) {
                return;
            }
            var p = pointIn(evt);
            previewLineEl.setAttribute('x2', p.x);
            previewLineEl.setAttribute('y2', p.y);
            var target = document.elementFromPoint(evt.clientX, evt.clientY);
            var nodeEl = target ? target.closest('[data-node-id]') : null;
            if (nodeEl && nodeEl.getAttribute('data-node-id') === fromId) {
                nodeEl = null;
            }
            setHovered(nodeEl);
        }

        function endDrag(evt) {
            if (!dragging) {
                return;
            }
            dragging = false;
            previewLineEl.classList.remove('active');
            canvasEl.classList.remove('wa-edge-dragging');
            setHovered(null);
            var target = document.elementFromPoint(evt.clientX, evt.clientY);
            var nodeEl = target ? target.closest('[data-node-id]') : null;
            var toId = nodeEl ? nodeEl.getAttribute('data-node-id') : null;
            if (toId && fromId && toId !== fromId) {
                dotNetRef.invokeMethodAsync('OnEdgeDropped', fromId, toId);
            }
            fromId = null;
        }

        canvasEl.addEventListener('pointerdown', beginDrag);
        document.addEventListener('pointermove', updateDrag);
        document.addEventListener('pointerup', endDrag);
    },

    // scrollEl: スクロール可能なキャンバスの外枠 (wa-graph-canvas)。
    // ノードやハンドル、エッジ以外の背景部分をドラッグしたときにスクロール位置を動かしてパンする。
    attachPan: function (scrollEl) {
        if (!scrollEl || scrollEl.__panAttached) {
            return;
        }
        scrollEl.__panAttached = true;

        var panning = false;
        var startX = 0, startY = 0, startLeft = 0, startTop = 0;

        function isInteractive(el) {
            return !!(el.closest('[data-node-id]') || el.closest('[data-edge-handle]')
                || el.closest('.wa-edge-hit') || el.closest('.wa-edge-delete-btn')
                || el.closest('.wa-canvas-tools') || el.closest('button'));
        }

        scrollEl.addEventListener('pointerdown', function (evt) {
            if (evt.button !== 0 || isInteractive(evt.target)) {
                return;
            }
            panning = true;
            startX = evt.clientX;
            startY = evt.clientY;
            startLeft = scrollEl.scrollLeft;
            startTop = scrollEl.scrollTop;
            scrollEl.classList.add('wa-panning');
            scrollEl.setPointerCapture?.(evt.pointerId);
        });

        scrollEl.addEventListener('pointermove', function (evt) {
            if (!panning) {
                return;
            }
            scrollEl.scrollLeft = startLeft - (evt.clientX - startX);
            scrollEl.scrollTop = startTop - (evt.clientY - startY);
        });

        function stopPan() {
            panning = false;
            scrollEl.classList.remove('wa-panning');
        }

        scrollEl.addEventListener('pointerup', stopPan);
        scrollEl.addEventListener('pointercancel', stopPan);
        scrollEl.addEventListener('pointerleave', stopPan);
    },

    // Ctrl+Z (テキスト入力中は除く) を拾って .NET 側の Undo を呼ぶ。
    attachUndoShortcut: function (dotNetRef) {
        if (document.__waUndoAttached) {
            return;
        }
        document.__waUndoAttached = true;
        document.addEventListener('keydown', function (evt) {
            if (!(evt.ctrlKey || evt.metaKey) || evt.key.toLowerCase() !== 'z') {
                return;
            }
            var tag = evt.target && evt.target.tagName;
            if (tag === 'INPUT' || tag === 'TEXTAREA' || (evt.target && evt.target.isContentEditable)) {
                return;
            }
            evt.preventDefault();
            dotNetRef.invokeMethodAsync('OnUndoShortcut');
        });
    }
};
