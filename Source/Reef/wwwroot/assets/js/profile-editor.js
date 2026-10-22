(function () {
    const NODE_WIDTH = 184;
    const NODE_HEIGHT = 48;
    const API = '/api/profile-documents';
    const OPTION_SOURCES = {
        connections: { url: '/api/connections', map: c => ({ value: c.id, label: c.name }) },
        destinations: { url: '/api/destinations', filter: d => d.type !== 'Email', map: d => ({ value: d.id, label: `${d.name} (${d.type})` }) },
        'email-destinations': { url: '/api/destinations', filter: d => d.type === 'Email', map: d => ({ value: d.id, label: d.name }) },
        templates: { url: '/api/templates', map: t => ({ value: t.id, label: t.outputFormat ? `${t.name} (${t.outputFormat})` : t.name }) },
        groups: { url: '/api/groups', map: g => ({ value: g.id, label: g.name }) }
    };

    const catalogs = {};
    let state = null;
    let root = null;
    let drag = null;

    function el(tag, attrs = {}, children = []) {
        const node = document.createElement(tag);
        for (const [key, value] of Object.entries(attrs)) {
            if (value === undefined || value === null || value === false) continue;
            if (key === 'class') node.className = value;
            else if (key === 'text') node.textContent = value;
            else if (key.startsWith('on')) node.addEventListener(key.slice(2), value);
            else node.setAttribute(key, value === true ? '' : value);
        }
        for (const child of [].concat(children)) if (child) node.append(child);
        return node;
    }

    function icon(name, cls = 'reef-ed-icon') {
        return el('i', { 'data-lucide': name, class: cls });
    }

    async function request(url, options = {}) {
        const response = await fetch(url, { ...options, headers: { ...getAuthHeaders(), 'Content-Type': 'application/json' } });
        const body = response.status === 204 ? null : await response.json().catch(() => null);
        if (!response.ok) {
            const error = new Error(body?.error || `Request failed with status ${response.status}`);
            error.errors = body?.errors || [];
            throw error;
        }
        return body;
    }

    function getPath(obj, path) {
        return path.split('.').reduce((current, key) => (current && typeof current === 'object' ? current[key] : undefined), obj);
    }

    function setPath(obj, path, value) {
        const keys = path.split('.');
        let current = obj;
        keys.slice(0, -1).forEach(key => {
            if (!current[key] || typeof current[key] !== 'object' || Array.isArray(current[key])) current[key] = {};
            current = current[key];
        });
        if (value === undefined) delete current[keys.at(-1)];
        else current[keys.at(-1)] = value;
    }

    function textOf(value) {
        if (value === undefined || value === null) return '';
        if (typeof value === 'boolean') return value ? 'true' : 'false';
        return String(value);
    }

    function visible(field, config) {
        if (field.hidden) return false;
        if (!field.showWhen) return true;
        const value = getPath(config, field.showWhen.key);
        if (!field.showWhen.values) return value !== undefined && value !== null && value !== '' && value !== false;
        return field.showWhen.values.some(v => v.toLowerCase() === textOf(value).toLowerCase());
    }

    function definition(type) {
        return state.catalog.nodes.find(n => n.type === type);
    }

    function orderedNodes() {
        return [...state.doc.nodes].sort((a, b) => definition(a.type).rank - definition(b.type).rank);
    }

    function selectedNode() {
        return state.doc.nodes.find(n => n.id === state.selected);
    }

    function markDirty() {
        state.dirty = true;
        root.querySelector('[data-role=save]').removeAttribute('aria-disabled');
    }

    async function loadOptions() {
        const urls = [...new Set(Object.values(OPTION_SOURCES).map(s => s.url))];
        const results = await Promise.all(urls.map(url => request(url).catch(() => [])));
        const byUrl = Object.fromEntries(urls.map((url, i) => [url, Array.isArray(results[i]) ? results[i] : []]));
        state.options = Object.fromEntries(Object.entries(OPTION_SOURCES).map(([name, source]) =>
            [name, byUrl[source.url].filter(source.filter || (() => true)).map(source.map)]));
    }

    function defaultsFor(fields) {
        const config = {};
        fields.forEach(field => {
            if (field.default !== undefined && field.default !== null) setPath(config, field.key, structuredClone(field.default));
        });
        return config;
    }

    function addNode(type) {
        if (state.doc.nodes.some(n => n.type === type)) {
            select(state.doc.nodes.find(n => n.type === type).id);
            return;
        }
        state.doc.nodes.push({ id: type, type, x: 0, y: 0, config: defaultsFor(definition(type).fields) });
        tidy();
        markDirty();
        select(type);
    }

    // Steps run in catalog order. Added steps reflow the chain top to bottom.
    function tidy() {
        const surface = root.querySelector('[data-role=surface]');
        const x = Math.max(40, Math.round((surface.clientWidth - NODE_WIDTH) / 2 / 8) * 8);
        orderedNodes().forEach((node, index) => {
            node.x = x;
            node.y = 40 + index * (NODE_HEIGHT + 40);
        });
    }

    function removeNode(id) {
        const node = state.doc.nodes.find(n => n.id === id);
        if (!node) return;
        if (definition(node.type).required) {
            window.showToast(`${definition(node.type).label} is part of every ${state.catalog.kind} profile and stays on the canvas`, 'info');
            return;
        }
        state.doc.nodes = state.doc.nodes.filter(n => n.id !== id);
        state.errors = state.errors.filter(e => e.nodeId !== id);
        markDirty();
        select(null);
    }

    function select(id) {
        state.selected = id;
        renderCanvas();
        renderPalette();
        renderInspector();
    }

    function renderPalette() {
        const list = root.querySelector('[data-role=palette]');
        list.replaceChildren(...state.catalog.nodes.filter(def => !def.required).sort((a, b) => a.rank - b.rank).map(def => {
            const present = state.doc.nodes.some(n => n.type === def.type);
            return el('button', {
                type: 'button',
                class: 'reef-ed-palette-item',
                draggable: present ? null : 'true',
                'aria-disabled': present ? 'true' : null,
                'data-tooltip': present ? `${def.label} is already on the canvas` : def.tooltip,
                'data-tooltip-position': 'right',
                ondragstart: e => { e.dataTransfer.setData('application/x-reef-node', def.type); e.dataTransfer.effectAllowed = 'copy'; },
                onclick: () => addNode(def.type)
            }, [icon(def.icon), el('span', { text: def.label })]);
        }));
        window.queueLucideRender && window.queueLucideRender();
    }

    function renderCanvas() {
        const surface = root.querySelector('[data-role=surface]');
        const edges = root.querySelector('[data-role=edges]');
        const nodes = orderedNodes();
        const errorNodes = new Set(state.errors.map(e => e.nodeId).filter(Boolean));

        surface.querySelectorAll('.reef-ed-node').forEach(n => n.remove());
        nodes.forEach(node => {
            const def = definition(node.type);
            const box = el('div', {
                class: 'reef-ed-node',
                role: 'button',
                tabindex: '0',
                'aria-pressed': state.selected === node.id ? 'true' : 'false',
                'aria-invalid': errorNodes.has(node.id) ? 'true' : null,
                'data-id': node.id,
                style: `left:${node.x}px;top:${node.y}px;width:${NODE_WIDTH}px;height:${NODE_HEIGHT}px`,
                onpointerdown: e => startDrag(e, node),
                onkeydown: e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); select(node.id); } }
            }, [icon(def.icon), el('span', { class: 'reef-ed-node-label', text: def.label }), el('span', { class: 'reef-ed-node-summary', text: summary(node) })]);
            surface.append(box);
        });

        const width = Math.max(surface.clientWidth, ...nodes.map(n => n.x + NODE_WIDTH + 80));
        const height = Math.max(surface.clientHeight, ...nodes.map(n => n.y + NODE_HEIGHT + 80));
        edges.setAttribute('width', width);
        edges.setAttribute('height', height);
        renderEdges();
        window.queueLucideRender && window.queueLucideRender();
    }

    function summary(node) {
        const def = definition(node.type);
        const first = def.fields.find(f => !f.hidden && visible(f, node.config || {}) && (f.kind === 'select' || f.key === 'Query'));
        if (!first) return '';
        const value = getPath(node.config || {}, first.key);
        const options = first.optionsSource ? state.options[first.optionsSource] : first.options;
        const match = options?.find(o => textOf(o.value) === textOf(value));
        if (match) return match.label;
        if (value === undefined || value === null || value === '' || first.optionsSource) return 'Not set up';
        return textOf(value).split('\n')[0];
    }

    function startDrag(e, node) {
        if (e.button !== 0) return;
        const target = e.currentTarget;
        target.setPointerCapture(e.pointerId);
        drag = { node, target, startX: e.clientX, startY: e.clientY, x: node.x, y: node.y, moved: false };
        target.onpointermove = move => {
            const dx = move.clientX - drag.startX, dy = move.clientY - drag.startY;
            if (!drag.moved && Math.hypot(dx, dy) < 4) return;
            drag.moved = true;
            node.x = Math.max(0, Math.round((drag.x + dx) / 8) * 8);
            node.y = Math.max(0, Math.round((drag.y + dy) / 8) * 8);
            target.style.left = `${node.x}px`;
            target.style.top = `${node.y}px`;
            renderEdges();
        };
        target.onpointerup = () => {
            target.onpointermove = null;
            target.onpointerup = null;
            if (drag.moved) markDirty();
            else select(node.id);
            drag = null;
        };
    }

    function renderEdges() {
        const nodes = orderedNodes();
        const parts = [];
        for (let i = 0; i < nodes.length - 1; i++) {
            const a = nodes[i], b = nodes[i + 1];
            const sideways = Math.abs(b.x - a.x) > Math.abs(b.y - a.y);
            const [x1, y1, x2, y2] = sideways
                ? [a.x + NODE_WIDTH, a.y + NODE_HEIGHT / 2, b.x, b.y + NODE_HEIGHT / 2]
                : [a.x + NODE_WIDTH / 2, a.y + NODE_HEIGHT, b.x + NODE_WIDTH / 2, b.y];
            const bend = Math.max(24, (sideways ? Math.abs(x2 - x1) : Math.abs(y2 - y1)) / 2);
            const curve = sideways ? `C${x1 + bend},${y1} ${x2 - bend},${y2}` : `C${x1},${y1 + bend} ${x2},${y2 - bend}`;
            parts.push(`<path d="M${x1},${y1} ${curve} ${x2},${y2}" class="reef-ed-edge"/>`,
                `<circle cx="${x1}" cy="${y1}" r="3.5" class="reef-ed-port"/>`, `<circle cx="${x2}" cy="${y2}" r="3.5" class="reef-ed-port"/>`);
        }
        root.querySelector('[data-role=edges]').innerHTML = parts.join('');
    }

    function renderInspector() {
        const panel = root.querySelector('[data-role=inspector]');
        const node = selectedNode();
        if (node) {
            const def = definition(node.type);
            node.config = node.config || {};
            panel.replaceChildren(
                el('div', { class: 'reef-ed-inspector-head' }, [
                    icon(def.icon),
                    el('h3', { text: def.label }),
                    el('span', { class: 'reef-ed-hint', 'data-tooltip': def.tooltip, 'data-tooltip-position': 'left' }, [icon('info', 'reef-ed-icon-sm')]),
                    def.required ? null : el('button', { type: 'button', class: 'reef-ed-icon-button', 'aria-label': `Remove ${def.label}`, 'data-tooltip': 'Remove step', 'data-tooltip-position': 'left', onclick: () => removeNode(node.id) }, [icon('trash-2', 'reef-ed-icon-sm')])
                ]),
                ...[fieldList(def.fields, node.config, node.id, () => renderInspector()), actions(node)].filter(Boolean)
            );
        } else {
            panel.replaceChildren(
                el('div', { class: 'reef-ed-inspector-head' }, [icon('settings-2'), el('h3', { text: 'Profile' })]),
                ...[fieldList(state.catalog.settings.filter(f => f.key !== 'Name'), state.doc.settings, null, () => renderInspector()), actions(null)].filter(Boolean)
            );
        }
        window.queueLucideRender && window.queueLucideRender();
    }

    function actions(node) {
        const render = window.ReefProfileEditorActions;
        if (typeof render !== 'function') return null;
        const sections = render({ kind: state.catalog.kind, id: state.doc.id, node, config: node ? node.config : state.doc.settings, dirty: state.dirty });
        return sections.length ? el('div', { class: 'reef-ed-actions' }, sections) : null;
    }

    function flatten() {
        const kind = state.catalog.kind;
        return request(`${API}/${kind}/flatten${state.doc.id ? `?id=${state.doc.id}` : ''}`, { method: 'POST', body: JSON.stringify(state.doc) });
    }

    async function run() {
        if (state.dirty || !state.doc.id) await save();
        if (state.dirty || !state.doc.id) return;
        const kind = state.catalog.kind;
        const button = root.querySelector('[data-role=run]');
        button.setAttribute('aria-busy', 'true');
        try {
            const result = kind === 'import'
                ? await request(`/api/profiles/${state.doc.id}/execute?type=import`, { method: 'POST', body: '{}' })
                : await request(`/api/executions/execute/${state.doc.id}`, { method: 'POST', body: '{}' });
            const failed = /fail/i.test(result.status || '') || result.success === false;
            const detail = result.error || result.errorMessage || (kind === 'import' ? `${result.rowsInserted ?? 0} inserted, ${result.rowsUpdated ?? 0} updated, ${result.rowsSkipped ?? 0} skipped, ${result.rowsFailed ?? 0} failed` : result.outputPath || '');
            window.showToast(`Run ${result.status || (failed ? 'failed' : 'finished')}${detail ? `: ${detail}` : ''}`, failed ? 'error' : /partial/i.test(result.status || '') ? 'warning' : 'success');
        } catch (error) {
            window.showToast(error.message, 'error');
        } finally {
            button.removeAttribute('aria-busy');
        }
    }

    function fieldList(fields, config, nodeId, rerender) {
        const form = el('div', { class: 'reef-ed-fields' });
        fields.filter(f => visible(f, config)).forEach(field => {
            const error = state.errors.find(e => e.nodeId === nodeId && e.field === field.key);
            form.append(fieldRow(field, config, error, rerender));
        });
        return form;
    }

    function fieldRow(field, config, error, rerender) {
        const id = `reef-ed-${field.key.replace(/\W/g, '-')}`;
        const value = getPath(config, field.key) ?? field.default ?? undefined;
        const commit = (next, structural) => {
            setPath(config, field.key, next);
            state.errors = state.errors.filter(e => e !== error);
            markDirty();
            if (structural) rerender();
            renderCanvas();
        };
        const tooltip = field.tooltip ? el('span', { class: 'reef-ed-hint', 'data-tooltip': field.tooltip, 'data-tooltip-position': 'left' }, [icon('info', 'reef-ed-icon-sm')]) : null;
        const invalid = error ? 'true' : null;

        if (field.kind === 'bool') {
            return el('label', { class: 'reef-ed-check', for: id }, [
                el('input', { id, type: 'checkbox', checked: value === true, 'aria-invalid': invalid, onchange: e => commit(e.target.checked, true) }),
                el('span', { text: field.label }), tooltip
            ]);
        }

        const label = el('label', { class: 'reef-ed-label', for: id }, [el('span', { text: field.label }), field.required ? el('span', { class: 'reef-ed-required', 'aria-hidden': 'true', text: '*' }) : null, tooltip]);
        let control;
        switch (field.kind) {
            case 'select': {
                const options = field.optionsSource ? state.options[field.optionsSource] || [] : field.options || [];
                const hasEmpty = options.some(o => o.value === '');
                control = el('select', { id, 'aria-invalid': invalid, onchange: e => {
                    const raw = e.target.value;
                    commit(raw === '' && !hasEmpty ? undefined : field.optionsSource ? Number(raw) : raw, true);
                } }, [
                    hasEmpty ? null : el('option', { value: '', text: !field.required ? 'None' : options.length === 0 ? 'Nothing to choose yet' : 'Choose' }),
                    ...options.map(o => el('option', { value: textOf(o.value), text: o.label, selected: textOf(o.value) === textOf(value) }))
                ]);
                break;
            }
            case 'textarea':
            case 'code':
                control = el('textarea', { id, rows: field.kind === 'code' ? 6 : 3, placeholder: field.placeholder, 'aria-invalid': invalid, spellcheck: 'false', oninput: e => commit(e.target.value || undefined) });
                control.value = textOf(value);
                break;
            case 'json':
                control = el('textarea', { id, rows: 4, placeholder: field.placeholder || '{ }', 'aria-invalid': invalid, spellcheck: 'false', oninput: e => {
                    const text = e.target.value.trim();
                    if (!text) return commit(undefined);
                    try { commit(JSON.parse(text)); } catch { commit(text); }
                } });
                control.value = value === undefined || value === null ? '' : typeof value === 'string' ? value : JSON.stringify(value, null, 2);
                break;
            case 'number':
                control = el('input', { id, type: 'number', inputmode: 'numeric', class: 'reef-ed-number', value: textOf(value), placeholder: field.placeholder, 'aria-invalid': invalid, oninput: e => commit(e.target.value === '' ? undefined : Number(e.target.value)) });
                break;
            case 'list':
                control = el('input', { id, type: 'text', value: Array.isArray(value) ? value.join(', ') : textOf(value), placeholder: field.placeholder, 'aria-invalid': invalid, oninput: e => {
                    const items = e.target.value.split(',').map(s => s.trim()).filter(Boolean);
                    commit(items.length ? items : undefined);
                } });
                break;
            case 'table':
                control = tableControl(field, Array.isArray(value) ? value : [], rows => commit(rows.length ? rows : undefined), rerender, invalid);
                break;
            default:
                control = el('input', { id, type: field.kind === 'password' ? 'password' : 'text', autocomplete: field.kind === 'password' ? 'new-password' : 'off', value: textOf(value), placeholder: field.placeholder, 'aria-invalid': invalid, oninput: e => commit(e.target.value === '' ? undefined : e.target.value) });
        }
        return el('div', { class: 'reef-ed-field' }, [label, control]);
    }

    function tableControl(field, rows, commit, rerender, invalid) {
        const update = () => commit(rows);
        const body = rows.map((row, index) => el('tr', {}, [
            ...field.columns.map(column => {
                const current = row[column.key];
                let cell;
                if (column.kind === 'bool') {
                    cell = el('input', { type: 'checkbox', 'aria-label': column.label, checked: current === true, onchange: e => { row[column.key] = e.target.checked; update(); } });
                } else if (column.kind === 'select') {
                    const options = column.optionsSource ? state.options[column.optionsSource] || [] : column.options;
                    const hasEmpty = options.some(o => o.value === '');
                    cell = el('select', { 'aria-label': column.label, onchange: e => {
                        if (e.target.value) row[column.key] = column.optionsSource ? Number(e.target.value) : e.target.value;
                        else delete row[column.key];
                        update();
                    } }, [
                        hasEmpty ? null : el('option', { value: '', text: 'Choose' }),
                        ...options.map(o => el('option', { value: textOf(o.value), text: o.label, selected: textOf(o.value) === textOf(current) }))
                    ]);
                } else {
                    cell = el('input', { type: 'text', 'aria-label': column.label, value: textOf(current), oninput: e => { row[column.key] = e.target.value; update(); } });
                }
                return el('td', { class: column.kind === 'bool' ? 'reef-ed-col-bool' : null }, [cell]);
            }),
            el('td', { class: 'reef-ed-col-action' }, [el('button', { type: 'button', class: 'reef-ed-icon-button', 'aria-label': 'Remove row', onclick: () => { rows.splice(index, 1); update(); rerender(); } }, [icon('x', 'reef-ed-icon-sm')])])
        ]));
        return el('div', { class: 'reef-ed-table', 'aria-invalid': invalid }, [
            el('table', {}, [
                el('thead', {}, [el('tr', {}, [...field.columns.map(c => el('th', { text: c.label, class: c.kind === 'bool' ? 'reef-ed-col-bool' : null, 'data-tooltip': c.tooltip, 'data-tooltip-position': 'top' })), el('th', { class: 'reef-ed-col-action' })])]),
                el('tbody', {}, body)
            ]),
            el('button', { type: 'button', class: 'reef-ed-link', onclick: () => { rows.push(defaultsFor(field.columns)); update(); rerender(); } }, [icon('plus', 'reef-ed-icon-sm'), el('span', { text: 'Add row' })])
        ]);
    }

    async function save() {
        if (state.saving) return;
        state.saving = true;
        const button = root.querySelector('[data-role=save]');
        button.setAttribute('aria-busy', 'true');
        try {
            const kind = state.catalog.kind;
            const saved = state.doc.id
                ? await request(`${API}/${kind}/${state.doc.id}`, { method: 'PUT', body: JSON.stringify(state.doc) })
                : await request(`${API}/${kind}`, { method: 'POST', body: JSON.stringify(state.doc) });
            state.doc = saved;
            state.errors = [];
            state.dirty = false;
            button.setAttribute('aria-disabled', 'true');
            renderHeader();
            select(state.selected && saved.nodes.some(n => n.id === state.selected) ? state.selected : null);
            window.showToast(`${saved.settings.Name} saved`, 'success');
            if (typeof window.loadProfiles === 'function') window.loadProfiles();
        } catch (error) {
            state.errors = error.errors || [];
            const first = state.errors.find(e => e.nodeId) || state.errors[0];
            window.showToast(state.errors.length > 1 ? `${error.message} (and ${state.errors.length - 1} more)` : error.message, 'error');
            renderHeader();
            if (first?.field === 'Name') root.querySelector('[data-role=name]').focus();
            select(first?.nodeId ?? (first?.field ? null : state.selected));
        } finally {
            state.saving = false;
            button.removeAttribute('aria-busy');
        }
    }

    function renderHeader() {
        const meta = root.querySelector('[data-role=meta]');
        meta.textContent = [state.catalog.kind === 'import' ? 'Import' : 'Export', state.doc.code].filter(Boolean).join(' · ');
        const name = root.querySelector('[data-role=name]');
        name.value = state.doc.settings.Name || '';
        name.setAttribute('aria-invalid', state.errors.some(e => !e.nodeId && e.field === 'Name') ? 'true' : 'false');
    }

    function build() {
        root = el('div', { id: 'profile-editor', class: 'reef-ed', role: 'dialog', 'aria-modal': 'true', 'aria-label': 'Profile editor' }, [
            el('header', { class: 'reef-ed-header' }, [
                el('button', { type: 'button', class: 'reef-ed-icon-button', 'aria-label': 'Back to profiles', 'data-tooltip': 'Back to profiles', 'data-tooltip-position': 'bottom', onclick: close }, [icon('arrow-left')]),
                el('input', { 'data-role': 'name', class: 'reef-ed-name', type: 'text', placeholder: 'Untitled profile', 'aria-label': 'Profile name',
                    oninput: e => { state.doc.settings.Name = e.target.value; state.errors = state.errors.filter(x => x.field !== 'Name' || x.nodeId); e.target.setAttribute('aria-invalid', 'false'); markDirty(); } }),
                el('span', { 'data-role': 'meta', class: 'reef-ed-meta' }),
                el('div', { class: 'reef-ed-header-actions' }, [
                    el('button', { type: 'button', class: 'reef-ed-button', 'data-tooltip': 'Line the steps up in run order', 'data-tooltip-position': 'bottom', onclick: () => { tidy(); markDirty(); renderCanvas(); } }, [icon('list-tree', 'reef-ed-icon-sm'), el('span', { text: 'Tidy up' })]),
                    el('button', { type: 'button', class: 'reef-ed-button', onclick: () => select(null) }, [icon('settings-2', 'reef-ed-icon-sm'), el('span', { text: 'Profile settings' })]),
                    el('button', { type: 'button', 'data-role': 'run', class: 'reef-ed-button', 'data-tooltip': 'Save and run the profile now', 'data-tooltip-position': 'bottom', onclick: run }, [icon('play', 'reef-ed-icon-sm'), el('span', { text: 'Run' })]),
                    el('button', { type: 'button', 'data-role': 'save', class: 'reef-ed-button reef-ed-primary', 'aria-disabled': 'true', 'data-tooltip': 'Ctrl+S', 'data-tooltip-position': 'bottom', onclick: save }, [el('span', { text: 'Save' })])
                ])
            ]),
            el('div', { class: 'reef-ed-body' }, [
                el('aside', { class: 'reef-ed-palette' }, [el('h2', { text: 'Steps' }), el('div', { 'data-role': 'palette', class: 'reef-ed-palette-list' })]),
                el('div', { class: 'reef-ed-canvas', 'data-role': 'canvas',
                    ondragover: e => { if (e.dataTransfer.types.includes('application/x-reef-node')) { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; } },
                    ondrop: e => {
                        const type = e.dataTransfer.getData('application/x-reef-node');
                        if (!type) return;
                        e.preventDefault();
                        addNode(type);
                    },
                    onpointerdown: e => { if (e.target.closest('.reef-ed-node') === null && e.target.closest('[data-role=surface]')) select(null); }
                }, [el('div', { class: 'reef-ed-surface', 'data-role': 'surface' }, [
                    (() => { const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg'); svg.setAttribute('data-role', 'edges'); svg.setAttribute('class', 'reef-ed-edges'); svg.setAttribute('aria-hidden', 'true'); return svg; })()
                ])]),
                el('aside', { class: 'reef-ed-inspector', 'data-role': 'inspector', 'aria-label': 'Step settings' })
            ])
        ]);
        document.body.append(root);
        document.addEventListener('keydown', onKeydown);
        window.addEventListener('beforeunload', onBeforeUnload);
    }

    function onKeydown(e) {
        if (!root || root.hidden) return;
        const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(document.activeElement?.tagName);
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') { e.preventDefault(); save(); return; }
        if (typing) return;
        if ((e.key === 'Delete' || e.key === 'Backspace') && state.selected) { e.preventDefault(); removeNode(state.selected); }
        else if (e.key === 'Escape') { if (state.selected) select(null); else close(); }
    }

    function onBeforeUnload(e) {
        if (root && !root.hidden && state?.dirty) e.preventDefault();
    }

    async function open(kind, id) {
        try {
            catalogs[kind] = catalogs[kind] || await request(`${API}/${kind}/catalog`);
            const doc = await request(id ? `${API}/${kind}/${id}` : `${API}/${kind}/new`);
            state = { catalog: catalogs[kind], doc, selected: null, dirty: false, errors: [], options: {} };
            await loadOptions();
        } catch (error) {
            window.showToast(error.message, 'error');
            return;
        }
        if (!root) build();
        root.hidden = false;
        document.documentElement.classList.add('reef-ed-open');
        renderHeader();
        root.querySelector('[data-role=save]').setAttribute('aria-disabled', 'true');
        select(null);
        if (!id) {
            root.querySelector('[data-role=save]').removeAttribute('aria-disabled');
            root.querySelector('[data-role=name]').focus();
        }
    }

    function close() {
        if (state?.dirty && !window.confirm('Leave without saving? Your changes to this profile will be lost.')) return;
        root.hidden = true;
        document.documentElement.classList.remove('reef-ed-open');
        state = null;
    }

    window.ReefProfileEditor = { open, close, helpers: { el, icon, request, textOf, flatten, rerender: () => renderInspector() } };
})();
