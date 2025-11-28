// connections.js - Handles connection CRUD for Reef UI

// Helper to escape HTML
function escapeHtml(text) {
    if (text === null || text === undefined) return '';
    return String(text).replace(/[&<>"']/g, function (c) {
        return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;','\'':'&#39;'}[c];
    });
}

// Helper to format relative time (e.g., '2 hours ago')
function formatRelativeTime(dateString) {
    const date = new Date(dateString);
    const now = new Date();
    const diff = Math.floor((now - date) / 1000);
    if (diff < 60) return `${diff} seconds ago`;
    if (diff < 3600) return `${Math.floor(diff/60)} minutes ago`;
    if (diff < 86400) return `${Math.floor(diff/3600)} hours ago`;
    return date.toLocaleDateString();
}

// Edit connection: open modal pre-filled
window.editConnection = function(id) {
    fetch('/api/connections', {
        method: 'GET',
        headers: getAuthHeaders()
    })
    .then(res => res.json())
    .then(connections => {
        const conn = connections.find(c => c.id === id);
        if (!conn) return;
        
        // First, open the modal (which sets it to "Add" mode)
        document.getElementById('add-connection-modal').classList.remove('hidden');
        
        // Then populate the fields
        document.getElementById('connection-name').value = conn.name;
        document.getElementById('connection-type').value = conn.type;
        document.getElementById('connection-string').value = conn.connectionString;
        document.getElementById('connection-tags').value = conn.tags || '';
        
        const form = document.getElementById('add-connection-form');
        form.setAttribute('data-edit-id', id);
        form.setAttribute('data-is-active', conn.isActive);
        
        // Update modal title to "Edit Connection"
        document.getElementById('connection-modal-title').textContent = 'Edit Connection';
        
        // Show the more options menu
        document.getElementById('connection-more-options').classList.remove('hidden');
        
        // Update the toggle text based on current status
        const toggleText = document.getElementById('connection-toggle-text');
        toggleText.textContent = conn.isActive ? 'Disable' : 'Enable';
        
        // Queue lucide icons render (batched to prevent layout thrashing)
        queueLucideRender();
    });
}

// Delete connection
window.deleteConnection = async function(id, name) {
    if (!confirm(`Are you sure you want to delete connection "${name}"? This action cannot be undone.`)) return;
    try {
        const response = await fetch(`/api/connections/${id}`, {
            method: 'DELETE',
            headers: getAuthHeaders()
        });
        if (response.ok) {
            await loadConnections();
        } else {
            showToast('Failed to delete connection.', 'error');
        }
    } catch (error) {
        showToast('Error deleting connection: ' + error.message, 'error');
    }
}

// Export helpers for use in HTML inline event handlers
window.escapeHtml = escapeHtml;
window.formatRelativeTime = formatRelativeTime;
