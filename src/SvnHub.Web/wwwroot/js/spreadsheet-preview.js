(() => {
    const root = document.querySelector('[data-spreadsheet-preview]');
    if (!root) return;

    const ROWS_PER_PAGE = 200;
    const MAX_COLUMNS = 100;
    const status = root.querySelector('[data-spreadsheet-status]');
    const tabs = root.querySelector('[data-spreadsheet-tabs]');
    const viewport = root.querySelector('[data-spreadsheet-viewport]');
    const grid = root.querySelector('[data-spreadsheet-grid]');
    const pager = root.querySelector('[data-spreadsheet-pager]');
    const pageLabel = root.querySelector('[data-spreadsheet-page]');
    const previousButton = root.querySelector('[data-spreadsheet-previous]');
    const nextButton = root.querySelector('[data-spreadsheet-next]');
    const summary = root.querySelector('[data-spreadsheet-summary]');
    const url = root.getAttribute('data-spreadsheet-url');
    let workbook = null;
    let activeSheetIndex = 0;
    let pageIndex = 0;

    function setStatus(text, variant = 'alert-secondary') {
        if (!status) return;
        status.textContent = text || '';
        status.classList.remove('alert-secondary', 'alert-danger', 'alert-success');
        status.classList.add(variant);
        status.hidden = !text;
    }

    function getWorksheetRange(worksheet) {
        if (!worksheet?.['!ref']) return null;

        try {
            return window.XLSX.utils.decode_range(worksheet['!ref']);
        } catch {
            return null;
        }
    }

    function formatCell(cell) {
        if (!cell) return '';
        if (cell.w !== undefined && cell.w !== null) return String(cell.w);

        try {
            return String(window.XLSX.utils.format_cell(cell) ?? '');
        } catch {
            return cell.v === undefined || cell.v === null ? '' : String(cell.v);
        }
    }

    function getSafeHyperlink(cell) {
        const target = cell?.l?.Target;
        if (typeof target !== 'string') return null;

        try {
            const parsed = new URL(target, window.location.href);
            return ['http:', 'https:', 'mailto:'].includes(parsed.protocol) ? parsed.href : null;
        } catch {
            return null;
        }
    }

    function getColumnWidth(worksheet, columnIndex) {
        const column = worksheet['!cols']?.[columnIndex];
        if (!column) return 112;
        if (Number.isFinite(column.wpx)) return Math.max(48, Math.min(360, column.wpx));
        if (Number.isFinite(column.wch)) return Math.max(48, Math.min(360, (column.wch * 7) + 12));
        return 112;
    }

    function buildVisibleMerges(worksheet, startRow, endRow, startColumn, endColumn) {
        const coveredCells = new Set();
        const anchors = new Map();

        for (const merge of worksheet['!merges'] || []) {
            const visibleStartRow = Math.max(startRow, merge.s.r);
            const visibleEndRow = Math.min(endRow, merge.e.r);
            const visibleStartColumn = Math.max(startColumn, merge.s.c);
            const visibleEndColumn = Math.min(endColumn, merge.e.c);
            if (visibleStartRow > visibleEndRow || visibleStartColumn > visibleEndColumn) continue;

            const anchorKey = `${visibleStartRow}:${visibleStartColumn}`;
            anchors.set(anchorKey, {
                rowSpan: visibleEndRow - visibleStartRow + 1,
                columnSpan: visibleEndColumn - visibleStartColumn + 1,
                sourceRow: merge.s.r,
                sourceColumn: merge.s.c,
            });

            for (let row = visibleStartRow; row <= visibleEndRow; row += 1) {
                for (let column = visibleStartColumn; column <= visibleEndColumn; column += 1) {
                    const key = `${row}:${column}`;
                    if (key !== anchorKey) coveredCells.add(key);
                }
            }
        }

        return { coveredCells, anchors };
    }

    function appendCellContent(element, cell) {
        const text = formatCell(cell);
        const hyperlink = getSafeHyperlink(cell);
        if (hyperlink && text) {
            const link = document.createElement('a');
            link.href = hyperlink;
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            link.textContent = text;
            element.append(link);
        } else {
            element.textContent = text;
        }

        const details = [];
        if (typeof cell?.f === 'string') details.push(`=${cell.f}`);
        for (const comment of cell?.c || []) {
            if (comment?.t) details.push(comment.t);
        }
        if (details.length > 0) element.title = details.join('\n\n');
    }

    function renderTabs() {
        if (!tabs || !workbook) return;
        tabs.textContent = '';
        tabs.hidden = workbook.SheetNames.length <= 1;
        tabs.setAttribute('role', 'tablist');

        workbook.SheetNames.forEach((name, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `spreadsheet-sheet-tab${index === activeSheetIndex ? ' active' : ''}`;
            button.textContent = name;
            button.title = name;
            button.setAttribute('role', 'tab');
            button.setAttribute('aria-selected', index === activeSheetIndex ? 'true' : 'false');
            button.addEventListener('click', () => {
                if (activeSheetIndex === index) return;
                activeSheetIndex = index;
                pageIndex = 0;
                renderTabs();
                renderSheet();
            });
            tabs.append(button);
        });
    }

    function renderEmptySheet(sheetName) {
        grid.textContent = '';
        const body = document.createElement('tbody');
        const row = document.createElement('tr');
        const cell = document.createElement('td');
        cell.className = 'spreadsheet-empty-sheet';
        cell.textContent = 'This sheet is empty.';
        row.append(cell);
        body.append(row);
        grid.append(body);
        pager.hidden = true;
        summary.hidden = false;
        summary.textContent = `${sheetName} | Empty sheet`;
    }

    function renderSheet() {
        if (!workbook || !grid || !viewport || !pager || !summary) return;

        const sheetName = workbook.SheetNames[activeSheetIndex];
        const worksheet = workbook.Sheets[sheetName];
        const range = getWorksheetRange(worksheet);
        viewport.hidden = false;
        if (!range) {
            renderEmptySheet(sheetName);
            return;
        }

        const totalRows = range.e.r - range.s.r + 1;
        const totalColumns = range.e.c - range.s.c + 1;
        const pageCount = Math.max(1, Math.ceil(totalRows / ROWS_PER_PAGE));
        pageIndex = Math.max(0, Math.min(pageIndex, pageCount - 1));
        const startRow = range.s.r + (pageIndex * ROWS_PER_PAGE);
        const endRow = Math.min(range.e.r, startRow + ROWS_PER_PAGE - 1);
        const startColumn = range.s.c;
        const endColumn = Math.min(range.e.c, startColumn + MAX_COLUMNS - 1);
        const { coveredCells, anchors } = buildVisibleMerges(
            worksheet,
            startRow,
            endRow,
            startColumn,
            endColumn);

        grid.textContent = '';
        const columnGroup = document.createElement('colgroup');
        const rowNumberColumn = document.createElement('col');
        rowNumberColumn.className = 'spreadsheet-row-number-column';
        columnGroup.append(rowNumberColumn);
        for (let column = startColumn; column <= endColumn; column += 1) {
            const columnElement = document.createElement('col');
            columnElement.style.width = `${getColumnWidth(worksheet, column)}px`;
            columnGroup.append(columnElement);
        }
        grid.append(columnGroup);

        const head = document.createElement('thead');
        const headerRow = document.createElement('tr');
        const corner = document.createElement('th');
        corner.className = 'spreadsheet-corner';
        corner.setAttribute('aria-hidden', 'true');
        headerRow.append(corner);
        for (let column = startColumn; column <= endColumn; column += 1) {
            const header = document.createElement('th');
            header.scope = 'col';
            header.textContent = window.XLSX.utils.encode_col(column);
            headerRow.append(header);
        }
        head.append(headerRow);
        grid.append(head);

        const body = document.createElement('tbody');
        for (let row = startRow; row <= endRow; row += 1) {
            const tableRow = document.createElement('tr');
            const configuredHeight = worksheet['!rows']?.[row]?.hpx;
            if (Number.isFinite(configuredHeight)) {
                tableRow.style.height = `${Math.max(20, Math.min(240, configuredHeight))}px`;
            }

            const rowHeader = document.createElement('th');
            rowHeader.scope = 'row';
            rowHeader.className = 'spreadsheet-row-number';
            rowHeader.textContent = String(row + 1);
            tableRow.append(rowHeader);

            for (let column = startColumn; column <= endColumn; column += 1) {
                const key = `${row}:${column}`;
                if (coveredCells.has(key)) continue;

                const merge = anchors.get(key);
                const sourceRow = merge?.sourceRow ?? row;
                const sourceColumn = merge?.sourceColumn ?? column;
                const address = window.XLSX.utils.encode_cell({ r: sourceRow, c: sourceColumn });
                const cell = worksheet[address];
                const tableCell = document.createElement('td');
                if (merge?.rowSpan > 1) tableCell.rowSpan = merge.rowSpan;
                if (merge?.columnSpan > 1) tableCell.colSpan = merge.columnSpan;
                if (cell?.t === 'n' || cell?.t === 'd') tableCell.classList.add('spreadsheet-cell-number');
                appendCellContent(tableCell, cell);
                tableRow.append(tableCell);
            }
            body.append(tableRow);
        }
        grid.append(body);

        pager.hidden = pageCount <= 1;
        previousButton.disabled = pageIndex === 0;
        nextButton.disabled = pageIndex >= pageCount - 1;
        pageLabel.textContent = `Rows ${startRow + 1}-${endRow + 1} of ${range.e.r + 1}`;

        const details = [
            sheetName,
            `${totalRows.toLocaleString()} rows`,
            `${totalColumns.toLocaleString()} columns`,
        ];
        if (totalColumns > MAX_COLUMNS) {
            details.push(`showing first ${MAX_COLUMNS} columns`);
        }
        summary.hidden = false;
        summary.textContent = details.join(' | ');
        viewport.scrollTo({ top: 0, left: 0 });
    }

    async function load() {
        if (!url || !window.XLSX) {
            setStatus('Preview failed: SheetJS is not available.', 'alert-danger');
            return;
        }

        try {
            setStatus('Loading workbook...', 'alert-secondary');
            const response = await fetch(url, { credentials: 'same-origin' });
            if (!response.ok) {
                throw new Error((await response.text()) || `HTTP ${response.status}`);
            }

            const bytes = await response.arrayBuffer();
            setStatus('Reading workbook...', 'alert-secondary');
            workbook = window.XLSX.read(bytes, {
                type: 'array',
                cellDates: true,
                cellFormula: true,
                cellNF: true,
                cellStyles: true,
            });

            if (!Array.isArray(workbook.SheetNames) || workbook.SheetNames.length === 0) {
                throw new Error('The workbook contains no worksheets.');
            }

            renderTabs();
            renderSheet();
            setStatus('', 'alert-success');
        } catch (error) {
            console.error(error);
            setStatus(`Preview failed: ${error?.message || error}`, 'alert-danger');
        }
    }

    previousButton?.addEventListener('click', () => {
        if (pageIndex <= 0) return;
        pageIndex -= 1;
        renderSheet();
    });
    nextButton?.addEventListener('click', () => {
        pageIndex += 1;
        renderSheet();
    });

    load();
})();
