/**
 * Lucide Icon Initialization for Blazor Enhanced Navigation (2025)
 * Uses Blazor 8+ official navigation events
 */

window.initLucide = () => {
    if (typeof lucide !== 'undefined') {
        try {
            lucide.createIcons();
        } catch (e) {
            // Silently ignore - element may have been removed during navigation
        }
    }
};

// Initial page load
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.initLucide();
    });
} else {
    window.initLucide();
}

// Blazor Enhanced Navigation - reinitialize after each page update
// Wait for Blazor to be available before registering the event listener
function registerBlazorEnhancedLoad() {
    if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
        Blazor.addEventListener('enhancedload', () => {
            // Wait for DOM to stabilize after Blazor's update
            requestAnimationFrame(() => {
                window.initLucide();
            });
        });
    } else {
        // Blazor not ready yet, retry after a short delay
        setTimeout(registerBlazorEnhancedLoad, 100);
    }
}

// Start checking for Blazor after DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', registerBlazorEnhancedLoad);
} else {
    registerBlazorEnhancedLoad();
}
