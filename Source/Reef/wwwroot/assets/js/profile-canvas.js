(function () {
    const NODE_DEFS = {
        source: { label: 'Source', icon: 'database', inputs: 0, outputs: 1, removable: false },
        preprocess: { label: 'Pre-process', icon: 'play', inputs: 1, outputs: 1, removable: true },
        deltasync: { label: 'Smart Sync', icon: 'refresh-cw', inputs: 1, outputs: 1, removable: true },
        splitoutput: { label: 'Split Output', icon: 'scissors', inputs: 1, outputs: 1, removable: true },
        template: { label: 'Template', icon: 'file-code', inputs: 1, outputs: 1, removable: true },
        emailexport: { label: 'Email Export', icon: 'mail', inputs: 1, outputs: 0, removable: true, terminal: true },
        destination: { label: 'Destination', icon: 'save', inputs: 1, outputs: 0, removable: false, terminal: true },
        postprocess: { label: 'Post-process', icon: 'check-circle', inputs: 1, outputs: 0, removable: true }
    };

    const ICONS = {
        database: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><ellipse cx="12" cy="5" rx="9" ry="3"></ellipse><path d="M3 5V19A9 3 0 0 0 21 19V5"></path><path d="M3 12A9 3 0 0 0 21 12"></path></svg>',
        play: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>',
        'refresh-cw': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2v6h-6"></path><path d="M3 12a9 9 0 0 1 15-6.7L21 8"></path><path d="M3 22v-6h6"></path><path d="M21 12a9 9 0 0 1-15 6.7L3 16"></path></svg>',
        scissors: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="6" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M20 4 8.12 15.88"></path><path d="M14.47 14.48 20 20"></path><path d="M8.12 8.12 12 12"></path></svg>',
        'file-code': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7Z"></path><path d="M14 2v4a2 2 0 0 0 2 2h4"></path><path d="m10 13-2 2 2 2"></path><path d="m14 13 2 2-2 2"></path></svg>',
        mail: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="2" y="4" width="20" height="16" rx="2"></rect><path d="m22 7-8.97 5.7a1.94 1.94 0 0 1-2.06 0L2 7"></path></svg>',
        save: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M15.2 3a2 2 0 0 1 1.4.6l3.8 3.8a2 2 0 0 1 .6 1.4V19a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"></path><path d="M17 21v-7H7v7"></path><path d="M7 3v4h8"></path></svg>',
        'check-circle': '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21.8 10A10 10 0 1 1 17 3.34"></path><path d="m9 11 3 3L22 4"></path></svg>'
    };

    const SECTION_TARGET = {
        source: { tab: 'general', section: 'node-section-source' },
        preprocess: { tab: 'general', section: 'node-section-preprocess' },
        postprocess: { tab: 'general', section: 'node-section-postprocess' },
        template: { tab: 'general', section: 'node-section-template' },
        destination: { tab: 'general', section: 'node-section-destination' },
        emailexport: { tab: 'general', section: 'emailConfigSection' },
        deltasync: { tab: 'deltaSync', section: null },
        splitoutput: { tab: 'splitOutput', section: null }
    };

    const FIELD_MAP = {
        preprocess: [
            ['preprocess-type', 'value'], ['preprocess-command', 'value'],
            ['preprocess-script-interpreter', 'value'], ['preprocess-script-source', 'value'],
            ['preprocess-script-body', 'value'], ['preprocess-script-env-allowlist', 'value'],
            ['preprocess-timeout', 'value'], ['preprocess-rollback-on-failure', 'checked'],
            ['preprocess-continue-on-error', 'checked']
        ],
        postprocess: [
            ['postprocess-type', 'value'], ['postprocess-command', 'value'],
            ['postprocess-script-interpreter', 'value'], ['postprocess-script-source', 'value'],
            ['postprocess-script-body', 'value'], ['postprocess-script-env-allowlist', 'value'],
            ['postprocess-timeout', 'value'], ['postprocess-skip-on-failure', 'checked'],
            ['postprocess-on-zero-rows', 'checked'], ['postprocess-continue-on-error', 'checked']
        ],
        deltasync: [
            ['deltaSyncReefIdColumn', 'value'], ['deltaSyncHashAlgorithm', 'value'],
            ['deltaSyncNumericPrecision', 'value'], ['deltaSyncDuplicateStrategy', 'value'],
            ['deltaSyncNullStrategy', 'value'], ['deltaSyncTrackDeletes', 'checked'],
            ['excludeReefIdFromOutput', 'checked']
        ],
        splitoutput: [
            ['splitKeyColumn', 'value'], ['splitFilenameTemplate', 'value'],
            ['splitBatchSize', 'value'], ['postProcessPerSplit', 'checked'],
            ['emailGroupBySplitKey', 'checked'], ['excludeSplitKeyFromOutput', 'checked']
        ],
        template: [
            ['template-id', 'value']
        ],
        destination: [
            ['destination-id', 'value'], ['destination-endpoint-id', 'value'], ['filenameTemplate', 'value']
        ],
        emailexport: [
            ['emailTemplateId', 'value'], ['useHardcodedRecipients', 'checked'],
            ['emailRecipientsColumn', 'value'], ['emailRecipientsHardcoded', 'value'],
            ['useHardcodedCc', 'checked'], ['emailCcColumn', 'value'], ['emailCcHardcoded', 'value'],
            ['useHardcodedSubject', 'checked'], ['emailSubjectColumn', 'value'], ['emailSubjectHardcoded', 'value'],
            ['emailSuccessThresholdPercent', 'value'], ['emailApprovalRequired', 'checked']
        ]
    };

    const ENABLE_FIELD = {
        preprocess: () => document.getElementById('preprocess-type').value !== '',
        postprocess: () => document.getElementById('postprocess-type').value !== '',
        deltasync: () => document.getElementById('deltaSyncEnabled').checked,
        splitoutput: () => document.getElementById('splitEnabled').checked,
        template: () => document.getElementById('template-id').value !== '',
        emailexport: () => document.getElementById('isEmailExport').checked
    };

    let editor = null;
    let canvasEl = null;
    let selected = new Set();
    let clipboard = null;

    function isEmailMode() {
        return document.getElementById('isEmailExport').checked;
    }

    function presentNodeTypes() {
        const types = ['source'];
        if (ENABLE_FIELD.preprocess()) types.push('preprocess');
        if (ENABLE_FIELD.deltasync()) types.push('deltasync');
        if (isEmailMode()) {
            types.push('emailexport');
        } else {
            if (ENABLE_FIELD.splitoutput()) types.push('splitoutput');
            if (ENABLE_FIELD.template()) types.push('template');
            types.push('destination');
        }
        if (ENABLE_FIELD.postprocess()) types.push('postprocess');
        return types;
    }

    function nodeHtml(type) {
        const def = NODE_DEFS[type];
        return `<div class="reef-node" data-node-type="${type}" data-tooltip="${def.label}" data-tooltip-position="top">` +
            `<span class="reef-node-icon">${ICONS[def.icon]}</span>` +
            `<span class="reef-node-label">${def.label}</span></div>`;
    }

    function layout(types) {
        const x = {}; let cursor = 40;
        types.forEach(t => { x[t] = cursor; cursor += 170; });
        return x;
    }

    function render() {
        if (!editor) return;
        editor.clear();
        selected.clear();
        const types = presentNodeTypes();
        const positions = layout(types);
        const idByType = {};
        types.forEach(type => {
            const def = NODE_DEFS[type];
            const cls = def.removable ? 'reef-node-optional' : 'reef-node-required';
            const id = editor.addNode(type, def.inputs, def.outputs, positions[type], 60, cls, {}, nodeHtml(type), false);
            idByType[type] = id;
        });
        for (let i = 0; i < types.length - 1; i++) {
            const from = types[i], to = types[i + 1];
            if (NODE_DEFS[from].outputs > 0 && NODE_DEFS[to].inputs > 0) {
                editor.addConnection(idByType[from], idByType[to], 'output_1', 'input_1');
            }
        }
        window.queueLucideRender && window.queueLucideRender();
    }

    function pulse(type) {
        const wrap = canvasEl.querySelector(`[data-node-type="${type}"]`);
        if (!wrap) return;
        wrap.classList.remove('reef-node-pulse');
        void wrap.offsetWidth;
        wrap.classList.add('reef-node-pulse');
    }

    function openNodeDrawer(type) {
        const target = SECTION_TARGET[type];
        if (!target) return;
        window.showTab(target.tab);
        if (target.section) {
            requestAnimationFrame(() => {
                const el = document.getElementById(target.section);
                if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
            });
        }
    }

    function setEnabled(type, on) {
        switch (type) {
            case 'preprocess':
                if (!on) { document.getElementById('preprocess-type').value = ''; window.togglePreProcessConfig && window.togglePreProcessConfig(); }
                break;
            case 'postprocess':
                if (!on) { document.getElementById('postprocess-type').value = ''; window.togglePostProcessConfig && window.togglePostProcessConfig(); }
                break;
            case 'deltasync': {
                const el = document.getElementById('deltaSyncEnabled');
                if (el.checked !== on) { el.checked = on; window.toggleDeltaSyncFields && window.toggleDeltaSyncFields(); }
                break;
            }
            case 'splitoutput': {
                const el = document.getElementById('splitEnabled');
                if (el.checked !== on) { el.checked = on; window.toggleSplitFields && window.toggleSplitFields(); }
                break;
            }
            case 'template':
                if (!on) document.getElementById('template-id').value = '';
                break;
            case 'emailexport':
                if (window.setProfileEmailMode) window.setProfileEmailMode(on);
                break;
        }
    }

    function removeNode(type) {
        const def = NODE_DEFS[type];
        if (!def.removable) {
            window.showToast(`${def.label} is required and cannot be removed`, 'info');
            return;
        }
        setEnabled(type, false);
        render();
    }

    function addNodeFromPalette(type) {
        const types = presentNodeTypes();
        if (types.includes(type)) { openNodeDrawer(type); return; }
        if ((type === 'emailexport' && types.includes('destination')) || (type === 'destination' && types.includes('emailexport'))) {
            window.showToast('Destination and Email Export are alternate output strategies, remove one before adding the other', 'error');
            return;
        }
        if (type === 'splitoutput' && isEmailMode()) {
            window.showToast('Split Output is not used with Email Export, use its own Group by split key option instead', 'error');
            return;
        }
        if (type === 'template' && isEmailMode()) {
            window.showToast('Template is not used with Email Export, email profiles carry their own template', 'error');
            return;
        }
        setEnabled(type, true);
        render();
        openNodeDrawer(type);
        pulse(type);
    }

    function readNodeConfig(type) {
        const fields = FIELD_MAP[type] || [];
        const data = {};
        fields.forEach(([id, prop]) => {
            const el = document.getElementById(id);
            if (el) data[id] = el[prop];
        });
        return data;
    }

    function writeNodeConfig(type, data) {
        const fields = FIELD_MAP[type] || [];
        fields.forEach(([id, prop]) => {
            const el = document.getElementById(id);
            if (el && Object.prototype.hasOwnProperty.call(data, id)) el[prop] = data[id];
        });
        [
            ['togglePreProcessConfig', 'preprocess'], ['togglePostProcessConfig', 'postprocess'],
            ['updateRecipientsModeDisplay', 'emailexport'], ['updateCcModeDisplay', 'emailexport'],
            ['updateSubjectModeDisplay', 'emailexport'], ['updateTemplateInfo', 'template']
        ].forEach(([fn, forType]) => {
            if (forType === type && typeof window[fn] === 'function') { try { window[fn](); } catch (e) {} }
        });
    }

    function copySelection() {
        if (selected.size === 0) { window.showToast('Select a node first (click it, or Ctrl+A for all)', 'info'); return; }
        const nodes = Array.from(selected).filter(t => t !== 'source').map(type => ({
            type, enabled: true, config: readNodeConfig(type)
        }));
        if (nodes.length === 0) { window.showToast('Nothing to copy', 'info'); return; }
        clipboard = nodes;
        window.showToast(`Copied ${nodes.length} node${nodes.length > 1 ? 's' : ''}`, 'success');
    }

    function pasteClipboard() {
        if (!clipboard || clipboard.length === 0) { window.showToast('Clipboard is empty', 'info'); return; }
        const currentOptional = presentNodeTypes().filter(t => !['source', 'destination'].includes(t));
        const blankCanvas = currentOptional.length === 0;
        const toApply = (clipboard.length > 1 && !blankCanvas) ? [clipboard[0]] : clipboard;
        toApply.forEach(node => {
            if (node.type === 'emailexport' && presentNodeTypes().includes('destination') && toApply.length > 1) return;
            setEnabled(node.type, true);
            writeNodeConfig(node.type, node.config);
        });
        render();
        window.showToast(`Pasted ${toApply.length} node${toApply.length > 1 ? 's' : ''}`, 'success');
    }

    function selectAll() {
        selected = new Set(presentNodeTypes());
        canvasEl.querySelectorAll('.reef-node').forEach(n => n.closest('.drawflow-node').classList.add('selected'));
    }

    function clearSelection() {
        selected.clear();
        canvasEl.querySelectorAll('.drawflow-node.selected').forEach(n => n.classList.remove('selected'));
    }

    function onKeydown(e) {
        const wrap = document.getElementById('profile-canvas-wrap');
        if (!wrap || wrap.offsetParent === null) return;
        const activeTag = document.activeElement && document.activeElement.tagName;
        if (activeTag === 'INPUT' || activeTag === 'TEXTAREA' || activeTag === 'SELECT') return;
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a') { e.preventDefault(); selectAll(); }
        else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'c') { e.preventDefault(); copySelection(); }
        else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'v') { e.preventDefault(); pasteClipboard(); }
    }

    function validate() {
        const types = presentNodeTypes();
        const hasEmail = types.includes('emailexport');
        const hasDestination = types.includes('destination');
        if (hasEmail && hasDestination) return 'emailexport and destination are alternate output strategies and cannot both be present';
        if (!hasEmail && !hasDestination) return 'profile needs an output strategy, add a destination or emailexport node';
        if (types.includes('splitoutput') && !hasDestination) return 'splitoutput requires a destination node';
        if (types.includes('template') && hasEmail) return 'template is not used with emailexport, email profiles carry their own template';
        return null;
    }

    function validateBeforeSave() {
        const error = validate();
        if (error) window.showToast(error, 'error');
        return error === null;
    }

    function init() {
        canvasEl = document.getElementById('profile-canvas');
        if (!canvasEl) return;
        if (editor) { render(); return; }
        editor = new window.Drawflow(canvasEl);
        editor.reroute = false;
        editor.editor_mode = 'edit';
        editor.zoom_max = 1;
        editor.zoom_min = 1;
        editor.start();

        editor.on('nodeSelected', id => {
            const info = editor.getNodeFromId(id);
            const type = info && info.name;
            if (!type) return;
            clearSelection();
            selected = new Set([type]);
            openNodeDrawer(type);
        });

        canvasEl.addEventListener('dblclick', e => {
            const nodeEl = e.target.closest('.drawflow-node');
            if (!nodeEl) return;
            const type = nodeEl.querySelector('.reef-node')?.dataset.nodeType;
            if (type) removeNode(type);
        });

        document.addEventListener('keydown', onKeydown);
        render();
    }

    function reset() {
        if (editor) { editor.clear(); }
        selected.clear();
    }

    window.ReefProfileCanvas = {
        init, render, reset, addNodeFromPalette, removeNode, validateBeforeSave, presentNodeTypes
    };
})();
