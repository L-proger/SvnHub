// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

(() => {
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
})();
