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

            // Only prevent deletion of the first user (admins can delete other admins)
            const canDelete = !isFirstUser;
            const deleteButtonClass = !canDelete
                ? 'text-gray-400 cursor-not-allowed'
                : 'text-red-600 hover:text-red-800';
            const deleteButtonDisabled = !canDelete ? 'disabled' : '';
            const deleteButtonTitle = isFirstUser ? 'Cannot delete the first user' : '';

            // Admins can edit all users (including other admins)
            const canEdit = true;
            const editButtonClass = 'text-blue-600 hover:text-blue-900 mr-3';
            const editButtonDisabled = '';
            const editButtonTitle = '';

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

        // Admins can edit all users (including other admins)
        const currentUsername = localStorage.getItem('reef_username');
        const isCurrentUser = user.username === currentUsername;

        // Populate edit modal
        document.getElementById('edit-user-id').value = user.id;
        document.getElementById('edit-username').value = user.username;
        document.getElementById('edit-username').disabled = true; // Username cannot be changed inline
        
        const displayNameField = document.getElementById('edit-display-name');
        if (displayNameField) {
            displayNameField.value = user.displayName || '';
        }

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

        // Store active status in hidden field
        document.getElementById('edit-is-active').value = user.isActive ? 'true' : 'false';

        // Update toggle button text based on current active status
        const toggleText = document.getElementById('user-toggle-text');
        if (toggleText) {
            toggleText.textContent = user.isActive ? 'Disable' : 'Enable';
        }

        document.getElementById('edit-password').value = '';

        // Always show password field (admins can change any user's password)
        const passwordContainer = document.getElementById('edit-password').parentElement;
        passwordContainer.style.display = 'block';

        // Always show change username button (admins can change any username)
        const changeUsernameButton = document.getElementById('change-username-button');
        if (changeUsernameButton) {
            changeUsernameButton.style.display = 'flex';
        }

        // Show modal
        document.getElementById('editUserModal').classList.remove('hidden');
        queueLucideRender();
    } catch (error) {
        showToast('Error loading user: ' + error.message, 'danger');
    }
}

// Toggle user options menu
function toggleUserOptionsMenu() {
    const menu = document.getElementById('user-options-menu');
    if (menu) {
        menu.classList.toggle('hidden');
    }
}

// Close user options menu when clicking outside
document.addEventListener('click', function(event) {
    const menu = document.getElementById('user-options-menu');
    const button = document.querySelector('[onclick="toggleUserOptionsMenu()"]');
    if (menu && button && !menu.contains(event.target) && !button.contains(event.target)) {
        menu.classList.add('hidden');
    }
});

// Toggle user active status
async function toggleUserActiveStatus() {
    const userId = parseInt(document.getElementById('edit-user-id').value);
    const activeField = document.getElementById('edit-is-active');
    const currentStatus = activeField.value === 'true';
    const newStatus = !currentStatus;

    try {
        // Check if this would disable the last active administrator
        const response = await fetch(`${window.location.origin}/api/admin/users`, {
            headers: getAuthHeaders()
        });
        if (!response.ok) throw new Error('Failed to load users');

        const users = await response.json();
        const user = users.find(u => u.id === userId);

        if (!user) {
            showToast('User not found', 'danger');
            return;
        }

        // Count active admins
        if ((user.role === 'Admin' || user.role === 'Administrator') && currentStatus && !newStatus) {
            // Trying to disable an admin, check if it's the last one
            const countResponse = await fetch(`${window.location.origin}/api/admin/users/count-active-admins`, {
                headers: getAuthHeaders()
            });
            if (countResponse.ok) {
                const data = await countResponse.json();
                if (data.count <= 1) {
                    showToast('Cannot disable the last active administrator', 'danger');
                    return;
                }
            }
        }

        // Toggle the status
        activeField.value = newStatus ? 'true' : 'false';

        // Update toggle button text
        const toggleText = document.getElementById('user-toggle-text');
        if (toggleText) {
            toggleText.textContent = newStatus ? 'Disable' : 'Enable';
        }

        // Close the menu
        const menu = document.getElementById('user-options-menu');
        if (menu) {
            menu.classList.add('hidden');
        }

        showToast(newStatus ? 'User will be enabled' : 'User will be disabled', 'info');
    } catch (error) {
        showToast('Error toggling user status: ' + error.message, 'danger');
    }
}

// Open change username modal
function openChangeUsernameModal() {
    const userId = document.getElementById('edit-user-id').value;
    const currentUsername = document.getElementById('edit-username').value;

    document.getElementById('change-username-user-id').value = userId;
    document.getElementById('current-username-display').value = currentUsername;
    document.getElementById('new-username-input').value = '';

    document.getElementById('changeUsernameModal').classList.remove('hidden');
    queueLucideRender();
}

// Close change username modal
function closeChangeUsernameModal() {
    document.getElementById('changeUsernameModal').classList.add('hidden');
    document.getElementById('change-username-user-id').value = '';
    document.getElementById('current-username-display').value = '';
    document.getElementById('new-username-input').value = '';
}

// Validate username format on client side
function validateUsernameFormat(username) {
    if (!username || username.trim().length === 0) {
        return 'Username cannot be empty';
    }

    const trimmedUsername = username.trim();

    // Check length
    if (trimmedUsername.length < 3) {
        return 'Username must be at least 3 characters long';
    }

    if (trimmedUsername.length > 50) {
        return 'Username cannot exceed 50 characters';
    }

    // Check format: allow alphanumeric, underscores, hyphens, dots, @ symbol (for email addresses)
    if (!/^[a-zA-Z0-9._@-]+$/.test(trimmedUsername)) {
        return 'Username can only contain letters, numbers, underscores, hyphens, dots, and @ symbols';
    }

    // If contains @, validate as email format
    if (trimmedUsername.includes('@')) {
        // Basic email validation
        const emailRegex = /^[a-zA-Z0-9._-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/;
        if (!emailRegex.test(trimmedUsername)) {
            return 'Invalid email address format';
        }
    } else {
        // For non-email usernames, apply the original rules
        // Cannot start or end with dots, underscores, or hyphens
        if (/^[._-]|[._-]$/.test(trimmedUsername)) {
            return 'Username cannot start or end with dots, underscores, or hyphens';
        }

        // Cannot have consecutive dots
        if (trimmedUsername.includes('..')) {
            return 'Username cannot contain consecutive dots';
        }
    }

    return null; // Valid
}

// Change username
async function changeUsername() {
    const userId = parseInt(document.getElementById('change-username-user-id').value);
    const newUsername = document.getElementById('new-username-input').value.trim();

    // Client-side validation
    const validationError = validateUsernameFormat(newUsername);
    if (validationError) {
        showToast(validationError, 'danger');
        return;
    }

    try {
        const response = await fetch(`${window.location.origin}/api/admin/users/${userId}/username`, {
            method: 'PUT',
            headers: getAuthHeaders(),
            body: JSON.stringify({ newUsername })
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to change username');
        }

        const result = await response.json();

        // Update the username in the edit modal
        document.getElementById('edit-username').value = newUsername;

        closeChangeUsernameModal();
        clearApiCache('/api/admin/users');  // Invalidate cache after mutation
        loadUsers();

        // If user changed their own username, update localStorage and token
        if (result.requiresTokenRefresh && result.newToken) {
            // Update localStorage
            localStorage.setItem('reef_username', newUsername);
            localStorage.setItem('reef_token', result.newToken);

            showToast('Username changed successfully. Your session has been updated.', 'success');

            // Optionally reload the page to ensure all UI updates
            setTimeout(() => {
                window.location.reload();
            }, 1500);
        } else {
            showToast('Username changed successfully', 'success');
        }
    } catch (error) {
        showToast('Error: ' + error.message, 'danger');
    }
}

// Update user from edit modal
async function updateUser() {
    const userId = parseInt(document.getElementById('edit-user-id').value);
    const role = document.getElementById('edit-role').value;
    const activeField = document.getElementById('edit-is-active');
    const isActive = activeField ? activeField.value === 'true' : true;
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

    const displayNameField = document.getElementById('edit-display-name');
    const displayName = displayNameField ? displayNameField.value.trim() : '';

    const payload = {
        role: role,
        isActive: isActive,
        displayName: displayName || null
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

        // If user updated their own display name, update localStorage and sidebar
        const currentUsername = localStorage.getItem('reef_username');
        const users = await (await fetch(`${window.location.origin}/api/admin/users`, {
            headers: getAuthHeaders()
        })).json();
        const updatedUser = users.find(u => u.id === userId);
        
        if (updatedUser && updatedUser.username === currentUsername) {
            // User edited themselves, update localStorage
            if (displayName) {
                localStorage.setItem('reef_display_name', displayName);
            } else {
                localStorage.removeItem('reef_display_name');
            }
            // Update sidebar immediately
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