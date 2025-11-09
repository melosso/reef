// Import Profiles Management Module
// Handles CRUD operations for import profiles

let allProfiles = [];
let filteredProfiles = [];
let currentSort = { field: 'name', direction: 'asc' };
let currentFilters = { source: '', status: '', destination: '', group: '' };
let isEditMode = false;
let fieldMappingCount = 0;
let validationRuleCount = 0;

// Page initialization
document.addEventListener('DOMContentLoaded', async function() {
    await loadProfiles();
    await loadConnections();
    await loadGroups();
    lucide.createIcons();
});

// Load all import profiles from API
async function loadProfiles() {
    try {
        const response = await fetch('/api/imports/profiles');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

        const result = await response.json();
        allProfiles = result.data || [];
        applyFilters();
        renderProfiles();
    } catch (error) {
        console.error('Error loading profiles:', error);
        showMessage('Error loading profiles: ' + error.message, 'error');
        document.getElementById('profiles-tbody').innerHTML = `
            <tr>
                <td colspan="7" class="px-6 py-4 text-center text-red-500">
                    Error loading profiles. Please refresh the page.
                </td>
            </tr>
        `;
    }
}

// Load connections for dropdowns
async function loadConnections() {
    try {
        const response = await fetch('/api/connections');
        if (!response.ok) return;

        const result = await response.json();
        const connections = result.data || [];

        // Update all connection select elements
        const selects = [
            'profile-db-connection',
            'profile-dest-db-connection'
        ];

        selects.forEach(selectId => {
            const select = document.getElementById(selectId);
            if (select) {
                connections.forEach(conn => {
                    const option = document.createElement('option');
                    option.value = conn.id;
                    option.textContent = `${conn.name} (${conn.type})`;
                    select.appendChild(option);
                });
            }
        });
    } catch (error) {
        console.error('Error loading connections:', error);
    }
}

// Load groups for dropdown
async function loadGroups() {
    try {
        const response = await fetch('/api/groups');
        if (!response.ok) return;

        const result = await response.json();
        const groups = result.data || [];

        const groupSelects = [
            'filter-group',
            'profile-group'
        ];

        groupSelects.forEach(selectId => {
            const select = document.getElementById(selectId);
            if (select) {
                groups.forEach(group => {
                    const option = document.createElement('option');
                    option.value = group.id;
                    option.textContent = group.name;
                    select.appendChild(option);
                });
            }
        });
    } catch (error) {
        console.error('Error loading groups:', error);
    }
}

// Apply filters to profiles
function applyFilters() {
    currentFilters.source = document.getElementById('filter-source').value;
    currentFilters.status = document.getElementById('filter-status').value;
    currentFilters.destination = document.getElementById('filter-destination').value;
    currentFilters.group = document.getElementById('filter-group').value;

    filteredProfiles = allProfiles.filter(profile => {
        const sourceMatch = !currentFilters.source || profile.sourceType === currentFilters.source;
        const statusMatch = !currentFilters.status ||
            (currentFilters.status === 'enabled' ? profile.isEnabled : !profile.isEnabled);
        const destMatch = !currentFilters.destination || profile.destinationType === currentFilters.destination;
        const groupMatch = !currentFilters.group || profile.groupId === parseInt(currentFilters.group);

        return sourceMatch && statusMatch && destMatch && groupMatch;
    });

    sortProfiles(currentSort.field);
}

// Sort profiles by field
function sortProfiles(field) {
    if (currentSort.field === field) {
        currentSort.direction = currentSort.direction === 'asc' ? 'desc' : 'asc';
    } else {
        currentSort.field = field;
        currentSort.direction = 'asc';
    }

    // Update sort icons
    document.querySelectorAll('[id^="sort-icon-"]').forEach(icon => {
        icon.setAttribute('data-lucide', 'chevrons-up-down');
    });
    const activeIcon = document.getElementById(`sort-icon-${field}`);
    if (activeIcon) {
        activeIcon.setAttribute('data-lucide', currentSort.direction === 'asc' ? 'chevron-up' : 'chevron-down');
    }
    lucide.createIcons();

    // Sort array
    filteredProfiles.sort((a, b) => {
        const aVal = String(a[field] || '').toLowerCase();
        const bVal = String(b[field] || '').toLowerCase();

        if (currentSort.direction === 'asc') {
            return aVal.localeCompare(bVal);
        } else {
            return bVal.localeCompare(aVal);
        }
    });

    renderProfiles();
}

// Render profiles table
function renderProfiles() {
    const tbody = document.getElementById('profiles-tbody');

    if (filteredProfiles.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="7" class="px-6 py-4 text-center text-gray-500">
                    No import profiles found.
                    <a href="javascript:openAddProfileModal()" class="text-blue-600 hover:underline">Create one</a>
                </td>
            </tr>
        `;
        return;
    }

    tbody.innerHTML = filteredProfiles.map(profile => `
        <tr class="hover:bg-gray-50">
            <td class="px-6 py-4 whitespace-nowrap">
                <div class="flex items-center">
                    <div class="text-sm font-semibold text-gray-900">${escapeHtml(profile.name)}</div>
                </div>
            </td>
            <td class="px-6 py-4 whitespace-nowrap">
                <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-blue-100 text-blue-800">
                    ${getSourceIcon(profile.sourceType)} ${profile.sourceType || 'Unknown'}
                </span>
            </td>
            <td class="px-6 py-4 whitespace-nowrap">
                <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full bg-green-100 text-green-800">
                    ${getDestinationIcon(profile.destinationType)} ${profile.destinationType || 'Unknown'}
                </span>
            </td>
            <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                ${profile.enableDeltaSync ? '<i data-lucide="check" class="h-5 w-5 text-green-600 inline"></i> Enabled' : '<i data-lucide="x" class="h-5 w-5 text-gray-400 inline"></i> Disabled'}
            </td>
            <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                ${profile.scheduleType ? profile.scheduleType : 'Manual'}
            </td>
            <td class="px-6 py-4 whitespace-nowrap">
                <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${
                    profile.isEnabled
                        ? 'bg-green-100 text-green-800'
                        : 'bg-gray-100 text-gray-800'
                }">
                    ${profile.isEnabled ? 'Enabled' : 'Disabled'}
                </span>
            </td>
            <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                <button onclick="editProfile(${profile.id})" class="text-blue-600 hover:text-blue-900 mr-3">Edit</button>
                <button onclick="executeProfile(${profile.id})" class="text-green-600 hover:text-green-900 mr-3">Run</button>
                <button onclick="deleteProfile(${profile.id})" class="text-red-600 hover:text-red-900">Delete</button>
            </td>
        </tr>
    `).join('');

    lucide.createIcons();
}

// Get source type icon
function getSourceIcon(sourceType) {
    const icons = {
        'rest': '🌐',
        's3': '☁️',
        'ftp': '📁',
        'database': '🗄️'
    };
    return icons[sourceType] || '📦';
}

// Get destination type icon
function getDestinationIcon(destType) {
    const icons = {
        'database': '🗄️',
        'file': '📄',
        's3': '☁️'
    };
    return icons[destType] || '📤';
}

// Open add profile modal
function openAddProfileModal() {
    resetProfileForm();
    isEditMode = false;
    document.getElementById('modal-title').textContent = 'Add Import Profile';
    document.getElementById('profile-id').value = '';
    showTab('general');
    document.getElementById('profile-modal').classList.remove('hidden');
    lucide.createIcons();
}

// Open edit profile modal
async function editProfile(profileId) {
    try {
        const response = await fetch(`/api/imports/profiles/${profileId}`);
        if (!response.ok) throw new Error('Failed to load profile');

        const result = await response.json();
        const profile = result.data;

        isEditMode = true;
        document.getElementById('modal-title').textContent = 'Edit Import Profile';

        // Populate form fields
        document.getElementById('profile-id').value = profile.id;
        document.getElementById('profile-name').value = profile.name || '';
        document.getElementById('profile-description').value = profile.description || '';
        document.getElementById('profile-group').value = profile.groupId || '';
        document.getElementById('profile-status').value = profile.isEnabled ? 'enabled' : 'disabled';

        // Source fields
        document.getElementById('profile-source-type').value = profile.sourceType || '';
        updateSourceFields();

        if (profile.sourceType === 'rest') {
            document.getElementById('profile-rest-url').value = profile.sourceConfig?.url || '';
            document.getElementById('profile-rest-auth').value = profile.sourceConfig?.authType || 'none';
            updateRestAuthFields();
            document.getElementById('profile-rest-token').value = profile.sourceConfig?.token || '';
            document.getElementById('profile-rest-pagination').value = profile.sourceConfig?.paginationType || 'none';
            document.getElementById('profile-rest-page-size').value = profile.sourceConfig?.pageSize || 100;
        } else if (profile.sourceType === 's3') {
            document.getElementById('profile-s3-bucket').value = profile.sourceConfig?.bucket || '';
            document.getElementById('profile-s3-prefix').value = profile.sourceConfig?.prefix || '';
            document.getElementById('profile-s3-region').value = profile.sourceConfig?.region || '';
        } else if (profile.sourceType === 'ftp') {
            document.getElementById('profile-ftp-host').value = profile.sourceConfig?.host || '';
            document.getElementById('profile-ftp-username').value = profile.sourceConfig?.username || '';
            document.getElementById('profile-ftp-password').value = profile.sourceConfig?.password || '';
            document.getElementById('profile-ftp-file-pattern').value = profile.sourceConfig?.filePattern || '';
            document.getElementById('profile-ftp-sftp').checked = profile.sourceConfig?.useSftp || false;
        } else if (profile.sourceType === 'database') {
            document.getElementById('profile-db-query').value = profile.sourceConfig?.query || '';
            document.getElementById('profile-db-connection').value = profile.sourceConfig?.connectionId || '';
        }

        // Destination fields
        document.getElementById('profile-destination-type').value = profile.destinationType || '';
        updateDestinationFields();

        if (profile.destinationType === 'database') {
            document.getElementById('profile-dest-db-connection').value = profile.destinationConfig?.connectionId || '';
            document.getElementById('profile-dest-table').value = profile.destinationConfig?.tableName || '';
            document.getElementById('profile-dest-write-mode').value = profile.destinationConfig?.writeMode || 'insert';
            document.getElementById('profile-upsert-keys').value = profile.destinationConfig?.upsertKeys || '';
        } else if (profile.destinationType === 'file') {
            document.getElementById('profile-dest-file-path').value = profile.destinationConfig?.filePath || '';
            document.getElementById('profile-dest-file-format').value = profile.destinationConfig?.format || 'csv';
        } else if (profile.destinationType === 's3') {
            document.getElementById('profile-dest-s3-bucket').value = profile.destinationConfig?.bucket || '';
            document.getElementById('profile-dest-s3-key').value = profile.destinationConfig?.key || '';
            document.getElementById('profile-dest-s3-region').value = profile.destinationConfig?.region || '';
        }

        // Schedule fields
        if (profile.scheduleType) {
            document.getElementById('profile-scheduled').checked = true;
            updateScheduleFields();
            document.getElementById('profile-schedule-type').value = profile.scheduleType || 'cron';
            updateScheduleTypeFields();

            if (profile.scheduleType === 'cron') {
                document.getElementById('profile-cron').value = profile.scheduleConfig?.cron || '';
            } else if (profile.scheduleType === 'interval') {
                document.getElementById('profile-interval-minutes').value = profile.scheduleConfig?.intervalMinutes || 60;
            } else if (profile.scheduleType === 'daily') {
                document.getElementById('profile-daily-time').value = profile.scheduleConfig?.time || '02:00';
            } else if (profile.scheduleType === 'weekly') {
                document.getElementById('profile-weekly-time').value = profile.scheduleConfig?.time || '02:00';
                if (profile.scheduleConfig?.daysOfWeek) {
                    profile.scheduleConfig.daysOfWeek.forEach(day => {
                        const checkbox = document.querySelector(`.weekly-day[value="${day}"]`);
                        if (checkbox) checkbox.checked = true;
                    });
                }
            }
        }

        // Advanced fields
        document.getElementById('profile-delta-sync').checked = profile.enableDeltaSync || false;
        document.getElementById('profile-error-strategy').value = profile.errorStrategy || 'fail';
        document.getElementById('profile-batch-size').value = profile.batchSize || 1000;
        document.getElementById('profile-timeout').value = profile.timeoutSeconds || 300;
        document.getElementById('profile-retry-enabled').checked = profile.enableRetry || false;
        updateRetryFields();
        document.getElementById('profile-max-retries').value = profile.maxRetries || 3;
        document.getElementById('profile-retry-delay').value = profile.retryDelaySeconds || 5;

        showTab('general');
        document.getElementById('profile-modal').classList.remove('hidden');
        lucide.createIcons();
        showMessage(`Loaded profile: ${profile.name}`, 'info', true);
    } catch (error) {
        console.error('Error loading profile:', error);
        showMessage('Error loading profile: ' + error.message, 'error');
    }
}

// Reset profile form
function resetProfileForm() {
    document.getElementById('profile-form').reset();
    document.getElementById('profile-id').value = '';
    document.getElementById('profile-status').value = 'enabled';
    document.getElementById('profile-rest-auth').value = 'none';
    document.getElementById('profile-rest-pagination').value = 'none';
    document.getElementById('profile-rest-page-size').value = 100;
    document.getElementById('profile-destination-type').value = '';
    document.getElementById('profile-dest-write-mode').value = 'insert';
    document.getElementById('profile-error-strategy').value = 'fail';
    document.getElementById('profile-batch-size').value = 1000;
    document.getElementById('profile-timeout').value = 300;
    document.getElementById('profile-schedule-type').value = 'cron';
    document.getElementById('profile-scheduled').checked = false;
    document.getElementById('profile-retry-enabled').checked = false;
    document.getElementById('profile-max-retries').value = 3;
    document.getElementById('profile-retry-delay').value = 5;

    // Clear field mappings and validation rules
    document.getElementById('field-mappings-container').innerHTML = '<div class="text-sm text-gray-500">No field mappings yet. Click "Add Mapping" to create one.</div>';
    document.getElementById('validation-rules-container').innerHTML = '<div class="text-sm text-gray-500">No validation rules yet. Click "Add Rule" to create one.</div>';
    fieldMappingCount = 0;
    validationRuleCount = 0;
}

// Close profile modal
function closeProfileModal() {
    document.getElementById('profile-modal').classList.add('hidden');
    resetProfileForm();
}

// Show specific tab
function showTab(tabName) {
    // Hide all tabs
    document.querySelectorAll('.tab-content').forEach(tab => {
        tab.classList.add('hidden');
    });

    // Remove active class from all buttons
    document.querySelectorAll('[id$="TabButton"]').forEach(btn => {
        btn.classList.remove('text-blue-600', 'border-b-blue-600');
        btn.classList.add('text-gray-700', 'border-b-transparent');
    });

    // Show selected tab
    const tabElement = document.getElementById(tabName + 'Tab');
    if (tabElement) {
        tabElement.classList.remove('hidden');
    }

    // Activate selected button
    const buttonElement = document.getElementById(tabName + 'TabButton');
    if (buttonElement) {
        buttonElement.classList.remove('text-gray-700', 'border-b-transparent');
        buttonElement.classList.add('text-blue-600', 'border-b-blue-600');
    }
}

// Update source fields visibility
function updateSourceFields() {
    const sourceType = document.getElementById('profile-source-type').value;

    ['rest-fields', 's3-fields', 'ftp-fields', 'db-fields'].forEach(id => {
        document.getElementById(id).classList.add('hidden');
    });

    if (sourceType === 'rest') {
        document.getElementById('rest-fields').classList.remove('hidden');
    } else if (sourceType === 's3') {
        document.getElementById('s3-fields').classList.remove('hidden');
    } else if (sourceType === 'ftp') {
        document.getElementById('ftp-fields').classList.remove('hidden');
    } else if (sourceType === 'database') {
        document.getElementById('db-fields').classList.remove('hidden');
    }
}

// Update REST auth fields
function updateRestAuthFields() {
    const authType = document.getElementById('profile-rest-auth').value;
    const authFields = document.getElementById('rest-auth-fields');

    if (authType !== 'none') {
        authFields.classList.remove('hidden');
    } else {
        authFields.classList.add('hidden');
    }
}

// Update destination fields visibility
function updateDestinationFields() {
    const destType = document.getElementById('profile-destination-type').value;

    ['dest-db-fields', 'dest-file-fields', 'dest-s3-fields'].forEach(id => {
        document.getElementById(id).classList.add('hidden');
    });

    if (destType === 'database') {
        document.getElementById('dest-db-fields').classList.remove('hidden');
    } else if (destType === 'file') {
        document.getElementById('dest-file-fields').classList.remove('hidden');
    } else if (destType === 's3') {
        document.getElementById('dest-s3-fields').classList.remove('hidden');
    }
}

// Update write mode fields
document.addEventListener('change', function(e) {
    if (e.target.id === 'profile-dest-write-mode') {
        const upsertContainer = document.getElementById('upsert-keys-container');
        if (e.target.value === 'upsert') {
            upsertContainer.classList.remove('hidden');
        } else {
            upsertContainer.classList.add('hidden');
        }
    }
});

// Update schedule fields
function updateScheduleFields() {
    const checked = document.getElementById('profile-scheduled').checked;
    const scheduleFields = document.getElementById('schedule-fields');

    if (checked) {
        scheduleFields.classList.remove('hidden');
    } else {
        scheduleFields.classList.add('hidden');
    }
}

// Update schedule type fields
function updateScheduleTypeFields() {
    const scheduleType = document.getElementById('profile-schedule-type').value;

    ['cron-field', 'interval-field', 'daily-field', 'weekly-field'].forEach(id => {
        document.getElementById(id).classList.add('hidden');
    });

    if (scheduleType === 'cron') {
        document.getElementById('cron-field').classList.remove('hidden');
    } else if (scheduleType === 'interval') {
        document.getElementById('interval-field').classList.remove('hidden');
    } else if (scheduleType === 'daily') {
        document.getElementById('daily-field').classList.remove('hidden');
    } else if (scheduleType === 'weekly') {
        document.getElementById('weekly-field').classList.remove('hidden');
    }
}

// Update retry fields
document.addEventListener('change', function(e) {
    if (e.target.id === 'profile-retry-enabled') {
        const retryFields = document.getElementById('retry-fields');
        if (e.target.checked) {
            retryFields.classList.remove('hidden');
        } else {
            retryFields.classList.add('hidden');
        }
    }
});

function updateRetryFields() {
    const retryEnabled = document.getElementById('profile-retry-enabled').checked;
    const retryFields = document.getElementById('retry-fields');

    if (retryEnabled) {
        retryFields.classList.remove('hidden');
    } else {
        retryFields.classList.add('hidden');
    }
}

// Add field mapping
function addFieldMapping() {
    const container = document.getElementById('field-mappings-container');

    if (container.querySelector('.text-sm.text-gray-500')) {
        container.innerHTML = '';
    }

    const mappingId = `mapping-${fieldMappingCount++}`;
    const mappingHtml = `
        <div id="${mappingId}" class="grid grid-cols-3 gap-3 p-3 border border-gray-200 rounded">
            <input type="text" placeholder="Source field" class="px-2 py-1 border border-gray-300 rounded text-sm">
            <i data-lucide="arrow-right" class="h-5 w-5 text-gray-400 self-center text-center"></i>
            <input type="text" placeholder="Destination field" class="px-2 py-1 border border-gray-300 rounded text-sm">
            <button type="button" onclick="removeFieldMapping('${mappingId}')" class="col-span-3 text-red-600 text-sm hover:underline">Remove</button>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', mappingHtml);
    lucide.createIcons();
}

// Remove field mapping
function removeFieldMapping(mappingId) {
    document.getElementById(mappingId).remove();

    if (document.getElementById('field-mappings-container').children.length === 0) {
        document.getElementById('field-mappings-container').innerHTML = '<div class="text-sm text-gray-500">No field mappings yet. Click "Add Mapping" to create one.</div>';
    }
}

// Add validation rule
function addValidationRule() {
    const container = document.getElementById('validation-rules-container');

    if (container.querySelector('.text-sm.text-gray-500')) {
        container.innerHTML = '';
    }

    const ruleId = `rule-${validationRuleCount++}`;
    const ruleHtml = `
        <div id="${ruleId}" class="grid grid-cols-4 gap-2 p-3 border border-gray-200 rounded">
            <input type="text" placeholder="Field name" class="px-2 py-1 border border-gray-300 rounded text-sm">
            <select class="px-2 py-1 border border-gray-300 rounded text-sm">
                <option value="required">Required</option>
                <option value="regex">Regex</option>
                <option value="minmax">Min/Max</option>
                <option value="enum">Enum</option>
            </select>
            <input type="text" placeholder="Validation value" class="px-2 py-1 border border-gray-300 rounded text-sm">
            <button type="button" onclick="removeValidationRule('${ruleId}')" class="text-red-600 text-sm hover:underline">Remove</button>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', ruleHtml);
}

// Remove validation rule
function removeValidationRule(ruleId) {
    document.getElementById(ruleId).remove();

    if (document.getElementById('validation-rules-container').children.length === 0) {
        document.getElementById('validation-rules-container').innerHTML = '<div class="text-sm text-gray-500">No validation rules yet. Click "Add Rule" to create one.</div>';
    }
}

// Save profile
async function saveProfile() {
    // Collect form data
    const profileId = document.getElementById('profile-id').value;
    const profileData = {
        name: document.getElementById('profile-name').value,
        description: document.getElementById('profile-description').value,
        groupId: document.getElementById('profile-group').value ? parseInt(document.getElementById('profile-group').value) : null,
        isEnabled: document.getElementById('profile-status').value === 'enabled',
        sourceType: document.getElementById('profile-source-type').value,
        sourceConfig: buildSourceConfig(),
        destinationType: document.getElementById('profile-destination-type').value,
        destinationConfig: buildDestinationConfig(),
        enableDeltaSync: document.getElementById('profile-delta-sync').checked,
        errorStrategy: document.getElementById('profile-error-strategy').value,
        batchSize: parseInt(document.getElementById('profile-batch-size').value),
        timeoutSeconds: parseInt(document.getElementById('profile-timeout').value),
        enableRetry: document.getElementById('profile-retry-enabled').checked,
        maxRetries: parseInt(document.getElementById('profile-max-retries').value),
        retryDelaySeconds: parseInt(document.getElementById('profile-retry-delay').value),
        scheduleType: document.getElementById('profile-scheduled').checked ? document.getElementById('profile-schedule-type').value : null,
        scheduleConfig: document.getElementById('profile-scheduled').checked ? buildScheduleConfig() : null
    };

    // Validate required fields
    if (!profileData.name) {
        showMessage('Please enter a profile name', 'error');
        return;
    }
    if (!profileData.sourceType) {
        showMessage('Please select a data source type', 'error');
        return;
    }
    if (!profileData.destinationType) {
        showMessage('Please select a destination type', 'error');
        return;
    }

    try {
        const method = profileId ? 'PUT' : 'POST';
        const url = profileId ? `/api/imports/profiles/${profileId}` : '/api/imports/profiles';

        const response = await fetch(url, {
            method: method,
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(profileData)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || `HTTP error! status: ${response.status}`);
        }

        showMessage(
            profileId ? 'Profile updated successfully' : 'Profile created successfully',
            'success'
        );
        closeProfileModal();
        await loadProfiles();
    } catch (error) {
        console.error('Error saving profile:', error);
        showMessage('Error saving profile: ' + error.message, 'error');
    }
}

// Build source config from form
function buildSourceConfig() {
    const sourceType = document.getElementById('profile-source-type').value;
    const config = {};

    if (sourceType === 'rest') {
        config.url = document.getElementById('profile-rest-url').value;
        config.authType = document.getElementById('profile-rest-auth').value;
        config.token = document.getElementById('profile-rest-token').value;
        config.paginationType = document.getElementById('profile-rest-pagination').value;
        config.pageSize = parseInt(document.getElementById('profile-rest-page-size').value);
    } else if (sourceType === 's3') {
        config.bucket = document.getElementById('profile-s3-bucket').value;
        config.prefix = document.getElementById('profile-s3-prefix').value;
        config.region = document.getElementById('profile-s3-region').value;
    } else if (sourceType === 'ftp') {
        config.host = document.getElementById('profile-ftp-host').value;
        config.username = document.getElementById('profile-ftp-username').value;
        config.password = document.getElementById('profile-ftp-password').value;
        config.filePattern = document.getElementById('profile-ftp-file-pattern').value;
        config.useSftp = document.getElementById('profile-ftp-sftp').checked;
    } else if (sourceType === 'database') {
        config.query = document.getElementById('profile-db-query').value;
        config.connectionId = parseInt(document.getElementById('profile-db-connection').value);
    }

    return config;
}

// Build destination config from form
function buildDestinationConfig() {
    const destType = document.getElementById('profile-destination-type').value;
    const config = {};

    if (destType === 'database') {
        config.connectionId = parseInt(document.getElementById('profile-dest-db-connection').value);
        config.tableName = document.getElementById('profile-dest-table').value;
        config.writeMode = document.getElementById('profile-dest-write-mode').value;
        config.upsertKeys = document.getElementById('profile-upsert-keys').value;
    } else if (destType === 'file') {
        config.filePath = document.getElementById('profile-dest-file-path').value;
        config.format = document.getElementById('profile-dest-file-format').value;
    } else if (destType === 's3') {
        config.bucket = document.getElementById('profile-dest-s3-bucket').value;
        config.key = document.getElementById('profile-dest-s3-key').value;
        config.region = document.getElementById('profile-dest-s3-region').value;
    }

    return config;
}

// Build schedule config from form
function buildScheduleConfig() {
    const scheduleType = document.getElementById('profile-schedule-type').value;
    const config = {};

    if (scheduleType === 'cron') {
        config.cron = document.getElementById('profile-cron').value;
    } else if (scheduleType === 'interval') {
        config.intervalMinutes = parseInt(document.getElementById('profile-interval-minutes').value);
    } else if (scheduleType === 'daily') {
        config.time = document.getElementById('profile-daily-time').value;
    } else if (scheduleType === 'weekly') {
        config.time = document.getElementById('profile-weekly-time').value;
        config.daysOfWeek = Array.from(document.querySelectorAll('.weekly-day:checked')).map(cb => cb.value);
    }

    return config;
}

// Test connection
async function testConnection() {
    showMessage('Testing connection...', 'info');

    try {
        const sourceType = document.getElementById('profile-source-type').value;
        const sourceConfig = buildSourceConfig();

        // Simulate test by trying to fetch from source
        if (sourceType === 'rest') {
            const response = await fetch(sourceConfig.url, {
                method: 'HEAD',
                headers: sourceConfig.token ? { 'Authorization': `Bearer ${sourceConfig.token}` } : {}
            });

            if (response.ok) {
                showMessage('Connection test successful!', 'success');
            } else {
                showMessage(`Connection test failed: HTTP ${response.status}`, 'error');
            }
        } else {
            showMessage('Test connection feature not available for this source type yet', 'info');
        }
    } catch (error) {
        console.error('Connection test error:', error);
        showMessage('Connection test failed: ' + error.message, 'error');
    }
}

// Execute import profile
async function executeProfile(profileId) {
    if (!confirm('Are you sure you want to execute this import profile now?')) return;

    showMessage('Starting import...', 'info');

    try {
        const response = await fetch(`/api/imports/profiles/${profileId}/execute`, {
            method: 'POST'
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || `HTTP error! status: ${response.status}`);
        }

        const result = await response.json();
        showMessage(`Import started. Execution ID: ${result.id}`, 'success');

        // Redirect to execution details after a moment
        setTimeout(() => {
            window.location.href = `/import-executions.html?id=${result.id}`;
        }, 1500);
    } catch (error) {
        console.error('Error executing profile:', error);
        showMessage('Error executing profile: ' + error.message, 'error');
    }
}

// Delete profile
async function deleteProfile(profileId) {
    if (!confirm('Are you sure you want to delete this profile? This cannot be undone.')) return;

    try {
        const response = await fetch(`/api/imports/profiles/${profileId}`, {
            method: 'DELETE'
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || `HTTP error! status: ${response.status}`);
        }

        showMessage('Profile deleted successfully', 'success');
        await loadProfiles();
    } catch (error) {
        console.error('Error deleting profile:', error);
        showMessage('Error deleting profile: ' + error.message, 'error');
    }
}

// Open/close help modal
function openHelpModal() {
    document.getElementById('help-modal').classList.remove('hidden');
    lucide.createIcons();
}

function closeHelpModal() {
    document.getElementById('help-modal').classList.add('hidden');
}

// Show message
function showMessage(message, type = 'info', autoHide = false) {
    const container = document.getElementById('message-container');
    const messageId = `msg-${Date.now()}`;

    const colors = {
        'success': 'bg-green-50 border-green-200 text-green-800',
        'error': 'bg-red-50 border-red-200 text-red-800',
        'info': 'bg-blue-50 border-blue-200 text-blue-800',
        'warning': 'bg-yellow-50 border-yellow-200 text-yellow-800'
    };

    const icons = {
        'success': 'check-circle',
        'error': 'alert-circle',
        'info': 'info',
        'warning': 'alert-triangle'
    };

    const messageHtml = `
        <div id="${messageId}" class="mb-4 p-4 border rounded-lg flex items-start ${colors[type] || colors['info']}">
            <i data-lucide="${icons[type]}" class="h-5 w-5 mr-3 flex-shrink-0"></i>
            <div class="flex-1">
                <p>${escapeHtml(message)}</p>
            </div>
            <button onclick="document.getElementById('${messageId}').remove()" class="ml-3 flex-shrink-0">
                <i data-lucide="x" class="h-5 w-5"></i>
            </button>
        </div>
    `;

    container.insertAdjacentHTML('beforeend', messageHtml);
    lucide.createIcons();

    if (autoHide) {
        setTimeout(() => {
            const msg = document.getElementById(messageId);
            if (msg) msg.remove();
        }, 3000);
    }
}

// Utility function to escape HTML
function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
