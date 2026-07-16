(() => {
    const root = document.querySelector('[data-spreadsheet-preview]');
    if (!root) return;

    const status = root.querySelector('[data-spreadsheet-status]');
    const viewer = root.querySelector('[data-spreadsheet-viewer]');
    const summary = root.querySelector('[data-spreadsheet-summary]');
    const url = root.getAttribute('data-spreadsheet-url');
    const COLUMN_INDEX_WIDTH = 60;
    const DEFAULT_COLUMN_WIDTH = 100;
    const MINIMUM_COLUMN_WIDTH = 48;
    const DEFAULT_FONT = { name: 'Arial', size: 10, bold: false, italic: false };
    const DEFAULT_ROW_HEIGHT = 25;

    function setStatus(text, variant = 'alert-secondary') {
        if (!status) return;
        status.textContent = text || '';
        status.classList.remove('alert-secondary', 'alert-danger', 'alert-success');
        status.classList.add(variant);
        status.hidden = !text;
    }

    function getRange(worksheet) {
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

    function normalizeColor(color) {
        const raw = color?.rgb;
        if (typeof raw !== 'string') return null;

        const value = raw.replace(/^#/, '');
        if (!/^[0-9a-f]{6}([0-9a-f]{2})?$/i.test(value)) return null;
        return `#${value.slice(-6)}`;
    }

    function getCellStyle(cell) {
        const source = cell?.s;
        if (!source) return null;

        const style = {};
        const background = normalizeColor(source.fgColor);
        const color = normalizeColor(source.font?.color);
        if (background) style.bgcolor = background;
        if (color) style.color = color;

        if (source.alignment) {
            if (['left', 'center', 'right'].includes(source.alignment.horizontal)) {
                style.align = source.alignment.horizontal;
            }
            if (['top', 'middle', 'bottom'].includes(source.alignment.vertical)) {
                style.valign = source.alignment.vertical;
            }
            if (source.alignment.wrapText) style.textwrap = true;
        }

        if (source.font) {
            const font = {};
            if (source.font.name) font.name = source.font.name;
            if (Number.isFinite(source.font.sz)) font.size = source.font.sz;
            if (source.font.bold) font.bold = true;
            if (source.font.italic) font.italic = true;
            if (Object.keys(font).length > 0) style.font = font;
            if (source.font.underline) style.underline = true;
            if (source.font.strike) style.strike = true;
        }

        return Object.keys(style).length > 0 ? style : null;
    }

    function getStyleIndex(style, styles, styleIndexes) {
        if (!style) return undefined;

        const key = JSON.stringify(style);
        if (styleIndexes.has(key)) return styleIndexes.get(key);

        const index = styles.length;
        styles.push(style);
        styleIndexes.set(key, index);
        return index;
    }

    function getColumnWidth(column) {
        if (!column) return DEFAULT_COLUMN_WIDTH;
        if (Number.isFinite(column.wpx)) return column.wpx;
        if (Number.isFinite(column.wch)) return (column.wch * 9) + 12;
        return DEFAULT_COLUMN_WIDTH;
    }

    function fitColumnWidths(sheet, viewportWidth) {
        const availableWidth = Math.max(0, Math.floor(viewportWidth - COLUMN_INDEX_WIDTH - 2));
        const widths = Array.from(
            { length: sheet.cols.len },
            (_, index) => sheet.cols[index]?.width ?? DEFAULT_COLUMN_WIDTH);
        const totalWidth = widths.reduce((total, width) => total + width, 0);
        if (totalWidth <= availableWidth || widths.length * MINIMUM_COLUMN_WIDTH > availableWidth) return;

        const remainingIndexes = new Set(widths.map((_, index) => index));
        let remainingWidth = availableWidth;

        while (remainingIndexes.size > 0) {
            const sourceWidth = [...remainingIndexes].reduce((total, index) => total + widths[index], 0);
            const scale = remainingWidth / sourceWidth;
            const constrainedIndexes = [...remainingIndexes]
                .filter(index => widths[index] * scale < MINIMUM_COLUMN_WIDTH);

            if (constrainedIndexes.length === 0) {
                for (const index of remainingIndexes) {
                    sheet.cols[index] = { width: Math.floor(widths[index] * scale) };
                }
                break;
            }

            for (const index of constrainedIndexes) {
                sheet.cols[index] = { width: MINIMUM_COLUMN_WIDTH };
                remainingIndexes.delete(index);
                remainingWidth -= MINIMUM_COLUMN_WIDTH;
            }
        }
    }

    function getFontPixelSize(pointSize) {
        return Math.round((pointSize || DEFAULT_FONT.size) * 4 / 3);
    }

    function getWrappedLineCount(context, text, width) {
        let lineCount = 0;

        for (const sourceLine of String(text).split('\n')) {
            if (context.measureText(sourceLine).width <= width) {
                lineCount += 1;
                continue;
            }

            let lineWidth = 0;
            let wrappedLines = 0;
            for (const character of sourceLine) {
                if (lineWidth >= width) {
                    wrappedLines += 1;
                    lineWidth = 0;
                }
                lineWidth += context.measureText(character).width + 1;
            }
            lineCount += Math.max(1, wrappedLines + 1);
        }

        return Math.max(1, lineCount);
    }

    function getCellWidth(sheet, columnIndex, cell) {
        const mergedColumns = Array.isArray(cell.merge) ? cell.merge[1] : 0;
        let width = 0;
        for (let index = columnIndex; index <= columnIndex + mergedColumns; index += 1) {
            width += sheet.cols[index]?.width ?? DEFAULT_COLUMN_WIDTH;
        }
        return Math.max(1, width - 12);
    }

    function autoFitWrappedRows(sheet) {
        const context = document.createElement('canvas').getContext('2d');
        if (!context) return;

        const styleIndexes = new Map(sheet.styles.map((style, index) => [JSON.stringify(style), index]));
        for (let rowIndex = 0; rowIndex < sheet.rows.len; rowIndex += 1) {
            const row = sheet.rows[rowIndex];
            if (!row?.cells || Number.isFinite(row.height)) continue;

            let requiredHeight = DEFAULT_ROW_HEIGHT;
            for (const [columnKey, cell] of Object.entries(row.cells)) {
                if (!cell?.text) continue;

                const sourceStyle = cell.style === undefined ? {} : sheet.styles[cell.style];
                const font = { ...DEFAULT_FONT, ...(sourceStyle?.font || {}) };
                const fontPixels = getFontPixelSize(font.size);
                context.font = `${font.italic ? 'italic ' : ''}${font.bold ? 'bold ' : ''}${fontPixels}px ${font.name}`;

                const lineCount = getWrappedLineCount(
                    context,
                    cell.text,
                    getCellWidth(sheet, Number(columnKey), cell));
                if (lineCount <= 1) continue;

                const wrappedStyle = { ...(sourceStyle || {}), textwrap: true };
                cell.style = getStyleIndex(wrappedStyle, sheet.styles, styleIndexes);
                requiredHeight = Math.max(requiredHeight, (lineCount * (fontPixels + 2)) + 10);
            }

            if (requiredHeight > DEFAULT_ROW_HEIGHT) row.height = requiredHeight;
        }
    }

    function enrichWorksheet(sheet, worksheet) {
        const range = getRange(worksheet);
        const rows = sheet.rows || (sheet.rows = {});
        rows.len = Math.max(rows.len || 0, 1, (range?.e.r ?? 0) + 1);
        const cols = sheet.cols || (sheet.cols = {});
        cols.len = Math.max(cols.len || 0, 1, (range?.e.c ?? 0) + 1);
        const styles = [];
        const styleIndexes = new Map();

        if (range) {
            for (let rowIndex = range.s.r; rowIndex <= range.e.r; rowIndex += 1) {
                const row = rows[rowIndex] || (rows[rowIndex] = { cells: {} });
                const cells = row.cells || (row.cells = {});
                for (let columnIndex = range.s.c; columnIndex <= range.e.c; columnIndex += 1) {
                    const address = window.XLSX.utils.encode_cell({ r: rowIndex, c: columnIndex });
                    const source = worksheet[address];
                    if (!source) continue;

                    const cell = cells[columnIndex] || (cells[columnIndex] = {});
                    // The preview shows the workbook's displayed value, while the
                    // official adapter preserves formulas for editable grids.
                    cell.text = formatCell(source);
                    const styleIndex = getStyleIndex(getCellStyle(source), styles, styleIndexes);
                    if (styleIndex !== undefined) cell.style = styleIndex;
                }

                const configuredHeight = worksheet['!rows']?.[rowIndex]?.hpx;
                if (Number.isFinite(configuredHeight)) {
                    row.height = Math.max(20, Math.min(500, configuredHeight));
                }
            }
        }

        for (let columnIndex = 0; columnIndex < cols.len; columnIndex += 1) {
            const width = getColumnWidth(worksheet['!cols']?.[columnIndex]);
            cols[columnIndex] = { width: Math.max(MINIMUM_COLUMN_WIDTH, Math.min(800, width)) };
        }

        sheet.styles = styles;
        return sheet;
    }

    function buildSpreadsheetData(workbook) {
        const sheets = window.stox(workbook);
        const sheetsByName = new Map(sheets.map(sheet => [sheet.name, sheet]));

        return workbook.SheetNames
            .map(name => {
                const sheet = sheetsByName.get(name) || { name, rows: { len: 1 }, merges: [] };
                return enrichWorksheet(sheet, workbook.Sheets[name]);
            });
    }

    function getWorkbookSummary(workbook) {
        const sheets = workbook.SheetNames.length;
        let populatedRows = 0;
        let populatedColumns = 0;

        for (const name of workbook.SheetNames) {
            const range = getRange(workbook.Sheets[name]);
            if (!range) continue;
            populatedRows += range.e.r - range.s.r + 1;
            populatedColumns = Math.max(populatedColumns, range.e.c - range.s.c + 1);
        }

        return `${sheets.toLocaleString()} sheet${sheets === 1 ? '' : 's'} | `
            + `${populatedRows.toLocaleString()} populated rows | `
            + `up to ${populatedColumns.toLocaleString()} columns`;
    }

    async function load() {
        if (!url || !window.XLSX || !window.stox || !window.x_spreadsheet || !viewer) {
            setStatus('Preview failed: spreadsheet viewer assets are not available.', 'alert-danger');
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
            const workbook = window.XLSX.read(bytes, {
                type: 'array',
                cellDates: true,
                cellFormula: true,
                cellNF: true,
                cellStyles: true,
            });

            if (!Array.isArray(workbook.SheetNames) || workbook.SheetNames.length === 0) {
                throw new Error('The workbook contains no worksheets.');
            }

            viewer.hidden = false;
            const workbookData = buildSpreadsheetData(workbook);
            for (const sheet of workbookData) {
                fitColumnWidths(sheet, viewer.clientWidth);
                autoFitWrappedRows(sheet);
            }
            window.x_spreadsheet(viewer, {
                mode: 'read',
                showToolbar: false,
                showContextmenu: false,
                showBottomBar: workbookData.length > 1,
                view: {
                    width: () => viewer.clientWidth,
                    height: () => viewer.clientHeight,
                },
                row: { len: 100, height: DEFAULT_ROW_HEIGHT },
                col: {
                    len: 26,
                    width: DEFAULT_COLUMN_WIDTH,
                    indexWidth: COLUMN_INDEX_WIDTH,
                    minWidth: MINIMUM_COLUMN_WIDTH,
                },
            }).loadData(workbookData);

            if (summary) {
                summary.textContent = getWorkbookSummary(workbook);
                summary.hidden = false;
            }
            setStatus('', 'alert-success');
        } catch (error) {
            console.error(error);
            viewer.hidden = true;
            setStatus(`Preview failed: ${error?.message || error}`, 'alert-danger');
        }
    }

    load();
})();
