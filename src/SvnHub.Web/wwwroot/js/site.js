// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
    const languageAliases = new Map([
        ['cs', 'csharp'],
        ['c#', 'csharp'],
        ['c++', 'cpp'],
        ['cc', 'cpp'],
        ['cxx', 'cpp'],
        ['hpp', 'cpp'],
        ['hh', 'cpp'],
        ['hxx', 'cpp'],
        ['h', 'c'],
        ['sv', 'verilog'],
        ['systemverilog', 'verilog'],
    ]);

    const languageLoadCache = new Map();

    function getRequestedLanguage(codeEl) {
        if (!codeEl || !codeEl.classList) return null;
        for (const cls of codeEl.classList) {
            if (cls.startsWith('language-') && cls.length > 'language-'.length) {
                const raw = cls.substring('language-'.length);
                return raw || null;
            }
        }
        return null;
    }

    function normalizeLanguage(lang) {
        if (!lang) return null;
        const lower = String(lang).trim().toLowerCase();
        return languageAliases.get(lower) || lower;
    }

    function loadLanguage(lang) {
        const hljs = window.hljs;
        if (!hljs || !lang) return Promise.resolve(false);

        if (hljs.getLanguage && hljs.getLanguage(lang)) {
            return Promise.resolve(true);
        }

        // Some IDs are built-in or don't need loading.
        if (lang === 'plaintext' || lang === 'text' || lang === 'nohighlight') {
            return Promise.resolve(true);
        }

        if (languageLoadCache.has(lang)) {
            return languageLoadCache.get(lang);
        }

        const promise = new Promise((resolve) => {
            const script = document.createElement('script');
            script.src = `/lib/highlightjs/languages/${encodeURIComponent(lang)}.min.js`;
            script.async = true;
            script.onload = () => resolve(true);
            script.onerror = () => resolve(false);
            document.head.appendChild(script);
        }).finally(() => {
            // Keep cache entry; prevents repeated failing loads.
        });

        languageLoadCache.set(lang, promise);
        return promise;
    }

    function highlightAllCode() {
        const hljs = window.hljs;
        if (!hljs || typeof hljs.highlightElement !== 'function') return;

        const blocks = Array.from(document.querySelectorAll('pre code'));

        const langs = new Set();
        for (const block of blocks) {
            const requested = normalizeLanguage(getRequestedLanguage(block));
            if (requested) {
                langs.add(requested);
            }
        }

        const loads = Array.from(langs).map(loadLanguage);

        Promise.all(loads).finally(() => {
            for (const block of blocks) {
                try {
                    hljs.highlightElement(block);
                } catch {
                    // ignore
                }
            }
        });
    }

    async function copyText(text) {
        if (navigator.clipboard && navigator.clipboard.writeText) {
            await navigator.clipboard.writeText(text ?? '');
            return;
        }

        const ta = document.createElement('textarea');
        ta.value = text ?? '';
        ta.setAttribute('readonly', '');
        ta.style.position = 'fixed';
        ta.style.left = '-10000px';
        ta.style.top = '-10000px';
        document.body.appendChild(ta);
        ta.focus();
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
    }

    function setMsg(el, text) {
        const targetSelector = el.getAttribute('data-copy-msg');
        if (!targetSelector) return;

        const target = document.querySelector(targetSelector);
        if (!target) return;

        target.textContent = text || '';
        if (text) {
            window.setTimeout(() => {
                if (target.textContent === text) target.textContent = '';
            }, 1500);
        }
    }

    document.addEventListener('click', async (e) => {
        const el = e.target.closest('[data-copy-text],[data-copy-from]');
        if (!el) return;
        if (el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true') return;

        let text = el.getAttribute('data-copy-text');
        if (!text) {
            const from = el.getAttribute('data-copy-from');
            if (from) {
                const src = document.querySelector(from);
                if (src) {
                    text = ('value' in src) ? (src.value ?? '') : (src.textContent ?? '');
                }
            }
        }

        try {
            await copyText(text ?? '');
            setMsg(el, 'Copied');
        } catch (err) {
            setMsg(el, 'Copy failed');
        }
    });

    function pad2(value) {
        return String(value).padStart(2, '0');
    }

    function formatLocalDateTime(date) {
        return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())} ${pad2(date.getHours())}:${pad2(date.getMinutes())}`;
    }

    function formatUtcDateTime(date) {
        return `${date.getUTCFullYear()}-${pad2(date.getUTCMonth() + 1)}-${pad2(date.getUTCDate())} ${pad2(date.getUTCHours())}:${pad2(date.getUTCMinutes())} UTC`;
    }

    function initLocalDateTimes() {
        const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone || 'Local time';
        document.querySelectorAll('[data-local-datetime]').forEach((el) => {
            const value = el.getAttribute('datetime');
            if (!value) return;

            const date = new Date(value);
            if (Number.isNaN(date.getTime())) return;

            const local = formatLocalDateTime(date);
            const utc = formatUtcDateTime(date);
            const relative = el.getAttribute('data-relative');

            el.textContent = local;
            el.title = `Local: ${local} ${timeZone}\nUTC: ${utc}${relative ? `\nRelative: ${relative}` : ''}`;
        });
    }

    function normalizeLabel(value) {
        return String(value || '').trim().split(/\s+/).filter(Boolean).join(' ');
    }

    function splitLabels(value) {
        return String(value || '')
            .split(/[,\n\r;]+/)
            .map(normalizeLabel)
            .filter(Boolean);
    }

    function initLabelEditor(editor) {
        const hidden = editor.querySelector('[data-label-editor-value]');
        const input = editor.querySelector('[data-label-editor-input]');
        const tokensEl = editor.querySelector('[data-label-editor-tokens]');
        const errorEl = editor.querySelector('[data-label-editor-error]');
        const suggestionsEl = editor.querySelector('[data-label-editor-suggestions-list]');
        if (!hidden || !input || !tokensEl) return;

        let labels = [];
        let suggestions = [];
        let visibleSuggestions = [];
        let activeSuggestionIndex = -1;
        let suggestionsOpen = false;

        try {
            suggestions = JSON.parse(editor.getAttribute('data-label-editor-suggestions') || '[]')
                .map(normalizeLabel)
                .filter(Boolean);
        } catch {
            suggestions = [];
        }

        function setError(message) {
            if (errorEl) errorEl.textContent = message || '';
        }

        function sync() {
            hidden.value = labels.join(', ');
        }

        function render() {
            editor.classList.toggle('label-editor-has-labels', labels.length > 0);
            tokensEl.replaceChildren();
            for (const label of labels) {
                const token = document.createElement('span');
                token.className = 'label-editor-token';

                const text = document.createElement('span');
                text.className = 'label-editor-token-text';
                text.textContent = label;
                token.appendChild(text);

                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'label-editor-remove';
                remove.setAttribute('aria-label', `Remove ${label}`);
                remove.textContent = 'x';
                remove.addEventListener('click', () => {
                    labels = labels.filter(item => item !== label);
                    sync();
                    render();
                    input.focus();
                    renderSuggestions();
                });
                token.appendChild(remove);
                tokensEl.appendChild(token);
            }
            if (suggestionsOpen) {
                renderSuggestions();
            }
        }

        function hideSuggestions() {
            suggestionsOpen = false;
            visibleSuggestions = [];
            activeSuggestionIndex = -1;
            input.setAttribute('aria-expanded', 'false');
            if (suggestionsEl) {
                suggestionsEl.hidden = true;
                suggestionsEl.replaceChildren();
            }
        }

        function renderSuggestions() {
            if (!suggestionsEl) return;
            suggestionsOpen = true;

            const query = normalizeLabel(input.value).toLowerCase();
            visibleSuggestions = suggestions
                .filter(label => !labels.some(item => item.toLowerCase() === label.toLowerCase()))
                .filter(label => query.length === 0 || label.toLowerCase().includes(query))
                .slice(0, 8);

            suggestionsEl.replaceChildren();
            activeSuggestionIndex = visibleSuggestions.length > 0
                ? Math.min(Math.max(activeSuggestionIndex, 0), visibleSuggestions.length - 1)
                : -1;

            if (visibleSuggestions.length === 0) {
                hideSuggestions();
                return;
            }

            visibleSuggestions.forEach((label, index) => {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = `label-editor-suggestion${index === activeSuggestionIndex ? ' active' : ''}`;
                button.textContent = label;
                button.addEventListener('mousedown', event => event.preventDefault());
                button.addEventListener('click', () => {
                    if (addFromSuggestion(label)) {
                        input.focus();
                    }
                });
                suggestionsEl.appendChild(button);
            });

            suggestionsEl.hidden = false;
            input.setAttribute('aria-expanded', 'true');
        }

        function canAdd(label) {
            if (!label) return false;
            if (label.length > 40) {
                setError('Label is too long. Use at most 40 characters.');
                return false;
            }
            if (/[\\/,;]/.test(label) || Array.from(label).some(ch => ch.charCodeAt(0) < 32 || ch.charCodeAt(0) === 127)) {
                setError('Label contains unsupported characters.');
                return false;
            }
            if (labels.some(item => item.toLowerCase() === label.toLowerCase())) {
                setError('');
                return false;
            }
            if (labels.length >= 20) {
                setError('Use at most 20 labels.');
                return false;
            }
            return true;
        }

        function addFromSuggestion(label) {
            if (!canAdd(label)) return false;
            labels.push(label);
            input.value = '';
            setError('');
            sync();
            render();
            renderSuggestions();
            return true;
        }

        function addFromText(value) {
            let changed = false;
            for (const raw of splitLabels(value)) {
                const label = normalizeLabel(raw);
                if (canAdd(label)) {
                    labels.push(label);
                    changed = true;
                }
            }
            if (changed) {
                setError('');
                sync();
                render();
            }
            return changed;
        }

        labels = [];
        addFromText(hidden.value);
        input.value = '';

        input.addEventListener('keydown', (event) => {
            if (event.key === 'ArrowDown' && visibleSuggestions.length > 0) {
                event.preventDefault();
                activeSuggestionIndex = (activeSuggestionIndex + 1) % visibleSuggestions.length;
                renderSuggestions();
                return;
            }

            if (event.key === 'ArrowUp' && visibleSuggestions.length > 0) {
                event.preventDefault();
                activeSuggestionIndex = (activeSuggestionIndex - 1 + visibleSuggestions.length) % visibleSuggestions.length;
                renderSuggestions();
                return;
            }

            if (event.key === 'Escape') {
                hideSuggestions();
                return;
            }

            if (event.key === 'Enter' || event.key === ',' || event.key === ';') {
                event.preventDefault();
                if (event.key === 'Enter' && visibleSuggestions.length > 0 && activeSuggestionIndex >= 0) {
                    addFromSuggestion(visibleSuggestions[activeSuggestionIndex]);
                } else if (addFromText(input.value)) {
                    input.value = '';
                }
            } else if (event.key === 'Backspace' && input.value === '' && labels.length > 0) {
                labels.pop();
                sync();
                render();
            }
        });

        input.addEventListener('input', renderSuggestions);
        input.addEventListener('focus', renderSuggestions);
        input.addEventListener('click', () => {
            if (!suggestionsOpen) {
                renderSuggestions();
            }
        });

        const control = editor.querySelector('[data-label-editor-control]');
        if (control) {
            control.addEventListener('click', (event) => {
                if (event.target.closest('.label-editor-remove')) {
                    return;
                }

                input.focus();
                renderSuggestions();
            });
        }

        input.addEventListener('blur', () => {
            if (addFromText(input.value)) {
                input.value = '';
            }
            window.setTimeout(hideSuggestions, 120);
        });

        input.addEventListener('paste', (event) => {
            const text = event.clipboardData?.getData('text');
            if (!text || !/[,\n\r;]/.test(text)) return;
            event.preventDefault();
            if (addFromText(text)) {
                input.value = '';
            }
        });

        const form = editor.closest('form');
        if (form) {
            form.addEventListener('submit', () => {
                if (addFromText(input.value)) {
                    input.value = '';
                }
                sync();
            });
        }
    }

    document.querySelectorAll('[data-label-editor]').forEach(initLabelEditor);
    initLocalDateTimes();

    const registrationForm = document.querySelector('[data-forget-repository-registrations]');
    if (registrationForm) {
        const selectAll = document.querySelector('[data-repository-registration-select-all]');
        const selections = Array.from(document.querySelectorAll('[data-repository-registration-select]'));
        const submit = registrationForm.querySelector('[data-forget-repository-registrations-button]');

        const updateRegistrationSelection = () => {
            const selectedCount = selections.filter(checkbox => checkbox.checked).length;
            submit.disabled = selectedCount === 0;
            submit.textContent = selectedCount === 0
                ? 'Forget selected'
                : `Forget selected (${selectedCount})`;

            selectAll.checked = selections.length > 0 && selectedCount === selections.length;
            selectAll.indeterminate = selectedCount > 0 && selectedCount < selections.length;
        };

        selectAll.addEventListener('change', () => {
            selections.forEach(checkbox => {
                checkbox.checked = selectAll.checked;
            });
            updateRegistrationSelection();
        });
        selections.forEach(checkbox => checkbox.addEventListener('change', updateRegistrationSelection));

        registrationForm.addEventListener('submit', event => {
            const selectedCount = selections.filter(checkbox => checkbox.checked).length;
            if (selectedCount === 0 || !window.confirm(
                `Forget ${selectedCount} selected registration(s)? Labels, grants, and indexed metadata will be removed. Files on disk will not be touched.`)) {
                event.preventDefault();
            }
        });

        updateRegistrationSelection();
    }

    // Run after the DOM is ready; site.js is loaded at the end of <body> so this is effectively immediate.
    highlightAllCode();
})();
