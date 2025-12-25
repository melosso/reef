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
// This is the official 2025 pattern from Microsoft
Blazor.addEventListener('enhancedload', () => {
    // Wait for DOM to stabilize after Blazor's update
    requestAnimationFrame(() => {
        window.initLucide();
    });
});
