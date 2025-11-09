// Import Quarantine Management Module
// Handles failed row review and management

let allQuarantined = [];
let filteredQuarantined = [];
let currentSort = { field: 'quarantinedAt', direction: 'desc' };
let currentFilters = { profile: '', status: '', errorType: '', search: '' };
let selectedRows = new Set();

// Page initialization
document.addEventListener('DOMContentLoaded', async function() {
    await loadQuarantined();
    await loadProfiles();
    lucide.createIcons();
});

// Load all quarantined rows from API
async function loadQuarantined() {
    try {
        const response = await fetch('/api/imports/quarantine');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

        const result = await response.json();
        allQuarantined = result.data || [];

        applyFilters();
        renderQuarantined();
        updateSummaryCards();
    } catch (error) {
        console.error('Error loading quarantine:', error);
        showMessage('Error loading quarantine data: ' + error.message, 'error');
        document.getElementById('quarantine-tbody').innerHTML = `
            <tr>
                <td colspan="8" class="px-6 py-4 text-center text-red-500">
                    Error loading quarantine data. Please refresh the page.
                </td>
            </tr>
        `;
    }
}

// Load profiles for dropdown
async function loadProfiles() {
    try {
        const response = await fetch('/api/imports/profiles');
        if (!response.ok) return;

        const result = await response.json();
        const profiles = result.data || [];

        const profileSelect = document.getElementById('filter-profile');
        profiles.forEach(profile => {
            const option = document.createElement('option');
            option.value = profile.id;
            option.textContent = profile.name;
            profileSelect.appendChild(option);
        });
    } catch (error) {
        console.error('Error loading profiles:', error);
    }
}

// Apply filters to quarantined rows
function applyFilters() {
    currentFilters.profile = document.getElementById('filter-profile').value;
    currentFilters.status = document.getElementById('filter-status').value;
    currentFilters.errorType = document.getElementById('filter-error-type').value;
    currentFilters.search = document.getElementById('filter-search').value.toLowerCase();

    filteredQuarantined = allQuarantined.filter(row => {
        const profileMatch = !currentFilters.profile || row.profileId === parseInt(currentFilters.profile);
        const statusMatch = !currentFilters.status || row.status === currentFilters.status;
        const errorTypeMatch = !currentFilters.errorType || row.errorType === currentFilters.errorType;
        const searchMatch = !currentFilters.search ||
            row.rowId.toString().includes(currentFilters.search) ||
            row.errorMessage.toLowerCase().includes(currentFilters.search);

        return profileMatch && statusMatch && errorTypeMatch && searchMatch;
    });

    sortQuarantine(currentSort.field);
}

// Clear all filters
function clearFilters() {
    document.getElementById('filter-profile').value = '';
    document.getElementById('filter-status').value = '';
    document.getElementById('filter-error-type').value = '';
    document.getElementById('filter-search').value = '';
    applyFilters();
}

// Sort quarantined rows
function sortQuarantine(field) {
    if (currentSort.field === field) {
        currentSort.direction = currentSort.direction === 'asc' ? 'desc' : 'asc';
    } else {
        currentSort.field = field;
        currentSort.direction = 'desc';
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
    filteredQuarantined.sort((a, b) => {
        let aVal = a[field];
        let bVal = b[field];

        if (field === 'quarantinedAt') {
            aVal = new Date(aVal);
            bVal = new Date(bVal);
        } else if (typeof aVal === 'string') {
            aVal = aVal.toLowerCase();
            bVal = bVal.toLowerCase();
        }

        if (currentSort.direction === 'asc') {
            return aVal > bVal ? 1 : -1;
        } else {
            return aVal < bVal ? 1 : -1;
        }
    });

    renderQuarantined();
}

// Render quarantined rows table
function renderQuarantined() {
    const tbody = document.getElementById('quarantine-tbody');

    if (filteredQuarantined.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="8" class="px-6 py-4 text-center text-gray-500">
                    No quarantined rows found.
                </td>
            </tr>
        `;
        return;
    }

    tbody.innerHTML = filteredQuarantined.map(row => {
        const isSelected = selectedRows.has(row.id);
        const statusColor = getStatusColor(row.status);

        return `
            <tr class="hover:bg-gray-50 ${isSelected ? 'bg-blue-50' : ''}">
                <td class="px-6 py-4 whitespace-nowrap">
                    <input type="checkbox" value="${row.id}" onchange="updateSelectedRows()" ${isSelected ? 'checked' : ''} class="rounded border-gray-300">
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="text-sm font-semibold text-gray-900">${row.rowId}</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="text-sm text-gray-700">${escapeHtml(row.profileName)}</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${getErrorTypeColor(row.errorType)}">
                        ${row.errorType.charAt(0).toUpperCase() + row.errorType.slice(1)}
                    </span>
                </td>
                <td class="px-6 py-4 text-sm text-gray-700">
                    <div class="truncate max-w-xs">${escapeHtml(row.errorMessage)}</div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                    ${formatDateTime(row.quarantinedAt)}
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${statusColor}">
                        ${row.status.charAt(0).toUpperCase() + row.status.slice(1)}
                    </span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button onclick="showRowDetails(${row.id})" class="text-blue-600 hover:text-blue-900">View</button>
                </td>
            </tr>
        `;
    }).join('');

    lucide.createIcons();
}

// Get error type color
function getErrorTypeColor(errorType) {
    const colors = {
        'validation': 'bg-yellow-100 text-yellow-800',
        'constraint': 'bg-red-100 text-red-800',
        'conversion': 'bg-orange-100 text-orange-800',
        'other': 'bg-gray-100 text-gray-800'
    };
    return colors[errorType] || 'bg-gray-100 text-gray-800';
}

// Get status color
function getStatusColor(status) {
    const colors = {
        'pending': 'bg-blue-100 text-blue-800',
        'reviewed': 'bg-yellow-100 text-yellow-800',
        'resolved': 'bg-green-100 text-green-800'
    };
    return colors[status] || 'bg-gray-100 text-gray-800';
}

// Format date and time
function formatDateTime(dateString) {
    if (!dateString) return '-';

    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
}

// Update summary cards
function updateSummaryCards() {
    const total = allQuarantined.length;
    const pending = allQuarantined.filter(r => r.status === 'pending').length;
    const reviewed = allQuarantined.filter(r => r.status === 'reviewed').length;

    document.getElementById('total-quarantined').textContent = total;
    document.getElementById('pending-review').textContent = pending;
    document.getElementById('reviewed-count').textContent = reviewed;
}

// Toggle select all checkbox
function toggleSelectAll() {
    const checked = document.getElementById('select-all').checked;

    if (checked) {
        filteredQuarantined.forEach(row => selectedRows.add(row.id));
    } else {
        selectedRows.clear();
    }

    renderQuarantined();
    updateSelectedRows();
}

// Update selected rows
function updateSelectedRows() {
    selectedRows.clear();
    document.querySelectorAll('input[type="checkbox"][value]').forEach(checkbox => {
        if (checkbox.checked) {
            selectedRows.add(parseInt(checkbox.value));
        }
    });

    const bulkActions = document.getElementById('bulk-actions');
    if (selectedRows.size > 0) {
        document.getElementById('selected-count').textContent = selectedRows.size;
        bulkActions.classList.remove('hidden');
    } else {
        bulkActions.classList.add('hidden');
    }

    document.getElementById('select-all').checked = selectedRows.size === filteredQuarantined.length && filteredQuarantined.length > 0;
}

// Show row details in modal
function showRowDetails(rowId) {
    const row = allQuarantined.find(r => r.id === rowId);
    if (!row) return;

    document.getElementById('row-details-title').textContent = `Row #${row.rowId} Details`;
    document.getElementById('row-error-type').textContent = row.errorType.charAt(0).toUpperCase() + row.errorType.slice(1);
    document.getElementById('row-error-message').textContent = row.errorMessage;
    document.getElementById('review-notes').value = row.reviewNotes || '';

    // Populate row data table
    const dataTable = document.getElementById('row-data-table');
    dataTable.innerHTML = Object.entries(row.rowData).map(([key, value]) => `
        <tr>
            <td class="px-3 py-2 text-gray-700 font-semibold">${escapeHtml(key)}</td>
            <td class="px-3 py-2 text-gray-700">${escapeHtml(value)}</td>
        </tr>
    `).join('');

    document.getElementById('row-details-modal').classList.remove('hidden');
    lucide.createIcons();
    window.currentRowId = rowId;
}

// Close row details modal
function closeRowDetailsModal() {
    document.getElementById('row-details-modal').classList.add('hidden');
}

// Mark row as reviewed
function markRowAsReviewed() {
    const rowId = window.currentRowId;
    const notes = document.getElementById('review-notes').value;

    const row = allQuarantined.find(r => r.id === rowId);
    if (row) {
        row.status = 'reviewed';
        row.reviewNotes = notes;
    }

    renderQuarantined();
    updateSummaryCards();
    closeRowDetailsModal();
    showMessage('Row marked as reviewed', 'success', true);
}

// Mark selected rows as reviewed
async function markAsReviewed() {
    if (selectedRows.size === 0) {
        showMessage('No rows selected', 'warning');
        return;
    }

    try {
        allQuarantined.forEach(row => {
            if (selectedRows.has(row.id)) {
                row.status = 'reviewed';
            }
        });

        selectedRows.clear();
        applyFilters();
        renderQuarantined();
        updateSummaryCards();
        updateSelectedRows();

        showMessage(`${selectedRows.size} rows marked as reviewed`, 'success', true);
    } catch (error) {
        console.error('Error marking rows as reviewed:', error);
        showMessage('Error marking rows as reviewed: ' + error.message, 'error');
    }
}

// Delete quarantined rows
async function deleteQuarantined() {
    if (selectedRows.size === 0) {
        showMessage('No rows selected', 'warning');
        return;
    }

    if (!confirm(`Delete ${selectedRows.size} quarantined rows? This cannot be undone.`)) return;

    try {
        allQuarantined = allQuarantined.filter(row => !selectedRows.has(row.id));
        selectedRows.clear();
        applyFilters();
        renderQuarantined();
        updateSummaryCards();
        updateSelectedRows();

        showMessage(`${selectedRows.size} rows deleted from quarantine`, 'success', true);
    } catch (error) {
        console.error('Error deleting quarantined rows:', error);
        showMessage('Error deleting rows: ' + error.message, 'error');
    }
}

// Refresh quarantine data
async function refreshQuarantine() {
    showMessage('Refreshing quarantine data...', 'info', false);
    await loadQuarantined();
    showMessage('Quarantine data refreshed', 'success', true);
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
