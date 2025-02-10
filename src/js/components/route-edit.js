// js/components/route-edit.js

class RouteEdit {
    constructor() {
        this.tabs = document.querySelectorAll('.tab-button');
        this.tabContents = document.querySelectorAll('.tab-content');
        
        // Bind methods
        this.switchTab = this.switchTab.bind(this);
        
        this.initialize();
    }

    initialize() {
        // Add click handlers to all tab buttons
        this.tabs.forEach(tab => {
            tab.addEventListener('click', (event) => {
                const tabName = event.target.dataset.tab;
                this.switchTab(tabName);
            });
        });

        // Show the first tab by default
        if (this.tabs.length > 0) {
            const firstTabName = this.tabs[0].dataset.tab;
            this.switchTab(firstTabName);
        }
    }

    switchTab(tabName) {
        try {
            // Remove active class from all tabs and contents
            this.tabs.forEach(tab => tab.classList.remove('active'));
            this.tabContents.forEach(content => content.classList.remove('active'));

            // Add active class to selected tab and content
            const selectedTab = document.querySelector(`.tab-button[data-tab="${tabName}"]`);
            const selectedContent = document.getElementById(`${tabName}-tab`);

            if (!selectedTab || !selectedContent) {
                throw new Error(`Tab ${tabName} not found`);
            }

            selectedTab.classList.add('active');
            selectedContent.classList.add('active');

        } catch (error) {
            console.error('Error switching tabs:', error);
            // Show the first tab as fallback
            if (this.tabs.length > 0 && this.tabContents.length > 0) {
                this.tabs[0].classList.add('active');
                this.tabContents[0].classList.add('active');
            }
        }
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    if (!window.authHelper.isLoggedIn()) {
        window.location.href = '/login.html';
        return;
    }
    new RouteEdit();
});