(function () {
    const FILTERS_KEY = 'reef_profiles_filters';
    const SORT_KEY = 'reef_profiles_sort';
    const SOURCE_LABELS = { Local: 'Local file', Sftp: 'SFTP', Http: 'HTTP API', S3: 'S3', AzureBlob: 'Azure Blob', NetworkShare: 'Network share' };

    const state = { rows: [], connections: [], groups: [], sort: { column: 'name', direction: 'asc' } };

    const $ = id => document.getElementById(id);
    let documentListeners = false;

    function escapeHtml(value) {
        const div = document.createElement('div');
        div.textContent = value ?? '';
        return div.innerHTML;
    }

    // The API returns UTC timestamps without a zone suffix.
    function utc(value) {
        return Date.parse(/[zZ]|[+-]\d\d:?\d\d$/.test(value) ? value : `${value}Z`);
    }

    function relative(value) {
        const seconds = (Date.now() - utc(value)) / 1000;
        if (seconds < 60) return 'Just now';
        if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
        if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
        if (seconds < 86400 * 30) return `${Math.floor(seconds / 86400)}d ago`;
        return new Date(utc(value)).toLocaleDateString();
    }

    async function request(url, options = {}) {
        const response = await fetch(url, { ...options, headers: getAuthHeaders() });
        const body = await response.json().catch(() => null);
        if (!response.ok) throw new Error(body?.error || `Request failed with status ${response.status}`);
        return body;
    }

    function readStored(key, fallback) {
        try { return JSON.parse(localStorage.getItem(key)) ?? fallback; } catch { return fallback; }
    }

    function exportRow(p, destinations, templates) {
        const destination = destinations.find(d => d.id === p.outputDestinationId);
        const template = p.templateId ? templates.find(t => t.id === p.templateId) : null;
        return {
            kind: 'export', id: p.id, name: p.name, code: p.code, enabled: p.isEnabled, lastRun: p.lastExecutedAt,
            groupId: p.groupId, group: p.groupName || '', connectionId: p.connectionId,
            source: p.connectionName || 'No connection', sourceSub: p.connectionIsActive === false ? 'Connection is off' : 'Query',
            sourceWarn: p.connectionIsActive === false,
            format: p.isEmailExport ? 'Email' : (template?.outputFormat || p.outputFormat || ''),
            destination: destination?.name || 'Not set up', destinationSub: destination?.type || '',
            isEmail: p.isEmailExport,
            runBlocked: !p.isEnabled ? 'Turn the profile on to run it' : p.connectionIsActive === false ? 'Turn the connection on to run it' : null
        };
    }

    function importRow(p) {
        const database = p.targetType !== 'LocalFile';
        return {
            kind: 'import', id: p.id, name: p.name, code: p.code, enabled: p.isEnabled, lastRun: p.lastExecutedAt,
            groupId: p.groupId, group: p.groupName || '', connectionId: p.targetConnectionId,
            source: p.sourceDestinationName || SOURCE_LABELS[p.sourceType] || p.sourceType || '', sourceSub: 'Import',
            format: p.sourceFormat || '',
            destination: database ? (p.targetConnectionName || 'Not set up') : 'Local file',
            destinationSub: database ? (p.targetTable || '') : (p.localTargetFormat || ''),
            runBlocked: p.isEnabled ? null : 'Turn the profile on to run it'
        };
    }

    async function load() {
        const [exports, imports, destinations, templates, connections, groups] = await Promise.all([
            request('/api/profiles'), request('/api/profiles?type=import'), request('/api/destinations'),
            request('/api/templates'), request('/api/connections'), request('/api/groups')
        ].map(p => p.catch(error => { window.showToast(error.message, 'error'); return []; })));
        state.rows = [...exports.map(p => exportRow(p, destinations, templates)), ...imports.map(importRow)];
        state.connections = connections;
        state.groups = groups;
        fillSelect($('filter-connection'), 'All connections', connections);
        fillSelect($('filter-group'), 'All groups', groups);
        render();
    }

    function fillSelect(select, label, items) {
        const current = select.value || readStored(FILTERS_KEY, {})[select.id] || '';
        select.replaceChildren(new Option(label, ''), ...items.map(i => new Option(i.name, i.id)));
        select.value = items.some(i => String(i.id) === String(current)) ? current : '';
    }

    function filters() {
        return {
            'search-input': $('search-input').value.trim().toLowerCase(),
            'filter-kind': $('filter-kind').value,
            'filter-status': $('filter-status').value,
            'filter-connection': $('filter-connection').value,
            'filter-group': $('filter-group').value
        };
    }

    function visibleRows() {
        const f = filters();
        try { localStorage.setItem(FILTERS_KEY, JSON.stringify(f)); } catch {}
        const { column, direction } = state.sort;
        const sign = direction === 'asc' ? 1 : -1;
        const key = row => column === 'lastrun' ? (row.lastRun ? utc(row.lastRun) : 0)
            : column === 'status' ? (row.enabled ? 0 : 1)
            : String(row[column] ?? '').toLowerCase();
        return state.rows
            .filter(row => !f['filter-kind'] || row.kind === f['filter-kind'])
            .filter(row => !f['filter-status'] || (f['filter-status'] === 'enabled') === row.enabled)
            .filter(row => !f['filter-connection'] || String(row.connectionId) === f['filter-connection'])
            .filter(row => !f['filter-group'] || String(row.groupId) === f['filter-group'])
            .filter(row => !f['search-input'] || [row.name, row.code, row.source, row.group, row.format, row.destination, row.destinationSub]
                .some(v => String(v ?? '').toLowerCase().includes(f['search-input'])))
            .sort((a, b) => {
                const x = key(a), y = key(b);
                return (x < y ? -1 : x > y ? 1 : 0) * sign || a.name.localeCompare(b.name);
            });
    }

    function render() {
        const rows = visibleRows();
        const active = Object.values(filters()).some(Boolean);
        $('clear-filters-btn').hidden = !active;
        $('profile-count').textContent = `${rows.length} of ${state.rows.length}`;
        document.querySelectorAll('.rf-table th[data-sort]').forEach(th => {
            const sort = th.dataset.sort === state.sort.column ? (state.sort.direction === 'asc' ? 'ascending' : 'descending') : 'none';
            if (th.getAttribute('aria-sort') === sort && th.querySelector('svg')) return;
            th.setAttribute('aria-sort', sort);
            const button = th.querySelector('button');
            const icon = sort === 'ascending' ? 'chevron-up' : sort === 'descending' ? 'chevron-down' : 'chevrons-up-down';
            button.replaceChildren(button.firstChild.cloneNode(), Object.assign(document.createElement('i'), { className: 'rf-sort' }));
            button.lastChild.setAttribute('data-lucide', icon);
        });

        const tbody = $('profiles-tbody');
        tbody.closest('table').classList.toggle('rf-empty', rows.length === 0);
        if (rows.length === 0) {
            tbody.innerHTML = state.rows.length === 0
                ? `<tr><td colspan="8"><h2>No profiles</h2><p>An export profile queries a database and writes the rows to a destination. An import profile reads a file or API into a table.</p>
                    <div class="rf-empty-actions"><button type="button" class="rf-button rf-primary" data-new="export">New export profile</button><button type="button" class="rf-button" data-new="import">New import profile</button></div></td></tr>`
                : `<tr><td colspan="8"><h2>No matching profiles</h2><p>No profile matches the current search and filters.</p><button type="button" class="rf-link" data-clear>Clear filters</button></td></tr>`;
            window.queueLucideRender && window.queueLucideRender();
            return;
        }

        tbody.innerHTML = rows.map(row => `
            <tr data-kind="${row.kind}" data-id="${row.id}">
                <td><span class="rf-name">${escapeHtml(row.name)}</span><span class="rf-sub">${row.kind === 'import' ? 'Import' : 'Export'}${row.code ? ` · <span class="rf-code">${escapeHtml(row.code)}</span>` : ''}</span></td>
                <td><span class="rf-name">${escapeHtml(row.source)}</span><span class="rf-sub${row.sourceWarn ? ' rf-warn' : ''}">${escapeHtml(row.sourceSub)}</span></td>
                <td class="rf-opt">${escapeHtml(row.group || 'None')}</td>
                <td class="rf-opt">${escapeHtml(row.format)}</td>
                <td><span class="rf-name">${escapeHtml(row.destination)}</span><span class="rf-sub">${escapeHtml(row.destinationSub)}</span></td>
                <td><span class="rf-status ${row.enabled ? 'rf-status-on' : 'rf-status-off'}"><svg viewBox="0 0 8 8" aria-hidden="true"><circle cx="4" cy="4" r="3.5" fill="${row.enabled ? 'currentColor' : 'none'}" stroke="currentColor"/></svg>${row.enabled ? 'On' : 'Off'}</span></td>
                <td title="${row.lastRun ? escapeHtml(new Date(utc(row.lastRun)).toLocaleString()) : ''}">${escapeHtml(row.lastRun ? relative(row.lastRun) : 'Never')}</td>
                <td><div class="rf-actions">
                    <button type="button" class="rf-action rf-action-run" data-action="run" ${row.runBlocked ? 'disabled' : ''} aria-label="Run ${escapeHtml(row.name)}" data-tooltip="${escapeHtml(row.runBlocked || (row.isEmail ? 'Run and send emails' : 'Run now'))}"><i data-lucide="play" class="rf-icon"></i></button>
                    <button type="button" class="rf-action" data-action="edit" aria-label="Edit ${escapeHtml(row.name)}" data-tooltip="Edit"><i data-lucide="pencil" class="rf-icon"></i></button>
                    <button type="button" class="rf-action" data-action="download" aria-label="Download ${escapeHtml(row.name)} as JSON" data-tooltip="Download JSON"><i data-lucide="download" class="rf-icon"></i></button>
                    <button type="button" class="rf-action" data-action="toggle" aria-label="${row.enabled ? 'Turn off' : 'Turn on'} ${escapeHtml(row.name)}" data-tooltip="${row.enabled ? 'Turn off' : 'Turn on'}"><i data-lucide="${row.enabled ? 'pause' : 'power'}" class="rf-icon"></i></button>
                    <button type="button" class="rf-action rf-action-danger" data-action="delete" aria-label="Delete ${escapeHtml(row.name)}" data-tooltip="Delete"><i data-lucide="trash-2" class="rf-icon"></i></button>
                </div></td>
            </tr>`).join('');
        window.queueLucideRender && window.queueLucideRender();
    }

    function rowFor(element) {
        const tr = element.closest('tr[data-id]');
        return tr && state.rows.find(r => r.kind === tr.dataset.kind && String(r.id) === tr.dataset.id);
    }

    async function run(row, button) {
        if (row.isEmail && !await window.showConfirmModal({
            title: `Send emails for "${row.name}"?`,
            message: 'The profile sends one email per row, or per group, to the recipients in the query result.',
            confirmText: 'Send emails', cancelText: 'Cancel', danger: true
        })) return;
        button.setAttribute('aria-busy', 'true');
        button.disabled = true;
        try {
            const result = row.kind === 'import'
                ? await request(`/api/profiles/${row.id}/execute?type=import`, { method: 'POST' })
                : await request(`/api/executions/execute/${row.id}`, { method: 'POST' });
            const counts = row.kind === 'import' ? `: ${result.rowsInserted ?? 0} inserted, ${result.rowsUpdated ?? 0} updated, ${result.rowsSkipped ?? 0} skipped, ${result.rowsFailed ?? 0} failed` : '';
            const failed = /fail/i.test(result?.status || '');
            window.showToast(failed ? `Run failed${result.error ? `: ${result.error}` : counts}` : `Run ${result?.status === 'PartialSuccess' ? 'partly succeeded' : 'succeeded'}${counts}`,
                failed ? 'error' : result?.status === 'PartialSuccess' ? 'warning' : 'success');
            if (row.isEmail && typeof window.updateApprovalBadge === 'function') await window.updateApprovalBadge();
        } catch (error) {
            window.showToast(error.message, 'error');
        } finally {
            await load();
        }
    }

    async function download(row) {
        const doc = await request(`/api/profile-documents/${row.kind}/${row.id}`);
        const link = document.createElement('a');
        link.href = URL.createObjectURL(new Blob([JSON.stringify(doc, null, 2)], { type: 'application/json' }));
        link.download = `${row.name.replace(/[^\w.-]+/g, '_')}.json`;
        link.click();
        URL.revokeObjectURL(link.href);
    }

    async function toggle(row) {
        const action = row.enabled ? 'disable' : 'enable';
        await request(`/api/${row.kind === 'import' ? 'import-profiles' : 'profiles'}/${row.id}/${action}`, { method: 'POST' });
        window.showToast(`${row.name} turned ${row.enabled ? 'off' : 'on'}`, 'success');
        await load();
    }

    async function remove(row) {
        if (!await window.showConfirmModal({ title: `Delete "${row.name}"?`, message: 'Deleting a profile cannot be undone.', confirmText: 'Delete profile', cancelText: 'Keep profile', danger: true })) return;
        await request(`/api/profiles/${row.id}${row.kind === 'import' ? '?type=import' : ''}`, { method: 'DELETE' });
        window.clearApiCache && window.clearApiCache('/api/profiles');
        window.showToast(`${row.name} deleted`, 'success');
        await load();
    }

    function setMenu(open) {
        $('newProfileMenu').hidden = !open;
        $('newProfileButton').setAttribute('aria-expanded', String(open));
        if (open) $('newProfileMenu').querySelector('button').focus();
    }

    function onDocumentClick(e) {
        if (!$('profiles-tbody')) return;
        const clear = e.target.closest('#clear-filters-btn, [data-clear]');
        if (clear) {
            ['search-input', 'filter-kind', 'filter-status', 'filter-connection', 'filter-group'].forEach(id => { $(id).value = ''; });
            render();
            return;
        }
        const create = e.target.closest('[data-new]');
        if (create) { setMenu(false); window.ReefProfileEditor.open(create.dataset.new); return; }
        if (e.target.closest('#newProfileButton')) { setMenu($('newProfileMenu').hidden); return; }
        if (!e.target.closest('#newProfileMenu')) setMenu(false);
    }

    function init() {
        const stored = readStored(FILTERS_KEY, {});
        ['search-input', 'filter-kind', 'filter-status'].forEach(id => { if (stored[id]) $(id).value = stored[id]; });
        state.sort = readStored(SORT_KEY, state.sort);

        ['search-input', 'filter-kind', 'filter-status', 'filter-connection', 'filter-group'].forEach(id => $(id).addEventListener('input', render));
        if (!documentListeners) {
            documentListeners = true;
            document.addEventListener('click', onDocumentClick);
            document.addEventListener('keydown', e => { if (e.key === 'Escape' && $('newProfileMenu') && !$('newProfileMenu').hidden) { setMenu(false); $('newProfileButton').focus(); } });
        }

        document.querySelector('.rf-table thead').addEventListener('click', e => {
            const th = e.target.closest('th[data-sort]');
            if (!th) return;
            state.sort = { column: th.dataset.sort, direction: state.sort.column === th.dataset.sort && state.sort.direction === 'asc' ? 'desc' : 'asc' };
            try { localStorage.setItem(SORT_KEY, JSON.stringify(state.sort)); } catch {}
            render();
        });

        $('profiles-tbody').addEventListener('click', async e => {
            const row = rowFor(e.target);
            if (!row) return;
            const button = e.target.closest('[data-action]');
            const action = button ? button.dataset.action : 'edit';
            try {
                if (action === 'edit') window.ReefProfileEditor.open(row.kind, row.id);
                else if (action === 'run') await run(row, button);
                else if (action === 'download') await download(row);
                else if (action === 'toggle') await toggle(row);
                else if (action === 'delete') await remove(row);
            } catch (error) {
                window.showToast(error.message, 'error');
            }
        });

        window.loadProfiles = load;
        load();
    }

    window.ReefProfilesList = { init };
})();
