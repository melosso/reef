const API_BASE = window.location.origin;
let token = localStorage.getItem('reef_token');
let groups = [];
let profiles = [];

async function init() {
    await requireAuth();
    token = localStorage.getItem('reef_token');
    lucide.createIcons();
    await loadGroups();
}

async function loadGroups() {
    try {
        const response = await fetch(`${API_BASE}/api/groups`, {
            headers: getAuthHeaders()
        });
        if (!response.ok) throw new Error('Failed to load groups');
        groups = await response.json();
        await loadProfiles();
        renderGroups();
        setUserSidebarInfo();
    } catch (error) {
        showMessage('Failed to load groups', 'error');
    }
}

async function loadProfiles() {
    try {
        const response = await fetch(`${API_BASE}/api/profiles`, {
            headers: getAuthHeaders()
        });
        if (response.ok) {
            profiles = await response.json();
        }
    } catch (error) {
        profiles = [];
    }
}

function renderGroups() {
    const tbody = document.getElementById('groups-tbody');
    if (groups.length === 0) {
        tbody.innerHTML = `
            <tr>
                <td colspan="4" class="px-6 py-8 text-center text-gray-500">
                    <i data-lucide="folder" class="h-12 w-12 mx-auto mb-2 text-gray-300"></i>
                    <p>No groups found. Create your first group to categorize your profiles.</p>
                </td>
            </tr>
        `;
        lucide.createIcons();
        return;
    }
    tbody.innerHTML = groups.map(group => {
        const groupProfiles = profiles.filter(p => p.groupId === group.id);
        return `
            <tr>
                <td class="px-6 py-4 whitespace-nowrap">
                    <div class="flex items-center">
                        <i data-lucide="folder" class="h-4 w-4 text-gray-400 mr-2"></i>
                        <span class="text-sm font-medium text-gray-900">${escapeHtml(group.name)}</span>
                    </div>
                </td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${escapeHtml(group.description || '')}</td>
                <td class="px-6 py-4 whitespace-nowrap text-sm text-gray-500">${groupProfiles.length}</td>
                <td class="px-6 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button onclick="editGroup(${group.id})" class="text-blue-600 hover:text-blue-900 mr-3">
                        <i data-lucide="edit" class="h-4 w-4 inline"></i>
                    </button>
                    <button onclick="deleteGroup(${group.id}, '${escapeHtml(group.name)}')" class="text-red-600 hover:text-red-900">
                        <i data-lucide="trash-2" class="h-4 w-4 inline"></i>
                    </button>
                </td>
            </tr>
        `;
    }).join('');
    lucide.createIcons();
}

function openAddGroupModal() {
    document.getElementById('modal-title').textContent = 'Add Group';
    document.getElementById('group-form').reset();
    document.getElementById('group-id').value = '';
    document.getElementById('group-modal').classList.remove('hidden');
}

function closeGroupModal() {
    document.getElementById('group-modal').classList.add('hidden');
}

async function editGroup(id) {
    const group = groups.find(g => g.id === id);
    if (!group) return;
    document.getElementById('modal-title').textContent = 'Edit Group';
    document.getElementById('group-id').value = group.id;
    document.getElementById('group-name').value = group.name;
    document.getElementById('group-description').value = group.description || '';
    document.getElementById('group-modal').classList.remove('hidden');
}

document.getElementById('group-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    const groupId = document.getElementById('group-id').value;
    const group = {
        name: document.getElementById('group-name').value,
        description: document.getElementById('group-description').value
    };
    try {
        const url = groupId ? `${API_BASE}/api/groups/${groupId}` : `${API_BASE}/api/groups`;
        const method = groupId ? 'PUT' : 'POST';
        const response = await fetch(url, {
            method: method,
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${token}`
            },
            body: JSON.stringify(group)
        });
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save group');
        }
        showMessage(`Group ${groupId ? 'updated' : 'created'} successfully`, 'success');
        closeGroupModal();
        await loadGroups();
    } catch (error) {
        showMessage(error.message, 'error');
    }
});

async function deleteGroup(id, name) {
    if (!confirm(`Are you sure you want to delete group "${name}"? This action cannot be undone.`)) {
        return;
    }
    try {
        const response = await fetch(`${API_BASE}/api/groups/${id}`, {
            method: 'DELETE',
            headers: { 'Authorization': `Bearer ${token}` }
        });
        if (!response.ok) throw new Error('Failed to delete group');
        showMessage('Group deleted successfully', 'success');
        await loadGroups();
    } catch (error) {
        showMessage(error.message, 'error');
    }
}

function showMessage(message, type) {
    const container = document.getElementById('message-container');
    const bgColor = type === 'success' ? 'bg-green-50 border-green-400 text-green-800' : 'bg-red-50 border-red-400 text-red-800';
    const icon = type === 'success' ? 'check-circle' : 'alert-circle';
    container.innerHTML = `
        <div class="${bgColor} border-l-4 p-4 mb-4 rounded" role="alert">
            <div class="flex items-center">
                <i data-lucide="${icon}" class="h-5 w-5 mr-2"></i>
                <p class="font-medium">${message}</p>
            </div>
        </div>
    `;
    lucide.createIcons();
    setTimeout(() => container.innerHTML = '', 5000);
}

function escapeHtml(text) {
    if (text === null || text === undefined) {
        return '';
    }
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
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

window.addEventListener('DOMContentLoaded', init);