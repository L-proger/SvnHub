(() => {
    const root = document.querySelector('[data-gerber-preview]');
    if (!root) return;

    const status = root.querySelector('[data-gerber-status]');
    const stage = root.querySelector('[data-gerber-stage]');
    const boards = new Map(Array.from(root.querySelectorAll('[data-gerber-board]')).map((el) => [el.dataset.gerberBoard, el]));
    const sideButtons = Array.from(root.querySelectorAll('[data-gerber-side]'));
    const filesJson = document.getElementById('gerber-preview-files');

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

        const files = [];
        for (let i = 0; i < entries.length; i += 1) {
            files.push(await fetchFile(entries[i], i, entries.length));
        }

        setStatus('Rendering board...', 'alert-secondary');

        const core = window.TracespaceCore;
        const readResult = await core.read(files);
        const plotResult = core.plot(readResult);
        const boardResult = core.renderBoard(core.renderLayers(plotResult));

        const top = boards.get('top');
        const bottom = boards.get('bottom');
        if (top) top.innerHTML = core.stringifySvg(boardResult.top);
        if (bottom) bottom.innerHTML = core.stringifySvg(boardResult.bottom);

        if (stage) stage.hidden = false;
        setStatus('', 'alert-success');
        showSide('top');
    }

    for (const button of sideButtons) {
        button.addEventListener('click', () => showSide(button.dataset.gerberSide));
    }

    render().catch((err) => {
        if (stage) stage.hidden = true;
        setStatus(`Preview failed: ${err?.message || err}`, 'alert-danger');
    });
})();
