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

    // Run after the DOM is ready; site.js is loaded at the end of <body> so this is effectively immediate.
    highlightAllCode();
})();
