class AboutPage {
    constructor() {
        this.initialize();
        this.setupScrollAnimation();
    }

    async initialize() {
        await Navigation.loadNavigation();
        
        const container = document.querySelector('.about-container');
        if (!container) {
            console.error('About container not found');
            return;
        }
    }

    setupScrollAnimation() {
        const sections = document.querySelectorAll('.story-section');
        
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.style.animation = `fadeInUp 0.6s ease forwards ${entry.target.dataset.delay || '0s'}`;
                }
            });
        }, {
            threshold: 0.2,
            rootMargin: '0px 0px -100px 0px'
        });

        sections.forEach((section, index) => {
            section.dataset.delay = `${index * 0.2}s`;
            observer.observe(section);
        });
    }

    destroy() {
        // Cleanup if needed
    }
}

document.addEventListener('DOMContentLoaded', () => {
    const aboutPage = new AboutPage();
    window.aboutPage = aboutPage;
});