// -----------------------------
// Safe LocalStorage Access
// -----------------------------
function safeGetItem(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        console.warn('localStorage unavailable');
        return null;
    }
}

function safeSetItem(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        console.warn('localStorage unavailable');
    }
}

// -----------------------------
// SHA-256 Hash Utility (unchanged)
// -----------------------------
window.sha256 = async function(str) {
    if (window.crypto && window.crypto.subtle && typeof window.crypto.subtle.digest === 'function') {
        const encoder = new TextEncoder();
        const data = encoder.encode(str);
        const hashBuffer = await window.crypto.subtle.digest('SHA-256', data);
        const hashArray = Array.from(new Uint8Array(hashBuffer));
        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
    } else {
        // Fallback: simple JS SHA-256 implementation
        function r(f,s,z){return f>>>s|f<<32-s}
        function sha256js(ascii){var K=[1116352408,1899447441,-1245643825,-373957723,961987163,1508970993,-1841331548,-1424204075,-670586216,310598401,607225278,1426881987,1925078388,-2132889090,-1680079193,-1046744716,-459576895,-272742522,264347078,604807628,770255983,1249150122,1555081692,1996064986,-1740746414,-1473132947,-1341970488,-1084653625,-958395405,-710438585,113926993,338241895,666307205,773529912,1294757372,1396182291,1695183700,1986661051,-2117940946,-1838011259,-1564481375,-1474664885,-1035236496,-949202525,-778901479,-694614492,-200395387,275423344,430227734,506948616,659060556,883997877,958139571,1322822218,1537002063,1747873779,1955562222,2024104815,-2067236844,-1933114872,-1866530822,-1538233109,-1090935817,-965641998],H=[1779033703,-1150833019,1013904242,-1521486534,1359893119,-1694144372,528734635,1541459225],l=ascii.length,s=[],i=0;for(;i<l;++i)s[i>>2]|=ascii.charCodeAt(i)<<24-8*(i%4);s[i>>2]|=0x80<<24-8*(i%4);s[(i+64>>9<<4)+15]=l*8;for(var w=[],j=0;j<s.length;j+=16){var a=H.slice(0),b=H.slice(0);for(var k=0;k<64;++k){var t=a[7]+(r(a[4],6)^r(a[4],11)^r(a[4],25))+(a[6]^(a[4]&a[5]^a[6]&a[4]))+K[k]+(w[k]=k<16?s[j+k]:((r(w[k-2],17)^r(w[k-2],19)^w[k-2]>>>10)+w[k-7]+(r(w[k-15],7)^r(w[k-15],18)^w[k-15]>>>3)+w[k-16])|0);a=[(t+(r(a[0],2)^r(a[0],13)^r(a[0],22))+(a[0]&a[1]^a[0]&a[2]^a[1]&a[2]))|0].concat(a);a[4]=(a[4]+t)|0;a.pop()}for(var k=0;k<8;++k)H[k]=(H[k]+a[k])|0}return H.map(function(h){return('00000000'+(h>>>0).toString(16)).slice(-8)}).join('')}
        return Promise.resolve(sha256js(str));
    }
};

// -----------------------------
// Tailwind CSS Configuration
// -----------------------------
tailwind.config = {
    theme: {
        extend: {
            fontFamily: {
                inter: ['Inter', 'sans-serif'],
            },
        },
    },
};

// -----------------------------
// Clipboard Copy Utility (HTTP/HTTPS compatible)
// -----------------------------
/**
 * Copy text to clipboard with fallback for non-HTTPS contexts
 * @param {string} text - Text to copy
 * @returns {Promise<boolean>} - True if successful
 */
async function copyToClipboard(text) {
    // Try modern clipboard API first (requires HTTPS or localhost)
    if (navigator.clipboard && navigator.clipboard.writeText) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            console.warn('Clipboard API failed, trying fallback:', err);
        }
    }

    // Fallback for HTTP or browsers without clipboard API
    try {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        textarea.style.position = 'fixed';
        textarea.style.left = '-9999px';
        textarea.style.top = '-9999px';
        textarea.setAttribute('readonly', '');
        document.body.appendChild(textarea);

        // Select the text
        textarea.select();
        textarea.setSelectionRange(0, text.length);

        // Copy using execCommand
        const successful = document.execCommand('copy');
        document.body.removeChild(textarea);

        if (successful) {
            return true;
        } else {
            throw new Error('execCommand copy failed');
        }
    } catch (err) {
        console.error('Fallback copy failed:', err);
        return false;
    }
}

// Make copyToClipboard available globally
window.copyToClipboard = copyToClipboard;

// -----------------------------
// Authorization Header Utility
// -----------------------------
function getAuthHeaders(contentType = 'application/json') {
    const token = safeGetItem('reef_token');
    return {
        'Content-Type': contentType,
        'Authorization': `Bearer ${token}`
    };
}

/**
 * Enhanced fetch that automatically refreshes token on 401 and retries once
 * @param {string} url - The URL to fetch
 * @param {object} options - Fetch options (will add auth headers if not present)
 * @returns {Promise<Response>} The fetch response
 */
async function authenticatedFetch(url, options = {}) {
    // Add auth headers if not already present
    if (!options.headers) {
        options.headers = getAuthHeaders();
    } else if (!options.headers.Authorization && !options.headers.authorization) {
        options.headers.Authorization = `Bearer ${safeGetItem('reef_token')}`;
    }

    let response = await fetch(url, options);

    // If we get a 401, token is completely invalid (expired, revoked, or from previous session)
    // Don't attempt to refresh - just redirect to login
    if (response.status === 401) {
        console.warn('Got 401 Unauthorized - token is invalid, redirecting to login');
        window.clearAuth();
        window.redirectToLogin();
    }

    return response;
}

// Make authenticatedFetch available globally
window.authenticatedFetch = authenticatedFetch;

// -------------------------
// API Response Caching (Stale-While-Revalidate Pattern)
// -------------------------
const apiCache = new Map();

/**
 * Fetch with caching support
 * Returns cached data immediately if available, fetches fresh data in background
 * @param {string} url - The URL to fetch
 * @param {object} options - Fetch options + cache options { ttl: number in ms }
 * @returns {Promise<Response>} The fetch response
 */
window.cachedFetch = async function(url, options = {}) {
    const cacheTTL = options.ttl || (5 * 60 * 1000); // Default 5 minutes
    const cacheKey = `${url}:${JSON.stringify(options)}`;
    const cached = apiCache.get(cacheKey);
    const now = Date.now();

    // Return cached response if still valid
    if (cached && (now - cached.timestamp) < cacheTTL) {
        return new Response(JSON.stringify(cached.data), {
            status: 200,
            statusText: 'OK (from cache)',
            headers: new Headers({
                'Content-Type': 'application/json',
                'X-Cache': 'HIT'
            })
        });
    }

    // Fetch fresh data in background
    try {
        const response = await authenticatedFetch(url, options);
        if (response.ok) {
            const data = await response.clone().json();
            apiCache.set(cacheKey, { data, timestamp: now });
            return response;
        }
        return response;
    } catch (error) {
        // If fetch fails and we have stale cache, return it
        if (cached) {
            console.warn(`Fetch failed for ${url}, returning stale cache:`, error);
            return new Response(JSON.stringify(cached.data), {
                status: 200,
                statusText: 'OK (stale cache)',
                headers: new Headers({
                    'Content-Type': 'application/json',
                    'X-Cache': 'STALE'
                })
            });
        }
        throw error;
    }
};

/**
 * Clear API cache (useful after mutations)
 * @param {string} pattern - Optional regex pattern to match URLs
 */
window.clearApiCache = function(pattern) {
    if (!pattern) {
        apiCache.clear();
        return;
    }
    const regex = new RegExp(pattern);
    for (const [key] of apiCache) {
        if (regex.test(key)) {
            apiCache.delete(key);
        }
    }
};

// -----------------------------
// Sidebar / Wiggle 
// -----------------------------
function triggerWiggle() {
    const element = document.getElementById('beta-tag');
    
    // Remove the class if it currently exists to reset the animation timer
    element.classList.remove('animate-wiggle');
    
    // Trigger a reflow to ensure the browser recognizes the removal before re-adding
    void element.offsetWidth;
    
    // Apply the animation class
    element.classList.add('animate-wiggle');
    
    // Optional: Remove the class after the duration (300ms) to clean up the DOM state
    element.addEventListener('animationend', () => {
        element.classList.remove('animate-wiggle');
    }, { once: true });
}

// -----------------------------
// Sidebar / User Info
// -----------------------------
function setUserSidebarInfo() {
    const username = safeGetItem('reef_username') || ' ';
    const displayName = safeGetItem('reef_display_name');
    const usernameDisplay = document.getElementById('username-display');
    const userInitial = document.getElementById('user-initial');
    
    // Use display name if available, otherwise use username
    const nameToDisplay = displayName || username;
    
    if (usernameDisplay) usernameDisplay.textContent = nameToDisplay.charAt(0).toUpperCase() + nameToDisplay.slice(1);
    if (userInitial) userInitial.textContent = nameToDisplay[0].toUpperCase();
}

// -----------------------------
// Toast Notification System
// -----------------------------
window.showToast = function(message, type = 'info', persistent = false) {
    let toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toast-container';
        toastContainer.className = 'fixed top-4 right-4 z-[9999] flex flex-col items-end space-y-2';
        document.body.appendChild(toastContainer);
    }

    const timestamp = new Date();
    const timeStr = timestamp.toLocaleTimeString();

    const toast = document.createElement('div');
    toast.className = [
        'flex items-center w-fit max-w-[90vw] sm:max-w-sm px-4 py-3 mb-2 rounded shadow text-white',
        'cursor-pointer select-none',
        type === 'success' ? 'bg-green-600' : type === 'error' ? 'bg-red-600' : type === 'warning' ? 'bg-yellow-600' : 'bg-gray-800',
        'opacity-0 transition-opacity duration-300'
    ].join(' ');
    toast.title = 'Click to copy · ' + timeStr;

    toast.innerHTML = `
        <span class="flex-1 pr-2">${message}</span>
        <div class="flex items-center ml-2 gap-2 shrink-0">
            <span class="text-xs opacity-60 tabular-nums">${timeStr}</span>
            <button class="text-white opacity-60 hover:opacity-100 transition-opacity text-lg leading-none" data-dismiss aria-label="Close">×</button>
        </div>`;

    // Click anywhere except dismiss button → copy message + timestamp
    toast.addEventListener('click', function(e) {
        if (e.target.closest('[data-dismiss]')) return;
        const text = '[' + timestamp.toLocaleString() + '] ' + message;
        copyToClipboard(text).then(ok => {
            if (!ok) return;
            // Brief ring feedback
            toast.classList.add('ring-2', 'ring-white', 'ring-inset', 'ring-opacity-50');
            setTimeout(() => toast.classList.remove('ring-2', 'ring-white', 'ring-inset', 'ring-opacity-50'), 500);
        });
    });

    // Dismiss button
    toast.querySelector('[data-dismiss]').addEventListener('click', function(e) {
        e.stopPropagation();
        dismissToast(toast);
    });

    toastContainer.appendChild(toast);

    // Fade in
    requestAnimationFrame(() => {
        toast.classList.remove('opacity-0');
        toast.classList.add('opacity-100');
    });

    // Auto-remove after duration
    const durationMap = { success: 3000, info: 4000, warning: 6000, error: 10000 };
    const duration = durationMap[type] ?? 4000;
    let autoTimer = persistent ? null : setTimeout(() => dismissToast(toast), duration);

    // Pause auto-dismiss while the user hovers
    toast.addEventListener('mouseenter', () => { if (autoTimer) { clearTimeout(autoTimer); autoTimer = null; } });
    toast.addEventListener('mouseleave', () => {
        if (!persistent && !autoTimer) autoTimer = setTimeout(() => dismissToast(toast), 2000);
    });
};

/**
 * Copy error message to clipboard and show confirmation
 * @param {HTMLElement} button - The copy button element
 * @param {string} message - The error message to copy
 */
window.copyErrorMessage = async function(button, message) {
    const success = await copyToClipboard(message);

    if (success) {
        // Store original content
        const originalSVG = button.innerHTML;
        const originalTitle = button.title;

        // Show confirmation
        button.title = 'Copied!';
        button.innerHTML = '<svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path></svg>';
        button.classList.add('opacity-100');

        // Restore after 2 seconds
        setTimeout(() => {
            button.innerHTML = originalSVG;
            button.title = originalTitle;
        }, 2000);
    }
};

/**
 * Escape special characters for use in HTML
 * @param {string} text - The text to escape
 * @returns {string} - The escaped string
 */
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Make escapeHtml available globally
window.escapeHtml = escapeHtml;

/**
 * Escape special characters for use in HTML onclick attributes
 * @param {string} str - The string to escape
 * @returns {string} - The escaped string
 */
function escapeForJS(str) {
    if (!str) return '';
    return str.replace(/\\/g, '\\\\')
              .replace(/'/g, "\\'")
              .replace(/"/g, '\\"')
              .replace(/\n/g, '\\n')
              .replace(/\r/g, '\\r');
}

// Unified fade utilities
function fadeIn(el) {
    el.classList.remove('opacity-0');
    el.classList.add('opacity-100', 'transition', 'duration-300');
}

function dismissToast(el) {
    if (!el || !el.parentNode) return;
    el.classList.remove('opacity-100');
    el.classList.add('opacity-0');
    setTimeout(() => { if (el.parentNode) el.remove(); }, 300);
}

function fadeOut(el) {
    el.classList.remove('opacity-100');
    el.classList.add('opacity-0', 'transition', 'duration-300');
    setTimeout(() => el.remove(), 300);
}

// -------------------------
// Lucide Icon Render
// -------------------------

/**
 * Render Lucide icons synchronously so they are present before first paint.
 * Calling this from an inline <script> block during body parsing means the
 * browser hasn't painted yet — icons appear on the very first frame with no
 * pop-in flash. Safe to call multiple times; already-converted icons are skipped.
 */
window.queueLucideRender = function() {
    if (typeof lucide !== 'undefined' && lucide.createIcons) {
        try {
            lucide.createIcons();
        } catch (e) {
            console.warn('Lucide render failed:', e);
        }
    }
}

// -----------------------------
// Modals
// -----------------------------
function openHelpModal() {
    const modal = document.getElementById('help-modal');
    if (!modal) return;
    modal.classList.remove('hidden');
    fadeIn(modal);
    queueLucideRender();
}

function closeHelpModal() {
    const modal = document.getElementById('help-modal');
    if (!modal) return;
    fadeOut(modal);
    setTimeout(() => modal.classList.add('hidden'), 300);
}

function closeModalOnClickOutside(event, modalId, closeFunction) {
    const modal = document.getElementById(modalId);
    if (event.target === modal) closeFunction();
}

document.addEventListener('DOMContentLoaded', () => {
    const helpModal = document.getElementById('help-modal');
    if (helpModal) helpModal.addEventListener('click', (e) => closeModalOnClickOutside(e, 'help-modal', closeHelpModal));

    const groupModal = document.getElementById('group-modal');
    if (groupModal && typeof closeGroupModal === 'function') {
        groupModal.addEventListener('click', (e) => closeModalOnClickOutside(e, 'group-modal', closeGroupModal));
    }
});

// -----------------------------
// Tooltip System
// -----------------------------
let tooltipEl;
let currentTooltipTarget = null;
let tooltipRAF = null;
let tooltipHideScheduled = false;

function showTooltip(el) {
    const text = el.getAttribute('data-tooltip');
    if (!text) return;

    // Cancel any pending hide
    tooltipHideScheduled = false;

    if (!tooltipEl) {
        tooltipEl = document.createElement('div');
        tooltipEl.className = `
            fixed z-[9999] bg-gray-800 text-white text-sm px-2 py-1 rounded-lg shadow-lg
            whitespace-pre-line max-w-xs break-words pointer-events-none
            transition-opacity duration-150 opacity-0
        `;
        document.body.appendChild(tooltipEl);
    }

    tooltipEl.innerHTML = text.split('\\n').join('<br>');
    tooltipEl.style.display = 'block';
    tooltipEl.style.opacity = '0';
    currentTooltipTarget = el;
    positionTooltip(el);

    requestAnimationFrame(() => {
        tooltipEl.style.opacity = '1';
    });
}

function hideTooltip() {
    if (tooltipEl && !tooltipHideScheduled) {
        tooltipHideScheduled = true;
        tooltipEl.style.opacity = '0';
        setTimeout(() => {
            if (tooltipEl) tooltipEl.style.display = 'none';
            currentTooltipTarget = null;
            tooltipHideScheduled = false;
        }, 150);
    }
}

function positionTooltip(el) {
    if (!tooltipEl) return;
    const rect = el.getBoundingClientRect();
    const offset = 8;
    let top = rect.top - tooltipEl.offsetHeight - offset;
    let left = rect.left + rect.width / 2 - tooltipEl.offsetWidth / 2;
    top = Math.max(4, top);
    left = Math.max(4, Math.min(left, window.innerWidth - tooltipEl.offsetWidth - 4));
    tooltipEl.style.top = `${top}px`;
    tooltipEl.style.left = `${left}px`;
}

document.addEventListener('mouseover', (e) => {
    const el = e.target.closest('[data-tooltip]');
    if (el) showTooltip(el);
});

document.addEventListener('mouseout', (e) => {
    const el = e.target.closest('[data-tooltip]');
    if (!el) return;
    const related = e.relatedTarget;
    if (related && el.contains(related)) return;
    // Only hide tooltip if we're not hovering over the tooltip itself
    if (tooltipEl && related && tooltipEl.contains(related)) return;
    // Don't hide if the related target is a sibling or within the same group
    if (related && el.parentElement) {
        const parentGroup = el.closest('.relative.group');
        if (parentGroup && parentGroup.contains(related)) return;
    }
    hideTooltip();
});

document.addEventListener('mousemove', (e) => {
    if (tooltipEl && tooltipEl.style.display === 'block' && currentTooltipTarget) {
        if (tooltipRAF) cancelAnimationFrame(tooltipRAF);
        tooltipRAF = requestAnimationFrame(() => positionTooltip(currentTooltipTarget));
    }
});

document.addEventListener('click', (e) => {
    if (!currentTooltipTarget) return;
    if (currentTooltipTarget.contains(e.target)) {
        hideTooltip();
        return;
    }
    hideTooltip();
});

// -----------------------------
// UX Enhancements
// -----------------------------
function enhanceInteractions() {
    const selectors = 'button, a, .card, .menu-item, .interactive';
    document.querySelectorAll(selectors).forEach(el => {
        el.classList.add(
            'transition',
            'duration-200',
            'ease-in-out',
            'active:scale-95'
        );
    });
}

function enhanceTooltips() {
    if (!tooltipEl) return;
    tooltipEl.classList.add(
        'rounded-lg',
        'shadow-lg',
        'pointer-events-none',
        'max-w-xs',
        'break-words'
    );
}

function responsiveGrids() {
    const lists = document.querySelectorAll('.connections-list, .cards-list');
    lists.forEach(el => {
        if (window.innerWidth >= 768) {
            el.classList.add('grid', 'grid-cols-2', 'gap-4');
        } else {
            el.classList.remove('grid', 'grid-cols-2', 'gap-4');
        }
    });
}

let resizeTimeout;
window.addEventListener('resize', () => {
    clearTimeout(resizeTimeout);
    resizeTimeout = setTimeout(responsiveGrids, 100);
});

// Skeleton Loader
function showSkeleton(selector, rows = 3) {
    const container = document.querySelector(selector);
    if (!container) return;
    container.innerHTML = '';
    for (let i = 0; i < rows; i++) {
        const skeleton = document.createElement('div');
        skeleton.className = 'animate-pulse bg-gray-300 h-4 mb-2 rounded w-full';
        container.appendChild(skeleton);
    }
}

// Expose UX helpers for the SPA router to call after content swaps
window.enhanceInteractions = enhanceInteractions;
window.enhanceTooltips = enhanceTooltips;
window.responsiveGrids = responsiveGrids;

// -----------------------------
// Run UX Enhancements
// -----------------------------
document.addEventListener('DOMContentLoaded', () => {
    enhanceInteractions();
    enhanceTooltips();
    responsiveGrids();

    // Update approval badge if not on login page
    const menuLink = document.querySelector('a[href="/email-approvals"]');
    if (menuLink) {
        updateApprovalBadge();
    }
});

// -----------------------------
// Remember Me Cookie Functionality
// -----------------------------
function setCookie(name, value, days = 30) {
    const date = new Date();
    date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
    const expires = "expires=" + date.toUTCString();
    document.cookie = name + "=" + encodeURIComponent(value) + ";" + expires + ";path=/";
}

function getCookie(name) {
    const nameEQ = name + "=";
    const cookies = document.cookie.split(';');
    for (let cookie of cookies) {
        cookie = cookie.trim();
        if (cookie.indexOf(nameEQ) === 0) {
            return decodeURIComponent(cookie.substring(nameEQ.length));
        }
    }
    return null;
}

function deleteCookie(name) {
    setCookie(name, "", -1);
}

// -----------------------------
// Approval Badge Update
// -----------------------------
/**
 * Adds or updates a notification badge on the Approvals menu item.
 * @param {number} count - The number of items to display.
 */
async function updateApprovalBadge(count) {
    // If count is not provided, fetch it from the API
    if (count === undefined) {
        try {
            const response = await authenticatedFetch('/api/email-approvals/count');
            if (response.ok) {
                const data = await response.json();
                count = data.count || 0;
            } else {
                console.warn('Failed to fetch approval count');
                return;
            }
        } catch (err) {
            console.warn('Error fetching approval count:', err);
            return;
        }
    }

    // 1. Select the specific menu item by its href
    const menuLink = document.querySelector('a[href="/email-approvals"]');
    
    if (!menuLink) return;

    // 2. Check if a badge already exists to update it, or create a new one
    let badge = menuLink.querySelector('.approval-count-badge');

    if (count > 0) {
        if (!badge) {
            badge = document.createElement('span');
            // Tailwind classes: 
            // ml-auto (pushes to right), bg-red-500 (red background), rounded-full (pill shape)
            badge.className = 'approval-count-badge ml-auto bg-red-500 text-white text-xs font-bold px-2 py-0.5 rounded-full';
            menuLink.appendChild(badge);
        }
        // Display infinity symbol for 99+, otherwise show the count
        badge.textContent = count >= 99 ? '∞' : count;
    } else {
        // 3. Remove the badge if the count is 0
        if (badge) badge.remove();
    }
}

// Make updateApprovalBadge available globally
window.updateApprovalBadge = updateApprovalBadge;

// Initialize remember me functionality on page load
document.addEventListener('DOMContentLoaded', function() {
    const rememberMeCheckbox = document.getElementById('remember-me');
    const usernameInput = document.getElementById('username');

    if (!rememberMeCheckbox || !usernameInput) return;

    // Restore saved username if it exists
    const savedUsername = getCookie('reef_remember_username');
    if (savedUsername) {
        usernameInput.value = savedUsername;
        rememberMeCheckbox.checked = true;
    }

    // Listen for checkbox changes
    rememberMeCheckbox.addEventListener('change', function() {
        if (this.checked) {
            const currentUsername = usernameInput.value.trim();
            if (currentUsername) {
                setCookie('reef_remember_username', currentUsername, 30);
            }
        } else {
            deleteCookie('reef_remember_username');
        }
    });

    // Also update cookie when username input changes (if checkbox is checked)
    usernameInput.addEventListener('input', function() {
        if (rememberMeCheckbox.checked) {
            const currentUsername = this.value.trim();
            if (currentUsername) {
                setCookie('reef_remember_username', currentUsername, 30);
            }
        }
    });
});

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