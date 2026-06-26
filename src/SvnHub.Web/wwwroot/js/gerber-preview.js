(() => {
    const root = document.querySelector('[data-gerber-preview]');
    if (!root) return;

    const status = root.querySelector('[data-gerber-status]');
    const layout = root.querySelector('[data-gerber-layout]');
    const stage = root.querySelector('[data-gerber-stage]');
    const layerPanel = root.querySelector('[data-gerber-layer-panel]');
    const boards = new Map(Array.from(root.querySelectorAll('[data-gerber-board]')).map((el) => [el.dataset.gerberBoard, el]));
    const sideButtons = Array.from(root.querySelectorAll('[data-gerber-side]'));
    const xrayButton = root.querySelector('[data-gerber-xray]');
    const opacityInput = root.querySelector('[data-gerber-opacity]');
    const opacityControl = root.querySelector('[data-gerber-opacity-control]');
    const filesJson = document.getElementById('gerber-preview-files');
    const visibleLayerIds = new Set();
    let tracespaceCore = null;
    let renderedLayers = null;
    let renderedFragments = null;
    let fileEntriesByName = new Map();
    let xrayEnabled = false;

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

    function showSide(side) {
        for (const [name, board] of boards) {
            board.hidden = name !== side;
        }

        for (const button of sideButtons) {
            const active = button.dataset.gerberSide === side;
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
        }
    }

    function updateXrayState(rerender) {
        const opacity = Math.max(20, Math.min(100, Number(opacityInput?.value || 45))) / 100;
        root.style.setProperty('--gerber-xray-opacity', opacity.toString());
        root.classList.toggle('gerber-preview-xray', xrayEnabled);

        if (xrayButton) {
            xrayButton.classList.toggle('active', xrayEnabled);
            xrayButton.setAttribute('aria-pressed', xrayEnabled ? 'true' : 'false');
        }

        if (opacityControl) {
            opacityControl.hidden = !xrayEnabled;
        }

        if (rerender) {
            renderVisibleBoard();
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

    function renderVisibleBoard() {
        if (!tracespaceCore || !renderedLayers) return;

        if (xrayEnabled) {
            renderXrayBoard();
            return;
        }

        const visibleLayers = renderedLayers.layers.filter((layer) => visibleLayerIds.has(layer.id));
        const boardResult = tracespaceCore.renderBoard({
            ...renderedLayers,
            layers: visibleLayers,
        });

        const top = boards.get('top');
        const bottom = boards.get('bottom');
        if (top) top.innerHTML = tracespaceCore.stringifySvg(boardResult.top);
        if (bottom) bottom.innerHTML = tracespaceCore.stringifySvg(boardResult.bottom);
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
        if (type === 'silkscreen') return 70;
        if (type === 'solderpaste') return 60;
        if (type === 'soldermask') return 50;
        if (side === 'top') return 40;
        if (side === 'inner') return 30;
        if (side === 'bottom') return 20;
        return 10;
    }

    function renderXrayBoard() {
        if (!renderedFragments) return;

        const viewBox = renderedFragments.boardShapeRenderFragment?.viewBox || [0, 0, 100, 100];
        const [x, y, width, height] = viewBox;
        const viewBoxText = viewBox.join(' ');
        const shape = renderedFragments.boardShapeRenderFragment?.svgFragment || '';
        const layers = renderedFragments.layers
            .filter((layer) => visibleLayerIds.has(layer.id))
            .slice()
            .sort((a, b) => getLayerSortWeight(a) - getLayerSortWeight(b));

        for (const [side, board] of boards) {
            const clipId = `gerber-xray-clip-${side}`;
            const transform = side === 'bottom'
                ? ` transform="translate(${(2 * x) + width},0) scale(-1,1)"`
                : '';
            const clip = shape ? ` clip-path="url(#${clipId})"` : '';
            const layerMarkup = layers.map((layer) => {
                const fragment = renderedFragments.svgFragmentsById[layer.id];
                if (!fragment) return '';

                const color = escapeAttribute(getLayerColor(layer));
                const label = escapeAttribute(`${getLayerLabel(layer)} ${layer.filename || ''}`.trim());
                return `<g class="gerber-xray-layer" color="${color}" aria-label="${label}">${fragment}</g>`;
            }).join('');
            const defs = shape ? `<defs><clipPath id="${clipId}">${shape}</clipPath></defs>` : '';

            board.innerHTML = `
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="${escapeAttribute(viewBoxText)}" role="img" aria-label="PCB X-ray ${escapeAttribute(side)} view">
                    ${defs}
                    <g${transform}${clip}>
                        <rect class="gerber-xray-substrate" x="${x}" y="${y}" width="${width}" height="${height}"></rect>
                        ${layerMarkup}
                    </g>
                </svg>`;
        }
    }

    function updateLayerButton(button, isVisible) {
        button.classList.toggle('active', isVisible);
        button.setAttribute('aria-pressed', isVisible ? 'true' : 'false');
        button.title = isVisible ? 'Hide layer' : 'Show layer';
    }

    function buildLayerPanel(layers) {
        if (!layerPanel) return;

        layerPanel.replaceChildren();

        for (const group of layerGroups) {
            const groupLayers = layers.filter((layer) => getLayerGroup(layer) === group.key);
            if (groupLayers.length === 0) continue;

            const section = document.createElement('div');
            section.className = 'gerber-layer-section';

            const heading = document.createElement('div');
            heading.className = 'gerber-layer-heading';
            heading.textContent = group.label;
            section.appendChild(heading);

            for (const layer of groupLayers) {
                const entry = fileEntriesByName.get((layer.filename || '').toLowerCase());
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'gerber-layer-toggle active';
                button.dataset.layerId = layer.id;
                button.style.setProperty('--gerber-layer-color', getLayerColor(layer));
                button.setAttribute('aria-pressed', 'true');

                const label = document.createElement('span');
                label.className = 'gerber-layer-name';
                label.textContent = getLayerLabel(layer);

                const fileName = document.createElement('span');
                fileName.className = 'gerber-layer-file text-truncate';
                fileName.textContent = layer.filename || entry?.name || '';

                const meta = document.createElement('span');
                meta.className = 'gerber-layer-meta';
                meta.textContent = entry?.sizeLabel || '';

                const icon = document.createElement('span');
                icon.className = 'gerber-layer-eye';
                icon.innerHTML = createEyeIcon();

                button.append(label, fileName, meta, icon);
                button.addEventListener('click', () => {
                    const isVisible = visibleLayerIds.has(layer.id);
                    if (isVisible) {
                        visibleLayerIds.delete(layer.id);
                    } else {
                        visibleLayerIds.add(layer.id);
                    }

                    updateLayerButton(button, !isVisible);
                    renderVisibleBoard();
                });

                section.appendChild(button);
            }

            layerPanel.appendChild(section);
        }
    }

    async function fetchFile(file, index, total) {
        setStatus(`Loading ${index + 1}/${total}: ${file.name}`, 'alert-secondary');
        const response = await fetch(file.url, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(`${file.name}: HTTP ${response.status}`);
        }

        const text = await response.text();
        return new File([text], file.name, { type: 'text/plain' });
    }

    async function render() {
        if (!window.TracespaceCore) {
            throw new Error('Tracespace renderer did not load.');
        }

        const entries = JSON.parse(filesJson?.textContent || '[]').filter((file) => file && file.url);
        if (entries.length === 0) {
            throw new Error('No CAM files are available for preview.');
        }
        fileEntriesByName = new Map(entries.map((entry) => [(entry.name || '').toLowerCase(), entry]));

        const files = [];
        for (let i = 0; i < entries.length; i += 1) {
            files.push(await fetchFile(entries[i], i, entries.length));
        }

        setStatus('Rendering board...', 'alert-secondary');

        tracespaceCore = window.TracespaceCore;
        const readResult = await tracespaceCore.read(files);
        const plotResult = tracespaceCore.plot(readResult);
        renderedLayers = tracespaceCore.renderLayers(plotResult);
        renderedFragments = tracespaceCore.renderFragments(plotResult);

        visibleLayerIds.clear();
        for (const layer of renderedLayers.layers) {
            visibleLayerIds.add(layer.id);
        }

        buildLayerPanel(renderedLayers.layers);
        renderVisibleBoard();

        if (layout) layout.hidden = false;
        if (stage) stage.hidden = false;
        setStatus('', 'alert-success');
        showSide('top');
    }

    for (const button of sideButtons) {
        button.addEventListener('click', () => showSide(button.dataset.gerberSide));
    }

    xrayButton?.addEventListener('click', () => {
        xrayEnabled = !xrayEnabled;
        updateXrayState(true);
    });

    opacityInput?.addEventListener('input', () => updateXrayState(false));
    updateXrayState();

    render().catch((err) => {
        if (layout) layout.hidden = true;
        if (stage) stage.hidden = true;
        setStatus(`Preview failed: ${err?.message || err}`, 'alert-danger');
    });
})();
