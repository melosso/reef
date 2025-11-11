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

    // If we get a 401, try refreshing the token and retry once
    if (response.status === 401) {
        console.log('Got 401, attempting token refresh...');
        const newToken = await window.refreshToken();
        
        if (newToken) {
            // Update the Authorization header with the new token
            if (typeof options.headers.Authorization !== 'undefined') {
                options.headers.Authorization = `Bearer ${newToken}`;
            } else if (typeof options.headers.authorization !== 'undefined') {
                options.headers.authorization = `Bearer ${newToken}`;
            }
            
            // Retry the request with the new token
            console.log('Retrying request with refreshed token');
            response = await fetch(url, options);
        } else {
            console.warn('Token refresh failed, redirecting to login');
            window.redirectToLogin();
        }
    }

    return response;
}

// Make authenticatedFetch available globally
window.authenticatedFetch = authenticatedFetch;

// -----------------------------
// Sidebar / User Info
// -----------------------------
function setUserSidebarInfo() {
    const username = safeGetItem('reef_username') || ' ';
    const usernameDisplay = document.getElementById('username-display');
    const userInitial = document.getElementById('user-initial');
    if (usernameDisplay) usernameDisplay.textContent = username.charAt(0).toUpperCase() + username.slice(1);
    if (userInitial) userInitial.textContent = username[0].toUpperCase();
}

// -----------------------------
// Toast Notification System
// -----------------------------
window.showToast = function(message, type = 'info', persistent = false) {
    let toastContainer = document.getElementById('toast-container');
    if (!toastContainer) {
        toastContainer = document.createElement('div');
        toastContainer.id = 'toast-container';
        toastContainer.className = 'fixed top-4 right-4 z-50 flex flex-col items-end space-y-2';
        document.body.appendChild(toastContainer);
    }

    const toast = document.createElement('div');
    toast.className = `
        flex items-center w-fit max-w-[90vw] sm:max-w-sm px-4 py-3 mb-2 rounded shadow text-white
        ${type === 'success' ? 'bg-green-600' : type === 'error' ? 'bg-red-600' : 'bg-gray-800'}
        opacity-0 transition-opacity duration-300
    `;
    toast.innerHTML = `<span class="flex-1">${message}</span>
                       <button class="ml-4 text-white opacity-70 hover:opacity-100" 
                               onclick="fadeOut(this.parentElement)">&times;</button>`;
    toastContainer.appendChild(toast);

    // Fade in
    requestAnimationFrame(() => {
        toast.classList.remove('opacity-0');
        toast.classList.add('opacity-100');
    });

    // Auto-remove after duration
    const duration = type === 'success' ? 20000 : 8000; // errors auto-hide after 8s
    if (!persistent) {
        setTimeout(() => fadeOut(toast), duration);
    }
};

// Unified fade utilities
function fadeIn(el) {
    el.classList.remove('opacity-0');
    el.classList.add('opacity-100', 'transition', 'duration-300');
}

function fadeOut(el) {
    el.classList.remove('opacity-100');
    el.classList.add('opacity-0', 'transition', 'duration-300');
    setTimeout(() => el.remove(), 300);
}

// -----------------------------
// Modals
// -----------------------------
function openHelpModal() {
    const modal = document.getElementById('help-modal');
    if (!modal) return;
    modal.classList.remove('hidden');
    fadeIn(modal);
    try { if (typeof lucide !== 'undefined' && lucide.createIcons) lucide.createIcons(); } catch {}
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

function showTooltip(el) {
    const text = el.getAttribute('data-tooltip');
    if (!text) return;

    if (!tooltipEl) {
        tooltipEl = document.createElement('div');
        tooltipEl.className = `
            fixed z-50 bg-gray-800 text-white text-sm px-2 py-1 rounded-lg shadow-lg
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
    if (tooltipEl) {
        tooltipEl.style.opacity = '0';
        setTimeout(() => {
            if (tooltipEl) tooltipEl.style.display = 'none';
            currentTooltipTarget = null;
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

// -----------------------------
// Run UX Enhancements
// -----------------------------
document.addEventListener('DOMContentLoaded', () => {
    enhanceInteractions();
    enhanceTooltips();
    responsiveGrids();
});
