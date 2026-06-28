(() => {
    const root = document.querySelector('[data-altium-preview]');
    if (!root) return;

    const frame = root.querySelector('.altium-preview-frame');
    const buttons = Array.from(root.querySelectorAll('[data-altium-side]'));
    const resetButton = root.querySelector('[data-altium-reset]');
    if (!frame) return;

    let svg = null;
    let baseViewBox = null;
    let viewport = { panX: 0, panY: 0, zoom: 1 };
    let panStart = null;

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function parseViewBox(value) {
        const parts = (value || '')
            .trim()
            .split(/[\s,]+/)
            .map(Number)
            .filter((item) => Number.isFinite(item));

        return parts.length === 4 && parts[2] > 0 && parts[3] > 0 ? parts : null;
    }

    function getSvgViewBox(nextSvg) {
        const explicit = parseViewBox(nextSvg.getAttribute('viewBox'));
        if (explicit) return explicit;

        const width = Number.parseFloat(nextSvg.getAttribute('width'));
        const height = Number.parseFloat(nextSvg.getAttribute('height'));
        if (Number.isFinite(width) && Number.isFinite(height) && width > 0 && height > 0) {
            return [0, 0, width, height];
        }

        return [0, 0, 1600, 1200];
    }

    function getDisplayViewBox() {
        const [baseX, baseY, baseWidth, baseHeight] = baseViewBox;
        const width = baseWidth / viewport.zoom;
        const height = baseHeight / viewport.zoom;
        const x = baseX + ((baseWidth - width) / 2) + (viewport.panX * baseWidth);
        const y = baseY + ((baseHeight - height) / 2) + (viewport.panY * baseHeight);
        return [x, y, width, height];
    }

    function applyViewBox() {
        if (!svg || !baseViewBox) return;
        svg.setAttribute('viewBox', getDisplayViewBox().join(' '));
    }

    function resetView() {
        viewport = { panX: 0, panY: 0, zoom: 1 };
        applyViewBox();
    }

    function zoomAt(clientX, clientY, delta) {
        if (!svg || !baseViewBox) return;

        const rect = svg.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;

        const oldZoom = viewport.zoom;
        const nextZoom = clamp(oldZoom * Math.exp(delta), 0.5, 32);
        if (Math.abs(nextZoom - oldZoom) < 0.001) return;

        const [baseX, baseY, baseWidth, baseHeight] = baseViewBox;
        const [oldX, oldY, oldWidth, oldHeight] = getDisplayViewBox();
        const pointerX = clamp((clientX - rect.left) / rect.width, 0, 1);
        const pointerY = clamp((clientY - rect.top) / rect.height, 0, 1);
        const targetX = oldX + (pointerX * oldWidth);
        const targetY = oldY + (pointerY * oldHeight);
        const nextWidth = baseWidth / nextZoom;
        const nextHeight = baseHeight / nextZoom;
        const nextX = targetX - (pointerX * nextWidth);
        const nextY = targetY - (pointerY * nextHeight);

        viewport = {
            panX: (nextX - baseX - ((baseWidth - nextWidth) / 2)) / baseWidth,
            panY: (nextY - baseY - ((baseHeight - nextHeight) / 2)) / baseHeight,
            zoom: nextZoom,
        };
        applyViewBox();
    }

    function stopPan(event) {
        if (!panStart || panStart.pointerId !== event.pointerId) return;
        panStart = null;
        frame.classList.remove('is-panning');
    }

    function attachSvgInteractions() {
        const previewDocument = frame.contentDocument;
        svg = previewDocument?.querySelector('svg') || null;
        if (!previewDocument || !svg) return;

        baseViewBox = getSvgViewBox(svg);
        previewDocument.documentElement.style.width = '100%';
        previewDocument.documentElement.style.height = '100%';

        if (previewDocument.body) {
            previewDocument.body.style.width = '100%';
            previewDocument.body.style.height = '100%';
            previewDocument.body.style.margin = '0';
            previewDocument.body.style.overflow = 'hidden';
        }

        svg.setAttribute('width', '100%');
        svg.setAttribute('height', '100%');
        svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
        svg.style.display = 'block';
        svg.style.pointerEvents = 'all';
        resetView();

        previewDocument.addEventListener('wheel', (event) => {
            event.preventDefault();
            zoomAt(event.clientX, event.clientY, -event.deltaY * 0.0015);
        }, { passive: false });

        previewDocument.addEventListener('pointerdown', (event) => {
            if (event.button !== 0) return;
            event.preventDefault();
            panStart = {
                pointerId: event.pointerId,
                clientX: event.clientX,
                clientY: event.clientY,
                panX: viewport.panX,
                panY: viewport.panY,
            };
            frame.classList.add('is-panning');
        });

        previewDocument.addEventListener('pointermove', (event) => {
            if (!panStart || panStart.pointerId !== event.pointerId || !baseViewBox) return;

            const rect = svg.getBoundingClientRect();
            if (rect.width <= 0 || rect.height <= 0) return;

            viewport = {
                panX: panStart.panX - ((event.clientX - panStart.clientX) / rect.width / viewport.zoom),
                panY: panStart.panY - ((event.clientY - panStart.clientY) / rect.height / viewport.zoom),
                zoom: viewport.zoom,
            };
            applyViewBox();
        });

        previewDocument.addEventListener('pointerup', stopPan);
        previewDocument.addEventListener('pointercancel', stopPan);
    }

    frame.addEventListener('load', attachSvgInteractions);
    resetButton?.addEventListener('click', resetView);

    buttons.forEach((button) => {
        button.addEventListener('click', () => {
            const url = button.getAttribute('data-altium-url');
            if (!url || button.classList.contains('active')) return;

            buttons.forEach((item) => {
                item.classList.remove('active');
                item.setAttribute('aria-pressed', 'false');
            });

            button.classList.add('active');
            button.setAttribute('aria-pressed', 'true');
            frame.src = url;
        });
    });
})();
