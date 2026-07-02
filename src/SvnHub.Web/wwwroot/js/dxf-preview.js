(() => {
    const root = document.querySelector('[data-dxf-preview]');
    if (!root) return;

    const status = root.querySelector('[data-dxf-status]');
    const layout = root.querySelector('[data-dxf-layout]');
    const stage = root.querySelector('[data-dxf-stage]');
    const layerPanel = root.querySelector('[data-dxf-layer-panel]');
    const resetButton = root.querySelector('[data-dxf-reset]');
    const url = root.getAttribute('data-dxf-url');
    let viewer = null;

    function setStatus(text, variant) {
        if (!status) return;
        status.textContent = text || '';
        status.classList.remove('alert-secondary', 'alert-danger', 'alert-success');
        status.classList.add(variant || 'alert-secondary');
        status.hidden = !text;
    }

    function getThemeColor(varName, fallback) {
        const value = getComputedStyle(document.documentElement).getPropertyValue(varName).trim();
        return value || fallback;
    }

    function resetView() {
        if (!viewer) return;

        const bounds = viewer.GetBounds?.();
        if (bounds) {
            const origin = viewer.GetOrigin?.() || { x: 0, y: 0 };
            viewer.FitView(
                bounds.minX - origin.x,
                bounds.maxX - origin.x,
                bounds.minY - origin.y,
                bounds.maxY - origin.y,
                0.08);
        }

        viewer.Render?.();
    }

    function layerColorToCss(layer) {
        if (!Number.isFinite(layer?.color)) {
            return 'var(--bs-secondary-color)';
        }

        return `#${layer.color.toString(16).padStart(6, '0')}`;
    }

    function renderLayers() {
        if (!layerPanel || !viewer) return;

        layerPanel.textContent = '';
        const layers = Array.from(viewer.GetLayers?.(true) || []);
        if (layers.length === 0) {
            layerPanel.hidden = true;
            return;
        }

        layerPanel.hidden = false;
        for (const layer of layers) {
            const row = document.createElement('label');
            row.className = 'dxf-layer-item';
            row.title = layer.displayName || layer.name;

            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.checked = true;
            checkbox.addEventListener('change', () => {
                viewer.ShowLayer(layer.name, checkbox.checked);
                viewer.Render();
            });

            const swatch = document.createElement('span');
            swatch.className = 'dxf-layer-swatch';
            swatch.style.backgroundColor = layerColorToCss(layer);

            const name = document.createElement('span');
            name.className = 'text-truncate';
            name.textContent = layer.displayName || layer.name;

            row.append(checkbox, swatch, name);
            layerPanel.append(row);
        }
    }

    async function load() {
        if (!url || !stage || !window.SvnHubDxfViewer?.DxfViewer || !window.SvnHubDxfViewer?.THREE) {
            setStatus('Preview failed: DXF viewer is not available.', 'alert-danger');
            return;
        }

        const { DxfViewer, THREE } = window.SvnHubDxfViewer;
        setStatus('Loading DXF preview...', 'alert-secondary');

        try {
            if (layout) {
                layout.hidden = false;
            }

            viewer = new DxfViewer(stage, {
                autoResize: true,
                antialias: true,
                clearColor: new THREE.Color(getThemeColor('--bs-body-bg', '#ffffff')),
                clearAlpha: 1,
                blackWhiteInversion: true,
                fileEncoding: 'utf-8',
            });

            if (!viewer.HasRenderer()) {
                setStatus('Preview failed: WebGL is not available.', 'alert-danger');
                return;
            }

            viewer.Subscribe?.('message', (event) => {
                const detail = event.detail || {};
                if (detail.level === 'error') {
                    setStatus(detail.message || 'DXF preview error.', 'alert-danger');
                }
            });

            await viewer.Load({
                url,
                progressCbk: (phase, done, total) => {
                    if (!total) {
                        setStatus(`Loading DXF preview (${phase})...`, 'alert-secondary');
                        return;
                    }

                    const percent = Math.max(0, Math.min(100, Math.round((done / total) * 100)));
                    setStatus(`Loading DXF preview (${phase}) ${percent}%...`, 'alert-secondary');
                },
            });

            renderLayers();
            resetView();
            setStatus('', 'alert-success');
        } catch (err) {
            console.error(err);
            setStatus(`Preview failed: ${err?.message || err}`, 'alert-danger');
        }
    }

    resetButton?.addEventListener('click', resetView);
    window.addEventListener('pagehide', () => viewer?.Destroy?.());

    load();
})();
