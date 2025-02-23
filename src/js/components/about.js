// js/components/about.js

class AboutPage {
    constructor() {
        
        this.initialize();
    }

    async initialize() {

        // First load navigation
        await Navigation.loadNavigation();

        // Create background elements
        const container = document.querySelector('.about-container');
        if (!container) {
            console.error('Home container not found');
            return;
        }
        
    }

    // Cleanup method
    destroy() {
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    const aboutPage = new AboutPage();
    
    // Store instance for potential cleanup
    window.aboutPage = aboutPage;
});