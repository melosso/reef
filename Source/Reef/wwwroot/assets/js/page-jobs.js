const API_BASE = '/api/jobs';
let allJobs = [];
let filteredJobs = [];
let profiles = [];
let connections = [];
let groups = [];
let destinations = [];

// Sorting state - Load from localStorage or use defaults
let currentSortColumn = localStorage.getItem('jobs_sortColumn') || 'name';
let currentSortDirection = localStorage.getItem('jobs_sortDirection') || 'asc';

// Status mapping: 0=Idle, 1=Queued/Scheduled, 2=Running, 3=Completed, 4=Failed, 5=Cancelled
const statusEnumMap = {
    'Idle': 0,
    'Scheduled': 1, 
    'Queued': 1,    // Map Queued to same value as Scheduled
    'Running': 2,
    'Completed': 3,
    'Failed': 4,
    'Cancelled': 5
};

// Schedule type mapping: 0=Manual, 1=Cron, 2=Interval, 3=Daily, 4=Weekly, 5=Monthly
const scheduleTypeEnumMap = {
    'Manual': 0,
    'Cron': 1,
    'CronExpression': 1,
    'Interval': 2,
    'Daily': 3,
    'Weekly': 4,
    'Monthly': 5
};

function getStatusInfo(status) {
    const statusValue = typeof status === 'string' ? statusEnumMap[status] : status;
    
    switch(statusValue) {
        case 0: return { label: 'Idle', color: 'text-gray-600', bg: 'bg-gray-100', icon: 'pause-circle' };
        case 1: return { label: 'Queued', color: 'text-blue-600', bg: 'bg-blue-100', icon: 'clock' };
        case 2: return { label: 'Running', color: 'text-green-600', bg: 'bg-green-100', icon: 'play-circle' };
        case 3: return { label: 'Completed', color: 'text-green-600', bg: 'bg-green-100', icon: 'check-circle' };
        case 4: return { label: 'Failed', color: 'text-red-600', bg: 'bg-red-100', icon: 'alert-circle' };
        case 5: return { label: 'Cancelled', color: 'text-gray-600', bg: 'bg-gray-100', icon: 'x-circle' };
        default: return { label: 'Unknown', color: 'text-gray-600', bg: 'bg-gray-100', icon: 'help-circle' };
    }
}

function getJobTypeInfo(type) {
    switch(type) {
        case 'ProfileExecution': 
        case 0: return { label: 'Profile Export', icon: 'file-code', color: 'text-blue-600' };
        case 'DataTransfer': 
        case 1: return { label: 'Data Transfer', icon: 'arrow-right-left', color: 'text-purple-600' };
        case 'CustomScript': 
        case 6: return { label: 'Custom Script', icon: 'code', color: 'text-orange-600' };
        default: return { label: 'Unknown', icon: 'help-circle', color: 'text-gray-600' };
    }
}

function getScheduleInfo(scheduleType) {
    let scheduleValue = scheduleType;

    // Convert string enum to number
    if (typeof scheduleType === 'string') {
        if (scheduleTypeEnumMap.hasOwnProperty(scheduleType)) {
            scheduleValue = scheduleTypeEnumMap[scheduleType];
        } else {
            scheduleValue = parseInt(scheduleType);
        }
    }

    switch(scheduleValue) {
        case 0: return { label: 'Manual', icon: 'hand', color: 'text-gray-600' };
        case 1: return { label: 'Cron', icon: 'calendar', color: 'text-purple-600' };
        case 2: return { label: 'Interval', icon: 'repeat', color: 'text-blue-600' };
        case 3: return { label: 'Daily', icon: 'sun', color: 'text-yellow-600' };
        case 4: return { label: 'Weekly', icon: 'calendar-days', color: 'text-indigo-600' };
        case 5: return { label: 'Monthly', icon: 'calendar-range', color: 'text-pink-600' };
        default: return { label: 'Unknown', icon: 'help-circle', color: 'text-gray-600' };
    }
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function formatDateTime(dateString) {
    if (!dateString) return 'Never';
    let date;
    try {
        // If already ends with Z or has timezone, don't add Z
        if (/Z$|[+-]\d{2}:?\d{2}$/.test(dateString)) {
            date = new Date(dateString);
        } else {
            date = new Date(dateString + 'Z');
        }
        if (isNaN(date.getTime())) return 'Never';
        return date.toLocaleString();
    } catch {
        return 'Never';
    }
}

function formatRelativeTime(dateString) {
    if (!dateString) return null;
    let date;
    try {
        // If already ends with Z or has timezone, don't add Z
        if (/Z$|[+-]\d{2}:?\d{2}$/.test(dateString)) {
            date = new Date(dateString);
        } else {
            date = new Date(dateString + 'Z');
        }
        if (isNaN(date.getTime())) return null;
    } catch {
        return null;
    }

    const now = new Date();
    const diffMs = now - date;
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return `${diffMins}m ago`;
    if (diffHours < 24) return `${diffHours}h ago`;
    if (diffDays < 7) return `${diffDays}d ago`;
    return null;
}

function showJobTab(tabName) {
    // Hide all tab contents
    document.querySelectorAll('.job-tab-content').forEach(tab => {
        tab.classList.add('hidden');
    });
    
    // Remove active class from all buttons
    document.querySelectorAll('.job-tab-btn').forEach(btn => {
        btn.classList.remove('job-tab-active', 'border-blue-500', 'text-blue-600');
        btn.classList.add('border-transparent', 'text-gray-500');
    });
    
    // Show selected tab
    document.getElementById(tabName).classList.remove('hidden');
    
    // Add active class to selected button
    const activeBtn = document.getElementById(tabName + 'TabButton');
    activeBtn.classList.add('job-tab-active', 'border-blue-500', 'text-blue-600');
    activeBtn.classList.remove('border-transparent', 'text-gray-500');
    
    lucide.createIcons();
}

function updateScheduleFields() {
    const scheduleType = parseInt(document.getElementById('job-scheduleType').value);
    const scheduleConfig = document.getElementById('schedule-config');
    
    let html = '';
    switch(scheduleType) {
        case 1: // Cron
            html = `
                <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Cron Expression</label>
                    <input type="text" id="job-cronExpression" placeholder="0 2 * * *"
                            class="w-full px-3 py-2 border border-gray-300 rounded-md">
                    <p class="text-xs text-gray-500 mt-1">Example: 0 2 * * * (daily at 2 AM)</p>
                </div>
            `;
            break;
        case 2: // Interval
            html = `
                <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Interval (minutes)</label>
                    <input type="number" id="job-intervalMinutes" value="60" min="1"
                            class="w-full px-3 py-2 border border-gray-300 rounded-md">
                </div>
            `;
            break;
        case 3: // Daily
        case 4: // Weekly
        case 5: // Monthly
            html = `
                <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Start Time</label>
                    <input type="time" id="job-startTime" value="02:00"
                            class="w-full px-3 py-2 border border-gray-300 rounded-md">
                </div>
            `;
            break;
    }
    
    scheduleConfig.innerHTML = html;
}

function sortJobs(column) {
    // Add toggle parameter to control direction toggling
    let toggle = true;
    if (typeof arguments[1] === 'boolean') toggle = arguments[1];
    // Toggle direction if clicking the same column, otherwise default to asc
    if (currentSortColumn === column) {
        if (toggle) {
            currentSortDirection = currentSortDirection === 'asc' ? 'desc' : 'asc';
        }
    } else {
        currentSortColumn = column;
        currentSortDirection = 'asc';
    }

    // Save sort preferences to localStorage
    localStorage.setItem('jobs_sortColumn', currentSortColumn);
    localStorage.setItem('jobs_sortDirection', currentSortDirection);

    // Update sort icons
    ['name', 'schedule', 'execution', 'status'].forEach(col => {
        const icon = document.getElementById(`sort-icon-${col}`);
        if (icon) {
            if (col === column) {
                icon.setAttribute('data-lucide', currentSortDirection === 'asc' ? 'chevron-up' : 'chevron-down');
            } else {
                icon.setAttribute('data-lucide', 'chevrons-up-down');
            }
        }
    });
    lucide.createIcons();

    // Sort the filtered jobs
    filteredJobs.sort((a, b) => {
        let aVal, bVal;
        
        switch(column) {
            case 'name':
                aVal = (a.name || '').toLowerCase();
                bVal = (b.name || '').toLowerCase();
                break;
            case 'schedule':
                // Sort by schedule type, then by name
                aVal = typeof a.scheduleType === 'string' ? parseInt(a.scheduleType) : (a.scheduleType || 0);
                bVal = typeof b.scheduleType === 'string' ? parseInt(b.scheduleType) : (b.scheduleType || 0);
                break;
            case 'execution':
                // Sort by last run time (null values last)
                aVal = a.lastRunTime ? new Date(a.lastRunTime).getTime() : 0;
                bVal = b.lastRunTime ? new Date(b.lastRunTime).getTime() : 0;
                break;
            case 'status':
                aVal = typeof a.status === 'string' ? statusEnumMap[a.status] : (a.status || 0);
                bVal = typeof b.status === 'string' ? statusEnumMap[b.status] : (b.status || 0);
                break;
            default:
                return 0;
        }

        let comparison = 0;
        if (aVal < bVal) comparison = -1;
        if (aVal > bVal) comparison = 1;

        // Secondary sort by name if not already sorting by name
        if (comparison === 0 && column !== 'name') {
            const aName = (a.name || '').toLowerCase();
            const bName = (b.name || '').toLowerCase();
            if (aName < bName) comparison = -1;
            if (aName > bName) comparison = 1;
        }

        return currentSortDirection === 'asc' ? comparison : -comparison;
    });

    renderJobs();
}

function toggleQueueMetrics() {
    const panel = document.getElementById('queue-metrics-panel');
    if (panel.classList.contains('hidden')) {
        panel.classList.remove('hidden');
        loadQueueMetrics();
    } else {
        panel.classList.add('hidden');
    }
    lucide.createIcons();
}

async function loadQueueMetrics() {
    try {
        const response = await fetch('/api/jobs/queue/metrics', {
            headers: getAuthHeaders()
        });
        
        if (!response.ok) {
            console.error('Failed to load queue metrics:', response.status);
            return;
        }
        
        const metrics = await response.json();
        updateQueueMetrics(metrics);
    } catch (error) {
        console.error('Error loading queue metrics:', error);
    }
}

function updateQueueMetrics(metrics) {
    document.getElementById('queue-depth').textContent = metrics.queueDepth || 0;
    document.getElementById('queue-active').textContent = metrics.activeJobs || 0;
    document.getElementById('queue-available').textContent = metrics.availableSlots || 0;
    document.getElementById('queue-max').textContent = metrics.maxConcurrentJobs || 10;
    document.getElementById('queue-processed').textContent = metrics.totalDequeued || 0;
    
    const maxJobs = metrics.maxConcurrentJobs || 10;
    const activeJobs = metrics.activeJobs || 0;
    const usagePercent = Math.round((activeJobs / maxJobs) * 100);
    
    document.getElementById('queue-usage-percent').textContent = `${usagePercent}%`;
    const usageBar = document.getElementById('queue-usage-bar');
    usageBar.style.width = `${usagePercent}%`;
    
    usageBar.className = 'h-2 rounded-full transition-all duration-300';
    if (usagePercent < 50) {
        usageBar.classList.add('bg-green-600');
    } else if (usagePercent < 80) {
        usageBar.classList.add('bg-yellow-600');
    } else {
        usageBar.classList.add('bg-red-600');
    }
}

async function refreshQueueMetrics() {
    await loadQueueMetrics();
    showToast('Queue metrics refreshed', 'success');
}

async function applyFilters() {
    const healthFilter = document.getElementById('filter-health').value;
    const statusFilter = document.getElementById('filter-status').value;
    const profileFilter = document.getElementById('filter-profile').value;
    const connectionFilter = document.getElementById('filter-connection').value;
    const groupFilter = document.getElementById('filter-group').value;

    // Save filter preferences to localStorage
    localStorage.setItem('jobs_filterHealth', healthFilter);
    localStorage.setItem('jobs_filterStatus', statusFilter);
    localStorage.setItem('jobs_filterProfile', profileFilter);
    localStorage.setItem('jobs_filterConnection', connectionFilter);
    localStorage.setItem('jobs_filterGroup', groupFilter);

    filteredJobs = allJobs.filter(job => {
        // Health filter
        if (healthFilter) {
            const failures = job.consecutiveFailures || 0;
            const isAutoPaused = job.tags && job.tags.includes('circuit-breaker');

            switch (healthFilter) {
                case 'auto-paused':
                    if (!isAutoPaused) return false;
                    break;
                case 'failing':
                    if (failures < 7 || isAutoPaused) return false;
                    break;
                case 'warning':
                    if (failures < 3 || failures >= 7 || isAutoPaused) return false;
                    break;
                case 'healthy':
                    if (failures >= 3 || isAutoPaused || !job.isEnabled) return false;
                    break;
                case 'disabled':
                    if (job.isEnabled) return false;
                    break;
            }
        }

        if (statusFilter && job.status != statusFilter) return false;
        if (profileFilter && job.profileId != profileFilter) return false;

        // Connection filter - find profile's connection
        if (connectionFilter) {
            const profile = profiles.find(p => p.id === job.profileId);
            if (!profile || profile.connectionId != connectionFilter) return false;
        }

        // Group filter - find profile's group
        if (groupFilter) {
            const profile = profiles.find(p => p.id === job.profileId);
            if (!profile || profile.groupId != groupFilter) return false;
        }

        return true;
    });

    sortJobs(currentSortColumn, false);
}

async function loadFilterData() {
    try {
        // Load profiles
        const profilesResponse = await fetch('/api/profiles', {
            headers: getAuthHeaders()
        });
        if (profilesResponse.ok) {
            profiles = await profilesResponse.json();
            const profileSelect = document.getElementById('filter-profile');
            profileSelect.innerHTML = '<option value="">Show All</option>' +
                profiles.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
        }

        // Load connections
        const connectionsResponse = await fetch('/api/connections', {
            headers: getAuthHeaders()
        });
        if (connectionsResponse.ok) {
            connections = await connectionsResponse.json();
            const connectionSelect = document.getElementById('filter-connection');
            connectionSelect.innerHTML = '<option value="">Show All</option>' +
                connections.map(c => `<option value="${c.id}">${escapeHtml(c.name)}</option>`).join('');
        }

        // Load groups
        const groupsResponse = await fetch('/api/groups', {
            headers: getAuthHeaders()
        });
        if (groupsResponse.ok) {
            groups = await groupsResponse.json();
            const groupSelect = document.getElementById('filter-group');
            groupSelect.innerHTML = '<option value="">Show All</option>' +
                groups.map(g => `<option value="${g.id}">${escapeHtml(g.name)}</option>`).join('');
        }

        // Restore saved filter values from localStorage
        restoreFilterPreferences();
    } catch (error) {
        console.error('Failed to load filter data:', error);
    }
}

function restoreFilterPreferences() {
    const savedStatus = localStorage.getItem('jobs_filterStatus');
    const savedProfile = localStorage.getItem('jobs_filterProfile');
    const savedConnection = localStorage.getItem('jobs_filterConnection');
    const savedGroup = localStorage.getItem('jobs_filterGroup');

    if (savedStatus) {
        const statusSelect = document.getElementById('filter-status');
        if (statusSelect) statusSelect.value = savedStatus;
    }

    if (savedProfile) {
        const profileSelect = document.getElementById('filter-profile');
        if (profileSelect) profileSelect.value = savedProfile;
    }

    if (savedConnection) {
        const connectionSelect = document.getElementById('filter-connection');
        if (connectionSelect) connectionSelect.value = savedConnection;
    }

    if (savedGroup) {
        const groupSelect = document.getElementById('filter-group');
        if (groupSelect) groupSelect.value = savedGroup;
    }
}

function openAddJobModal() {
    document.getElementById('job-modal-title').textContent = 'Add New Job';
    document.getElementById('add-job-modal').classList.remove('hidden');
    document.getElementById('add-job-form').reset();
    document.getElementById('add-job-form').removeAttribute('data-edit-id');
    document.getElementById('job-enabled').checked = true;
    updateScheduleFields();
    loadProfilesAndDestinations();
    
    showJobTab('jobGeneral');
    
    const webhooksTabButton = document.getElementById('jobWebhooksTabButton');
    webhooksTabButton.disabled = true;
    webhooksTabButton.classList.add('opacity-50', 'cursor-not-allowed');
    
    document.getElementById('jobWebhooksList').innerHTML = '<p class="text-gray-500 text-center py-4">Save the job to enable webhooks.</p>';
    
    lucide.createIcons();
}

function closeAddJobModal() {
    document.getElementById('add-job-modal').classList.add('hidden');
}

async function loadProfilesAndDestinations() {
    try {
        const profilesResponse = await fetch('/api/profiles', {
            headers: getAuthHeaders()
        });
        if (profilesResponse.ok) {
            const profiles = await profilesResponse.json();
            const profileSelect = document.getElementById('job-profileId');
            profileSelect.innerHTML = '<option value="">Select profile...</option>' +
                profiles.map(p => `<option value="${p.id}">${escapeHtml(p.name)}</option>`).join('');
        }

        const destResponse = await fetch('/api/destinations', {
            headers: getAuthHeaders()
        });
        if (destResponse.ok) {
            const destinations = await destResponse.json();
            const destSelect = document.getElementById('job-destinationId');
            destSelect.innerHTML = '<option value="">Select destination...</option>' +
                destinations.map(d => `<option value="${d.id}">${escapeHtml(d.name)}</option>`).join('');
        }
    } catch (error) {
        console.error('Failed to load profiles/destinations:', error);
    }
}

function checkAuth() {
    return requireAuth();
}

async function loadJobs() {
    await checkAuth();
    setUserSidebarInfo();

    try {
        const [jobsResponse, profilesResponse, destinationsResponse] = await Promise.all([
            fetch(API_BASE, { headers: getAuthHeaders() }),
            fetch('/api/profiles', { headers: getAuthHeaders() }),
            fetch('/api/destinations', { headers: getAuthHeaders() })
        ]);

        if (jobsResponse.ok) {
            allJobs = await jobsResponse.json();
        }

        if (profilesResponse.ok) {
            profiles = await profilesResponse.json();
        }

        if (destinationsResponse.ok) {
            destinations = await destinationsResponse.json();
        }

        // Enrich jobs with profile names
        allJobs.forEach(job => {
            if (job.profileId) {
                const profile = profiles.find(p => p.id === job.profileId);
                if (profile) {
                    job.profileName = profile.name;
                }
            }
        });

        // Normalize status to numbers
        allJobs.forEach(j => {
            if (typeof j.status === 'string' && statusEnumMap.hasOwnProperty(j.status)) {
                j.status = statusEnumMap[j.status];
            }
        });

        // Apply filters
        filteredJobs = [...allJobs];
        applyFilters();

        // Update stats
        const stats = {
            total: allJobs.length,
            running: allJobs.filter(j => j.status === 2).length,
            queued: allJobs.filter(j => j.status === 1).length,
            idle: allJobs.filter(j => j.status === 0).length,
            failed: allJobs.filter(j => j.status === 4).length
        };

        document.getElementById('stat-total').textContent = stats.total;
        document.getElementById('stat-running').textContent = stats.running;
        document.getElementById('stat-queued').textContent = stats.queued;
        document.getElementById('stat-idle').textContent = stats.idle;
        document.getElementById('stat-failed').textContent = stats.failed;

        // Check for auto-paused jobs and show banner
        checkForPausedJobs();

    } catch (error) {
        console.error('Failed to load jobs:', error);
    }
}

// Get user-friendly health status for a job
function getJobHealthStatus(job) {
    if (job.tags && job.tags.includes('circuit-breaker')) {
        return {
            label: 'Auto-Paused',
            icon: 'pause-circle',
            color: 'text-red-700',
            bg: 'bg-red-100',
            description: 'Paused after 10 consecutive failures'
        };
    }

    const failures = job.consecutiveFailures || 0;

    if (failures >= 7) {
        return {
            label: 'Failing',
            icon: 'alert-triangle',
            color: 'text-orange-700',
            bg: 'bg-orange-100',
            description: `${failures} failures - may auto-pause soon`
        };
    }

    if (failures >= 3) {
        return {
            label: 'Warning',
            icon: 'alert-circle',
            color: 'text-yellow-700',
            bg: 'bg-yellow-100',
            description: `${failures} consecutive failures`
        };
    }

    if (!job.isEnabled) {
        return {
            label: 'Disabled',
            icon: 'x-circle',
            color: 'text-gray-700',
            bg: 'bg-gray-100',
            description: 'Manually disabled by user'
        };
    }

    return {
        label: 'Healthy',
        icon: 'check-circle',
        color: 'text-green-700',
        bg: 'bg-green-100',
        description: 'Running normally'
    };
}

// Check for auto-paused jobs and show banner
function checkForPausedJobs() {
    const pausedJobs = allJobs.filter(j => j.tags && j.tags.includes('circuit-breaker'));
    const banner = document.getElementById('auto-paused-banner');

    if (pausedJobs.length > 0) {
        document.getElementById('paused-jobs-count').textContent = pausedJobs.length;
        banner.classList.remove('hidden');
        banner.style.display = '';
    } else {
        banner.classList.add('hidden');
        banner.style.display = 'none';
    }

    lucide.createIcons();
}

// Filter to show only paused jobs
function filterPausedJobs() {
    const healthFilter = document.getElementById('filter-health');
    if (healthFilter) {
        healthFilter.value = 'auto-paused';
        applyFilters();
    }
}

// Dismiss the banner
function dismissBanner() {
    const banner = document.getElementById('auto-paused-banner');
    banner.classList.add('hidden');
    banner.style.display = 'none';
    localStorage.setItem('jobs_bannerDismissed', Date.now());
}


// Show banner again after 7 days
document.addEventListener('DOMContentLoaded', () => {
    const banner = document.getElementById('auto-paused-banner');
    const dismissedAt = localStorage.getItem('jobs_bannerDismissed');
    const sevenDays = 7 * 24 * 60 * 60 * 1000; // 7 days in ms

    // Show banner again if never dismissed or it's been 7+ days
    if (!dismissedAt || (Date.now() - dismissedAt) > sevenDays) {
        banner.classList.remove('hidden');
        localStorage.removeItem('jobs_bannerDismissed');
    } else {
        banner.classList.add('hidden');
    }
});

// Resume an auto-paused job
async function resumeJob(jobId) {
    if (!confirm('Resume this job? Make sure you\'ve fixed the underlying issue that caused failures.')) {
        return;
    }

    try {
        const response = await fetch(`/api/jobs/${jobId}/resume`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast('Job resumed successfully! It will run according to its schedule.', 'success');
            await loadJobs();
        } else {
            const error = await response.text();
            showToast('Failed to resume job: ' + error, 'error');
        }
    } catch (error) {
        showToast('error', 'Network error: ' + error.message);
    }
}

// Reset failure counter
async function acknowledgeFailures(jobId) {
    if (!confirm('Reset failure counter? This will give the job a fresh start.')) {
        return;
    }

    try {
        const response = await fetch(`/api/jobs/${jobId}/acknowledge-failures`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast('Failure counter reset', 'success');
            await loadJobs();
        } else {
            showToast('Failed to reset counter', 'error');
        }
    } catch (error) {
        showToast('Failed to reset counter', 'error');
    }
}

function renderJobs() {
    const jobsTbody = document.getElementById('jobs-tbody');

    if (!filteredJobs || filteredJobs.length === 0) {
        jobsTbody.innerHTML = `
            <tr>
                <td colspan="6" class="px-6 py-12 text-center">
                    <i data-lucide="briefcase" class="h-12 w-12 text-gray-300 mx-auto mb-4"></i>
                    <p class="text-lg font-semibold text-gray-700 mb-2">No jobs found</p>
                    <p class="text-gray-500">Create your first job to get started.</p>
                </td>
            </tr>
        `;
        lucide.createIcons();
        return;
    }

    jobsTbody.innerHTML = filteredJobs.map(job => {
        const statusInfo = getStatusInfo(job.status);
        const typeInfo = getJobTypeInfo(job.type);
        const scheduleInfo = getScheduleInfo(job.scheduleType);
        const healthInfo = getJobHealthStatus(job);

        const scheduleTypeValue = typeof job.scheduleType === 'string' ? parseInt(job.scheduleType) : job.scheduleType;
        
        let scheduleDetails = '';
        if (scheduleTypeValue === 1 && job.cronExpression) {
            scheduleDetails = `<code class="text-xs bg-gray-100 px-2 py-1 rounded">${escapeHtml(job.cronExpression)}</code>`;
        } else if (scheduleTypeValue === 2 && job.intervalMinutes) {
            scheduleDetails = `Every ${job.intervalMinutes} min`;
        } else if (scheduleTypeValue === 3 && job.startTime) {
            scheduleDetails = `Daily at ${job.startTime}`;
        } else if (scheduleTypeValue === 4 && job.startTime) {
            scheduleDetails = `Weekly at ${job.startTime}`;
        } else if (scheduleTypeValue === 5 && job.startTime) {
            scheduleDetails = `Monthly at ${job.startTime}`;
        }

        const lastRun = formatDateTime(job.lastRunTime);
        const lastRunRelative = formatRelativeTime(job.lastRunTime);
        const nextRun = formatDateTime(job.nextRunTime);

        return `
            <tr class="hover:bg-gray-50 transition-colors duration-150" id="job-row-${job.id}">
                <td class="px-6 py-4">
                    <button onclick="toggleJobDetails(${job.id})" class="text-gray-400 hover:text-gray-600 focus:outline-none transition-transform duration-200" id="expand-btn-${job.id}">
                        <i data-lucide="chevron-right" class="h-4 w-4"></i>
                    </button>
                </td>
                <td class="px-6 py-4">
                    <div class="flex items-start space-x-3">
                        <div class="flex-shrink-0 mt-1">
                            <div class="h-10 w-10 rounded-lg ${typeInfo.color.replace('text', 'bg').replace('600', '100')} flex items-center justify-center">
                                <i data-lucide="${typeInfo.icon}" class="h-5 w-5 ${typeInfo.color}"></i>
                            </div>
                        </div>
                        <div class="flex-1 min-w-0">
                            <div class="flex items-center space-x-2">
                                <p class="text-sm font-semibold text-gray-900 truncate">${escapeHtml(job.name)}</p>
                                <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${healthInfo.bg} ${healthInfo.color}">
                                    <i data-lucide="${healthInfo.icon}" class="h-3 w-3 mr-1"></i>
                                    ${healthInfo.label}
                                </span>
                            </div>
                            ${job.description ? `<p class="text-xs text-gray-500 mt-1">${escapeHtml(job.description)}</p>` : ''}
                            ${(job.tags && job.tags.includes('circuit-breaker')) ? `
                                <div class="mt-2 inline-flex items-center px-2 py-1 rounded bg-red-50 border border-red-200 text-red-800 text-xs">
                                    <i data-lucide="pause-circle" class="h-3 w-3 mr-1"></i>
                                    <span><strong>Auto-paused</strong> after 10 consecutive job failures. Requires manual resume to re-enable.</span>
                                </div>
                            ` : (job.consecutiveFailures >= 3) ? `
                                <div class="mt-2 inline-flex items-center px-2 py-1 rounded bg-yellow-50 border border-yellow-200 text-yellow-800 text-xs">
                                    <i data-lucide="alert-circle" class="h-3 w-3 mr-1"></i>
                                    <span><strong>${job.consecutiveFailures} failures in a row</strong></span>
                                </div>
                            ` : ''}
                            <div class="flex items-center mt-2 space-x-4 text-xs text-gray-500">
                                <span class="flex items-center">
                                    <i data-lucide="${typeInfo.icon}" class="h-3 w-3 mr-1"></i>
                                    ${typeInfo.label}
                                </span>
                                ${job.tags && !job.tags.includes('circuit-breaker') ? `
                                    <span class="flex items-center">
                                        <i data-lucide="tag" class="h-3 w-3 mr-1"></i>
                                        ${escapeHtml(job.tags).split(',').slice(0, 2).join(', ')}
                                    </span>
                                ` : ''}
                                ${job.profileId && job.profileName ? `<span class="flex items-center"><i data-lucide="file-code" class="h-3 w-3 mr-1"></i>${escapeHtml(job.profileName)}</span>` : job.profileId ? `<span class="flex items-center"><i data-lucide="file-code" class="h-3 w-3 mr-1"></i>Profile #${job.profileId}</span>` : ''}
                            </div>
                        </div>
                    </div>
                </td>
                <td class="px-6 py-4">
                    <div class="flex items-center space-x-2">
                        <i data-lucide="${scheduleInfo.icon}" class="h-4 w-4 ${scheduleInfo.color}"></i>
                        <div>
                            <p class="text-sm font-medium text-gray-900">${scheduleInfo.label}</p>
                            ${scheduleDetails ? `<p class="text-xs text-gray-500 mt-1">${scheduleDetails}</p>` : ''}
                        </div>
                    </div>
                </td>
                <td class="px-6 py-4">
                    <div class="grid grid-cols-2 gap-4">
                        <div>
                            <p class="text-xs text-gray-500 mb-1">Last Run</p>
                            <p class="text-sm font-medium text-gray-900">${lastRunRelative || lastRun}</p>
                        </div>
                        <div>
                            <p class="text-xs text-gray-500 mb-1">Next Run</p>
                            <p class="text-sm font-medium ${scheduleTypeValue === 0 ? 'text-gray-400' : 'text-gray-900'}">${scheduleTypeValue === 0 ? 'Manual' : nextRun}</p>
                        </div>
                    </div>
                </td>
                <td class="px-6 py-4">
                    <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${statusInfo.bg} ${statusInfo.color}">
                        <i data-lucide="${statusInfo.icon}" class="h-3 w-3 mr-1"></i>
                        ${statusInfo.label}
                    </span>
                </td>
                <td class="px-6 py-4 text-right text-sm font-medium">
                    <div class="relative inline-block text-left">
                        <button onclick="toggleJobMenu(${job.id}, event)" id="job-menu-btn-${job.id}" class="text-gray-400 hover:text-gray-600 focus:outline-none">
                            <i data-lucide="more-vertical" class="h-5 w-5"></i>
                        </button>
                        <div id="job-menu-${job.id}" class="hidden fixed mt-2 w-48 rounded-md shadow-lg bg-white ring-1 ring-black ring-opacity-5 z-50">
                            <div class="py-1">
                                <button onclick="event.stopPropagation(); triggerJob(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-100">
                                    <i data-lucide="play" class="h-4 w-4 inline mr-2"></i>
                                    Run Now
                                </button>
                                <button onclick="event.stopPropagation(); editJob(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-100">
                                    <i data-lucide="edit" class="h-4 w-4 inline mr-2"></i>
                                    Edit
                                </button>
                                <button onclick="event.stopPropagation(); closeJobMenu(${job.id}); viewJobHistory(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-100">
                                    <i data-lucide="history" class="h-4 w-4 inline mr-2"></i>
                                    History
                                </button>
                                ${(job.tags && job.tags.includes('circuit-breaker')) ? `
                                    <button onclick="event.stopPropagation(); closeJobMenu(${job.id}); resumeJob(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-green-700 hover:bg-green-50 border-t border-gray-200">
                                        <i data-lucide="play" class="h-4 w-4 inline mr-2"></i>
                                        Resume Job Now
                                    </button>
                                ` : ''}
                                ${(job.consecutiveFailures >= 3 && job.consecutiveFailures < 10) ? `
                                    <button onclick="event.stopPropagation(); closeJobMenu(${job.id}); acknowledgeFailures(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-yellow-700 hover:bg-yellow-50 border-t border-gray-200">
                                        <i data-lucide="check-circle" class="h-4 w-4 inline mr-2"></i>
                                        Reset Failures
                                    </button>
                                ` : ''}
                                <button onclick="event.stopPropagation(); closeJobMenu(${job.id}); deleteJob(${job.id})" class="block w-full text-left px-4 py-2 text-sm text-red-600 hover:bg-red-50 border-t border-gray-200">
                                    <i data-lucide="trash-2" class="h-4 w-4 inline mr-2"></i>
                                    Delete
                                </button>
                            </div>
                        </div>
                    </div>
                </td>
            </tr>
            <tr id="job-details-${job.id}" class="hidden bg-gray-50">
                <td colspan="6" class="px-6 py-4">
                    <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
                        <div>
                            <h4 class="text-sm font-semibold text-gray-900 mb-3">Configuration</h4>
                            <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                                <div>
                                    <dt class="text-gray-500">Priority:</dt>
                                    <dd class="font-medium">${['Low', 'Normal', 'High', 'Critical'][job.priority] || 'Normal'}</dd>
                                </div>
                                <div>
                                    <dt class="text-gray-500">Max Retries:</dt>
                                    <dd class="font-medium">${job.maxRetries || 0}</dd>
                                </div>
                                <div>
                                    <dt class="text-gray-500">Timeout:</dt>
                                    <dd class="font-medium">${job.timeoutMinutes || 0} min</dd>
                                </div>
                                <div>
                                    <dt class="text-gray-500">Concurrent:</dt>
                                    <dd class="font-medium">${job.allowConcurrent ? 'Yes' : 'No'}</dd>
                                </div>
                            </dl>
                        </div>
                        <div>
                            <h4 class="text-sm font-semibold text-gray-900 mb-3">Details</h4>
                            <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
                                ${job.profileId ? `
                                <div>
                                    <dt class="text-gray-500">Profile:</dt>
                                    <dd class="font-medium text-blue-600">${escapeHtml(job.profileName || 'Unknown')}</dd>
                                </div>
                                ` : ''}
                                ${job.destinationId ? `
                                <div>
                                    <dt class="text-gray-500">Destination:</dt>
                                    <dd class="font-medium text-blue-600">${escapeHtml(destinations.find(d => d.id === job.destinationId)?.name || 'Unknown')}</dd>
                                </div>
                                ` : ''}
                                <div>
                                    <dt class="text-gray-500">Created:</dt>
                                    <dd class="font-medium text-xs">${formatDateTime(job.createdAt)}</dd>
                                </div>
                            </dl>
                        </div>
                    </div>
                    <div class="mt-4 flex justify-end">
                        <button onclick="viewJobHistory(${job.id})" class="text-sm text-blue-600 hover:text-blue-800 flex items-center">
                            <i data-lucide="external-link" class="h-4 w-4 mr-1"></i>
                            View Full Execution History
                        </button>
                    </div>
                </td>
            </tr>
        `;
    }).join('');
    
    lucide.createIcons();
}

function toggleJobMenu(jobId, event) {
    if (event) {
        event.stopPropagation();
    }
    
    const menu = document.getElementById(`job-menu-${jobId}`);
    const button = document.getElementById(`job-menu-btn-${jobId}`);
    const isCurrentlyHidden = menu.classList.contains('hidden');
    
    // Close all other menus
    document.querySelectorAll('[id^="job-menu-"]:not([id*="-btn-"])').forEach(m => {
        m.classList.add('hidden');
    });
    
    // Toggle current menu
    if (isCurrentlyHidden) {
        // Position the menu relative to the button (using viewport coordinates)
        const buttonRect = button.getBoundingClientRect();
        
        // Position below the button, aligned to the right
        menu.style.top = `${buttonRect.bottom + 5}px`;
        menu.style.right = `${window.innerWidth - buttonRect.right}px`;
        menu.style.left = 'auto';
        
        menu.classList.remove('hidden');
    }
}

function closeJobMenu(jobId) {
    const menu = document.getElementById(`job-menu-${jobId}`);
    if (menu) {
        menu.classList.add('hidden');
    }
}

function toggleJobDetails(jobId) {
    const detailsRow = document.getElementById(`job-details-${jobId}`);
    const expandBtn = document.getElementById(`expand-btn-${jobId}`);
    
    detailsRow.classList.toggle('hidden');
    
    const icon = expandBtn.querySelector('i');
    if (detailsRow.classList.contains('hidden')) {
        expandBtn.style.transform = 'rotate(0deg)';
    } else {
        expandBtn.style.transform = 'rotate(90deg)';
    }
    
    lucide.createIcons();
}

async function triggerJob(id) {
    try {
        const response = await fetch(`${API_BASE}/${id}/trigger`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast('Job triggered successfully', 'success');
            await pollJobStatus(id);
        } else {
            const error = await response.json();
            showToast(`Error: ${error.message || 'Failed to trigger job'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
}

async function pollJobStatus(jobId, maxAttempts = 30, intervalMs = 1000) {
    let attempts = 0;
    while (attempts < maxAttempts) {
        try {
            const response = await fetch(`${API_BASE}/${jobId}`, {
                headers: getAuthHeaders()
            });
            if (response.ok) {
                const job = await response.json();
                if (job.status !== 2 && job.status !== 1) {
                    await loadJobs();
                    return;
                }
            }
        } catch (e) {
            // Ignore errors, try again
        }
        await new Promise(res => setTimeout(res, intervalMs));
        attempts++;
    }
    await loadJobs();
}

async function editJob(id) {
    document.querySelectorAll('[id^="job-menu-"]').forEach(menu => menu.classList.add('hidden'));
    
    try {
        const response = await fetch(`${API_BASE}/${id}`, {
            headers: getAuthHeaders()
        });

        if (!response.ok) {
            showToast('Failed to load job', 'error');
            return;
        }

        const job = await response.json();
        
        // Load profiles and destinations first, so dropdown options are available
        await loadProfilesAndDestinations();
        
        document.getElementById('job-name').value = job.name || '';
        document.getElementById('job-description').value = job.description || '';
        document.getElementById('job-type').value = job.type || '';
        document.getElementById('job-profileId').value = job.profileId || '';
        document.getElementById('job-destinationId').value = job.destinationId || '';
        
        // Convert schedule type string to number if needed
        let scheduleTypeValue = job.scheduleType;
        if (typeof scheduleTypeValue === 'string' && scheduleTypeEnumMap.hasOwnProperty(scheduleTypeValue)) {
            scheduleTypeValue = scheduleTypeEnumMap[scheduleTypeValue];
        } else if (typeof scheduleTypeValue === 'string') {
            scheduleTypeValue = parseInt(scheduleTypeValue) || 0;
        } else {
            scheduleTypeValue = scheduleTypeValue || 0;
        }
        document.getElementById('job-scheduleType').value = scheduleTypeValue;
        
        updateScheduleFields();

        document.getElementById('job-priority').value = job.priority || 1;
        document.getElementById('job-maxRetries').value = job.maxRetries || 3;
        document.getElementById('job-timeoutMinutes').value = job.timeoutMinutes || 60;
        document.getElementById('job-allowConcurrent').checked = job.allowConcurrent || false;
        document.getElementById('job-tags').value = job.tags || '';
        document.getElementById('job-enabled').checked = job.isEnabled;
        document.getElementById('job-autoPauseEnabled').checked = job.autoPauseEnabled !== undefined ? job.autoPauseEnabled : true;

        if (scheduleTypeValue === 1 && job.cronExpression) {
            const cronField = document.getElementById('job-cronExpression');
            if (cronField) cronField.value = job.cronExpression;
        } else if (scheduleTypeValue === 2 && job.intervalMinutes !== undefined) {
            const intervalField = document.getElementById('job-intervalMinutes');
            if (intervalField) intervalField.value = job.intervalMinutes;
        } else if ((scheduleTypeValue === 3 || scheduleTypeValue === 4 || scheduleTypeValue === 5) && job.startTime) {
            const startTimeField = document.getElementById('job-startTime');
            if (startTimeField) startTimeField.value = job.startTime;
        }

        const form = document.getElementById('add-job-form');
        form.setAttribute('data-edit-id', id);

        document.getElementById('job-modal-title').textContent = 'Edit Job';
        
        showJobTab('jobGeneral');
        
        const webhooksTabButton = document.getElementById('jobWebhooksTabButton');
        webhooksTabButton.disabled = false;
        webhooksTabButton.classList.remove('opacity-50', 'cursor-not-allowed');
        
        await loadJobWebhooks(id);

        document.getElementById('add-job-modal').classList.remove('hidden');
        
        lucide.createIcons();
    } catch (error) {
        showToast(`Error loading job: ${error.message}`, 'error');
    }
}

async function deleteJob(id) {
    if (!confirm('Are you sure you want to delete this job?')) {
        return;
    }

    try {
        const response = await fetch(`${API_BASE}/${id}`, {
            method: 'DELETE',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast('Job deleted successfully', 'success');
            await loadJobs();
        } else {
            const error = await response.json();
            showToast(`Error: ${error.message || 'Failed to delete job'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
}

function viewJobHistory(jobId) {
    window.location.href = `/executions.html?jobId=${jobId}`;
}

document.getElementById('add-job-form').addEventListener('submit', async (e) => {
    e.preventDefault();

    const form = e.target;
    const editId = form.getAttribute('data-edit-id');
    const isEdit = !!editId;

    const scheduleType = parseInt(document.getElementById('job-scheduleType').value);
    
    let cronExpression = null;
    let intervalMinutes = null;
    let startTime = null;

    if (scheduleType === 1) {
        cronExpression = document.getElementById('job-cronExpression')?.value || null;
    } else if (scheduleType === 2) {
        intervalMinutes = parseInt(document.getElementById('job-intervalMinutes')?.value) || null;
    } else if (scheduleType === 3 || scheduleType === 4 || scheduleType === 5) {
        startTime = document.getElementById('job-startTime')?.value || null;
    }

    const job = {
        name: document.getElementById('job-name').value,
        description: document.getElementById('job-description').value,
        type: document.getElementById('job-type').value,
        profileId: parseInt(document.getElementById('job-profileId').value) || null,
        destinationId: parseInt(document.getElementById('job-destinationId').value) || null,
        scheduleType: scheduleType,
        cronExpression: cronExpression,
        intervalMinutes: intervalMinutes,
        startTime: startTime,
        priority: parseInt(document.getElementById('job-priority').value),
        maxRetries: parseInt(document.getElementById('job-maxRetries').value),
        timeoutMinutes: parseInt(document.getElementById('job-timeoutMinutes').value),
        allowConcurrent: document.getElementById('job-allowConcurrent').checked,
        tags: document.getElementById('job-tags').value,
        isEnabled: document.getElementById('job-enabled').checked,
        autoPauseEnabled: document.getElementById('job-autoPauseEnabled').checked
    };

    if (isEdit) {
        job.id = parseInt(editId);
    }

    try {
        const url = isEdit ? `${API_BASE}/${editId}` : API_BASE;
        const method = isEdit ? 'PUT' : 'POST';

        const response = await fetch(url, {
            method: method,
            headers: {
                ...getAuthHeaders(),
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(job)
        });

        if (response.ok) {
            showToast(`Job ${isEdit ? 'updated' : 'created'} successfully`, 'success');
            closeAddJobModal();
            await loadJobs();
        } else {
            const error = await response.json();
            showToast(`Error: ${error.message || 'Failed to save job'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
});

async function loadJobWebhooks(jobId) {
    try {
        const response = await fetch(`/api/webhooks?jobId=${jobId}`, {
            headers: getAuthHeaders()
        });

        if (!response.ok) {
            console.error('Failed to load webhooks');
            return;
        }

        const webhooks = await response.json();
        const webhooksList = document.getElementById('jobWebhooksList');

        if (!webhooks || webhooks.length === 0) {
            webhooksList.innerHTML = `
                <div class="text-center py-6 text-gray-500">
                    <i data-lucide="link-2-off" class="w-12 h-12 mx-auto mb-2 text-gray-400"></i>
                    <p>No webhooks configured for this job</p>
                    <p class="text-sm mt-1">Click "Create Webhook" to add one</p>
                </div>
            `;
            lucide.createIcons();
            return;
        }

        webhooksList.innerHTML = webhooks.map(webhook => {
            const statusColor = webhook.isActive ? 'text-green-600' : 'text-gray-400';
            const statusBadge = webhook.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-800';
            const statusText = webhook.isActive ? 'Active' : 'Inactive';
            const lastTriggered = webhook.lastTriggeredAt
                ? `Last triggered: ${formatDateTime(webhook.lastTriggeredAt)}`
                : 'Never triggered';

            return `
                <div class="border rounded-lg p-4 ${webhook.isActive ? 'bg-white' : 'bg-gray-50'}">
                    <div class="flex items-start justify-between">
                        <div class="flex-1">
                            <div class="flex items-center space-x-2 mb-2">
                                <span class="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${statusBadge}">
                                    ${statusText}
                                </span>
                                <span class="text-sm text-gray-500">Triggered ${webhook.triggerCount || 0} times</span>
                            </div>
                            <div class="flex items-center space-x-2 mb-2">
                                <code class="flex-1 px-3 py-2 bg-gray-100 rounded text-xs font-mono text-gray-700 overflow-x-auto">
                                    ${window.location.origin}/webhooks/${webhook.token}
                                </code>
                                <button onclick="copyJobWebhookUrl('${webhook.token}')" class="p-2 text-gray-400 hover:text-gray-600" title="Copy URL">
                                    <i data-lucide="copy" class="w-4 h-4"></i>
                                </button>
                            </div>
                            <p class="text-xs text-gray-500">${lastTriggered}</p>
                        </div>
                        <div class="ml-4 flex flex-col space-y-1">
                            <button onclick="toggleJobWebhook(${webhook.id}, ${webhook.isActive}, ${jobId})"
                                    class="p-2 text-gray-400 hover:text-gray-600"
                                    title="${webhook.isActive ? 'Disable' : 'Enable'}">
                                <i data-lucide="${webhook.isActive ? 'pause-circle' : 'play-circle'}" class="w-4 h-4"></i>
                            </button>
                            <button onclick="regenerateJobWebhookToken(${webhook.id}, ${jobId})"
                                    class="p-2 text-yellow-400 hover:text-yellow-600"
                                    title="Regenerate Token">
                                <i data-lucide="refresh-cw" class="w-4 h-4"></i>
                            </button>
                            <button onclick="testJobWebhook('${webhook.token}')"
                                    class="p-2 text-blue-400 hover:text-blue-600"
                                    title="Test Webhook">
                                <i data-lucide="send" class="w-4 h-4"></i>
                            </button>
                            <button onclick="deleteJobWebhook(${webhook.id}, ${jobId})"
                                    class="p-2 text-red-400 hover:text-red-600"
                                    title="Delete">
                                <i data-lucide="trash-2" class="w-4 h-4"></i>
                            </button>
                        </div>
                    </div>
                </div>
            `;
        }).join('');

        lucide.createIcons();
    } catch (error) {
        console.error('Error loading webhooks:', error);
    }
}

async function createJobWebhook() {
    const form = document.getElementById('add-job-form');
    const jobId = form.getAttribute('data-edit-id');

    if (!jobId) {
        showToast('Please save the job first', 'error');
        return;
    }

    try {
        const response = await fetch('/api/webhooks', {
            method: 'POST',
            headers: {
                ...getAuthHeaders(),
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ jobId: parseInt(jobId) })
        });

        if (response.ok) {
            const webhook = await response.json();
            showToast('Webhook created successfully', 'success');
            await loadJobWebhooks(jobId);
            showJobWebhookToken(webhook.token, jobId);
        } else {
            const error = await response.json();
            showToast(`Error: ${error.message || 'Failed to create webhook'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
}

async function deleteJobWebhook(webhookId, jobId) {
    if (!confirm('Are you sure you want to delete this webhook?')) {
        return;
    }

    try {
        const response = await fetch(`/api/webhooks/${webhookId}`, {
            method: 'DELETE',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast('Webhook deleted successfully', 'success');
            await loadJobWebhooks(jobId);
        } else {
            const error = await response.json();
            showToast(`Error: ${error.message || 'Failed to delete webhook'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
}

async function copyJobWebhookUrl(token) {
    const url = `${window.location.origin}/webhooks/${token}`;
    const success = await copyToClipboard(url);
    if (success) {
        showToast('Webhook URL copied to clipboard', 'success');
    } else {
        showToast('Failed to copy URL', 'error');
    }
}

async function toggleJobWebhook(webhookId, isActive, jobId) {
    const action = isActive ? 'disable' : 'enable';

    try {
        const response = await fetch(`/api/webhooks/${webhookId}/${action}`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            showToast(`Webhook ${action}d successfully`, 'success');
            await loadJobWebhooks(jobId);
        } else {
            showToast(`Error ${action}ing webhook`, 'error');
        }
    } catch (error) {
        showToast(`Error ${action}ing webhook`, 'error');
    }
}

async function regenerateJobWebhookToken(webhookId, jobId) {
    if (!confirm('Are you sure you want to regenerate the token? The old token will stop working immediately.')) {
        return;
    }

    try {
        const response = await fetch(`/api/webhooks/${webhookId}/regenerate`, {
            method: 'POST',
            headers: getAuthHeaders()
        });

        if (response.ok) {
            const data = await response.json();
            showToast('Token regenerated successfully', 'success');
            await loadJobWebhooks(jobId);
            showJobWebhookToken(data.token, jobId);
        } else {
            showToast('Error regenerating webhook token', 'error');
        }
    } catch (error) {
        showToast('Error regenerating webhook token', 'error');
    }
}

function testJobWebhook(token) {
    const url = `${window.location.origin}/webhooks/${token}`;
    const curlCommand = `curl -X POST "${url}" \\
-H "Content-Type: application/json" \\
-d '{"parameters": {"key": "value"}}'`;

    const modal = `
        <div id="testJobWebhookModal" class="fixed z-[60] inset-0 overflow-y-auto">
            <div class="flex items-center justify-center min-h-screen px-4">
                <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity" onclick="closeTestJobWebhookModal()"></div>
                <div class="relative bg-white rounded-lg shadow-xl max-w-2xl w-full p-6">
                    <div class="flex items-start justify-between mb-4">
                        <h3 class="text-lg font-medium text-gray-900">Test Webhook</h3>
                        <button onclick="closeTestJobWebhookModal()" class="text-gray-400 hover:text-gray-600">
                            <i data-lucide="x" class="w-5 h-5"></i>
                        </button>
                    </div>
                    <div class="space-y-4">
                        <div>
                            <p class="text-sm text-gray-600 mb-2">Use this curl command to test the webhook:</p>
                            <div class="bg-gray-900 text-gray-100 rounded p-4 font-mono text-xs overflow-x-auto">
                                ${curlCommand}
                            </div>
                            <button onclick="copyToClipboard(\`${curlCommand.replace(/`/g, '\\`')}\`).then(ok => showToast(ok ? 'Command copied!' : 'Failed to copy', ok ? 'success' : 'error'));"
                                    class="mt-2 text-sm text-blue-600 hover:text-blue-700">
                                <i data-lucide="copy" class="w-4 h-4 inline mr-1"></i>
                                Copy command
                            </button>
                        </div>
                        <div class="bg-blue-50 border-l-4 border-blue-400 p-3">
                            <p class="text-sm text-blue-700">
                                <strong>Tip:</strong> You can add custom parameters in the JSON body to pass data to your job.
                            </p>
                        </div>
                    </div>
                    <div class="mt-6 flex justify-end">
                        <button onclick="closeTestJobWebhookModal()"
                                class="px-4 py-2 bg-blue-600 text-white rounded hover:bg-blue-700">
                            Close
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;

    document.body.insertAdjacentHTML('beforeend', modal);
    lucide.createIcons();
}

function closeTestJobWebhookModal() {
    const modal = document.getElementById('testJobWebhookModal');
    if (modal) modal.remove();
}

function showJobWebhookToken(token, jobId) {
    const webhookUrl = `${window.location.origin}/webhooks/${token}`;
    
    const modal = `
        <div id="jobWebhookTokenModal" class="fixed z-50 inset-0 overflow-y-auto">
            <div class="flex items-end justify-center min-h-screen pt-4 px-4 pb-20 text-center sm:block sm:p-0">
                <div class="fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"></div>
                <div class="inline-block align-bottom bg-white rounded-lg text-left overflow-hidden shadow-xl transform transition-all sm:my-8 sm:align-middle sm:max-w-lg sm:w-full">
                    <div class="bg-white px-4 pt-5 pb-4 sm:p-6 sm:pb-4">
                        <div class="sm:flex sm:items-start">
                            <div class="mx-auto flex-shrink-0 flex items-center justify-center h-12 w-12 rounded-full bg-blue-100 sm:mx-0 sm:h-10 sm:w-10">
                                <i data-lucide="link" class="h-6 w-6 text-blue-600"></i>
                            </div>
                            <div class="mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left flex-1">
                                <h3 class="text-lg leading-6 font-medium text-gray-900 mb-4">Webhook URL</h3>
                                <p class="text-sm text-gray-500 mb-3">Use this URL to trigger the job from external systems:</p>
                                <div class="bg-gray-50 rounded p-3 mb-3">
                                    <code class="text-xs break-all">${webhookUrl}</code>
                                </div>
                                <button onclick="copyToClipboard('${webhookUrl}').then(ok => showToast(ok ? 'URL copied to clipboard' : 'Failed to copy', ok ? 'success' : 'error'));"
                                        class="w-full inline-flex justify-center rounded-md border border-gray-300 shadow-sm px-4 py-2 bg-white text-base font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 sm:text-sm mb-3">
                                    <i data-lucide="copy" class="w-4 h-4 mr-2"></i>
                                    Copy URL
                                </button>
                                <div class="bg-blue-50 border-l-4 border-blue-400 p-3">
                                    <p class="text-sm text-blue-700">
                                        <strong>Example:</strong><br>
                                        <code class="text-xs">curl -X POST ${webhookUrl}</code>
                                    </p>
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="bg-gray-50 px-4 py-3 sm:px-6 sm:flex sm:flex-row-reverse">
                        <button onclick="closeJobWebhookTokenModal()" 
                                class="w-full inline-flex justify-center rounded-md border border-transparent shadow-sm px-4 py-2 bg-blue-600 text-base font-medium text-white hover:bg-blue-700 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 sm:ml-3 sm:w-auto sm:text-sm">
                            <i data-lucide="check" class="w-4 h-4 mr-2"></i>
                            Done
                        </button>
                    </div>
                </div>
            </div>
        </div>
    `;
    
    document.body.insertAdjacentHTML('beforeend', modal);
    lucide.createIcons();
}

function closeJobWebhookTokenModal() {
    const modal = document.getElementById('jobWebhookTokenModal');
    if (modal) modal.remove();
}

let userMenuExpanded = false;

function toggleUserMenu() {
    const menu = document.getElementById('user-menu');
    const chevron = document.getElementById('chevron-icon');
    userMenuExpanded = !userMenuExpanded;
    
    if (userMenuExpanded) {
        menu.classList.remove('user-menu-collapsed');
        menu.classList.add('user-menu-expanded');
        chevron.style.transform = 'rotate(180deg)';
    } else {
        menu.classList.remove('user-menu-expanded');
        menu.classList.add('user-menu-collapsed');
        chevron.style.transform = 'rotate(0deg)';
    }
    
    setTimeout(() => lucide.createIcons(), 50);
}

function logout() {
    clearAuth();
    redirectToLogin();
}

// Close menus when clicking outside
document.addEventListener('click', (e) => {
    if (!e.target.closest('[id^="job-menu-btn-"]') && !e.target.closest('[id^="job-menu-"]:not([id*="-btn-"])')) {
        document.querySelectorAll('[id^="job-menu-"]:not([id*="-btn-"])').forEach(menu => {
            menu.classList.add('hidden');
        });
    }
});

window.addEventListener('DOMContentLoaded', async () => {
    await requireAuth();
    lucide.createIcons();
    await loadFilterData();
    await loadJobs();
    setInterval(loadJobs, 10000);
    
    const createJobWebhookBtn = document.getElementById('createJobWebhookBtn');
    if (createJobWebhookBtn) {
        createJobWebhookBtn.addEventListener('click', createJobWebhook);
    }
});