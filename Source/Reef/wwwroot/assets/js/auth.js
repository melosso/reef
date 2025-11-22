// auth.js

let authCache = { token: null, valid: false };
let refreshPromise = null;

/**
 * Lightweight API health check with timeout
 * @param {number} timeout in ms
 * @returns {Promise<boolean>}
 */
async function checkApiAvailability(timeout = 3000) {
    try {
        const controller = new AbortController();
        const id = setTimeout(() => controller.abort(), timeout);

        // Use the correct endpoint
        const res = await fetch('/health/', { signal: controller.signal });
        clearTimeout(id);

        return res.ok;
    } catch (err) {
        console.warn('API unavailable', err);
        return false;
    }
}


/**
 * Refreshes the JWT token
 */
async function refreshToken() {
    if (refreshPromise) return refreshPromise;

    const currentToken = localStorage.getItem('reef_token');
    if (!currentToken) return null;

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
 * Require authentication for a page
 * - Checks API availability
 * - Uses cached token if offline
 */
async function requireAuth() {
    const token = localStorage.getItem('reef_token');
    if (!token) {
        redirectToLogin();
        return null;
    }

    // If token is cached and valid, use it
    if (authCache.token === token && authCache.valid) {
        return token;
    }

    // Check API availability first
    const apiAvailable = await checkApiAvailability();
    if (!apiAvailable) {
        if (authCache.token === token && authCache.valid) {
            // allow temporary offline access
            return token;
        } else {
            clearAuth();
            redirectToLogin();
            return null;
        }
    }

    try {
        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token })
        });

        if (!res.ok) {
            if (res.status === 401) {
                const newToken = await refreshToken();
                if (newToken) return newToken;
            }
            clearAuth();
            redirectToLogin();
            return null;
        }

        const data = await res.json();
        authCache = { token, valid: data.valid };

        if (!data.valid) {
            const newToken = await refreshToken();
            if (newToken) return newToken;

            clearAuth();
            redirectToLogin();
            return null;
        }

        return token;

    } catch (err) {
        console.warn('Network/auth error', err);
        if (authCache.token === token && authCache.valid) return token;

        clearAuth();
        redirectToLogin();
        return null;
    }
}

/**
 * Clear local authentication
 */
function clearAuth() {
    localStorage.removeItem('reef_token');
    localStorage.removeItem('reef_username');
    localStorage.removeItem('reef_role');
    authCache = { token: null, valid: false };
}

/**
 * Redirect to login/logoff page
 */
function redirectToLogin() {
    window.location.href = '/logoff';
}

/**
 * Redirect if already authenticated
 */
async function redirectIfAuthenticated() {
    const token = localStorage.getItem('reef_token');
    if (!token) return;

    if (authCache.token === token && authCache.valid) {
        window.location.href = '/admin';
        return;
    }

    try {
        const apiAvailable = await checkApiAvailability();
        if (!apiAvailable) return;

        const res = await fetch('/api/auth/validate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token })
        });
        const data = await res.json();
        authCache = { token, valid: data.valid };

        if (data.valid) {
            window.location.href = '/admin';
        }
    } catch (err) {
        console.warn('Network/auth error on redirect check', err);
    }
}

/**
 * Background token validation & API monitoring
 */
setInterval(async () => {
    const token = localStorage.getItem('reef_token');
    if (!token) return;

    const apiAvailable = await checkApiAvailability();
    if (!apiAvailable) {
        if (authCache.token !== token || !authCache.valid) {
            console.warn('API down, clearing auth');
            clearAuth();
            redirectToLogin();
        }
        return;
    }

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
}, 3 * 60 * 1000); // Every 3 minutes

// Expose globally
window.requireAuth = requireAuth;
window.refreshToken = refreshToken;
window.clearAuth = clearAuth;
window.redirectToLogin = redirectToLogin;
window.redirectIfAuthenticated = redirectIfAuthenticated;
window.checkApiAvailability = checkApiAvailability;
