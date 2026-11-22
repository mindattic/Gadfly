// Keyboard reach for tables that scroll sideways (GAD-T507, WCAG 2.1.1 and 1.4.10). On a narrow screen a wide data table
// scrolls inside its own box rather than pushing the page wider; a box like that with no link or button in it can't be
// scrolled from the keyboard in every browser, so this gives it tabindex="0", a region role and a name taken from the
// table's caption or the nearest heading. Only while it actually overflows: at desktop width nothing changes. Runs on load,
// after Blazor re-renders (a debounced MutationObserver) and on resize. No dependencies, nothing sent anywhere.
(function () {
    'use strict';
    var SELECTOR = '.grid, .table-scroll, .gr-table-wrap, .help-table, .prose .table-scroll';
    var FOCUSABLE = 'a[href], button, input, select, textarea, summary, [tabindex]:not([tabindex="-1"])';
    var pending = false;

    function nameFor(el) {
        var table = el.tagName === 'TABLE' ? el : el.querySelector('table');
        var caption = table && table.querySelector('caption');
        if (caption && caption.textContent.trim()) return caption.textContent.trim();
        var node = el;
        while (node && node !== document.body) {
            var prev = node.previousElementSibling;
            while (prev) {
                if (/^H[1-6]$/.test(prev.tagName)) return prev.textContent.trim();
                prev = prev.previousElementSibling;
            }
            node = node.parentElement;
        }
        return 'Table';
    }

    function update() {
        pending = false;
        var boxes = document.querySelectorAll(SELECTOR);
        for (var i = 0; i < boxes.length; i++) {
            var el = boxes[i];
            // A .grid inside a scrolling wrapper is handled by the wrapper.
            if (el.parentElement && el.parentElement.closest('[data-scroll-region]') && !el.hasAttribute('data-scroll-region')) continue;
            var overflows = el.scrollWidth > el.clientWidth + 1;
            var reachable = el.querySelector(FOCUSABLE) !== null;
            if (overflows && !reachable) {
                if (!el.hasAttribute('data-scroll-region')) {
                    el.setAttribute('data-scroll-region', '');
                    el.setAttribute('tabindex', '0');
                    if (el.tagName !== 'TABLE') el.setAttribute('role', 'region');
                    if (!el.hasAttribute('aria-label') && el.tagName !== 'TABLE') el.setAttribute('aria-label', nameFor(el) + ' (scrolls sideways)');
                }
            } else if (el.hasAttribute('data-scroll-region')) {
                el.removeAttribute('data-scroll-region');
                el.removeAttribute('tabindex');
                if (el.getAttribute('role') === 'region') el.removeAttribute('role');
                if (/\(scrolls sideways\)$/.test(el.getAttribute('aria-label') || '')) el.removeAttribute('aria-label');
            }
        }
    }

    function schedule() {
        if (pending) return;
        pending = true;
        (window.requestAnimationFrame || setTimeout)(update);
    }

    function start() {
        update();
        new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
        window.addEventListener('resize', schedule);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start);
    else start();
})();
