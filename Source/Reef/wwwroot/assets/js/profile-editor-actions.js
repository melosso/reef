(function () {
    const h = () => window.ReefProfileEditor.helpers;

    function section(title, children) {
        const { el } = h();
        return el('section', { class: 'reef-ed-fields reef-ed-section' }, [el('h4', { text: title }), ...children]);
    }

    function button(label, iconName, onclick, tooltip) {
        const { el, icon } = h();
        return el('button', { type: 'button', class: 'reef-ed-button', 'data-tooltip': tooltip, 'data-tooltip-position': 'left', onclick: async e => {
            const target = e.currentTarget;
            target.setAttribute('aria-busy', 'true');
            try { await onclick(); } catch (error) { window.showToast(error.message, 'error'); } finally { target.removeAttribute('aria-busy'); }
        } }, [icon(iconName, 'reef-ed-icon-sm'), el('span', { text: label })]);
    }

    function note(text) {
        return h().el('p', { class: 'reef-ed-hint-text', text });
    }

    function dataTable(columns, rows) {
        const { el, textOf } = h();
        return el('div', { class: 'reef-ed-preview-scroll' }, [el('table', {}, [
            el('thead', {}, [el('tr', {}, columns.map(c => el('th', { text: c })))]),
            el('tbody', {}, rows.map(row => el('tr', {}, columns.map(c => el('td', { text: textOf(row[c]) })))))
        ])]);
    }

    function output() {
        return h().el('div', { class: 'reef-ed-preview' });
    }

    function needsSaved(context, title) {
        return context.id ? null : section(title, [note('Save the profile first')]);
    }

    function queryPreview(context) {
        const box = output();
        return section('Preview', [
            button('Preview rows', 'table', async () => {
                const result = await h().request('/api/profiles/test-query-preview', { method: 'POST', body: JSON.stringify({ connectionId: context.config.ConnectionId || 0, query: context.config.Query || '' }) });
                if (!result.success) throw new Error(result.error || 'The query failed');
                box.replaceChildren(note(`${result.rowCount} rows in ${result.totalExecutionTimeMs} ms, showing up to 25`), dataTable(result.columns, result.rows));
            }),
            box
        ]);
    }

    function scriptTest(context, prefix) {
        if (context.config[`${prefix}Type`] !== 'Script') return null;
        const box = output();
        return section('Test', [
            button('Run script', 'terminal', async () => {
                const cfg = context.config[`${prefix}Config`] || {};
                const result = await h().request('/api/profiles/test-script', { method: 'POST', body: JSON.stringify({
                    profileId: context.id || null, interpreter: cfg.Interpreter || '', scriptPathOrInline: cfg.ScriptPathOrInline || '',
                    scriptIsInline: cfg.ScriptIsInline !== false, timeout: cfg.Timeout || 30, envAllowlist: cfg.EnvAllowlist || null
                }) });
                const { el } = h();
                box.replaceChildren(
                    note(`Exit code ${result.exitCode} in ${result.elapsedMs} ms`),
                    el('pre', { class: 'reef-ed-output', text: [result.stdout, result.stderr, result.errorMessage].filter(Boolean).join('\n') || 'No output' })
                );
                if (!result.success) window.showToast(result.errorMessage || `Script exited with code ${result.exitCode}`, 'error');
            }, 'Runs the script once without saving'),
            box
        ]);
    }

    function deltaSync(context) {
        const missing = needsSaved(context, 'Sync state');
        if (missing) return missing;
        const base = context.kind === 'import' ? `/api/import-profiles/${context.id}` : `/api/profiles/${context.id}`;
        const box = output();
        const load = async () => {
            const stats = await h().request(`${base}/delta-sync/stats`);
            if (!stats.enabled) { box.replaceChildren(note(stats.message || 'Smart Sync is not saved on this profile yet')); return; }
            box.replaceChildren(note(`${stats.activeRows} tracked, ${stats.deletedRows} deleted, last run ${stats.newRowsLastRun ?? 0} new, ${stats.changedRowsLastRun ?? 0} changed, ${stats.unchangedRowsLastRun ?? 0} unchanged`));
        };
        load().catch(() => box.replaceChildren(note('Sync state is not available')));
        return section('Sync state', [
            box,
            button('Reset sync state', 'rotate-ccw', async () => {
                if (!window.confirm('Reset Smart Sync? The next run treats every row as new.')) return;
                await h().request(`${base}/delta-sync/reset`, { method: context.kind === 'import' ? 'DELETE' : 'POST', body: context.kind === 'import' ? undefined : '{}' });
                window.showToast('Smart Sync state reset', 'success');
                await load();
            }, 'Forget all tracked rows')
        ]);
    }

    function emailPreview(context) {
        const missing = needsSaved(context, 'Preview');
        if (missing) return missing;
        const box = output();
        return section('Preview', [
            button('Preview first email', 'mail-open', async () => {
                const result = await h().request(`/api/profiles/${context.id}/preview-email-html`, { method: 'POST', body: '{}' });
                if (!result.success) throw new Error(result.message || 'Preview failed');
                box.replaceChildren(note(result.message || ''), h().el('iframe', { class: 'reef-ed-frame', sandbox: '', title: 'Email preview', srcdoc: result.html || '' }));
            }, 'Uses the saved profile'),
            box
        ]);
    }

    function importSource() {
        return section('Test', [
            button('Check source', 'plug', async () => {
                const profile = await h().flatten();
                const result = await h().request('/api/import-profiles/test-source', { method: 'POST', body: JSON.stringify(profile) });
                window.showToast(result.message || (result.success ? 'Source is reachable' : 'Source check failed'), result.success ? 'success' : 'error');
            })
        ]);
    }

    function importPreview() {
        const box = output();
        return section('Preview', [
            button('Preview records', 'table', async () => {
                const profile = await h().flatten();
                const result = await h().request('/api/import-profiles/preview', { method: 'POST', body: JSON.stringify({ ...profile, importProfileId: null, maxRows: 10 }) });
                if (!result.success) throw new Error(result.errorMessage || 'Preview failed');
                box.replaceChildren(note(`${result.totalRowsParsed} records read, showing ${result.rowsReturned}`), dataTable(result.columns, result.rows));
                (result.parseWarnings || []).slice(0, 3).forEach(w => window.showToast(w, 'warning'));
            }, 'Reads the source with the current settings'),
            box
        ]);
    }

    function importTarget(context) {
        const box = output();
        return section('Test', [
            button('Check target', 'plug', async () => {
                const profile = await h().flatten();
                const result = await h().request('/api/import-profiles/test-target', { method: 'POST', body: JSON.stringify(profile) });
                window.showToast(result.message || (result.success ? 'Target is reachable' : 'Target check failed'), result.success ? 'success' : 'error');
                if (!result.success || context.config.TargetType === 'LocalFile') return;
                const schema = await h().request('/api/import-profiles/target-schema', { method: 'POST', body: JSON.stringify(profile) });
                const columns = Array.isArray(schema) ? schema : schema.columns || [];
                box.replaceChildren(note(columns.length ? `Columns: ${columns.map(c => c.columnName || c.name).join(', ')}` : 'No columns found'));
            }),
            box
        ]);
    }

    function webhooks(context) {
        const missing = needsSaved(context, 'Webhooks');
        if (missing) return missing;
        const { el, request } = h();
        const owner = context.kind === 'import' ? 'importProfileId' : 'profileId';
        const list = el('div', { class: 'reef-ed-list' });
        const reveal = output();

        const showUrl = (url) => {
            const field = el('input', { type: 'text', readonly: true, value: url, 'aria-label': 'Webhook URL', onfocus: e => e.target.select() });
            reveal.replaceChildren(note('Copy this URL now, the token is shown only once'), field);
            navigator.clipboard?.writeText(url).then(() => window.showToast('Webhook URL copied', 'success')).catch(() => {});
        };

        const load = async () => {
            const hooks = await request(`/api/webhooks?${owner}=${context.id}`);
            list.replaceChildren(...(hooks.length ? hooks.map(hook => el('div', { class: 'reef-ed-list-row' }, [
                el('span', { class: 'reef-ed-list-main', text: hook.token }),
                el('span', { class: 'reef-ed-hint-text', text: `${hook.isActive ? 'On' : 'Off'}, ${hook.triggerCount} calls` }),
                el('button', { type: 'button', class: 'reef-ed-link', onclick: () => act(`/api/webhooks/${hook.id}/${hook.isActive ? 'disable' : 'enable'}`, 'POST') }, [el('span', { text: hook.isActive ? 'Turn off' : 'Turn on' })]),
                el('button', { type: 'button', class: 'reef-ed-link', onclick: async () => {
                    if (!window.confirm('Create a new token? The current URL stops working.')) return;
                    const result = await request(`/api/webhooks/${hook.id}/regenerate`, { method: 'POST', body: '{}' });
                    showUrl(result.url);
                    await load();
                } }, [el('span', { text: 'New token' })]),
                el('button', { type: 'button', class: 'reef-ed-link reef-ed-danger', onclick: () => window.confirm('Delete this webhook?') && act(`/api/webhooks/${hook.id}`, 'DELETE') }, [el('span', { text: 'Delete' })])
            ])) : [note('No webhooks yet')]));
        };

        const act = async (url, method) => {
            try {
                await request(url, { method, body: method === 'POST' ? '{}' : undefined });
                await load();
            } catch (error) {
                window.showToast(error.message, 'error');
            }
        };

        load().catch(error => list.replaceChildren(note(error.message)));
        return section('Webhooks', [
            list,
            button('Add webhook', 'link', async () => {
                const result = await request('/api/webhooks', { method: 'POST', body: JSON.stringify({ [owner]: context.id }) });
                showUrl(result.url);
                await load();
            }, 'Trigger this profile with an HTTP POST'),
            reveal
        ]);
    }

    const BY_STEP = {
        'export:settings': c => [webhooks(c)],
        'import:settings': c => [webhooks(c)],
        'export:source': c => [queryPreview(c)],
        'export:preprocess': c => [scriptTest(c, 'PreProcess')],
        'export:postprocess': c => [scriptTest(c, 'PostProcess')],
        'export:deltasync': c => [deltaSync(c)],
        'export:emailexport': c => [emailPreview(c)],
        'import:preprocess': c => [scriptTest(c, 'PreProcess')],
        'import:postprocess': c => [scriptTest(c, 'PostProcess')],
        'import:source': () => [importSource()],
        'import:parse': () => [importPreview()],
        'import:target': c => [importTarget(c)],
        'import:deltasync': c => [deltaSync(c)]
    };

    window.ReefProfileEditorActions = context => {
        const render = BY_STEP[`${context.kind}:${context.node ? context.node.type : 'settings'}`];
        return render ? render(context).filter(Boolean) : [];
    };
})();
