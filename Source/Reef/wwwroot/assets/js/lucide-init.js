/**
 * Lucide Icon Initialization for Blazor Components
 * This script initializes Lucide icons after Blazor renders components
 */

window.initLucide = () => {
    if (typeof lucide !== 'undefined') {
        lucide.createIcons();
    } else {
        console.warn('Lucide library not loaded yet. Icons will not be rendered.');
    }
};

// Auto-initialize on DOM content loaded
document.addEventListener('DOMContentLoaded', () => {
    window.initLucide();
});

// Re-initialize after Blazor enhanced navigation
document.addEventListener('enhancedload', () => {
    window.initLucide();
});
