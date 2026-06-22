'use strict';

// Dedicated full page, not a modal - uses its own lightweight dirty guard
// instead of the shared modal dirty-watcher (resumable server state already
// covers most of what that protects against).

const API_BASE = window.location.origin;

let recipes = [];
let connections = [];
let groups = [];
let currentRun = null;
let currentStepDirty = false;

// Client-side only - the recipe list is small enough that filtering server-side would just
// be a round-trip for no benefit. "All" is synthetic, never a real Category value.
const ALL_CATEGORIES = 'All';
let recipeSearchQuery = '';
let activeCategory = ALL_CATEGORIES;

// Pure client-side UI affordance, never persisted server-side. Simple mode (default) hides
// CanAutoProvision steps (e.g. WooCommerce's "connection"/"group" steps) from the rail
// entirely - they were already silently provisioned for real by RecipeService.StartRecipeAsync.
// Flipping this on reveals them again so the user can override with their own entity, which
// just flows through the normal save path (ExecuteStepAsync) like any manual step.
let advancedMode = false;

// Per-runId: has the resume summary already been shown this session?
const resumeSummaryShownForRunId = new Set();

async function init() {
    queueLucideRender();
    await loadRecipes();
}

async function loadRecipes() {
    try {
        const response = await fetch(`${API_BASE}/api/recipes`, { headers: getAuthHeaders() });
        if (!response.ok) throw new Error('Failed to load recipes');
        recipes = await response.json();
        renderGallery();
    } catch (error) {
        showMessage('Failed to load recipes', 'error');
    }
}

function renderGallery() {
    const controls = document.getElementById('recipe-gallery-controls');
    const grid = document.getElementById('recipe-gallery-grid');

    if (!recipes.length) {
        controls.classList.add('hidden');
        grid.className = 'grid grid-cols-1';
        grid.innerHTML = `
            <div class="bg-white rounded-lg shadow p-8 text-center text-slate-500">
                <i data-lucide="package" class="h-10 w-10 mx-auto mb-3 text-slate-300"></i>
                <p>No recipes available yet.</p>
            </div>`;
        queueLucideRender();
        return;
    }

    controls.classList.remove('hidden');
    grid.className = 'grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4';
    renderCategoryChips();
    renderRecipeGrid();
}

function recipeCategories() {
    return [...new Set(recipes.map(r => r.category))];
}

function renderCategoryChips() {
    const container = document.getElementById('recipe-category-chips');
    const categories = [ALL_CATEGORIES, ...recipeCategories()];

    container.innerHTML = categories.map(cat => {
        const isActive = cat === activeCategory;
        return `
            <button type="button" onclick="setActiveCategory('${escapeForJS(cat)}')"
                    class="h-8 px-3 rounded-full text-xs font-medium border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 ${isActive ? 'bg-slate-900 text-white border-slate-900' : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'}">
                ${escapeHtml(cat)}
            </button>`;
    }).join('');
}

function setActiveCategory(category) {
    activeCategory = category;
    renderCategoryChips();
    renderRecipeGrid();
}

function onRecipeFilterChanged() {
    recipeSearchQuery = (document.getElementById('recipe-search-input').value || '').trim().toLowerCase();
    renderRecipeGrid();
}

function filteredRecipes() {
    return recipes.filter(recipe => {
        if (activeCategory !== ALL_CATEGORIES && recipe.category !== activeCategory) return false;
        if (!recipeSearchQuery) return true;
        return recipe.name.toLowerCase().includes(recipeSearchQuery) ||
            recipe.description.toLowerCase().includes(recipeSearchQuery);
    });
}

function renderRecipeGrid() {
    const grid = document.getElementById('recipe-gallery-grid');
    const visible = filteredRecipes();

    if (!visible.length) {
        grid.innerHTML = `
            <div class="bg-white rounded-lg shadow p-8 text-center text-slate-500 md:col-span-2 lg:col-span-3">
                <i data-lucide="search-x" class="h-10 w-10 mx-auto mb-3 text-slate-300"></i>
                <p>No recipes match your search or filter.</p>
            </div>`;
        queueLucideRender();
        return;
    }

    grid.innerHTML = visible.map(recipe => recipeCardHtml(recipe)).join('');
    queueLucideRender();
}

function recipeCardHtml(recipe) {
    const isInProgress = !!recipe.existingRunId;
    const hasCompletedRun = !!recipe.lastCompletedRunId;

    // Installed takes precedence over "Completed" - both would describe a finished setup
    // and showing both is redundant noise on one card. In-progress always wins over either
    // since it reflects the most recent run state.
    const badge = isInProgress
        ? '<span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-amber-100 text-amber-800">In progress</span>'
        : recipe.isInstalled
            ? '<span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs font-medium bg-green-100 text-green-800"><i data-lucide="check" class="h-3 w-3"></i>Installed</span>'
            : '';

    const primaryLabel = isInProgress ? 'Resume Setup' : hasCompletedRun ? 'Run Again' : 'Start Setup';
    const primaryAction = isInProgress
        ? `resumeRecipe(${recipe.existingRunId})`
        : `startRecipe('${escapeForJS(recipe.key)}')`;

    const reconfigureButton = hasCompletedRun
        ? `<button onclick="startReconfigure('${escapeForJS(recipe.key)}', ${recipe.lastCompletedRunId})"
                   class="h-9 px-3 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 shrink-0">
               Reconfigure
           </button>`
        : '';

    return `
        <div class="bg-white rounded-lg shadow p-5 flex flex-col gap-3">
            <div class="flex items-start gap-3">
                <div class="h-10 w-10 rounded-md bg-blue-50 text-blue-600 flex items-center justify-center shrink-0">
                    <i data-lucide="${escapeHtml(recipe.icon)}" class="h-5 w-5"></i>
                </div>
                <div class="min-w-0">
                    <h3 class="text-base font-semibold text-slate-900">${escapeHtml(recipe.name)}</h3>
                    <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-slate-100 text-slate-600 mt-1">${escapeHtml(recipe.category)}</span>
                </div>
            </div>
            <p class="text-sm text-slate-500 flex-1">${escapeHtml(recipe.description)}</p>
            <div class="flex items-center justify-between gap-2">
                <p class="text-xs text-slate-400">${recipe.stepCount} steps</p>
                ${badge}
            </div>
            <div class="flex items-center gap-2 pt-1">
                ${reconfigureButton}
                <button onclick="${primaryAction}"
                        class="h-9 px-3 flex-1 bg-slate-900 text-white text-sm font-medium rounded-md hover:bg-slate-800 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">
                    ${primaryLabel}
                </button>
            </div>
        </div>`;
}

async function startRecipe(key) {
    try {
        const response = await fetch(`${API_BASE}/api/recipes/${key}/start`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.error || 'Failed to start recipe');
        }
        currentRun = await response.json();
        advancedMode = false;
        await preloadReferenceData();
        showWizard();
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

async function resumeRecipe(runId) {
    try {
        const response = await fetch(`${API_BASE}/api/recipes/runs/${runId}`, { headers: getAuthHeaders() });
        if (!response.ok) throw new Error('Failed to load recipe run');
        currentRun = await response.json();
        advancedMode = false;
        await preloadReferenceData();

        const hasPriorProgress = currentRun.steps.some(s => s.entityId || s.verified || s.skipped);
        if (hasPriorProgress && !resumeSummaryShownForRunId.has(runId)) {
            resumeSummaryShownForRunId.add(runId);
            showResumeSummary();
        } else {
            showWizard();
        }
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

// Pre-seeds a new run from a prior Completed run's entities/params (RecipeService
// cloneFromRunId) so re-pointing at a different store reuses the same Connection/Group/
// Profile records - only the WooCommerce-specific fields need re-entering.
async function startReconfigure(key, completedRunId) {
    const confirmed = await showConfirmModal({
        title: 'Reconfigure this recipe?',
        message: 'This starts a new setup run pre-filled from your last completed setup. Your existing Connection, Group, and Profiles are reused - you\'ll just need to enter the new store\'s WooCommerce credentials where it applies.',
        confirmText: 'Reconfigure',
        cancelText: 'Cancel'
    });
    if (!confirmed) return;

    try {
        const response = await fetch(`${API_BASE}/api/recipes/${key}/reconfigure`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify({ runId: completedRunId })
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.error || 'Failed to start reconfiguration');
        }
        currentRun = await response.json();
        advancedMode = false;
        await preloadReferenceData();
        showWizard();
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

function showResumeSummary() {
    document.getElementById('recipe-gallery').classList.add('hidden');
    document.getElementById('wizard-view').classList.add('hidden');
    document.getElementById('completion-view').classList.add('hidden');
    document.getElementById('resume-summary-view').classList.remove('hidden');

    document.getElementById('resume-summary-recipe-name').textContent = `Resume: ${currentRun.recipeName}`;

    const list = document.getElementById('resume-summary-list');
    list.innerHTML = currentRun.steps.map(step => {
        const icon = stepStatusIcon(step);
        const status = step.skipped ? 'Skipped'
            : step.verified ? 'Verified'
            : step.entityId ? 'Saved, not yet verified'
            : 'Not started';
        const statusClass = step.skipped ? 'text-slate-500'
            : step.verified ? 'text-green-700'
            : step.entityId ? 'text-amber-700'
            : 'text-slate-500';

        return `
            <button onclick="resumeAtStep('${escapeForJS(step.stepKey)}')"
                    class="w-full flex items-center gap-3 px-3 py-2 rounded-md text-left hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">
                ${icon}
                <span class="text-sm text-slate-700 flex-1">${escapeHtml(step.title)}</span>
                <span class="text-xs font-medium ${statusClass}">${status}</span>
            </button>`;
    }).join('');

    queueLucideRender();
}

function resumeAtStep(stepKey) {
    currentRun.currentStepKey = stepKey;
    showWizard();
}

function confirmResumeFromSummary() {
    showWizard();
}

async function preloadReferenceData() {
    try {
        const [connRes, groupRes] = await Promise.all([
            fetch(`${API_BASE}/api/connections`, { headers: getAuthHeaders() }),
            fetch(`${API_BASE}/api/groups`, { headers: getAuthHeaders() })
        ]);
        connections = connRes.ok ? await connRes.json() : [];
        groups = groupRes.ok ? await groupRes.json() : [];
    } catch {
        connections = [];
        groups = [];
    }
}

function showWizard() {
    document.getElementById('recipe-gallery').classList.add('hidden');
    document.getElementById('completion-view').classList.add('hidden');
    document.getElementById('resume-summary-view').classList.add('hidden');
    document.getElementById('wizard-view').classList.remove('hidden');

    document.getElementById('wizard-recipe-name').textContent = currentRun.recipeName;
    document.getElementById('wizard-recipe-description').textContent =
        currentRun.status === 'Completed' ? 'This setup is complete.' : 'Follow each step below - save and verify before moving on.';

    const toggleSlot = document.getElementById('wizard-advanced-toggle-slot');
    if (toggleSlot) toggleSlot.innerHTML = renderAdvancedToggle();

    renderStepRail();
    renderActiveStep();
    queueLucideRender();
}

function exitWizard() {
    currentRun = null;
    currentStepDirty = false;
    document.getElementById('wizard-view').classList.add('hidden');
    document.getElementById('completion-view').classList.add('hidden');
    document.getElementById('resume-summary-view').classList.add('hidden');
    document.getElementById('recipe-gallery').classList.remove('hidden');
    loadRecipes();
}

async function abandonWizard() {
    if (!currentRun) return;
    const confirmed = await showConfirmModal({
        title: 'Abandon this setup?',
        message: 'Anything already created (Connections, Profiles, etc.) stays intact and usable - only the wizard\'s progress tracking is cleared.',
        confirmText: 'Abandon setup',
        cancelText: 'Keep going',
        danger: true
    });
    if (!confirmed) return;

    try {
        await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/abandon`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        showMessage('Setup abandoned', 'success');
        exitWizard();
    } catch (error) {
        showMessage('Failed to abandon setup', 'error');
    }
}

// Simple mode (default, advancedMode === false) skips CanAutoProvision steps entirely - not
// even a "done automatically" placeholder row - since RecipeService already silently
// provisioned a real Connection/Group for them. Advanced mode shows every step like normal.
function visibleSteps() {
    return currentRun.steps.filter(s => advancedMode || !s.canAutoProvision);
}

function toggleAdvancedMode() {
    advancedMode = !advancedMode;
    // If the user is currently sitting on a step that just became hidden, hop to the first
    // visible step so the panel doesn't show an orphaned step.
    if (!advancedMode && !visibleSteps().some(s => s.stepKey === currentRun.currentStepKey)) {
        const firstVisible = visibleSteps()[0];
        if (firstVisible) currentRun.currentStepKey = firstVisible.stepKey;
    }
    renderStepRail();
    renderActiveStep();
}

function renderAdvancedToggle() {
    const hasAutoProvisionedSteps = currentRun.steps.some(s => s.canAutoProvision);
    if (!hasAutoProvisionedSteps) return '';
    return `
        <button type="button" onclick="toggleAdvancedMode()"
                class="inline-flex items-center gap-1.5 h-8 px-3 rounded-md text-xs font-medium border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 ${advancedMode ? 'bg-slate-900 text-white border-slate-900' : 'bg-white text-slate-600 border-slate-200 hover:bg-slate-50'}">
            <i data-lucide="sliders-horizontal" class="h-3.5 w-3.5"></i>
            Advanced${advancedMode ? ': on' : ''}
        </button>`;
}

function renderStepRail() {
    const rail = document.getElementById('step-rail');
    const steps = visibleSteps();
    const groupedShared = steps.filter(s => s.isShared);

    const renderGroup = (steps, label) => `
        ${label ? `<h4 class="px-2 pt-3 pb-1 text-[10px] font-bold text-slate-400 uppercase tracking-wider">${label}</h4>` : ''}
        ${steps.map(step => renderRailItem(step)).join('')}
    `;

    // Each flow (flowGroup) gets its own rail section rather than one flattened list.
    const flowGroups = [];
    steps.filter(s => !s.isShared).forEach(step => {
        const label = step.flowGroup || 'Setup';
        let group = flowGroups.find(g => g.label === label);
        if (!group) {
            group = { label, steps: [] };
            flowGroups.push(group);
        }
        group.steps.push(step);
    });

    rail.innerHTML = renderGroup(groupedShared, 'Shared') +
        flowGroups.map(g => renderGroup(g.steps, g.label)).join('');
    queueLucideRender();
}

function renderRailItem(step) {
    const isActive = step.stepKey === currentRun.currentStepKey;
    const icon = stepStatusIcon(step);
    const activeClass = isActive ? 'bg-blue-50 text-blue-700 font-medium' : 'text-slate-600 hover:bg-slate-50';

    return `
        <button onclick="goToStep('${escapeForJS(step.stepKey)}')" aria-current="${isActive ? 'step' : 'false'}"
                class="w-full flex items-center gap-2.5 px-2.5 py-2 rounded-md text-sm text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 ${activeClass}">
            ${icon}
            <span class="truncate">${escapeHtml(step.title)}</span>
            ${step.isOptional ? '<span class="ml-auto text-[10px] text-slate-400 uppercase">optional</span>' : ''}
        </button>`;
}

function stepStatusIcon(step) {
    if (step.skipped) return '<i data-lucide="minus-circle" class="h-4 w-4 text-slate-300 shrink-0"></i>';
    if (step.verified) return '<i data-lucide="check-circle-2" class="h-4 w-4 text-green-500 shrink-0"></i>';
    if (step.entityId) return '<i data-lucide="circle-dot" class="h-4 w-4 text-amber-500 shrink-0"></i>';
    return '<i data-lucide="circle" class="h-4 w-4 text-slate-300 shrink-0"></i>';
}

async function goToStep(stepKey) {
    if (currentStepDirty) {
        const confirmed = await showConfirmModal({
            title: 'Discard unsaved changes?',
            message: 'You have unsaved changes on this step. Switching steps will discard them.',
            confirmText: 'Discard & switch',
            cancelText: 'Stay here',
            danger: true
        });
        if (!confirmed) return;
    }
    currentRun.currentStepKey = stepKey;
    currentStepDirty = false;
    renderStepRail();
    renderActiveStep();
}

function getStep(stepKey) {
    return currentRun.steps.find(s => s.stepKey === stepKey);
}

function renderActiveStep() {
    const panel = document.getElementById('step-panel');
    const step = getStep(currentRun.currentStepKey);
    if (!step) {
        panel.innerHTML = `<p class="text-slate-500">Step not found.</p>`;
        return;
    }

    panel.innerHTML = `
        <div class="flex items-center justify-between mb-4">
            <h3 class="text-base font-semibold text-slate-900">${escapeHtml(step.title)}</h3>
            ${step.isOptional ? `<button onclick="skipCurrentStep()" class="text-sm text-slate-500 hover:text-slate-700">Skip this step</button>` : ''}
        </div>
        <div id="step-form-area"></div>
        <div id="step-result-area" class="mt-4" aria-live="polite"></div>
        <div class="flex items-center gap-3 mt-6">
            <button type="button" id="step-save-btn" onclick="saveCurrentStep()"
                    class="h-9 px-4 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 flex items-center">
                <svg id="step-save-spinner" class="animate-spin h-4 w-4 mr-2 hidden" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                    <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
                    <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"></path>
                </svg>
                <span id="step-save-btn-text">${step.hasVerifier ? 'Save & Verify' : 'Save'}</span>
            </button>
            ${canAdvance() ? `<button type="button" onclick="advanceOrComplete()" class="h-9 px-4 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">Next step</button>` : ''}
        </div>
    `;

    renderStepForm(step);
    if (step.lastVerifyMessage) {
        renderStepResult(step.verified, step.lastVerifyMessage);
    }
    queueLucideRender();
    wireDirtyWatcher();
}

function canAdvance() {
    const step = getStep(currentRun.currentStepKey);
    return step && (step.verified || step.skipped || !step.hasVerifier);
}

function wireDirtyWatcher() {
    const area = document.getElementById('step-form-area');
    if (!area) return;
    area.querySelectorAll('input, select, textarea').forEach(el => {
        el.addEventListener('input', () => { currentStepDirty = true; });
        el.addEventListener('change', () => { currentStepDirty = true; });
    });
}

// "shipments-*" step keys are Flow B (Tracking Link) - same shape as Flow A's steps,
// just different staging table/template/query defaults.
function isShipmentsStep(stepKey) {
    return (stepKey || '').startsWith('shipments-');
}

// ExactGlobeRecipe's two flows use "debtors-*"/"items-*" prefixes instead of "shipments-*" -
// a different two-flow shape (no shared import side), so it gets its own helpers rather than
// overloading isShipmentsStep's semantics.
function isDebtorsStep(stepKey) {
    return (stepKey || '').startsWith('debtors-');
}

function isItemsStep(stepKey) {
    return (stepKey || '').startsWith('items-');
}

function isExactGlobeRecipe() {
    return currentRun && currentRun.recipeKey === 'exact-globe-data-export';
}

function renderStepForm(step) {
    const area = document.getElementById('step-form-area');
    const params = (step.params) || {};

    switch (step.entityType) {
        case 'Connection':
            area.innerHTML = connectionForm(params);
            break;
        case 'Group':
            area.innerHTML = groupForm(params);
            break;
        case 'Destination':
            area.innerHTML = destinationForm(params, step.stepKey);
            renderDestinationReuseBanner(step.stepKey);
            break;
        case 'StagingTable':
            area.innerHTML = stagingTableForm(params, step.stepKey);
            break;
        case 'ImportProfile':
            area.innerHTML = importProfileForm(params, step.stepKey);
            break;
        case 'QueryTemplate':
            area.innerHTML = queryTemplateForm(params, step.stepKey);
            break;
        case 'Profile':
            area.innerHTML = exportProfileForm(params, step.stepKey);
            break;
        case 'Job':
            area.innerHTML = jobsForm(params, step.stepKey);
            break;
        default:
            area.innerHTML = `<p class="text-slate-500 text-sm">No form available for this step.</p>`;
    }
}

function connectionOptionsHtml(selectedId) {
    return connections.map(c => `<option value="${c.id}" ${c.id === selectedId ? 'selected' : ''}>${escapeHtml(c.name)} (${escapeHtml(c.type)})</option>`).join('');
}

function groupOptionsHtml(selectedId) {
    return `<option value="">None</option>` +
        groups.map(g => `<option value="${g.id}" ${g.id === selectedId ? 'selected' : ''}>${escapeHtml(g.name)}</option>`).join('');
}

function connectionForm(p) {
    const exactGlobe = isExactGlobeRecipe();
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">${exactGlobe
                ? 'This is your real Exact Globe+ business database - Debtors and Items are queried directly from it, so pick the actual connection rather than a staging copy.'
                : 'This is the database where staging tables for synced WooCommerce data will live. Pick an existing connection or create a new one.'}</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-connection-existing" class="block text-sm font-medium text-slate-700 mb-1">Use existing connection</label>
                <select id="wf-connection-existing" onchange="applyExistingConnection(this.value)"
                        class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    <option value="">Create new connection...</option>
                    ${connectionOptionsHtml(p.existingConnectionId)}
                </select>
            </div>
            <div>
                <label for="wf-connection-name" class="block text-sm font-medium text-slate-700 mb-1">Name</label>
                <input type="text" id="wf-connection-name" value="${escapeHtml(p.name || (exactGlobe ? 'Exact Globe+ Database' : 'Store Database'))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-connection-type" class="block text-sm font-medium text-slate-700 mb-1">Type</label>
                <select id="wf-connection-type" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    <option value="SqlServer" ${p.type === 'SqlServer' ? 'selected' : ''}>SQL Server</option>
                    <option value="MySQL" ${p.type === 'MySQL' ? 'selected' : ''}>MySQL</option>
                    <option value="PostgreSQL" ${p.type === 'PostgreSQL' ? 'selected' : ''}>PostgreSQL</option>
                </select>
            </div>
            <div>
                <label for="wf-connection-string" class="block text-sm font-medium text-slate-700 mb-1">Connection String</label>
                <textarea id="wf-connection-string" rows="3" placeholder="Server=localhost;Database=mydb;User Id=sa;Password=***;"
                          class="w-full px-3 py-2 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.connectionStringDisplay || '')}</textarea>
                <p class="text-xs text-gray-500 mt-1">Stored encrypted. Leave unchanged when reusing an existing connection.</p>
            </div>
        </div>`;
}

// The "destination" step is shared (IsShared=true) across flows - only one Destination
// ever exists per run. If it's already verified, show reuse confirmation instead of
// implying SMTP creds need re-entering.
function renderDestinationReuseBanner(stepKey) {
    const banner = document.getElementById('destination-reuse-banner');
    if (!banner) return;
    const step = getStep(stepKey);
    if (step && step.entityId && step.verified) {
        banner.innerHTML = `
            <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
                <p class="text-sm text-blue-800"><i data-lucide="check-circle-2" class="h-4 w-4 inline -mt-0.5 mr-1 text-blue-600"></i>You already created and verified an Email destination in this setup - reusing it here. Editing the fields below updates that same destination for both flows.</p>
            </div>`;
        queueLucideRender();
    } else {
        banner.innerHTML = '';
    }
}

function applyExistingConnection(idStr) {
    const id = parseInt(idStr);
    const conn = connections.find(c => c.id === id);
    const nameEl = document.getElementById('wf-connection-name');
    const typeEl = document.getElementById('wf-connection-type');
    if (conn && nameEl && typeEl) {
        nameEl.value = conn.name;
        typeEl.value = conn.type;
    }
    currentStepDirty = true;
}

function groupForm(p) {
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">Groups are purely organizational - they let you find the Import and Export Profiles this recipe creates from the regular Groups/Profiles pages later.</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-group-name" class="block text-sm font-medium text-slate-700 mb-1">Group Name</label>
                <input type="text" id="wf-group-name" value="${escapeHtml(p.name || (isExactGlobeRecipe() ? 'Exact Globe+ Data Export (Store)' : 'WooCommerce Recipe'))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-group-description" class="block text-sm font-medium text-slate-700 mb-1">Description</label>
                <textarea id="wf-group-description" rows="2"
                          class="w-full px-3 py-2 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.description || 'Created by the Store WooCommerce Order Confirmation recipe')}</textarea>
            </div>
        </div>`;
}

function destinationForm(p, stepKey) {
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">Order confirmation and tracking emails are both sent through this single SMTP destination - set it up once and both flows reuse it. Verifying this step sends a real test email.</p>
        </div>
        <div id="destination-reuse-banner"></div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-dest-name" class="block text-sm font-medium text-slate-700 mb-1">Destination Name</label>
                <input type="text" id="wf-dest-name" value="${escapeHtml(p.name || 'WooCommerce Order Emails')}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label for="wf-dest-smtpServer" class="block text-sm font-medium text-slate-700 mb-1">SMTP Host</label>
                    <input type="text" id="wf-dest-smtpServer" value="${escapeHtml(p.smtpServer || '')}" placeholder="smtp.gmail.com"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
                <div>
                    <label for="wf-dest-smtpPort" class="block text-sm font-medium text-slate-700 mb-1">SMTP Port</label>
                    <input type="number" id="wf-dest-smtpPort" value="${p.smtpPort || 587}"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
            </div>
            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label for="wf-dest-smtpUsername" class="block text-sm font-medium text-slate-700 mb-1">Username</label>
                    <input type="text" id="wf-dest-smtpUsername" value="${escapeHtml(p.smtpUsername || '')}"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
                <div>
                    <label for="wf-dest-smtpPassword" class="block text-sm font-medium text-slate-700 mb-1">Password</label>
                    <input type="password" id="wf-dest-smtpPassword" placeholder="Leave unchanged to keep existing value"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
            </div>
            <div>
                <label for="wf-dest-fromAddress" class="block text-sm font-medium text-slate-700 mb-1">From Address</label>
                <input type="email" id="wf-dest-fromAddress" value="${escapeHtml(p.fromAddress || '')}" placeholder="orders@yourstore.com"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
        </div>`;
}

function stagingTableForm(p, stepKey) {
    const sharedConn = getStep('connection');
    const shipments = isShipmentsStep(stepKey);
    const defaultTable = shipments ? 'StoreShipments' : 'StoreOrders';
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">Reef doesn't auto-create tables for incoming data, so the wizard issues the <code>CREATE TABLE</code> for you here. Safe to re-run - it only creates the table if it doesn't already exist.</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-staging-connectionId" class="block text-sm font-medium text-slate-700 mb-1">Connection</label>
                <select id="wf-staging-connectionId" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    ${connectionOptionsHtml(p.connectionId || sharedConn?.entityId)}
                </select>
            </div>
            <div>
                <label for="wf-staging-tableName" class="block text-sm font-medium text-slate-700 mb-1">Table Name</label>
                <input type="text" id="wf-staging-tableName" value="${escapeHtml(p.tableName || defaultTable)}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
        </div>`;
}

// WooCommerce auths with a Consumer Key/Secret pair as Basic Auth; Magento's REST API uses
// a single integration Bearer token instead - two genuinely different auth shapes, so the
// form swaps its credential fields based on recipe rather than forcing one shape on both.
function isMagentoRecipe() {
    return currentRun && currentRun.recipeKey === 'magento-tracking-link';
}

function importProfileForm(p, stepKey) {
    const sharedConn = getStep('connection');
    const sharedGroup = getStep('group');
    const shipments = isShipmentsStep(stepKey);
    const magento = isMagentoRecipe();
    const defaultTable = magento ? 'MagentoShipments' : (shipments ? 'StoreShipments' : 'StoreOrders');
    const endpoint = magento ? '/rest/V1/shipments' : '/wp-json/wc/v3/orders';

    const credentialFields = magento ? `
            <div>
                <label for="wf-import-integrationToken" class="block text-sm font-medium text-slate-700 mb-1">Integration Token</label>
                <input type="password" id="wf-import-integrationToken" placeholder="Leave unchanged to keep existing value"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                <p class="text-xs text-gray-500 mt-1">Sent as <code>Authorization: Bearer {token}</code>. Generate one under Magento Admin &rarr; System &rarr; Integrations.</p>
            </div>` : `
            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label for="wf-import-consumerKey" class="block text-sm font-medium text-slate-700 mb-1">Consumer Key</label>
                    <input type="text" id="wf-import-consumerKey" value="${escapeHtml(p.consumerKey || '')}"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
                <div>
                    <label for="wf-import-consumerSecret" class="block text-sm font-medium text-slate-700 mb-1">Consumer Secret</label>
                    <input type="password" id="wf-import-consumerSecret" placeholder="Leave unchanged to keep existing value"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
            </div>`;

    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">${magento
                ? `Pulls shipment tracking data from your Magento store's REST API (<code>${endpoint}</code>) using an integration token as a Bearer token.`
                : shipments
                    ? `Pulls shipment tracking data for orders from your WooCommerce store's REST API (<code>${endpoint}</code>) using your store's Consumer Key/Secret as Basic Auth.`
                    : `Pulls orders from your WooCommerce store's REST API (<code>${endpoint}</code>) using your store's Consumer Key/Secret as Basic Auth.`}</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-import-name" class="block text-sm font-medium text-slate-700 mb-1">Profile Name</label>
                <input type="text" id="wf-import-name" value="${escapeHtml(p.name || (magento ? 'Magento Tracking Import' : (shipments ? 'WooCommerce Tracking Import' : 'WooCommerce Order Import')))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-import-storeUrl" class="block text-sm font-medium text-slate-700 mb-1">Store URL</label>
                <input type="url" id="wf-import-storeUrl" value="${escapeHtml(p.storeUrl || '')}" placeholder="https://yourstore.com"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            ${credentialFields}
            <div class="grid grid-cols-2 gap-4">
                <div>
                    <label for="wf-import-connectionId" class="block text-sm font-medium text-slate-700 mb-1">Target Connection</label>
                    <select id="wf-import-connectionId" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                        ${connectionOptionsHtml(p.targetConnectionId || sharedConn?.entityId)}
                    </select>
                </div>
                <div>
                    <label for="wf-import-targetTable" class="block text-sm font-medium text-slate-700 mb-1">Target Table</label>
                    <input type="text" id="wf-import-targetTable" value="${escapeHtml(p.targetTable || defaultTable)}"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
            </div>
            <div>
                <label for="wf-import-groupId" class="block text-sm font-medium text-slate-700 mb-1">Group</label>
                <select id="wf-import-groupId" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    ${groupOptionsHtml(p.groupId || sharedGroup?.entityId)}
                </select>
            </div>
        </div>`;
}

function queryTemplateForm(p, stepKey) {
    const exactGlobe = isExactGlobeRecipe();
    if (exactGlobe) {
        const items = isItemsStep(stepKey);
        return `
            <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
                <p class="text-sm text-blue-800">A ready-made eExact-compliant XML template for ${items ? 'Items' : 'Debtors'} is pre-filled below. Customize it if you like, or save as-is - verifying renders it with a real (or sample) ${items ? 'item' : 'debtor'} row.</p>
            </div>
            <div class="space-y-4 max-w-2xl">
                <div>
                    <label for="wf-template-name" class="block text-sm font-medium text-slate-700 mb-1">Template Name</label>
                    <input type="text" id="wf-template-name" value="${escapeHtml(p.name || (items ? 'eExact XML Template for Items' : 'eExact XML Template for Debtors'))}"
                           class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                </div>
                <div>
                    <label for="wf-template-content" class="block text-sm font-medium text-slate-700 mb-1">Scriban Template (XML body)</label>
                    <textarea id="wf-template-content" rows="14"
                              class="w-full px-3 py-2 bg-white border border-slate-200 text-xs font-mono rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.template !== undefined ? p.template : '')}</textarea>
                    <p class="text-xs text-gray-500 mt-1">Leave blank to use the built-in eExact XML template for ${items ? 'Items' : 'Debtors'}.</p>
                </div>
            </div>`;
    }

    const shipments = isShipmentsStep(stepKey);
    const magento = isMagentoRecipe();
    const brand = magento ? 'Magento' : 'WooCommerce';
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">A ready-made ${shipments || magento ? 'tracking link' : 'order confirmation'} email template is pre-filled below. Customize it if you like, or save as-is - verifying renders it with a real (or sample) ${shipments || magento ? 'shipment' : 'order'} row.</p>
        </div>
        <div class="space-y-4 max-w-2xl">
            <div>
                <label for="wf-template-name" class="block text-sm font-medium text-slate-700 mb-1">Template Name</label>
                <input type="text" id="wf-template-name" value="${escapeHtml(p.name || (magento ? 'Magento Tracking Update' : (shipments ? 'WooCommerce Tracking Update' : 'WooCommerce Order Confirmation')))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-template-content" class="block text-sm font-medium text-slate-700 mb-1">Scriban Template (HTML email body)</label>
                <textarea id="wf-template-content" rows="10"
                          class="w-full px-3 py-2 bg-white border border-slate-200 text-xs font-mono rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.template !== undefined ? p.template : '')}</textarea>
                <p class="text-xs text-gray-500 mt-1">Leave blank to use the built-in ${brand} ${shipments || magento ? 'Tracking Update' : 'Order Confirmation'} template.</p>
            </div>
        </div>`;
}

function exportProfileForm(p, stepKey) {
    if (isExactGlobeRecipe()) return exactGlobeExportProfileForm(p, stepKey);

    const sharedConn = getStep('connection');
    const sharedGroup = getStep('group');
    const sharedDest = getStep('destination');
    const shipments = isShipmentsStep(stepKey);
    const magento = isMagentoRecipe();
    const templateStep = getStep(shipments ? 'shipments-query-template' : 'query-template');
    const defaultQuery = magento
        ? 'SELECT * FROM MagentoShipments WHERE EmailSent = 0'
        : (shipments ? 'SELECT * FROM StoreShipments WHERE EmailSent = 0' : 'SELECT * FROM StoreOrders WHERE EmailSent = 0');
    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">This Export Profile selects unsent ${shipments || magento ? 'shipments' : 'orders'} and emails each one using the template from the previous step.</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-export-name" class="block text-sm font-medium text-slate-700 mb-1">Profile Name</label>
                <input type="text" id="wf-export-name" value="${escapeHtml(p.name || (magento ? 'Magento Tracking Email' : (shipments ? 'WooCommerce Tracking Email' : 'WooCommerce Order Confirmation Email')))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-export-connectionId" class="block text-sm font-medium text-slate-700 mb-1">Connection</label>
                <select id="wf-export-connectionId" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    ${connectionOptionsHtml(p.connectionId || sharedConn?.entityId)}
                </select>
            </div>
            <div>
                <label for="wf-export-query" class="block text-sm font-medium text-slate-700 mb-1">Query</label>
                <textarea id="wf-export-query" rows="3"
                          class="w-full px-3 py-2 bg-white border border-slate-200 text-xs font-mono rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.query || defaultQuery)}</textarea>
            </div>
            <input type="hidden" id="wf-export-destinationId" value="${sharedDest?.entityId || ''}">
            <input type="hidden" id="wf-export-emailTemplateId" value="${templateStep?.entityId || ''}">
            <input type="hidden" id="wf-export-groupId" value="${sharedGroup?.entityId || ''}">
            <div>
                <label for="wf-export-emailSubject" class="block text-sm font-medium text-slate-700 mb-1">Email Subject</label>
                <input type="text" id="wf-export-emailSubject" value="${escapeHtml(p.emailSubject || (shipments || magento ? 'Your order is on its way' : 'Your order is confirmed'))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
        </div>`;
}

// No email/Destination step for this recipe - file output path lives inline in the Profile
// itself (OutputDestinationType="Local" + OutputDestinationConfig JSON), so this form has no
// destination/template-id hidden fields beyond the QueryTemplate this flow's own previous step created.
function exactGlobeExportProfileForm(p, stepKey) {
    const sharedConn = getStep('connection');
    const sharedGroup = getStep('group');
    const items = isItemsStep(stepKey);
    const templateStep = getStep(items ? 'items-query-template' : 'debtors-query-template');
    const defaultQuery = items ? 'SELECT * FROM dbo.Items' : 'SELECT * FROM dbo.cicmpy';
    const defaultPath = items ? 'exports/exact-globe/items' : 'exports/exact-globe/debtors';

    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">This Export Profile runs the query below against your Exact Globe+ database and writes the result as an XML file using the template from the previous step - no email, no separate Destination to configure.</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <div>
                <label for="wf-export-name" class="block text-sm font-medium text-slate-700 mb-1">Profile Name</label>
                <input type="text" id="wf-export-name" value="${escapeHtml(p.name || (items ? 'Exact Globe+ Items Export' : 'Exact Globe+ Debtors Export'))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-export-connectionId" class="block text-sm font-medium text-slate-700 mb-1">Connection</label>
                <select id="wf-export-connectionId" class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                    ${connectionOptionsHtml(p.connectionId || sharedConn?.entityId)}
                </select>
            </div>
            <div>
                <label for="wf-export-query" class="block text-sm font-medium text-slate-700 mb-1">Query</label>
                <textarea id="wf-export-query" rows="3"
                          class="w-full px-3 py-2 bg-white border border-slate-200 text-xs font-mono rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">${escapeHtml(p.query || defaultQuery)}</textarea>
            </div>
            <input type="hidden" id="wf-export-templateId" value="${templateStep?.entityId || ''}">
            <input type="hidden" id="wf-export-groupId" value="${sharedGroup?.entityId || ''}">
            <div>
                <label for="wf-export-outputPath" class="block text-sm font-medium text-slate-700 mb-1">Output Folder</label>
                <input type="text" id="wf-export-outputPath" value="${escapeHtml(p.outputPath || defaultPath)}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                <p class="text-xs text-gray-500 mt-1">Folder (relative to Reef's exports directory) where the generated .xml file is written.</p>
            </div>
        </div>`;
}

function jobsForm(p, stepKey) {
    const shipments = isShipmentsStep(stepKey);
    const items = isItemsStep(stepKey);
    const exactGlobe = isExactGlobeRecipe();
    const magento = isMagentoRecipe();
    // ErrorDigestRecipe and ExactGlobeRecipe both have no import side (they query the user's
    // own existing tables directly), so hasImport is false and the export Profile schedules on
    // its own interval instead of chaining OnDependency after an import job (see
    // RecipeService.SaveJobAsync) - this generalizes across both no-import recipes unchanged.
    const importStep = exactGlobe ? null : getStep(shipments ? 'shipments-import-profile' : 'import-profile');
    const exportStep = exactGlobe
        ? getStep(items ? 'items-export-profile' : 'debtors-export-profile')
        : getStep(shipments ? 'shipments-export-profile' : 'export-profile');
    const hasImport = !!importStep;
    // Magento has no equivalently easy native webhook mechanism wired up, unlike
    // WooCommerce's Tracking Link flow - polling only.
    const webhookSection = shipments ? renderWebhookSection() : '';

    const intervalLabel = hasImport ? 'Poll Interval (minutes)' : 'Run Interval (minutes)';
    const defaultInterval = hasImport ? 15 : 1440;
    const defaultExportJobName = p.exportJobName || (exactGlobe
        ? (items ? 'Export Items to XML' : 'Export Debtors to XML')
        : (shipments || magento ? 'Send Tracking Emails' : exportStep ? 'Send Order Confirmations' : 'Run Scheduled Export'));

    return `
        <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
            <p class="text-sm text-blue-800">${hasImport
                ? `Optional: schedule the import to poll automatically, with the export running right after. Skip this if you'd rather trigger things manually via <code>POST /api/${shipments ? 'import-profiles' : 'profiles'}/{id}/execute</code>.`
                : `Optional: schedule this export to run automatically on an interval. Skip this if you'd rather trigger it manually via <code>POST /api/profiles/{id}/execute</code>.`}</p>
        </div>
        <div class="space-y-4 max-w-lg">
            <input type="hidden" id="wf-jobs-importProfileId" value="${importStep?.entityId || ''}">
            <input type="hidden" id="wf-jobs-profileId" value="${exportStep?.entityId || ''}">
            ${hasImport ? `
            <div>
                <label for="wf-jobs-importJobName" class="block text-sm font-medium text-slate-700 mb-1">Import Job Name</label>
                <input type="text" id="wf-jobs-importJobName" value="${escapeHtml(p.importJobName || (shipments || magento ? 'Sync Tracking Updates' : 'Sync Orders'))}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>` : `<input type="hidden" id="wf-jobs-importJobName" value="">`}
            <div>
                <label for="wf-jobs-importIntervalMinutes" class="block text-sm font-medium text-slate-700 mb-1">${intervalLabel}</label>
                <input type="number" id="wf-jobs-importIntervalMinutes" value="${p.importIntervalMinutes || defaultInterval}" min="1"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
            </div>
            <div>
                <label for="wf-jobs-exportJobName" class="block text-sm font-medium text-slate-700 mb-1">Export Job Name</label>
                <input type="text" id="wf-jobs-exportJobName" value="${escapeHtml(defaultExportJobName)}"
                       class="w-full h-9 px-3 py-1 bg-white border border-slate-200 text-sm rounded-md outline-none focus:border-slate-400 focus:ring-3 focus:ring-slate-900/10">
                <p class="text-xs text-gray-500 mt-1">${hasImport ? 'Runs automatically right after the import job completes.' : 'Runs automatically on the interval above.'}</p>
            </div>
            ${webhookSection}
        </div>`;
}

// Belt-and-suspenders fast path on top of the polling Import Job, not a replacement.
function renderWebhookSection() {
    return `
        <div class="border-t border-slate-200 pt-4 mt-2">
            <div class="bg-blue-50 border-l-4 border-blue-400 p-4 mb-4">
                <p class="text-sm text-blue-800">Optional fast path: register a webhook so WooCommerce can push tracking updates to Reef immediately instead of waiting for the next poll. This runs <strong>on top of</strong> the polling import job above, not instead of it.</p>
            </div>
            <button type="button" onclick="registerTrackingWebhook()"
                    class="h-9 px-4 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">
                Generate Webhook URL
            </button>
            <div id="webhook-result-area" class="mt-3"></div>
        </div>`;
}

function collectStepParams(step) {
    switch (step.entityType) {
        case 'Connection':
            return {
                name: val('wf-connection-name'),
                type: val('wf-connection-type'),
                connectionString: val('wf-connection-string'),
                existingConnectionId: val('wf-connection-existing') ? parseInt(val('wf-connection-existing')) : null
            };
        case 'Group':
            return { name: val('wf-group-name'), description: val('wf-group-description') };
        case 'Destination': {
            const config = {
                SmtpServer: val('wf-dest-smtpServer'),
                SmtpPort: parseInt(val('wf-dest-smtpPort')) || 587,
                SmtpUsername: val('wf-dest-smtpUsername'),
                SmtpPassword: val('wf-dest-smtpPassword'),
                FromAddress: val('wf-dest-fromAddress'),
                EnableSsl: true,
                SmtpAuthType: 'Basic'
            };
            return { name: val('wf-dest-name'), configurationJson: JSON.stringify(config), smtpServer: config.SmtpServer, smtpPort: config.SmtpPort, smtpUsername: config.SmtpUsername, fromAddress: config.FromAddress };
        }
        case 'StagingTable':
            return { connectionId: parseInt(val('wf-staging-connectionId')), tableName: val('wf-staging-tableName') };
        case 'ImportProfile': {
            const storeUrl = (val('wf-import-storeUrl') || '').replace(/\/$/, '');
            const magento = isMagentoRecipe();
            const sourceConfig = magento ? {
                Url: `${storeUrl}/rest/V1/shipments`,
                AuthType: 'Bearer',
                AuthToken: val('wf-import-integrationToken')
            } : {
                Url: `${storeUrl}/wp-json/wc/v3/orders`,
                AuthType: 'Basic',
                AuthToken: btoa(`${val('wf-import-consumerKey')}:${val('wf-import-consumerSecret')}`)
            };
            return {
                name: val('wf-import-name'),
                storeUrl,
                consumerKey: magento ? undefined : val('wf-import-consumerKey'),
                sourceConfig: JSON.stringify(sourceConfig),
                httpMethod: 'GET',
                httpPaginationEnabled: true,
                httpPaginationConfig: JSON.stringify({ Type: 'Page', PageParam: 'page', LimitParam: 'per_page', Limit: 50, MaxPages: 1000 }),
                httpDataRootPath: '$',
                targetConnectionId: parseInt(val('wf-import-connectionId')),
                targetTable: val('wf-import-targetTable'),
                upsertKeyColumns: magento ? 'MagentoOrderId' : undefined,
                groupId: val('wf-import-groupId') ? parseInt(val('wf-import-groupId')) : null
            };
        }
        case 'QueryTemplate':
            return { name: val('wf-template-name'), template: val('wf-template-content') || undefined };
        case 'Profile':
            if (isExactGlobeRecipe()) {
                return {
                    name: val('wf-export-name'),
                    connectionId: parseInt(val('wf-export-connectionId')),
                    query: val('wf-export-query'),
                    templateId: val('wf-export-templateId') ? parseInt(val('wf-export-templateId')) : null,
                    groupId: val('wf-export-groupId') ? parseInt(val('wf-export-groupId')) : null,
                    outputPath: val('wf-export-outputPath')
                };
            }
            return {
                name: val('wf-export-name'),
                connectionId: parseInt(val('wf-export-connectionId')),
                query: val('wf-export-query'),
                destinationId: val('wf-export-destinationId') ? parseInt(val('wf-export-destinationId')) : null,
                emailTemplateId: val('wf-export-emailTemplateId') ? parseInt(val('wf-export-emailTemplateId')) : null,
                groupId: val('wf-export-groupId') ? parseInt(val('wf-export-groupId')) : null,
                emailSubject: val('wf-export-emailSubject')
            };
        case 'Job':
            return {
                importProfileId: val('wf-jobs-importProfileId') ? parseInt(val('wf-jobs-importProfileId')) : null,
                profileId: val('wf-jobs-profileId') ? parseInt(val('wf-jobs-profileId')) : null,
                importJobName: val('wf-jobs-importJobName'),
                importIntervalMinutes: parseInt(val('wf-jobs-importIntervalMinutes')) || 15,
                exportJobName: val('wf-jobs-exportJobName')
            };
        default:
            return {};
    }
}

// Jobs step must be saved first so the ImportProfile id is known.
async function registerTrackingWebhook() {
    const resultArea = document.getElementById('webhook-result-area');
    const step = getStep(currentRun.currentStepKey);
    if (!step) return;

    try {
        const response = await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/steps/${step.stepKey}/webhook`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.error || 'Failed to register webhook');
        }
        const result = await response.json();
        if (resultArea) {
            resultArea.innerHTML = `
                <div class="bg-green-50 border border-green-200 rounded-md p-4">
                    <p class="text-sm text-green-800 mb-2">Webhook registered. Paste this URL into WooCommerce &rarr; Settings &rarr; Advanced &rarr; Webhooks (topic: order status changed):</p>
                    <div class="flex items-center gap-2">
                        <input type="text" readonly value="${escapeHtml(result.url)}" id="webhook-url-input"
                               class="flex-1 h-9 px-3 py-1 bg-white border border-slate-200 text-xs font-mono rounded-md outline-none">
                        <button type="button" onclick="copyWebhookUrl()" class="h-9 px-3 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 shrink-0">Copy</button>
                    </div>
                </div>`;
        }
        showMessage('Webhook registered', 'success');
    } catch (error) {
        if (resultArea) {
            resultArea.innerHTML = `<div class="bg-red-50 border border-red-200 text-red-800 rounded-md p-4 text-sm">${escapeHtml(error.message)}</div>`;
        }
        showMessage(error.message, 'error');
    }
}

function copyWebhookUrl() {
    const input = document.getElementById('webhook-url-input');
    if (!input) return;
    navigator.clipboard?.writeText(input.value);
    showMessage('Webhook URL copied', 'success');
}

function val(id) {
    const el = document.getElementById(id);
    return el ? el.value : '';
}

async function saveCurrentStep() {
    const step = getStep(currentRun.currentStepKey);
    if (!step) return;

    const btn = document.getElementById('step-save-btn');
    const spinner = document.getElementById('step-save-spinner');
    const btnText = document.getElementById('step-save-btn-text');
    setSpinner(btn, spinner, btnText, true, step.hasVerifier ? 'Saving...' : 'Saving...');

    try {
        const params = collectStepParams(step);
        const saveResponse = await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/steps/${step.stepKey}/save`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(params)
        });
        if (!saveResponse.ok) {
            const error = await saveResponse.json().catch(() => ({}));
            throw new Error(error.error || 'Failed to save step');
        }

        let updatedStep = await saveResponse.json();
        currentStepDirty = false;

        if (step.hasVerifier) {
            btnText.textContent = 'Verifying...';
            const verifyResponse = await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/steps/${step.stepKey}/verify`, {
                method: 'POST',
                headers: getAuthHeaders()
            });
            if (!verifyResponse.ok) {
                const error = await verifyResponse.json().catch(() => ({}));
                throw new Error(error.error || 'Failed to verify step');
            }
            updatedStep = await verifyResponse.json();
        }

        await refreshRunState();
        renderStepResult(updatedStep.verified || !step.hasVerifier, updatedStep.lastVerifyMessage || 'Saved.');
        showMessage(updatedStep.verified || !step.hasVerifier ? 'Step saved and verified' : 'Saved, but verification failed', updatedStep.verified || !step.hasVerifier ? 'success' : 'error');
    } catch (error) {
        renderStepResult(false, error.message);
        showMessage(error.message, 'error');
    } finally {
        setSpinner(btn, spinner, btnText, false, step.hasVerifier ? 'Save & Verify' : 'Save');
    }
}

async function skipCurrentStep() {
    const step = getStep(currentRun.currentStepKey);
    if (!step) return;
    try {
        await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/steps/${step.stepKey}/skip`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        await refreshRunState();
        showMessage('Step skipped', 'success');
    } catch (error) {
        showMessage('Failed to skip step', 'error');
    }
}

async function refreshRunState() {
    const response = await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}`, { headers: getAuthHeaders() });
    if (!response.ok) return;
    const previousStepKey = currentRun.currentStepKey;
    currentRun = await response.json();
    currentRun.currentStepKey = previousStepKey; // "Next step" button advances explicitly
    renderStepRail();
    renderActiveStep();
}

function renderStepResult(success, message) {
    const area = document.getElementById('step-result-area');
    if (!area) return;
    const cls = success ? 'bg-green-50 border-green-200 text-green-800' : 'bg-red-50 border-red-200 text-red-800';
    const icon = success ? 'check-circle' : 'alert-circle';
    area.innerHTML = `
        <div class="${cls} border rounded-md p-4">
            <div class="flex items-start gap-2">
                <i data-lucide="${icon}" class="h-5 w-5 shrink-0 mt-0.5"></i>
                <p class="text-sm whitespace-pre-wrap">${escapeHtml(message)}</p>
            </div>
        </div>`;
    queueLucideRender();
}

function setSpinner(btn, spinner, btnText, loading, loadingLabel) {
    if (!btn || !spinner || !btnText) return;
    btn.disabled = loading;
    spinner.classList.toggle('hidden', !loading);
    if (loading) btnText.textContent = loadingLabel;
}

async function advanceOrComplete() {
    const recipe = currentRun;
    const steps = visibleSteps();
    const idx = steps.findIndex(s => s.stepKey === recipe.currentStepKey);
    const next = steps[idx + 1];

    if (next) {
        currentRun.currentStepKey = next.stepKey;
        renderStepRail();
        renderActiveStep();
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/api/recipes/runs/${currentRun.runId}/complete`, {
            method: 'POST',
            headers: getAuthHeaders()
        });
        if (!response.ok) {
            const error = await response.json().catch(() => ({}));
            throw new Error(error.error || 'Failed to complete recipe');
        }
        showCompletion();
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

function showCompletion() {
    document.getElementById('wizard-view').classList.add('hidden');
    document.getElementById('completion-view').classList.remove('hidden');

    const isWooCommerce = currentRun.recipeKey === 'woocommerce-order-confirmation';
    const descriptionEl = document.getElementById('completion-description');
    if (descriptionEl) {
        descriptionEl.textContent = isWooCommerce
            ? 'Your WooCommerce Order Confirmation and Tracking Link flows are live and verified end to end.'
            : `Your ${currentRun.recipeName || 'recipe'} setup is live and verified end to end.`;
    }
    const groupStep = getStep('group');
    const importStep = getStep('import-profile');
    const exportStep = getStep('export-profile');
    const shipmentsImportStep = getStep('shipments-import-profile');
    const shipmentsExportStep = getStep('shipments-export-profile');
    const links = document.getElementById('completion-links');

    const buttons = [];
    if (groupStep?.entityId) {
        buttons.push(`<a href="/groups" class="h-9 px-4 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 inline-flex items-center">View Group</a>`);
    }
    if (importStep?.entityId) {
        const importLabel = isMagentoRecipe() ? 'Run Tracking Import Now' : 'Run Order Import Now';
        buttons.push(`<button onclick="runProfileNow(${importStep.entityId}, true)" class="h-9 px-4 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">${importLabel}</button>`);
    }
    if (exportStep?.entityId) {
        const label = isWooCommerce ? 'Run Order Export Now' : 'Run Export Now';
        buttons.push(`<button onclick="runProfileNow(${exportStep.entityId}, false)" class="h-9 px-4 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">${label}</button>`);
    }
    if (shipmentsImportStep?.entityId) {
        buttons.push(`<button onclick="runProfileNow(${shipmentsImportStep.entityId}, true)" class="h-9 px-4 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">Run Tracking Import Now</button>`);
    }
    if (shipmentsExportStep?.entityId) {
        buttons.push(`<button onclick="runProfileNow(${shipmentsExportStep.entityId}, false)" class="h-9 px-4 bg-blue-600 text-white text-sm font-medium rounded-md hover:bg-blue-700 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1">Run Tracking Export Now</button>`);
    }
    buttons.push(`<a href="/profiles" class="h-9 px-4 border border-slate-200 rounded-md text-sm font-medium text-slate-700 hover:bg-slate-50 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-1 inline-flex items-center">View Profiles</a>`);

    links.innerHTML = buttons.join('');
    queueLucideRender();
}

async function runProfileNow(id, isImport) {
    try {
        const path = isImport ? `import-profiles/${id}/execute` : `profiles/${id}/execute`;
        const response = await fetch(`${API_BASE}/api/${path}`, { method: 'POST', headers: getAuthHeaders() });
        if (!response.ok) throw new Error('Failed to trigger execution');
        showMessage('Execution started - check the Executions page for results.', 'success');
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

function showMessage(message, type) {
    if (typeof window.showToast === 'function') {
        window.showToast(message, type === 'error' ? 'error' : type === 'success' ? 'success' : 'info');
        return;
    }
    const container = document.getElementById('message-container');
    if (!container) return;
    const bgColor = type === 'success' ? 'bg-green-50 border-green-400 text-green-800' : 'bg-red-50 border-red-400 text-red-800';
    container.innerHTML = `<div class="${bgColor} border-l-4 p-4 mb-4 rounded">${escapeHtml(message)}</div>`;
    setTimeout(() => { container.innerHTML = ''; }, 5000);
}

window.addEventListener('beforeunload', (e) => {
    if (currentStepDirty) {
        e.preventDefault();
        e.returnValue = '';
    }
});
