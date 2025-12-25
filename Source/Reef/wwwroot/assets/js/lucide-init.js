/**
 * Lucide Icon Initialization for Blazor Components
 * This script initializes Lucide icons after Blazor renders components
 */

window.initLucide = () => {
    if (typeof lucide !== 'undefined') {
        // Double requestAnimationFrame ensures DOM is fully stable
        requestAnimationFrame(() => {
            requestAnimationFrame(() => {
                try {
                    lucide.createIcons();
                } catch (e) {
                    // Silently ignore all errors during Blazor navigation
                    // DOM manipulation conflicts are expected and harmless
                }
            });
        });
    }
};

// Auto-initialize on DOM content loaded
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.initLucide();
    });
} else {
    // DOM already loaded
    window.initLucide();
}
