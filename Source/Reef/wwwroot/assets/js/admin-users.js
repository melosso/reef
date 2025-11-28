// assets/js/admin-users.js
// Handles loading and rendering users in the admin panel

// Helper function to format relative time
function getRelativeTime(dateString) {
    if (!dateString) return '-';

    const now = new Date();
    const date = new Date(dateString);
    const diffMs = now - date;
    const diffSecs = Math.floor(diffMs / 1000);
    const diffMins = Math.floor(diffSecs / 60);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    if (diffSecs < 60) {
        return 'Just now';
    } else if (diffMins < 60) {
        return `${diffMins} minute${diffMins !== 1 ? 's' : ''} ago`;
    } else if (diffHours < 24) {
        return `${diffHours} hour${diffHours !== 1 ? 's' : ''} ago`;
    } else if (diffDays < 7) {
        return `${diffDays} day${diffDays !== 1 ? 's' : ''} ago`;
    } else if (diffDays < 30) {
        const weeks = Math.floor(diffDays / 7);
        return `${weeks} week${weeks !== 1 ? 's' : ''} ago`;
    } else if (diffDays < 365) {
        const months = Math.floor(diffDays / 30);
        return `${months} month${months !== 1 ? 's' : ''} ago`;
    } else {
        const years = Math.floor(diffDays / 365);
        return `${years} year${years !== 1 ? 's' : ''} ago`;
    }
}

async function loadUsers() {
    try {
        const response = await cachedFetch(`${window.location.origin}/api/admin/users`, {
            headers: getAuthHeaders(),
            ttl: 3 * 60 * 1000  // Cache for 3 minutes
        });
        if (!response.ok) throw new Error('Failed to load users');
        const users = await response.json();

        // Debug: Log first user to check property names
        // if (users.length > 0) {
        //     console.log('First user object:', users[0]);
        //     console.log('LastSeenAt value:', users[0].lastSeenAt || users[0].LastSeenAt);
        // }

        const tbody = document.getElementById('users-table');
        const currentUserRole = localStorage.getItem('reef_role');

        tbody.innerHTML = users.map(user => {
            const isFirstUser = user.id === 1;
            // Get current user by comparing username from localStorage
            const currentUsername = localStorage.getItem('reef_username');
            const isCurrentUser = user.username === currentUsername;
            const isOtherAdmin = (user.role === 'Admin' || user.role === 'Administrator') && currentUserRole === 'Admin' && !isCurrentUser;

            // Disable delete button if: first user OR other admin
            const canDelete = !isFirstUser && !isOtherAdmin;
            const deleteButtonClass = !canDelete
                ? 'text-gray-400 cursor-not-allowed'
                : 'text-red-600 hover:text-red-800';
            const deleteButtonDisabled = !canDelete ? 'disabled' : '';
            const deleteButtonTitle = isFirstUser ? 'Cannot delete the first user'
                                     : isOtherAdmin ? 'Cannot delete another admin'
                                     : '';

            // Disable edit button if: other admin (but allow editing current user even if admin)
            const canEdit = !isOtherAdmin;
            const editButtonClass = !canEdit
                ? 'text-gray-400 cursor-not-allowed mr-3'
                : 'text-blue-600 hover:text-blue-900 mr-3';
            const editButtonDisabled = !canEdit ? 'disabled' : '';
            const editButtonTitle = isOtherAdmin ? 'Cannot modify another admin' : '';

            // Handle both camelCase and PascalCase from API
            const lastSeenAt = user.lastLoginAt || user.lastSeenAt || user.LastSeenAt;
            const lastSeenText = getRelativeTime(lastSeenAt);
            const lastSeenTitle = lastSeenAt ? new Date(lastSeenAt).toLocaleString() : 'Never';

            return `
            <tr>
                <td class="px-6 py-4 text-sm font-medium text-gray-900">${user.username}</td>
                <td class="px-6 py-4 text-sm"><span class="px-2 py-1 text-xs font-semibold rounded-full ${user.role === 'Admin' || user.role === 'Administrator' ? 'bg-purple-100 text-purple-800' : 'bg-gray-100 text-gray-800'}">${user.role}</span></td>
                <td class="px-6 py-4 text-sm"><span class="px-2 py-1 text-xs font-semibold rounded-full ${user.isActive ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'}">${user.isActive ? 'Active' : 'Inactive'}</span></td>
                <td class="px-6 py-4 text-sm text-gray-500">${user.createdAt ? new Date(user.createdAt).toLocaleDateString() : '-'}</td>
                <td class="px-6 py-4 text-sm text-gray-500" title="${lastSeenTitle}">${lastSeenText}</td>
                <td class="px-6 py-4 text-sm text-right">
                    <button onclick="editUser(${user.id})" class="${editButtonClass}" ${editButtonDisabled} ${editButtonTitle ? `title="${editButtonTitle}"` : ''}>
                        <i data-lucide="edit" class="h-4 w-4 inline"></i>
                    </button>
                    <button onclick="deleteUser(${user.id}, '${user.username}')" class="${deleteButtonClass}" ${deleteButtonDisabled} ${deleteButtonTitle ? `title="${deleteButtonTitle}"` : ''}>
                        <i data-lucide="trash-2" class="h-4 w-4 inline"></i>
                    </button>
                </td>
            </tr>
            `;
        }).join('');
        queueLucideRender();
    } catch (error) {
        console.error('Error loading users:', error);
    }
}

// Edit user - opens modal with user data
async function editUser(userId) {
    try {
        const response = await fetch(`${window.location.origin}/api/admin/users`, {
            headers: getAuthHeaders()
        });
        if (!response.ok) throw new Error('Failed to load user');

        const users = await response.json();
        const user = users.find(u => u.id === userId);

        if (!user) {
            showToast('User not found', 'danger');
            return;
        }

        // Prevent editing other admin accounts (but allow editing current user)
        const currentUserRole = localStorage.getItem('reef_role');
        const currentUsername = localStorage.getItem('reef_username');
        const isCurrentUser = user.username === currentUsername;
        const isOtherAdmin = (user.role === 'Admin' || user.role === 'Administrator') && currentUserRole === 'Admin' && !isCurrentUser;
        if (isOtherAdmin) {
            showToast('Cannot modify another admin account', 'danger');
            return;
        }

        // Populate edit modal
        document.getElementById('edit-user-id').value = user.id;
        document.getElementById('edit-username').value = user.username;
        document.getElementById('edit-username').disabled = true; // Username cannot be changed

        const roleSelect = document.getElementById('edit-role');
        roleSelect.value = user.role;

        // Disable role change for the first user (id=1)
        const isFirstUser = user.id === 1;
        roleSelect.disabled = isFirstUser;
        if (isFirstUser) {
            roleSelect.title = 'Cannot change the role of the first admin user';
            roleSelect.classList.add('cursor-not-allowed', 'opacity-50');
        } else {
            roleSelect.title = '';
            roleSelect.classList.remove('cursor-not-allowed', 'opacity-50');
        }

        document.getElementById('edit-is-active').checked = user.isActive;
        document.getElementById('edit-password').value = '';

        // Show password field: allow admins to change their OWN password, but not other admin passwords
        const passwordContainer = document.getElementById('edit-password').parentElement;
        if ((user.role === 'Admin' || user.role === 'Administrator') && !isCurrentUser) {
            // Hide password field for other admins
            passwordContainer.style.display = 'none';
        } else {
            // Show password field for regular users and for the current user (even if admin)
            passwordContainer.style.display = 'block';
        }

        // Show modal
        document.getElementById('editUserModal').classList.remove('hidden');
        queueLucideRender();
    } catch (error) {
        showToast('Error loading user: ' + error.message, 'danger');
    }
}

// Update user from edit modal
async function updateUser() {
    const userId = parseInt(document.getElementById('edit-user-id').value);
    const role = document.getElementById('edit-role').value;
    const isActive = document.getElementById('edit-is-active').checked;
    const password = document.getElementById('edit-password').value.trim();
    const passwordConfirm = document.getElementById('edit-password-confirm').value.trim();

    // Validate password confirmation
    if (password && password !== passwordConfirm) {
        showToast('Passwords do not match', 'danger');
        return;
    }

    // Prevent role change for the first user
    if (userId === 1) {
        // Get the original role from the user data to check if it was changed
        try {
            const response = await fetch(`${window.location.origin}/api/admin/users`, {
                headers: getAuthHeaders()
            });
            if (response.ok) {
                const users = await response.json();
                const user = users.find(u => u.id === userId);
                if (user && user.role !== role) {
                    showToast('Cannot change the role of the first admin user', 'danger');
                    return;
                }
            }
        } catch (e) {
            console.error('Error validating user role:', e);
        }
    }

    // Validate: allow password changes for regular users and the current user, but not for other admins
    if (password) {
        // Check if target user is another admin (not the current user)
        try {
            const response = await fetch(`${window.location.origin}/api/admin/users`, {
                headers: getAuthHeaders()
            });
            if (response.ok) {
                const users = await response.json();
                const user = users.find(u => u.id == userId);
                const currentUsername = localStorage.getItem('reef_username');
                const isCurrentUser = user.username === currentUsername;

                if (user && (user.role === 'Admin' || user.role === 'Administrator') && !isCurrentUser) {
                    showToast('Cannot change password for other admin users', 'danger');
                    return;
                }
            }
        } catch (e) {
            console.error('Error validating user role:', e);
        }
    }

    const payload = {
        role: role,
        isActive: isActive
    };

    // Only include password if provided
    if (password) {
        payload.password = password;
    }

    try {
        const response = await fetch(`${window.location.origin}/api/admin/users/${userId}`, {
            method: 'PUT',
            headers: getAuthHeaders(),
            body: JSON.stringify(payload)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to update user');
        }

        closeEditUserModal();
        clearApiCache('/api/admin/users');  // Invalidate cache after mutation
        loadUsers();
        showToast('User updated successfully', 'success');
    } catch (error) {
        showToast('Error: ' + error.message, 'danger');
    }
}

// Handles loading and rendering keys in the admin panel
async function loadApiKeys() {
    try {
        const response = await cachedFetch(`${API_BASE}/api/admin/api-keys`, {
            headers: getAuthHeaders(),
            ttl: 3 * 60 * 1000  // Cache for 3 minutes
        });

        if (!response.ok) throw new Error('Failed to load API keys');

        const keys = await response.json();
        const tbody = document.getElementById('apikeys-table');
        
        if (keys.length === 0) {
            tbody.innerHTML = '<tr><td colspan="5" class="px-6 py-4 text-center text-gray-500">No API keys found</td></tr>';
            return;
        }

        tbody.innerHTML = keys.map(key => `
            <tr>
                <td class="px-6 py-4 text-sm font-medium text-gray-900">${key.Name}</td>
                <td class="px-6 py-4 text-sm text-gray-500">${new Date(key.CreatedAt).toLocaleDateString()}</td>
                <td class="px-6 py-4 text-sm text-gray-500">${key.ExpiresAt ? new Date(key.ExpiresAt).toLocaleDateString() : 'Never'}</td>
                <td class="px-6 py-4 text-sm text-gray-500">${key.LastUsedAt ? new Date(key.LastUsedAt).toLocaleString() : 'Never'}</td>
                <td class="px-6 py-4 text-sm text-right">
                    <button onclick="revokeApiKey(${key.Id}, '${key.Name}')" class="text-red-600 hover:text-red-800">
                        <i data-lucide="trash-2" class="h-4 w-4"></i>
                    </button>
                </td>
            </tr>
        `).join('');
        queueLucideRender();
    } catch (error) {
        console.error('Error loading API keys:', error);
    }
}