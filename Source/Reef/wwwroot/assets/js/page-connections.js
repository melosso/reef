lucide.createIcons();

function checkAuth() {
    return requireAuth();
}

let userMenuExpanded = false;

// Toggle user menu
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
    
    // Re-render icons after DOM update
    setTimeout(() => lucide.createIcons(), 50);
}

function logout() {
    clearAuth();
    redirectToLogin();
}

function openAddConnectionModal() {
    document.getElementById('add-connection-modal').classList.remove('hidden');
    document.getElementById('connection-modal-title').textContent = 'Add New Connection';
    document.getElementById('connection-more-options').classList.add('hidden');
    lucide.createIcons();
}

function closeAddConnectionModal() {
    document.getElementById('add-connection-modal').classList.add('hidden');
    document.getElementById('add-connection-form').reset();
    document.getElementById('add-connection-form').removeAttribute('data-edit-id');
    document.getElementById('add-connection-form').removeAttribute('data-is-active');
    document.getElementById('connection-options-menu').classList.add('hidden');
}

function toggleConnectionOptionsMenu() {
    const menu = document.getElementById('connection-options-menu');
    menu.classList.toggle('hidden');
}

// Close dropdown when clicking outside
document.addEventListener('click', function(event) {
    const menu = document.getElementById('connection-options-menu');
    const button = event.target.closest('[onclick="toggleConnectionOptionsMenu()"]');
    if (!button && !menu?.contains(event.target)) {
        menu?.classList.add('hidden');
    }
});

async function toggleConnectionActiveStatus() {
    const form = document.getElementById('add-connection-form');
    const connectionId = form.getAttribute('data-edit-id');
    const isActive = form.getAttribute('data-is-active') === 'true';
    const connectionName = document.getElementById('connection-name').value;
    
    const action = isActive ? 'deactivate' : 'activate';
    const confirmMessage = isActive 
        ? `Are you sure you want to deactivate "${connectionName}"?\n\nNote: Any profiles, jobs, and webhooks using this connection will fail to execute until you reactivate it.`
        : `Are you sure you want to activate "${connectionName}"?`;
    
    if (!confirm(confirmMessage)) {
        document.getElementById('connection-options-menu').classList.add('hidden');
        return;
    }

    try {
        await checkAuth();
        
        // Get current connection data
        const response = await fetch(`/api/connections/${connectionId}`, {
            method: 'GET',
            headers: getAuthHeaders()
        });
        
        if (!response.ok) {
            showToast('Failed to load connection details', 'error');
            return;
        }
        
        const connection = await response.json();
        
        // Update the isActive status
        connection.isActive = !isActive;
        
        // Send PUT request with updated connection
        const updateResponse = await fetch(`/api/connections/${connectionId}`, {
            method: 'PUT',
            headers: getAuthHeaders(),
            body: JSON.stringify(connection)
        });
        
        if (updateResponse.ok) {
            showToast(`Connection ${action}d successfully`, 'success');
            closeAddConnectionModal();
            await loadConnections();
        } else {
            showToast(`Failed to ${action} connection`, 'error');
        }
    } catch (error) {
        showToast(`Error ${action}ing connection: ${error.message}`, 'error');
    }
    
    document.getElementById('connection-options-menu').classList.add('hidden');
}

async function testConnection() {
    const btn = document.getElementById('test-connection-btn');
    const spinner = document.getElementById('test-connection-spinner');
    const btnText = document.getElementById('test-connection-btn-text');
    if (btn && spinner && btnText) {
        btn.disabled = true;
        spinner.classList.remove('hidden');
        btnText.textContent = 'Testing...';
    }
    const type = document.getElementById('connection-type').value;
    const connectionString = document.getElementById('connection-string').value;
    const form = document.getElementById('add-connection-form');
    const connectionId = form && form.hasAttribute('data-edit-id') ? parseInt(form.getAttribute('data-edit-id')) : null;

    if (!connectionString) {
        showToast('Please enter a connection string', 'error');
        if (btn && spinner && btnText) {
            btn.disabled = false;
            spinner.classList.add('hidden');
            btnText.textContent = 'Test Connection';
        }
        return;
    }

    // Create AbortController for timeout
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 30000); // 30 second timeout

    try {
        await checkAuth();
        const body = { type, connectionString };
        if (connectionId) body.connectionId = connectionId;
        const response = await fetch('/api/connections/test', {
            method: 'POST',
            headers: getAuthHeaders(),
            body: JSON.stringify(body),
            signal: controller.signal
        });

        // Clear timeout on successful response
        clearTimeout(timeoutId);

        let result;
        try {
            result = await response.json();
        } catch (jsonError) {
            showToast('Connection test error: Invalid or empty server response.', 'error');
            if (btn && spinner && btnText) {
                btn.disabled = false;
                spinner.classList.add('hidden');
                btnText.textContent = 'Test Connection';
            }
            return;
        }
        if (result && result.success) {
            showToast(`Connection successful! (${result.responseTimeMs}ms)`, 'success');
        } else {
            showToast(`Connection failed: ${result && result.message ? result.message : 'Unknown error'}`, 'error');
        }
        // Reload connections to update last tested date
        if (typeof loadConnections === 'function') {
            await loadConnections();
        }
        if (btn && spinner && btnText) {
            btn.disabled = false;
            spinner.classList.add('hidden');
            btnText.textContent = 'Test Connection';
        }
    } catch (error) {
        clearTimeout(timeoutId);
        if (error.name === 'AbortError') {
            showToast('Connection test timed out after 30 seconds', 'error');
        } else {
            showToast(`Connection test error: ${error.message}`, 'error');
        }
        if (btn && spinner && btnText) {
            btn.disabled = false;
            spinner.classList.add('hidden');
            btnText.textContent = 'Test Connection';
        }
    }
}

async function loadConnections() {

    await checkAuth();
    setUserSidebarInfo();
    const connectionsList = document.getElementById('connections-list');
    try {
        const response = await fetch('/api/connections', {
            method: 'GET',
            headers: getAuthHeaders()
        });
        let connections = [];
        if (response.ok) {
            connections = await response.json();
        }
        if (!connections || connections.length === 0) {
            // Show placeholder if no connections
            connectionsList.innerHTML = `
                <div class="flex flex-col items-center justify-center py-12">
                    <i data-lucide="plug" class="h-12 w-12 text-gray-300 mb-4"></i>
                    <p class="text-lg font-semibold text-gray-700 mb-2">No connections found</p>
                    <p class="text-gray-500 mb-4">Create your first connection to get started.</p>
                </div>
            `;
        } else {
            // Render connections with working edit/delete
            connectionsList.innerHTML = connections.map(conn => `
                <div class="flex items-center justify-between p-4 border border-gray-200 rounded-lg hover:bg-gray-50">
                    <div class="flex items-center space-x-4">
                        <i data-lucide="database" class="h-8 w-8 text-blue-500"></i>
                        <div>
                            <p class="font-medium text-gray-900">${escapeHtml(conn.name)}</p>
                            <p class="text-sm text-gray-500 disable-select">${escapeHtml(conn.type)}${conn.lastTestedAt && conn.lastTestResult ? (conn.lastTestResult.startsWith('Success') ? ` • Last tested successfully ${formatRelativeTime(conn.lastTestedAt)}` : ` • <span class="text-red-600">Last test failed ${formatRelativeTime(conn.lastTestedAt)}</span>`) : ''}</p>
                        </div>
                    </div>
                    <div class="flex items-center space-x-2">
                        <span class="px-2 py-1 text-xs ${conn.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-200 text-gray-500'} rounded">${conn.isActive ? 'Active' : 'Inactive'}</span>
                        <button class="p-2 text-gray-400 hover:text-gray-600" onclick="editConnection(${conn.id})" data-tooltip="Edit" data-tooltip-position="bottom">
                            <i data-lucide="edit" class="h-4 w-4"></i>
                        </button>
                        <button class="p-2 text-gray-400 hover:text-red-600" onclick="deleteConnection(${conn.id}, '${escapeHtml(conn.name)}')" data-tooltip="Delete" data-tooltip-position="bottom">
                            <i data-lucide="trash-2" class="h-4 w-4"></i>
                        </button>
                    </div>
                </div>
            `).join('');
        }
        lucide.createIcons();
    } catch (error) {
        connectionsList.innerHTML = `<p class="text-sm text-red-600">Failed to load connections.</p>`;
        console.error('Failed to load connections:', error);
    }
}


document.getElementById('add-connection-form').addEventListener('submit', async (e) => {

    e.preventDefault();
    await checkAuth();
    const form = document.getElementById('add-connection-form');
    const name = document.getElementById('connection-name').value;
    const type = document.getElementById('connection-type').value;
    const connectionString = document.getElementById('connection-string').value;
    const tags = document.getElementById('connection-tags').value;
    const isEdit = form && form.hasAttribute('data-edit-id');
    const editId = isEdit ? form.getAttribute('data-edit-id') : null;

    // Preserve isActive status when editing
    const isActive = isEdit ? (form.getAttribute('data-is-active') === 'true') : true;

    // For NEW connections only, test the connection first (non-intrusive)
    if (!isEdit) {
        try {
            const testResponse = await fetch('/api/connections/test', {
                method: 'POST',
                headers: getAuthHeaders(),
                body: JSON.stringify({ type, connectionString })
            });

            let testResult;
            try {
                testResult = await testResponse.json();
            } catch (jsonError) {
                showToast('Connection warning: Unable to verify connection', 'warning');
            }

            if (testResult && testResult.success) {
                showToast(`Connection verified successfully! (${testResult.responseTimeMs}ms)`, 'success');
            } else {
                showToast(`We couldn't verify the connection, but it'll be saved anyway. Reason: ${testResult && testResult.message ? testResult.message : 'Unknown error'}`, 'warning');
            }
        } catch (error) {
            showToast(`We couldn't test the connection, but it'll be saved anyway. Reason: ${testResult && testResult.message ? testResult.message : 'Unknown error'}`, 'warning');
        }
    }

    // Combine fields for hash (can adjust as needed)
    const hashInput = `${name}|${type}|${connectionString}|${tags}`;
    const hash = await sha256(hashInput);

    try {
        let url = '/api/connections';
        let method = 'POST';
        if (isEdit && editId) {
            url = `/api/connections/${editId}`;
            method = 'PUT';
        }
        const response = await fetch(url, {
            method,
            headers: getAuthHeaders(),
            body: JSON.stringify({ name, type, connectionString, tags, hash, isActive })
        });
        if (response.ok) {
            closeAddConnectionModal();
            await loadConnections(); // Ensure UI updates after creation/update
        } else {
            let error;
            try {
                error = await response.json();
            } catch (jsonError) {
                showToast('Error: Invalid or empty server response.', 'error');
                return;
            }
            showToast(`Error: ${error && error.message ? error.message : 'Unknown error'}`, 'error');
        }
    } catch (error) {
        showToast(`Error: ${error.message}`, 'error');
    }
});

loadConnections();
