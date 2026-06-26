(() => {
    const root = document.querySelector('[data-gerber-preview]');
    if (!root) return;

    const status = root.querySelector('[data-gerber-status]');
    const layout = root.querySelector('[data-gerber-layout]');
    const stage = root.querySelector('[data-gerber-stage]');
    const layerPanel = root.querySelector('[data-gerber-layer-panel]');
    const boards = new Map(Array.from(root.querySelectorAll('[data-gerber-board]')).map((el) => [el.dataset.gerberBoard, el]));
    const sideButtons = Array.from(root.querySelectorAll('[data-gerber-side]'));
    const opacityInput = root.querySelector('[data-gerber-opacity]');
    const opacityControl = root.querySelector('[data-gerber-opacity-control]');
    const filesJson = document.getElementById('gerber-preview-files');
    const visibleLayerIds = new Set();
    let stackup = null;
    let fileEntriesByName = new Map();
    let currentView = 'top';
    let viewport = { panX: 0, panY: 0, zoom: 1 };
    let panStart = null;

    const minZoom = 0.25;
    const maxZoom = 8;

    const layerGroups = [
        { key: 'top', label: 'Top' },
        { key: 'bottom', label: 'Bottom' },
        { key: 'inner', label: 'Inner' },
        { key: 'mechanical', label: 'Mechanical' },
        { key: 'other', label: 'Other' },
    ];

    const layerColors = {
        copper: '#0b8f5a',
        soldermask: '#148a44',
        silkscreen: '#f5f7fb',
        solderpaste: '#d7b70f',
        drill: '#35a7ff',
        outline: '#20c997',
        drawing: '#d63384',
    };

    function setStatus(text, variant) {
        if (!status) return;
        status.textContent = text || '';
        status.classList.remove('alert-secondary', 'alert-danger', 'alert-success');
        status.classList.add(variant || 'alert-secondary');
        status.hidden = !text;
    }

    function clamp(value, min, max) {
        return Math.min(max, Math.max(min, value));
    }

    function getVisibleBoardEntry() {
        return Array.from(boards.entries()).find(([, board]) => !board.hidden) || Array.from(boards.entries())[0] || null;
    }

    function getBoardBaseViewBox(side) {
        if (side === 'layers') return getStackupViewBox();
        if (side === 'bottom') return stackup?.bottom?.viewBox || getStackupViewBox();
        return stackup?.top?.viewBox || getStackupViewBox();
    }

    function getDisplayViewBox(baseViewBox) {
        const [baseX, baseY, baseWidth, baseHeight] = baseViewBox;
        const width = baseWidth / viewport.zoom;
        const height = baseHeight / viewport.zoom;
        const x = baseX + ((baseWidth - width) / 2) + (viewport.panX * baseWidth);
        const y = baseY + ((baseHeight - height) / 2) + (viewport.panY * baseHeight);
        return [x, y, width, height];
    }

    function applyViewportViewBox() {
        for (const [side, board] of boards) {
            const svg = board.querySelector('svg');
            if (!svg) continue;

            const viewBox = getDisplayViewBox(getBoardBaseViewBox(side));
            svg.setAttribute('viewBox', viewBox.join(' '));
        }
    }

    function resetViewport() {
        viewport = { panX: 0, panY: 0, zoom: 1 };
        applyViewportViewBox();
    }

    function zoomAt(clientX, clientY, delta) {
        if (!stage) return;

        const visible = getVisibleBoardEntry();
        if (!visible) return;

        const [side, board] = visible;
        const svg = board.querySelector('svg');
        if (!svg) return;

        const rect = svg.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;

        const baseViewBox = getBoardBaseViewBox(side);
        const oldViewBox = getDisplayViewBox(baseViewBox);
        const oldZoom = viewport.zoom;
        const nextZoom = clamp(oldZoom * Math.exp(delta), minZoom, maxZoom);
        if (nextZoom === oldZoom) return;

        const [, , baseWidth, baseHeight] = baseViewBox;
        const [oldX, oldY, oldWidth, oldHeight] = oldViewBox;
        const pointerX = clamp((clientX - rect.left) / rect.width, 0, 1);
        const pointerY = clamp((clientY - rect.top) / rect.height, 0, 1);
        const targetX = oldX + (pointerX * oldWidth);
        const targetY = oldY + (pointerY * oldHeight);
        const nextWidth = baseWidth / nextZoom;
        const nextHeight = baseHeight / nextZoom;
        const nextX = targetX - (pointerX * nextWidth);
        const nextY = targetY - (pointerY * nextHeight);

        viewport = {
            panX: (nextX - baseViewBox[0] - ((baseWidth - nextWidth) / 2)) / baseWidth,
            panY: (nextY - baseViewBox[1] - ((baseHeight - nextHeight) / 2)) / baseHeight,
            zoom: nextZoom,
        };
        applyViewportViewBox();
    }

    function showSide(side) {
        currentView = side;
        for (const [name, board] of boards) {
            board.hidden = name !== side;
        }

        for (const button of sideButtons) {
            const active = button.dataset.gerberSide === side;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        }

        updateViewState(true);
    }

    function updateViewState(rerender) {
        const opacity = Math.max(5, Math.min(100, Number(opacityInput?.value || 50))) / 100;
        root.style.setProperty('--gerber-layer-opacity', opacity.toString());
        root.classList.toggle('gerber-preview-layers', currentView === 'layers');

        if (opacityControl) {
            opacityControl.hidden = currentView !== 'layers';
        }

        syncLayerControlState();

        if (rerender) {
            renderBoard();
        }
    }

    function getLayerLabel(layer) {
        const type = (layer.type || '').toLowerCase();
        switch (type) {
            case 'copper': return 'Copper';
            case 'soldermask': return 'Solder mask';
            case 'silkscreen': return 'Silkscreen';
            case 'solderpaste': return 'Solder paste';
            case 'drill': return 'Drill';
            case 'outline': return 'Outline';
            case 'drawing': return 'Drawing';
            default: return layer.type || 'Layer';
        }
    }

    function getLayerId(layer) {
        return layer.converter?.id || layer.options?.id || layer.filename || '';
    }

    function getLayerGroup(layer) {
        const side = (layer.side || '').toLowerCase();
        const type = (layer.type || '').toLowerCase();

        if (side === 'top' || side === 'bottom' || side === 'inner') {
            return side;
        }

        if (type === 'drill' || type === 'outline') {
            return 'mechanical';
        }

        return 'other';
    }

    function getLayerColor(layer) {
        const type = (layer.type || '').toLowerCase();
        const side = (layer.side || '').toLowerCase();
        if (type === 'copper' && side === 'top') return '#d18b2f';
        if (type === 'copper' && side === 'bottom') return '#7c5cff';
        if (type === 'copper' && side === 'inner') return '#23a6d5';
        return layerColors[type] || '#6c757d';
    }

    function createEyeIcon() {
        return `
            <svg aria-hidden="true" viewBox="0 0 24 24" width="16" height="16" focusable="false">
                <path fill="currentColor" d="M12 5c5.3 0 9 5.1 9 7s-3.7 7-9 7-9-5.1-9-7 3.7-7 9-7Zm0 2c-3.9 0-6.6 3.5-7 5 .4 1.5 3.1 5 7 5s6.6-3.5 7-5c-.4-1.5-3.1-5-7-5Zm0 2.5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5Z"></path>
            </svg>`;
    }

    function escapeAttribute(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('"', '&quot;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;');
    }

    function getLayerSortWeight(layer) {
        const side = (layer.side || '').toLowerCase();
        const type = (layer.type || '').toLowerCase();

        if (type === 'outline') return 90;
        if (type === 'drill') return 80;
        if (type === 'solderpaste') return 70;
        if (type === 'silkscreen') return 60;
        if (type === 'soldermask') return 50;
        if (type === 'copper' && side === 'top') return 40;
        if (type === 'copper' && side === 'inner') return 30;
        if (type === 'copper' && side === 'bottom') return 20;
        return 10;
    }

    function addViewBoxes(first, second) {
        if (!first) return second;
        if (!second) return first;

        const x = Math.min(first[0], second[0]);
        const y = Math.min(first[1], second[1]);
        const right = Math.max(first[0] + first[2], second[0] + second[2]);
        const bottom = Math.max(first[1] + first[3], second[1] + second[3]);
        return [x, y, right - x, bottom - y];
    }

    function getStackupViewBox() {
        return addViewBoxes(stackup?.top?.viewBox, stackup?.bottom?.viewBox) || [0, 0, 100, 100];
    }

    function getStackupUnits() {
        const count = { in: 0, mm: 0 };
        for (const layer of stackup?.layers || []) {
            const units = layer.converter?.units;
            if (units === 'in' || units === 'mm') {
                count[units] += 1;
            }
        }

        return count.in > count.mm ? 'in' : 'mm';
    }

    function getLayerScale(layer) {
        const units = layer.converter?.units;
        const stackupUnits = getStackupUnits();
        if (units === 'mm' && stackupUnits === 'in') return 1 / 25.4;
        if (units === 'in' && stackupUnits === 'mm') return 25.4;
        return 1;
    }

    function getVisibleLayers() {
        return (stackup?.layers || [])
            .filter((layer) => visibleLayerIds.has(getLayerId(layer)))
            .filter((layer) => (layer.converter?.layer || []).length > 0)
            .slice()
            .sort((a, b) => getLayerSortWeight(a) - getLayerSortWeight(b));
    }

    function renderLayerFragments(layers) {
        return layers.map((layer) => {
            const converter = layer.converter;
            if (!converter) return '';

            const id = escapeAttribute(getLayerId(layer));
            const color = escapeAttribute(getLayerColor(layer));
            const label = escapeAttribute(`${getLayerLabel(layer)} ${layer.filename || ''}`.trim());
            const defs = (converter.defs || []).join('');
            const content = (converter.layer || []).join('');
            const scale = getLayerScale(layer);
            const scaleAttribute = scale === 1 ? '' : ` transform="scale(${scale})"`;

            return `
                <g id="${id}" class="gerber-composite-layer gerber-layer-render" style="--gerber-layer-color:${color}" fill="${color}" stroke="${color}" aria-label="${label}">
                    <defs>${defs}</defs>
                    <g${scaleAttribute}>${content}</g>
                </g>`;
        }).join('');
    }

    function renderBoard() {
        if (!stackup) return;

        if (currentView === 'layers') {
            clearBoard('top');
            clearBoard('bottom');
            renderInspectionBoard();
            return;
        }

        clearBoard('layers');
        clearBoard(currentView === 'top' ? 'bottom' : 'top');

        const board = boards.get(currentView);
        if (board) {
            board.innerHTML = currentView === 'bottom'
                ? stackup.bottom?.svg || ''
                : stackup.top?.svg || '';
        }

        applyViewportViewBox();
    }

    function clearBoard(side) {
        const board = boards.get(side);
        if (board) {
            board.replaceChildren();
        }
    }

    function renderInspectionBoard() {
        const board = boards.get('layers');
        if (!board) return;

        const [x, y, width, height] = getStackupViewBox();
        const viewBoxText = [x, y, width, height].join(' ');
        const layers = getVisibleLayers();
        const layerMarkup = renderLayerFragments(layers);
        const yFlip = height + (2 * y);
        const transform = `translate(0,${yFlip}) scale(1,-1)`;

        board.innerHTML = `
            <svg xmlns="http://www.w3.org/2000/svg" stroke-linecap="round" stroke-linejoin="round" stroke-width="0" fill-rule="evenodd" viewBox="${escapeAttribute(viewBoxText)}" role="img" aria-label="PCB layers view">
                <rect class="gerber-composite-substrate" x="${x}" y="${y}" width="${width}" height="${height}"></rect>
                <g transform="${transform}">
                    ${layerMarkup}
                </g>
            </svg>`;

        applyViewportViewBox();
    }

    function updateLayerButton(button, isVisible) {
        button.classList.toggle('active', isVisible);
        button.setAttribute('aria-pressed', isVisible ? 'true' : 'false');
        button.title = isVisible ? 'Hide layer' : 'Show layer';
    }

    function syncLayerControlState() {
        if (!layerPanel) return;

        const enabled = currentView === 'layers';
        for (const button of layerPanel.querySelectorAll('.gerber-layer-toggle')) {
            button.classList.toggle('disabled', !enabled);
            button.setAttribute('aria-disabled', enabled ? 'false' : 'true');
            const action = button.classList.contains('active') ? 'Hide layer' : 'Show layer';
            button.setAttribute('aria-label', enabled ? action : 'Switch to Layers to change layer visibility');
        }
    }

    function buildLayerPanel(layers) {
        if (!layerPanel) return;

        layerPanel.replaceChildren();

        for (const group of layerGroups) {
            const groupLayers = layers
                .filter((layer) => getLayerGroup(layer) === group.key)
                .slice()
                .sort((a, b) => getLayerSortWeight(a) - getLayerSortWeight(b));
            if (groupLayers.length === 0) continue;

            const section = document.createElement('div');
            section.className = 'gerber-layer-section';

            const heading = document.createElement('div');
            heading.className = 'gerber-layer-heading';
            heading.textContent = group.label;
            section.appendChild(heading);

            for (const layer of groupLayers) {
                const id = getLayerId(layer);
                const entry = fileEntriesByName.get((layer.filename || '').toLowerCase());
                const tooltip = [entry?.name || layer.filename, entry?.sizeLabel]
                    .filter((value) => value && String(value).trim().length > 0)
                    .join(' - ');
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'gerber-layer-toggle active';
                button.dataset.layerId = id;
                button.style.setProperty('--gerber-layer-color', getLayerColor(layer));
                button.setAttribute('aria-pressed', 'true');
                button.setAttribute('aria-disabled', currentView === 'layers' ? 'false' : 'true');
                button.classList.toggle('disabled', currentView !== 'layers');
                button.title = tooltip || getLayerLabel(layer);
                button.setAttribute('aria-label', 'Hide layer');

                const label = document.createElement('span');
                label.className = 'gerber-layer-name text-truncate';
                label.textContent = getLayerLabel(layer);

                const icon = document.createElement('span');
                icon.className = 'gerber-layer-eye';
                icon.innerHTML = createEyeIcon();

                button.append(label, icon);
                button.addEventListener('click', () => {
                    if (currentView !== 'layers') return;

                    const isVisible = visibleLayerIds.has(id);
                    if (isVisible) {
                        visibleLayerIds.delete(id);
                    } else {
                        visibleLayerIds.add(id);
                    }

                    updateLayerButton(button, !isVisible);
                    renderBoard();
                });

                section.appendChild(button);
            }

            layerPanel.appendChild(section);
        }

        syncLayerControlState();
    }

    async function fetchFile(file, index, total) {
        setStatus(`Loading ${index + 1}/${total}: ${file.name}`, 'alert-secondary');
        const response = await fetch(file.url, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(`${file.name}: HTTP ${response.status}`);
        }

        return {
            filename: file.name,
            gerber: await response.text(),
        };
    }

    async function render() {
        if (!window.pcbStackup) {
            throw new Error('Tracespace renderer did not load.');
        }

        const entries = JSON.parse(filesJson?.textContent || '[]').filter((file) => file && file.url);
        if (entries.length === 0) {
            throw new Error('No CAM files are available for preview.');
        }
        fileEntriesByName = new Map(entries.map((entry) => [(entry.name || '').toLowerCase(), entry]));

        const layers = [];
        for (let i = 0; i < entries.length; i += 1) {
            layers.push(await fetchFile(entries[i], i, entries.length));
        }

        setStatus('Rendering board...', 'alert-secondary');

        stackup = await window.pcbStackup(layers, {
            attributes: { class: 'w-100 h-100' },
        });

        visibleLayerIds.clear();
        for (const layer of stackup.layers || []) {
            visibleLayerIds.add(getLayerId(layer));
        }

        if (layout) layout.hidden = false;
        if (stage) stage.hidden = false;
        setStatus('', 'alert-success');
        buildLayerPanel(stackup.layers || []);
        showSide('top');
    }

    for (const button of sideButtons) {
        button.addEventListener('click', () => showSide(button.dataset.gerberSide));
    }

    stage?.addEventListener('wheel', (event) => {
        if (!stackup) return;
        event.preventDefault();
        zoomAt(event.clientX, event.clientY, -event.deltaY * 0.0015);
    }, { passive: false });

    stage?.addEventListener('pointerdown', (event) => {
        if (!stackup || event.button !== 0) return;
        panStart = {
            pointerId: event.pointerId,
            clientX: event.clientX,
            clientY: event.clientY,
            panX: viewport.panX,
            panY: viewport.panY,
        };
        stage.setPointerCapture(event.pointerId);
        stage.classList.add('is-panning');
    });

    stage?.addEventListener('pointermove', (event) => {
        if (!panStart || panStart.pointerId !== event.pointerId) return;

        const visible = getVisibleBoardEntry();
        const svg = visible?.[1].querySelector('svg');
        if (!svg) return;

        const rect = svg.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;

        viewport = {
            ...viewport,
            panX: panStart.panX - ((event.clientX - panStart.clientX) / rect.width / viewport.zoom),
            panY: panStart.panY - ((event.clientY - panStart.clientY) / rect.height / viewport.zoom),
        };
        applyViewportViewBox();
    });

    function stopPan(event) {
        if (!panStart || panStart.pointerId !== event.pointerId) return;
        panStart = null;
        stage?.classList.remove('is-panning');
    }

    stage?.addEventListener('pointerup', stopPan);
    stage?.addEventListener('pointercancel', stopPan);
    stage?.addEventListener('dblclick', resetViewport);

    opacityInput?.addEventListener('input', () => updateViewState(false));
    updateViewState(false);

    render().catch((err) => {
        if (layout) layout.hidden = true;
        if (stage) stage.hidden = true;
        setStatus(`Preview failed: ${err?.message || err}`, 'alert-danger');
    });
})();
