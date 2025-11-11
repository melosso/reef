// auth.js - Resilient centralized authentication for Reef UI

let authCache = { token: null, valid: false };
let refreshPromise = null; // Prevent concurrent refresh attempts

/**
 * Refreshes the JWT token by calling the /api/auth/refresh endpoint.
 * @returns {Promise<string|null>} The new token, or null if refresh failed.
 */
async function refreshToken() {
    // Prevent concurrent refresh attempts
    if (refreshPromise) {
        return refreshPromise;
    }

    const currentToken = localStorage.getItem('reef_token');
    if (!currentToken) {
        return null;
    }

    refreshPromise = (async () => {
        try {
            const res = await fetch('/api/auth/refresh', {
                method: 'POST',
                headers: { 
                    'Authorization': `Bearer ${currentToken}`,
                    'Content-Type': 'application/json'
                }
            });

            if (!res.ok) {
                console.warn('Token refresh failed', res.status);
                clearAuth();
                return null;
            }

            const data = await res.json();
            
            if (data.token) {
                localStorage.setItem('reef_token', data.token);
                localStorage.setItem('reef_username', data.username);
                localStorage.setItem('reef_role', data.role);
                authCache = { token: data.token, valid: true };
                console.log('Token refreshed successfully');
                return data.token;
            }

            return null;
        } catch (err) {
            console.error('Token refresh error', err);
            return null;
        } finally {
            refreshPromise = null;
        }
    })();

    return refreshPromise;
}

/**
 * Checks if a valid JWT token exists in localStorage by validating with the backend.
 * If invalid, clears credentials and redirects to /logoff.html.
 * If transient network errors occur, allows a grace period using cached valid token.
 * @returns {Promise<string|null>} Resolves to the valid token, or null if not authenticated.
 */
async function requireAuth() {
    const token = localStorage.getItem('reef_token');
    if (!token) {
        redirectToLogin();
        return null;
    }

    // Use cached validation if token hasn't changed
    if (authCache.token === token && authCache.valid) {
        return token;
    }

    try {
        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token })
        });

        if (!res.ok) {
            console.warn('Auth validation error', res.status);
            
            // Try to refresh the token if we get a 401
            if (res.status === 401) {
                const newToken = await refreshToken();
                if (newToken) {
                    return newToken;
                }
            }
            
            // fallback to cached valid token if available
            if (authCache.token === token && authCache.valid) return token;
            clearAuth();
            redirectToLogin();
            return null;
        }

        const data = await res.json();
        authCache = { token, valid: data.valid };

        if (!data.valid) {
            // Try to refresh the token
            const newToken = await refreshToken();
            if (newToken) {
                return newToken;
            }
            
            clearAuth();
            redirectToLogin();
            return null;
        }

        return token;

    } catch (err) {
        console.warn('Network/auth error', err);
        // fallback to cached valid token if available
        if (authCache.token === token && authCache.valid) return token;
        clearAuth();
        redirectToLogin();
        return null;
    }
}

/**
 * Clears all authentication info from localStorage.
 */
function clearAuth() {
    localStorage.removeItem('reef_token');
    localStorage.removeItem('reef_username');
    localStorage.removeItem('reef_role');
    authCache = { token: null, valid: false };
}

/**
 * Redirects to the login/logoff page.
 */
function redirectToLogin() {
    window.location.href = '/logoff.html';
}

/**
 * Checks if a valid token exists and, if so, redirects to /admin.html.
 */
async function redirectIfAuthenticated() {
    const token = localStorage.getItem('reef_token');
    if (!token) return;

    // Use cached valid token if available
    if (authCache.token === token && authCache.valid) {
        window.location.href = '/admin.html';
        return;
    }

    try {
        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token })
        });
        const data = await res.json();
        authCache = { token, valid: data.valid };
        if (data.valid) {
            window.location.href = '/admin.html';
        }
    } catch (err) {
        console.warn('Network/auth error on redirect check', err);
        // fail silently, don’t redirect
    }
}

// Background token validation and refresh - runs every 3 minutes
setInterval(async () => {
    const token = localStorage.getItem('reef_token');
    if (!token) return;

    try {
        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token })
        });

        if (!res.ok) {
            console.warn('Background auth check failed, attempting refresh');
            await refreshToken();
            return;
        }

        const data = await res.json();
        authCache = { token, valid: data.valid };
        
        if (!data.valid) {
            console.warn('Token invalid, attempting refresh');
            await refreshToken();
        }
    } catch (err) {
        console.warn('Background auth check error', err);
    }
}, 3 * 60 * 1000); // Check every 3 minutes

window.requireAuth = requireAuth;
window.refreshToken = refreshToken;
window.clearAuth = clearAuth;
window.redirectToLogin = redirectToLogin;
window.redirectIfAuthenticated = redirectIfAuthenticated;
