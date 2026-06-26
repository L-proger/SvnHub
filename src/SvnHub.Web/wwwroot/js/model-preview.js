(() => {
    const root = document.querySelector('[data-model-preview]');
    if (!root) return;

    const status = root.querySelector('[data-model-status]');
    const stage = root.querySelector('[data-model-stage]');
    const resetButton = root.querySelector('[data-model-reset]');
    const filesJson = document.getElementById('model-preview-files');
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

    function parseRgbColor(value, fallback) {
        const match = /rgba?\((\d+),\s*(\d+),\s*(\d+)/i.exec(value);
        if (!match || !window.OV) return fallback;
        return new OV.RGBColor(Number(match[1]), Number(match[2]), Number(match[3]));
    }

    function parseRgbaColor(value, fallback) {
        const match = /rgba?\((\d+),\s*(\d+),\s*(\d+)(?:,\s*([\d.]+))?/i.exec(value);
        if (!match || !window.OV) return fallback;
        const alpha = match[4] === undefined ? 255 : Math.round(Number(match[4]) * 255);
        return new OV.RGBAColor(Number(match[1]), Number(match[2]), Number(match[3]), alpha);
    }

    async function fetchFile(file, index, total) {
        setStatus(`Loading ${index + 1}/${total}: ${file.name}`, 'alert-secondary');
        const response = await fetch(file.url, { credentials: 'same-origin' });
        if (!response.ok) {
            throw new Error(`${file.name}: HTTP ${response.status}`);
        }

        const buffer = await response.arrayBuffer();
        return new File([buffer], file.name);
    }

    function fitViewer() {
        if (!viewer) return;
        const ovViewer = viewer.GetViewer();
        const sphere = ovViewer.GetBoundingSphere(() => true);
        ovViewer.AdjustClippingPlanesToSphere(sphere);
        if (OV.Direction?.Y !== undefined) {
            ovViewer.SetUpVector(OV.Direction.Y, false);
        }
        ovViewer.FitSphereToWindow(sphere, true);
    }

    async function render() {
        if (!window.OV) {
            throw new Error('Online 3D Viewer engine did not load.');
        }

        const entries = JSON.parse(filesJson?.textContent || '[]').filter((file) => file && file.url);
        if (entries.length === 0) {
            throw new Error('No model files are available for preview.');
        }

        const files = [];
        for (let i = 0; i < entries.length; i += 1) {
            files.push(await fetchFile(entries[i], i, entries.length));
        }

        setStatus('Importing model...', 'alert-secondary');
        stage.innerHTML = '';

        const surface = getThemeColor('--sp-surface-2', 'rgb(248, 249, 250)');
        const bodyColor = getThemeColor('--bs-body-color', 'rgb(33, 37, 41)');
        const defaultColor = parseRgbColor(bodyColor, new OV.RGBColor(200, 200, 200));
        const backgroundColor = parseRgbaColor(surface, new OV.RGBAColor(248, 249, 250, 255));

        viewer = new OV.EmbeddedViewer(stage, {
            backgroundColor,
            defaultColor,
            defaultLineColor: defaultColor,
            edgeSettings: new OV.EdgeSettings(true, defaultColor, 35),
            onModelLoaded: () => {
                setStatus('', 'alert-success');
            },
            onModelLoadFailed: () => {
                setStatus('Preview failed: model import failed.', 'alert-danger');
            },
        });

        viewer.LoadModelFromFileList(files);
    }

    resetButton?.addEventListener('click', fitViewer);

    render().catch((err) => {
        setStatus(`Preview failed: ${err?.message || err}`, 'alert-danger');
    });
})();
