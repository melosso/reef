// Import Executions Management Module
// Handles monitoring and tracking of import execution history

let allExecutions = [];
let filteredExecutions = [];
let currentSort = { field: 'startTime', direction: 'desc' };
let currentFilters = { profile: '', status: '', date: '', search: '' };
let currentPage = 1;
let pageSize = 25;

// Page initialization
document.addEventListener('DOMContentLoaded', async function() {
    await loadExecutions();
    await loadProfiles();
    startAutoRefresh();
    lucide.createIcons();
});

// Auto-refresh executions every 10 seconds
function startAutoRefresh() {
    setInterval(async () => {
        // Only refresh if there are running executions
        const hasRunning = filteredExecutions.some(e => e.status === 'running');
        if (hasRunning) {
            await loadExecutions(false); // silent refresh
        }
    }, 10000);
}

// Load all execution histories from API
async function loadExecutions(shouldShowMessage = true) {
    try {
        const response = await fetch('/api/imports/executions');
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);

        const result = await response.json();
        allExecutions = result.data || [];

        applyFilters();
        renderExecutions();
        updateSummaryCards();

        if (shouldShowMessage) {
            // Nothing to show on successful load
        }
    } catch (error) {
        console.error('Error loading executions:', error);
        if (shouldShowMessage) {
            showMessage('Error loading executions: ' + error.message, 'error');
        }
        document.getElementById('executions-tbody').innerHTML = `
            <tr>
                <td colspan="7" class="px-6 py-4 text-center text-red-500">
                    Error loading executions. Please refresh the page.
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

// Apply filters to executions
function applyFilters() {
    currentFilters.profile = document.getElementById('filter-profile').value;
    currentFilters.status = document.getElementById('filter-status').value;
    currentFilters.date = document.getElementById('filter-date').value;
    currentFilters.search = document.getElementById('filter-search').value.toLowerCase();

    filteredExecutions = allExecutions.filter(execution => {
        const profileMatch = !currentFilters.profile || execution.profileId === parseInt(currentFilters.profile);
        const statusMatch = !currentFilters.status || execution.status === currentFilters.status;
        const searchMatch = !currentFilters.search ||
            execution.id.toString().includes(currentFilters.search) ||
            execution.profileName.toLowerCase().includes(currentFilters.search);

        // Date filter
        let dateMatch = true;
        if (currentFilters.date) {
            const execTime = new Date(execution.startTime);
            const now = new Date();

            switch (currentFilters.date) {
                case 'today':
                    dateMatch = execTime.toDateString() === now.toDateString();
                    break;
                case 'week':
                    dateMatch = (now - execTime) < 7 * 24 * 60 * 60 * 1000;
                    break;
                case 'month':
                    dateMatch = (now - execTime) < 30 * 24 * 60 * 60 * 1000;
                    break;
                default:
                    dateMatch = true;
            }
        }

        return profileMatch && statusMatch && searchMatch && dateMatch;
    });

    currentPage = 1;
    sortExecutions(currentSort.field);
}

// Clear all filters
function clearFilters() {
    document.getElementById('filter-profile').value = '';
    document.getElementById('filter-status').value = '';
    document.getElementById('filter-date').value = '';
    document.getElementById('filter-search').value = '';
    applyFilters();
}

// Sort executions by field
function sortExecutions(field) {
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
    filteredExecutions.sort((a, b) => {
        let aVal = a[field];
        let bVal = b[field];

        // Handle date comparison
        if (field === 'startTime' || field === 'endTime') {
            aVal = new Date(aVal || 0);
            bVal = new Date(bVal || 0);
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

    renderExecutions();
}

// Render executions table with pagination
function renderExecutions() {
    const tbody = document.getElementById('executions-tbody');
    const startIdx = (currentPage - 1) * pageSize;
    const endIdx = startIdx + pageSize;
    const pageExecutions = filteredExecutions.slice(startIdx, endIdx);

    if (pageExecutions.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="7" class="px-6 py-4 text-center text-gray-500">
                    No executions found.
                </td>
            </tr>
        `;
        updatePagination();
        return;
    }

    tbody.innerHTML = pageExecutions.map(execution => {
        const duration = calculateDuration(execution.startTime, execution.endTime);
        const statusColor = getStatusColor(execution.status);
        const statusIcon = getStatusIcon(execution.status);

        return `
            <tr class="hover:bg-gray-50 cursor-pointer" onclick="showExecutionDetails(${execution.id})">
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="text-sm font-semibold text-gray-900">#${execution.id}</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="text-sm text-gray-700">${escapeHtml(execution.profileName)}</span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap">
                    <span class="px-2 py-1 inline-flex text-xs leading-5 font-semibold rounded-full ${statusColor}">
                        ${statusIcon} ${execution.status.charAt(0).toUpperCase() + execution.status.slice(1)}
                    </span>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                    ${formatDateTime(execution.startTime)}
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm font-semibold text-gray-900">
                    ${duration}
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-700">
                    <span class="text-green-600">${execution.rowsWritten}</span> / ${execution.rowsRead}
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button onclick="event.stopPropagation(); showExecutionDetails(${execution.id})" class="text-blue-600 hover:text-blue-900">View</button>
                </td>
            </tr>
        `;
    }).join('');

    updatePagination();
    lucide.createIcons();
}

// Get status color class
function getStatusColor(status) {
    const colors = {
        'completed': 'bg-green-100 text-green-800',
        'running': 'bg-yellow-100 text-yellow-800',
        'failed': 'bg-red-100 text-red-800',
        'canceled': 'bg-gray-100 text-gray-800'
    };
    return colors[status] || 'bg-gray-100 text-gray-800';
}

// Get status icon
function getStatusIcon(status) {
    const icons = {
        'completed': '✓',
        'running': '⟳',
        'failed': '✕',
        'canceled': '⊘'
    };
    return icons[status] || '—';
}

// Calculate duration between two timestamps
function calculateDuration(startTime, endTime) {
    if (!startTime) return '-';
    if (!endTime) return 'In Progress...';

    const start = new Date(startTime);
    const end = new Date(endTime);
    const diffMs = end - start;

    const hours = Math.floor(diffMs / 3600000);
    const minutes = Math.floor((diffMs % 3600000) / 60000);
    const seconds = Math.floor((diffMs % 60000) / 1000);

    if (hours > 0) {
        return `${hours}h ${minutes}m`;
    } else if (minutes > 0) {
        return `${minutes}m ${seconds}s`;
    } else {
        return `${seconds}s`;
    }
}

// Format date and time
function formatDateTime(dateString) {
    if (!dateString) return '-';

    const date = new Date(dateString);
    const now = new Date();
    const diffHours = (now - date) / 3600000;

    if (diffHours < 24) {
        // Show time only
        return date.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit' });
    } else if (diffHours < 168) {
        // Show day of week
        return date.toLocaleDateString('en-US', { weekday: 'short' });
    } else {
        // Show date
        return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
    }
}

// Update summary cards
function updateSummaryCards() {
    const total = allExecutions.length;
    const successful = allExecutions.filter(e => e.status === 'completed').length;
    const failed = allExecutions.filter(e => e.status === 'failed').length;
    const running = allExecutions.filter(e => e.status === 'running').length;

    document.getElementById('total-executions').textContent = total;
    document.getElementById('successful-executions').textContent = successful;
    document.getElementById('failed-executions').textContent = failed;
    document.getElementById('running-executions').textContent = running;
}

// Show execution details in modal
async function showExecutionDetails(executionId) {
    const execution = allExecutions.find(e => e.id === executionId);
    if (!execution) return;

    try {
        // Load full details from API
        const response = await fetch(`/api/imports/executions/${executionId}/details`);
        if (!response.ok) throw new Error('Failed to load details');

        const result = await response.json();
        const details = result.data;

        // Populate overview tab
        document.getElementById('detail-id').textContent = `#${details.id}`;
        document.getElementById('detail-profile').textContent = details.profileName || execution.profileName;
        document.getElementById('detail-triggered-by').textContent = execution.triggeredBy;
        document.getElementById('detail-start-time').textContent = formatDateTime(details.startTime);
        document.getElementById('detail-end-time').textContent = details.endTime ? formatDateTime(details.endTime) : '-';
        document.getElementById('detail-duration').textContent = calculateDuration(details.startTime, details.endTime);
        document.getElementById('detail-message').textContent = details.message || '-';

        // Populate status badge
        const statusColor = getStatusColor(details.status);
        document.getElementById('detail-status').innerHTML = `
            <span class="px-3 py-1 inline-flex text-sm leading-5 font-semibold rounded-full ${statusColor}">
                ${getStatusIcon(details.status)} ${details.status.charAt(0).toUpperCase() + details.status.slice(1)}
            </span>
        `;

        // Populate statistics
        document.getElementById('detail-rows-read').textContent = details.rowsRead.toLocaleString();
        document.getElementById('detail-rows-written').textContent = details.rowsWritten.toLocaleString();
        document.getElementById('detail-rows-failed').textContent = details.rowsFailed.toLocaleString();
        document.getElementById('detail-rows-skipped').textContent = details.rowsSkipped.toLocaleString();

        // Load logs
        const logsResponse = await fetch(`/api/imports/executions/${executionId}/logs`);
        if (logsResponse.ok) {
            const logsResult = await logsResponse.json();
            const logs = logsResult.data || [];
            const logsDiv = document.getElementById('detail-logs');
            if (logs.length > 0) {
                logsDiv.innerHTML = logs.map(log => `<p>${escapeHtml(log)}</p>`).join('');
            }
        }

        // Load errors
        const errorsResponse = await fetch(`/api/imports/executions/${executionId}/errors`);
        if (errorsResponse.ok) {
            const errorsResult = await errorsResponse.json();
            const errors = errorsResult.data || [];
            const errorsDiv = document.getElementById('detail-errors');
            if (errors.length > 0) {
                errorsDiv.innerHTML = errors.map(error => `
                    <div class="border border-red-200 bg-red-50 rounded-lg p-3">
                        <p class="text-sm font-semibold text-red-900">Row ${error.rowNumber || '?'}</p>
                        <p class="text-sm text-red-700 mt-1">${escapeHtml(error.message || 'Unknown error')}</p>
                    </div>
                `).join('');
            }
        }

        document.getElementById('details-modal-title').textContent = `Execution #${details.id} Details`;
        showDetailsTab('overview');
        document.getElementById('details-modal').classList.remove('hidden');
        lucide.createIcons();

    } catch (error) {
        console.error('Error loading execution details:', error);
        showMessage('Error loading execution details: ' + error.message, 'error');
    }
}

// Close details modal
function closeDetailsModal() {
    document.getElementById('details-modal').classList.add('hidden');
}

// Show details tab
function showDetailsTab(tabName) {
    // Hide all tabs
    document.querySelectorAll('[id$="Tab"]').forEach(tab => {
        if (tab.id.includes('Button')) return; // Skip buttons
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

// Pagination functions
function updatePagination() {
    const total = filteredExecutions.length;
    const totalPages = Math.ceil(total / pageSize);
    const startRow = (currentPage - 1) * pageSize + 1;
    const endRow = Math.min(currentPage * pageSize, total);

    document.getElementById('page-info').textContent = `${startRow} to ${endRow} of ${total}`;
}

function previousPage() {
    if (currentPage > 1) {
        currentPage--;
        renderExecutions();
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
}

function nextPage() {
    const totalPages = Math.ceil(filteredExecutions.length / pageSize);
    if (currentPage < totalPages) {
        currentPage++;
        renderExecutions();
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }
}

// Refresh executions manually
async function refreshExecutions() {
    showMessage('Refreshing executions...', 'info', false);
    await loadExecutions(false);
    showMessage('Executions refreshed', 'success', true);
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
