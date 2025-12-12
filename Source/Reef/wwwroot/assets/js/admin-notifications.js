// Admin Notifications Management
// Handles loading, updating, and saving system notification settings

let currentNotificationSettings = null;
let emailDestinations = [];

/**
 * Check if user has admin permissions
 * Returns true if user is an administrator
 */
function checkNotificationPermissions() {
    const userRole = localStorage.getItem('reef_role');
    const isAdmin = userRole === 'Admin' || userRole === 'Administrator';

    const blurOverlay = document.getElementById('notifications-permission-blur');

    if (!isAdmin) {
        // Show blur overlay if user is not admin
        if (blurOverlay) blurOverlay.classList.remove('hidden');
        return false;
    } else {
        // Hide blur overlay if user is admin
        if (blurOverlay) blurOverlay.classList.add('hidden');
        return true;
    }
}

/**
 * Load notification settings on tab show
 */
function loadNotificationSettings() {
    // Check permissions first
    if (!checkNotificationPermissions()) {
        return;
    }

    // Load both settings and destinations in parallel, then populate UI
    Promise.all([
        fetch(`${API_BASE}/api/admin/notifications`, {
            headers: getAuthHeaders()
        }).then(response => {
            if (!response.ok) throw new Error('Failed to load notification settings');
            return response.json();
        }),
        fetch(`${API_BASE}/api/destinations?type=Email`, {
            headers: getAuthHeaders()
        }).then(response => {
            if (!response.ok) throw new Error('Failed to load destinations');
            return response.json();
        })
    ])
    .then(([settings, destinationData]) => {
        currentNotificationSettings = settings;
        emailDestinations = destinationData.data || destinationData || [];

        // Populate dropdown options
        populateDestinationSelectOptions();

        // Then populate UI with values (will now have all options available)
        populateUI(settings);
    })
    .catch(error => {
        console.error('Error loading notification settings:', error);
        showToast('Error loading notification settings', 'danger');
    });
}

/**
 * Populate the destination select dropdown with options
 */
function populateDestinationSelectOptions() {
    const select = document.getElementById('notification-destination');

    // Clear existing options (keep the first "Select" option)
    while (select.options.length > 1) {
        select.remove(1);
    }

    // Add destinations
    emailDestinations.forEach(dest => {
        const option = document.createElement('option');
        option.value = dest.id;
        option.textContent = dest.name;
        select.appendChild(option);
    });
}

/**
 * Populate the UI with notification settings
 */
function populateUI(settings) {
    // Check if settings is null, undefined, or empty object
    if (!settings || Object.keys(settings).length === 0) {
        // Default settings
        settings = {
            isEnabled: false,
            destinationId: 0,
            recipientEmails: '',
            notifyOnProfileFailure: true,
            notifyOnProfileSuccess: false,
            notifyOnJobFailure: true,
            notifyOnJobSuccess: false,
            notifyOnDatabaseSizeThreshold: true,
            databaseSizeThresholdBytes: 1073741824, // 1 GB default
            notifyOnNewApiKey: true,
            notifyOnNewUser: false,
            notifyOnNewWebhook: false,
            notifyOnNewEmailApproval: false,
            newEmailApprovalCooldownHours: 24
        };
    }

    document.getElementById('notify-enabled').checked = settings.isEnabled;
    document.getElementById('notification-destination').value = settings.destinationId || '';
    document.getElementById('notification-recipients').value = settings.recipientEmails || '';
    document.getElementById('notify-profile-failure').checked = settings.notifyOnProfileFailure;
    document.getElementById('notify-profile-success').checked = settings.notifyOnProfileSuccess;
    document.getElementById('notify-job-failure').checked = settings.notifyOnJobFailure;
    document.getElementById('notify-job-success').checked = settings.notifyOnJobSuccess;
    document.getElementById('notify-db-size').checked = settings.notifyOnDatabaseSizeThreshold;
    document.getElementById('notify-api-key').checked = settings.notifyOnNewApiKey;
    document.getElementById('notify-user').checked = settings.notifyOnNewUser;
    document.getElementById('notify-webhook').checked = settings.notifyOnNewWebhook;
    document.getElementById('notify-email-approval').checked = settings.notifyOnNewEmailApproval;
    document.getElementById('email-approval-cooldown-hours').value = settings.newEmailApprovalCooldownHours || 24;

    // Convert bytes to MB for display
    const thresholdMb = settings.databaseSizeThresholdBytes ? Math.round(settings.databaseSizeThresholdBytes / (1024 * 1024)) : 1024;
    document.getElementById('db-threshold-mb').value = thresholdMb;
}

/**
 * Handle destination selection change
 * Note: Recipients are NOT auto-populated as notifications are opt-in
 * and recipients are often different from the sender email address
 */
function handleDestinationChange() {
    const destinationId = parseInt(document.getElementById('notification-destination').value);
    const recipientField = document.getElementById('notification-recipients');

    if (destinationId === 0 || isNaN(destinationId)) {
        recipientField.placeholder = 'Please select a destination first';
    } else {
        recipientField.placeholder = 'admin@example.com, ops@example.com';
    }

    handleNotificationChange();
}

/**
 * Handle notification settings change (debounced for real-time feedback)
 */
function handleNotificationChange() {
    // Could be used for real-time validation or live preview
    // For now, just updates on save
}

/**
 * Save notification settings
 */
async function saveNotificationSettings() {
    // Check permissions first
    if (!checkNotificationPermissions()) {
        showToast('You do not have permission to modify notification settings', 'danger');
        return;
    }

    const destinationId = parseInt(document.getElementById('notification-destination').value);

    if (destinationId === 0 || isNaN(destinationId)) {
        showToast('Please select an email destination', 'warning');
        return;
    }

    // Convert MB to bytes for storage
    const thresholdMb = parseInt(document.getElementById('db-threshold-mb').value);
    if (isNaN(thresholdMb) || thresholdMb < 100) {
        showToast('Threshold must be at least 100 MB', 'warning');
        return;
    }

    const cooldownHours = parseInt(document.getElementById('email-approval-cooldown-hours').value);
    if (isNaN(cooldownHours) || cooldownHours < 1) {
        showToast('Cooldown period must be at least 1 hour', 'warning');
        return;
    }

    const settings = {
        id: currentNotificationSettings?.id || 0,
        isEnabled: document.getElementById('notify-enabled').checked,
        destinationId: destinationId,
        destinationName: document.getElementById('notification-destination').options[document.getElementById('notification-destination').selectedIndex].text,
        recipientEmails: document.getElementById('notification-recipients').value.trim() || null,
        notifyOnProfileFailure: document.getElementById('notify-profile-failure').checked,
        notifyOnProfileSuccess: document.getElementById('notify-profile-success').checked,
        notifyOnJobFailure: document.getElementById('notify-job-failure').checked,
        notifyOnJobSuccess: document.getElementById('notify-job-success').checked,
        notifyOnDatabaseSizeThreshold: document.getElementById('notify-db-size').checked,
        databaseSizeThresholdBytes: thresholdMb * 1024 * 1024, // Convert MB to bytes
        notifyOnNewApiKey: document.getElementById('notify-api-key').checked,
        notifyOnNewUser: document.getElementById('notify-user').checked,
        notifyOnNewWebhook: document.getElementById('notify-webhook').checked,
        notifyOnNewEmailApproval: document.getElementById('notify-email-approval').checked,
        newEmailApprovalCooldownHours: cooldownHours,
        createdAt: currentNotificationSettings?.createdAt || new Date().toISOString(),
        updatedAt: new Date().toISOString(),
        hash: ''
    };

    try {
        const response = await fetch(`${API_BASE}/api/admin/notifications`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(settings)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save notification settings');
        }

        showToast('Notification settings saved successfully', 'success');
        currentNotificationSettings = settings;
    } catch (error) {
        console.error('Error saving notification settings:', error);
        showToast('Error: ' + error.message, 'danger');
    }
}

/**
 * Send a test notification to verify connection
 */
async function sendTestNotification() {
    // Check permissions first
    if (!checkNotificationPermissions()) {
        showToast('You do not have permission to test notifications', 'danger');
        return;
    }

    const destinationId = parseInt(document.getElementById('notification-destination').value);

    if (destinationId === 0 || isNaN(destinationId)) {
        showToast('Please select an email destination first', 'warning');
        return;
    }

    const recipientEmails = document.getElementById('notification-recipients').value.trim();

    const button = document.getElementById('test-notification-btn');
    const originalText = button.innerHTML;
    button.disabled = true;
    button.innerHTML = '<i data-lucide="loader" class="h-4 w-4 mr-2 animate-spin"></i>Testing...';

    try {
        const response = await fetch(`${API_BASE}/api/admin/notifications/test`, {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify({
                destinationId: destinationId,
                recipientEmails: recipientEmails || null
            })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to send test notification');
        }

        showToast('Test notification sent! Check your email.', 'success');
    } catch (error) {
        console.error('Error sending test notification:', error);
        showToast('Error: ' + error.message, 'danger');
    } finally {
        button.disabled = false;
        button.innerHTML = originalText;
        lucide.createIcons();
    }
}

/**
 * Toggle email approval settings visibility
 */
function toggleEmailApprovalSettings() {
    const settingsDiv = document.getElementById('email-approval-settings');
    if (settingsDiv) {
        settingsDiv.classList.toggle('hidden');
        // Re-render lucide icons when toggling
        setTimeout(() => lucide.createIcons(), 50);
    }
}

/**
 * Toggle database size settings visibility
 */
function toggleDatabaseSizeSettings() {
    const settingsDiv = document.getElementById('database-size-settings');
    if (settingsDiv) {
        settingsDiv.classList.toggle('hidden');
        // Re-render lucide icons when toggling
        setTimeout(() => lucide.createIcons(), 50);
    }
}

/**
 * Initialize notifications when page loads
 * Hook into the admin tab system
 */
document.addEventListener('DOMContentLoaded', () => {
    // Override the showTab function to load notifications when needed
    const originalShowTab = window.showTab;
    window.showTab = function(tabName) {
        originalShowTab(tabName);
        if (tabName === 'notifications') {
            loadNotificationSettings();
        }
    };
});
