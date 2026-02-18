// router.js — Lightweight SPA router for Reef
// Intercepts same-origin navigation, swaps only the content div, and keeps the
// sidebar persistent so it never flashes or re-renders between page loads.
// Falls back to a full navigation on any error.

(function () {
    'use strict';

    const CONTENT_CLASS = 'flex-1 flex flex-col overflow-hidden';

    function getContentEl() {
        return document.querySelector('.flex-1.flex.flex-col.overflow-hidden');
    }

    // ─── Page timer tracking ──────────────────────────────────────────────────
    // Intercept setInterval / setTimeout globally so timers created by the
    // initial server-rendered page AND by SPA-navigated page scripts are all
    // tracked and can be cancelled when the user navigates away.
    //
    // NOTE: This patch runs after main.js / auth.js (head scripts) but before
    // the page's inline <body> scripts, so shared-script timers (toast fades,
    // tooltip hides, resize debounce) are NOT captured — only page-specific
    // polling intervals are.
    const _pageTimerIds = [];
    const _origSetInterval  = window.setInterval.bind(window);
    const _origSetTimeout   = window.setTimeout.bind(window);
    const _origClearInterval = window.clearInterval.bind(window);
    const _origClearTimeout  = window.clearTimeout.bind(window);

    window.setInterval = function () {
        const id = _origSetInterval.apply(window, arguments);
        _pageTimerIds.push({ id, interval: true });
        return id;
    };
    window.setTimeout = function () {
        const id = _origSetTimeout.apply(window, arguments);
        _pageTimerIds.push({ id, interval: false });
        return id;
    };
    // Keep clearInterval/clearTimeout in sync so manually-cleared timers are
    // removed from the list and don't generate spurious cancels later.
    window.clearInterval = function (id) {
        const idx = _pageTimerIds.findIndex(t => t.id === id && t.interval);
        if (idx !== -1) _pageTimerIds.splice(idx, 1);
        return _origClearInterval(id);
    };
    window.clearTimeout = function (id) {
        const idx = _pageTimerIds.findIndex(t => t.id === id && !t.interval);
        if (idx !== -1) _pageTimerIds.splice(idx, 1);
        return _origClearTimeout(id);
    };

    function cleanupPageTimers() {
        _pageTimerIds.forEach(({ id, interval }) => {
            if (interval) _origClearInterval(id);
            else          _origClearTimeout(id);
        });
        _pageTimerIds.length = 0;
    }

    // ─── External script loader ───────────────────────────────────────────────
    // Track every <script src> already in the document so we only load each
    // page-specific script once (filter.js, connections.js, etc.)
    const loadedSrcs = new Set(
        [...document.querySelectorAll('script[src]')].map(s => s.src)
    );

    // ─── Tag initial server-rendered extras ───────────────────────────────────
    // On first SPA navigation we need to remove the modals and scripts that the
    // server injected outside the .flex.h-screen wrapper (they belong to the
    // current page and must be replaced with the next page's equivalents).
    // Tag them now, before any navigation happens, so the cleanup step knows
    // exactly which elements to discard.
    (function tagInitialExtras() {
        const flexRoot = document.querySelector('.flex.h-screen');
        if (!flexRoot) return;
        [...document.body.children]
            .filter(el => el !== flexRoot)
            .forEach(el => { el.dataset.pageExtra = 'true'; });
    }());

    function loadScript(src) {
        const abs = new URL(src, location.origin).href;
        if (loadedSrcs.has(abs)) return Promise.resolve();
        // Belt-and-suspenders: check the live DOM in case a script was added
        // after router.js was parsed (e.g. {{PAGE_SCRIPTS}} on first load).
        if ([...document.querySelectorAll('script[src]')].some(s => s.src === abs)) {
            loadedSrcs.add(abs);
            return Promise.resolve();
        }
        loadedSrcs.add(abs);
        return new Promise(resolve => {
            const s = document.createElement('script');
            s.src = src;
            s.onload = resolve;
            s.onerror = resolve; // don't block navigation on a missing script
            document.head.appendChild(s);
        });
    }

    // ─── Inline script executor ───────────────────────────────────────────────
    // Runs the inline <script> blocks from the newly loaded content.
    // Pages register their initialization logic via DOMContentLoaded listeners,
    // which never fire again in an SPA.  We temporarily patch addEventListener
    // and window.onload so those handlers are captured and called immediately
    // after execution.
    function executeInlineScripts(container) {
        const domReadyCallbacks = [];

        // Patch document.addEventListener
        const origDocOn = document.addEventListener.bind(document);
        document.addEventListener = function (type, fn, opts) {
            if (type === 'DOMContentLoaded') { domReadyCallbacks.push(fn); return; }
            origDocOn(type, fn, opts);
        };

        // Patch window.addEventListener
        const origWinOn = window.addEventListener.bind(window);
        window.addEventListener = function (type, fn, opts) {
            if (type === 'DOMContentLoaded') { domReadyCallbacks.push(fn); return; }
            origWinOn(type, fn, opts);
        };

        // Snapshot window.onload before execution so we can detect if a script
        // assigned a new handler.  Avoid Object.defineProperty — onload may live
        // on the prototype and that path throws TypeError on restore.
        const onloadBefore = window.onload;

        // Clone and execute each inline script.
        //
        // Problem: top-level `const`/`let` declarations are bound to the
        // global *lexical* environment.  Once declared they cannot be
        // re-declared — not even as `var` — which causes a SyntaxError the
        // second time the same (or a sibling) page script runs.
        //
        // Solution: wrap every script in an IIFE so its let/const are
        // function-scoped and never reach the global lexical environment.
        // To keep `onclick="fn()"` handlers working, hoist top-level
        // `function` declarations to `window.fn = function` *before* wrapping
        // so they land on the global object.  Inner (nested) functions that
        // happen to start a line are also exposed on window — harmless in
        // practice for this code-base.
        [...container.querySelectorAll('script:not([src])')].forEach(old => {
            const s = document.createElement('script');
            const hoisted = old.textContent
                .replace(/^(\s*)async\s+function\s+(\w+)\s*\(/gm,
                         '$1window.$2 = async function(')
                .replace(/^(\s*)function\s+(\w+)\s*\(/gm,
                         '$1window.$2 = function(');
            s.textContent = `(function(){\n${hoisted}\n})();`;
            document.body.appendChild(s);
            document.body.removeChild(s);
        });

        // Restore patched APIs
        document.addEventListener = origDocOn;
        window.addEventListener = origWinOn;

        // Run captured DOMContentLoaded handlers
        domReadyCallbacks.forEach(fn => { try { fn(); } catch (e) { console.warn('[router] DOMContentLoaded handler error:', e); } });

        // Run any onload handler that was assigned during script execution
        const onloadAfter = window.onload;
        if (typeof onloadAfter === 'function' && onloadAfter !== onloadBefore) {
            try { onloadAfter(); } catch (e) { console.warn('[router] onload handler error:', e); }
        }
    }

    // ─── Active nav updater ───────────────────────────────────────────────────
    function setActiveNav(pageName) {
        document.querySelectorAll('#sidebar nav a[href]').forEach(a => {
            const href = a.getAttribute('href').replace(/^\//, '');
            a.classList.remove('bg-slate-900', 'text-slate-100',
                               'hover:bg-slate-700', 'hover:text-slate-100');
            if (href === pageName) {
                a.classList.add('bg-slate-900', 'text-slate-100');
            } else {
                a.classList.add('hover:bg-slate-700', 'hover:text-slate-100');
            }
        });
    }

    // ─── Core navigate function ───────────────────────────────────────────────
    async function navigate(url, pushState) {
        let response;
        try {
            response = await fetch(url, { headers: { 'X-SPA': '1' } });
        } catch {
            window.location.href = url;
            return;
        }

        // If the server redirected to logoff/index (auth failure), follow it fully
        const finalUrl = response.url;
        if (!response.ok || !response.headers.get('content-type')?.includes('text/html')) {
            window.location.href = finalUrl;
            return;
        }
        if (new URL(finalUrl).pathname === '/logoff' || new URL(finalUrl).pathname === '/index') {
            window.location.href = finalUrl;
            return;
        }

        let html;
        try { html = await response.text(); } catch {
            window.location.href = url;
            return;
        }

        const parser = new DOMParser();
        const newDoc = parser.parseFromString(html, 'text/html');

        // Load any page-specific <script src> declared in the fetched page's <head>
        // (already-loaded scripts are skipped by loadScript)
        const headScripts = [...newDoc.querySelectorAll('head script[src]')]
            .map(s => s.getAttribute('src'))
            .filter(src => ![
                '_tailwind', '_lucide', 'auth.js', 'main.js', 'router.js'
            ].some(skip => src.includes(skip)));

        for (const src of headScripts) {
            await loadScript(src);
        }

        // Find new content div
        const newContent = newDoc.querySelector('.' + CONTENT_CLASS.replace(/ /g, '.'));
        if (!newContent) { window.location.href = url; return; }

        // Swap content — use View Transitions API when available for a smooth
        // cross-fade; fall back to an instant swap on older browsers.
        const current = getContentEl();
        if (!current) { window.location.href = url; return; }

        const pageName = new URL(url, location.origin).pathname.replace(/^\//, '') || 'dashboard';

        const doSwap = () => {
            current.replaceWith(newContent);
            document.title = newDoc.title;
            setActiveNav(pageName);
        };

        if (document.startViewTransition) {
            await document.startViewTransition(doSwap).finished;
        } else {
            doSwap();
        }

        // History
        if (pushState) {
            history.pushState({ url }, '', url);
        }

        // ─── Sync page extras (modals, scripts outside the flex wrapper) ──────
        // Pages put their modals and the main <script> block AFTER the closing
        // </div> of .flex.h-screen, making them direct children of <body>.
        // The content-div swap above only moves the inner flex-1 element; these
        // extras would stay in the parsed document and never run.  Fix: move
        // non-script extras (modals) to the live document, and collect all
        // inline scripts (from both the content div and the body extras) into a
        // single fragment so executeInlineScripts fires exactly once.

        // 1. Remove extras left by the previous page (tagged at startup or on
        //    the last SPA navigation).
        document.querySelectorAll('[data-page-extra]').forEach(el => el.remove());

        // 2. Identify what lives outside .flex.h-screen in the parsed document.
        const parsedFlexRoot = newDoc.querySelector('.flex.h-screen');
        const parsedBodyExtras = parsedFlexRoot
            ? [...newDoc.body.children].filter(el => el !== parsedFlexRoot)
            : [];

        // 3. Build a unified script container BEFORE moving modals to the live
        //    document.  Some pages have unclosed div elements before their
        //    <script> blocks, causing the parser to nest scripts inside those
        //    divs rather than as direct body children.  A body-level
        //    querySelectorAll finds scripts at any depth; the parsedFlexRoot
        //    guard prevents double-counting scripts inside the swapped content.
        const scriptFragment = document.createDocumentFragment();
        [...newContent.querySelectorAll('script:not([src])')].forEach(s =>
            scriptFragment.appendChild(s.cloneNode(true))
        );
        [...newDoc.body.querySelectorAll('script:not([src])')].forEach(s => {
            if (!parsedFlexRoot || !parsedFlexRoot.contains(s)) {
                scriptFragment.appendChild(s.cloneNode(true));
            }
        });

        // 4. Move modals and other non-script extras into the live document and
        //    tag them so the next navigation can clean them up.
        parsedBodyExtras
            .filter(el => el.tagName !== 'SCRIPT')
            .forEach(el => {
                el.dataset.pageExtra = 'true';
                document.body.appendChild(el);
            });

        // Cancel all timers from the previous page before running new page scripts.
        // This stops stale setInterval polling (e.g. jobs auto-refresh) from
        // firing after the DOM has been swapped and accessing elements that no
        // longer exist.
        cleanupPageTimers();

        // Re-run page initialization (captures DOMContentLoaded / onload patterns)
        executeInlineScripts(scriptFragment);

        // Re-render Lucide icons in new content
        if (typeof window.queueLucideRender === 'function') {
            window.queueLucideRender();
        }

        // Re-run UX enhancements on new content
        if (typeof window.enhanceInteractions === 'function') window.enhanceInteractions();
        if (typeof window.enhanceTooltips === 'function') window.enhanceTooltips();
        if (typeof window.responsiveGrids === 'function') window.responsiveGrids();

        // Scroll content area to top
        newContent.scrollTop = 0;
    }

    // ─── Click interception ───────────────────────────────────────────────────
    document.addEventListener('click', function (e) {
        const a = e.target.closest('a[href]');
        if (!a) return;

        const href = a.getAttribute('href');
        if (!href) return;

        // Skip: external, protocol, anchor, target, download, logoff, login
        if (href.startsWith('http') || href.startsWith('//') ||
            href.startsWith('#') || href.includes(':') ||
            a.target || a.hasAttribute('download')) return;

        const page = href.replace(/^\//, '');
        if (page === 'logoff' || page === 'index' || page === '') return;

        e.preventDefault();
        navigate(href, true);
    });

    // ─── Back / forward ───────────────────────────────────────────────────────
    window.addEventListener('popstate', function (e) {
        navigate(window.location.href, false);
    });

    // Record initial URL in history state
    history.replaceState({ url: window.location.href }, '', window.location.href);

})();
