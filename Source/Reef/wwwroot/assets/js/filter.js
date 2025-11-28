/**
 * Modern Search and Filter Module for Reef
 * Provides reusable search, filtering, and localStorage persistence
 */

class ReefFilter {
    constructor(options = {}) {
        this.pageKey = options.pageKey || 'reef_filter_default';
        this.allItems = [];
        this.filteredItems = [];

        // Load saved preferences
        this.preferences = this.loadPreferences();

        // Initialize with defaults if not present
        if (!this.preferences.searchTerm) this.preferences.searchTerm = '';
        if (!this.preferences.filterStatus) this.preferences.filterStatus = 'All';
        if (!this.preferences.selectedTags) this.preferences.selectedTags = [];
    }

    /**
     * Load filter preferences from localStorage
     */
    loadPreferences() {
        const key = `${this.pageKey}_prefs`;
        const stored = localStorage.getItem(key);
        if (stored) {
            try {
                return JSON.parse(stored);
            } catch (e) {
                console.warn('Failed to parse stored preferences:', e);
            }
        }
        return {};
    }

    /**
     * Save filter preferences to localStorage
     */
    savePreferences() {
        const key = `${this.pageKey}_prefs`;
        localStorage.setItem(key, JSON.stringify(this.preferences));
    }

    /**
     * Update search term
     */
    setSearchTerm(term) {
        this.preferences.searchTerm = term;
        this.savePreferences();
        return this.applyFilters();
    }

    /**
     * Update status filter (All/Enabled/Disabled)
     */
    setStatusFilter(status) {
        this.preferences.filterStatus = status || 'All';
        this.savePreferences();
        return this.applyFilters();
    }

    /**
     * Toggle a tag in the selected tags
     */
    toggleTag(tag) {
        if (!this.preferences.selectedTags) {
            this.preferences.selectedTags = [];
        }

        const index = this.preferences.selectedTags.indexOf(tag);
        if (index > -1) {
            this.preferences.selectedTags.splice(index, 1);
        } else {
            this.preferences.selectedTags.push(tag);
        }

        this.savePreferences();
        return this.applyFilters();
    }

    /**
     * Clear all tag filters
     */
    clearTagFilters() {
        this.preferences.selectedTags = [];
        this.savePreferences();
        return this.applyFilters();
    }

    /**
     * Check if a tag is selected
     */
    isTagSelected(tag) {
        return this.preferences.selectedTags && this.preferences.selectedTags.includes(tag);
    }

    /**
     * Set items to filter
     */
    setItems(items) {
        this.allItems = items || [];
        return this.applyFilters();
    }

    /**
     * Apply all active filters
     */
    applyFilters() {
        let filtered = [...this.allItems];

        // Apply search filter
        if (this.preferences.searchTerm) {
            const search = this.preferences.searchTerm.toLowerCase();
            filtered = filtered.filter(item => {
                // Search in name, type, description, tags
                const name = (item.name || '').toLowerCase();
                const type = (item.type || '').toLowerCase();
                const description = (item.description || '').toLowerCase();
                const tags = (item.tags || '').toLowerCase();

                return name.includes(search) ||
                       type.includes(search) ||
                       description.includes(search) ||
                       tags.includes(search);
            });
        }

        // Apply status filter
        if (this.preferences.filterStatus && this.preferences.filterStatus !== 'All') {
            const isActive = this.preferences.filterStatus === 'Enabled';
            filtered = filtered.filter(item => item.isActive === isActive);
        }

        // Apply tag filters (AND logic - item must have ALL selected tags)
        if (this.preferences.selectedTags && this.preferences.selectedTags.length > 0) {
            filtered = filtered.filter(item => {
                if (!item.tags) return false;
                const itemTags = item.tags.split(',').map(t => t.trim().toLowerCase());
                return this.preferences.selectedTags.every(tag =>
                    itemTags.includes(tag.toLowerCase())
                );
            });
        }

        this.filteredItems = filtered;
        return this.filteredItems;
    }

    /**
     * Get all unique tags from items
     */
    getAllTags() {
        const tagSet = new Set();
        this.allItems.forEach(item => {
            if (item.tags) {
                item.tags.split(',').forEach(tag => {
                    tagSet.add(tag.trim());
                });
            }
        });
        return Array.from(tagSet).sort();
    }

    /**
     * Get filter statistics
     */
    getStats() {
        return {
            totalItems: this.allItems.length,
            filteredItems: this.filteredItems.length,
            activeFilters: {
                search: this.preferences.searchTerm,
                status: this.preferences.filterStatus,
                tags: this.preferences.selectedTags
            }
        };
    }

    /**
     * Clear all filters
     */
    clearAllFilters() {
        this.preferences.searchTerm = '';
        this.preferences.filterStatus = 'All';
        this.preferences.selectedTags = [];
        this.savePreferences();
        return this.applyFilters();
    }

    /**
     * Make tags clickable in the rendered items
     * Call this after rendering your items to the DOM
     */
    setupTagClickHandlers(selector = '[data-tag-filter]') {
        document.addEventListener('click', (e) => {
            const tagElement = e.target.closest(selector);
            if (tagElement) {
                e.preventDefault();
                e.stopPropagation();
                const tag = tagElement.getAttribute('data-tag-filter');
                if (tag) {
                    this.toggleTag(tag);
                    // Trigger a custom event that the page can listen to
                    window.dispatchEvent(new CustomEvent('filterChange', { detail: this }));
                }
            }
        });
    }
}

/**
 * Helper function to render filter UI
 */
function renderFilterBar(filter, options = {}) {
    const containerId = options.containerId || 'filter-container';
    const container = document.getElementById(containerId);

    if (!container) return;

    const allTags = filter.getAllTags();
    const stats = filter.getStats();

    const html = `
        <div class="bg-white rounded-lg shadow mb-6">
            <div class="p-4">
                <!-- Search Box -->
                <div class="mb-4">
                    <div class="relative">
                        <i data-lucide="search" class="absolute left-3 top-3 h-4 w-4 text-gray-400"></i>
                        <input type="text"
                               id="search-input"
                               class="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
                               placeholder="Search by name, type, description, or tags..."
                               value="${escapeHtml(filter.preferences.searchTerm)}">
                    </div>
                </div>

                <!-- Status Filter & Tags Summary -->
                <div class="flex items-center justify-between flex-wrap gap-2 mb-4">
                    <!-- Status Filter -->
                    <div class="flex items-center gap-2">
                        <span class="text-sm text-gray-600 font-medium">Status:</span>
                        <div class="flex gap-1">
                            ${['All', 'Enabled', 'Disabled'].map(status => `
                                <button class="status-filter-btn px-3 py-1 text-xs font-medium rounded-full transition ${
                                    filter.preferences.filterStatus === status
                                        ? 'bg-blue-600 text-white'
                                        : 'bg-gray-200 text-gray-700 hover:bg-gray-300'
                                }"
                                        data-status="${status}"
                                        onclick="window.reefFilter.setStatusFilter('${status}'); renderFilterBar(window.reefFilter, { containerId: '${containerId}' }); window.dispatchEvent(new CustomEvent('filterChange', { detail: window.reefFilter }));">
                                    ${status}
                                </button>
                            `).join('')}
                        </div>
                    </div>

                    <!-- Clear Filters Button (if any active) -->
                    ${(filter.preferences.searchTerm || filter.preferences.filterStatus !== 'All' || filter.preferences.selectedTags.length > 0) ? `
                        <button class="text-sm text-blue-600 hover:text-blue-700 font-medium"
                                onclick="window.reefFilter.clearAllFilters(); renderFilterBar(window.reefFilter, { containerId: '${containerId}' }); window.dispatchEvent(new CustomEvent('filterChange', { detail: window.reefFilter }));">
                            Clear All
                        </button>
                    ` : ''}
                </div>

                <!-- Tags Filter -->
                ${allTags.length > 0 ? `
                    <div>
                        <div class="text-sm text-gray-600 font-medium mb-2">Filter by tags:</div>
                        <div class="flex flex-wrap gap-2">
                            ${allTags.map(tag => `
                                <button class="tag-filter-btn inline-flex items-center px-3 py-1 rounded-full text-xs font-medium transition cursor-pointer ${
                                    filter.isTagSelected(tag)
                                        ? 'bg-blue-600 text-white'
                                        : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
                                }"
                                        data-tag="${escapeHtml(tag)}"
                                        onclick="window.reefFilter.toggleTag('${escapeHtml(tag)}'); renderFilterBar(window.reefFilter, { containerId: '${containerId}' }); window.dispatchEvent(new CustomEvent('filterChange', { detail: window.reefFilter }));">
                                    ${escapeHtml(tag)}
                                </button>
                            `).join('')}
                        </div>
                    </div>
                ` : ''}

                <!-- Results Info -->
                <div class="text-xs text-gray-500 mt-3">
                    Showing ${stats.filteredItems} of ${stats.totalItems} items
                </div>
            </div>
        </div>
    `;

    container.innerHTML = html;

    // Queue lucide icon render (batched to prevent layout thrashing)
    queueLucideRender();

    // Setup search input event
    const searchInput = document.getElementById('search-input');
    if (searchInput) {
        searchInput.addEventListener('input', (e) => {
            filter.setSearchTerm(e.target.value);
            // Don't re-render the filter bar on input - just update the filter and trigger the event
            // The page-specific event handler will re-render the data
            window.dispatchEvent(new CustomEvent('filterChange', { detail: filter }));
        });
    }
}

/**
 * Helper to escape HTML in strings
 */
function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}
