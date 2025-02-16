// js/components/index.js

class HomeBackground {
    constructor() {
        this.backgrounds = [
            '/images/background1.jpg',
            '/images/background2.jpg',
            '/images/background3.jpg'
        ];
        this.currentIndex = 0;
        this.elements = [];
        this.interval = null;
        
        this.initialize();
    }

    async initialize() {

        // First load navigation
        await Navigation.loadNavigation();

        // Create background elements
        const container = document.querySelector('.home-container');
        if (!container) {
            console.error('Home container not found');
            return;
        }
        
        // Create two background elements for smooth transitions
        for (let i = 0; i < 2; i++) {
            const bg = document.createElement('div');
            bg.className = 'home-background';
            container.insertBefore(bg, container.firstChild);
            this.elements.push(bg);
        }

        // Set initial background
        this.elements[0].style.backgroundImage = `url(${this.backgrounds[0]})`;
        this.elements[0].classList.add('active');

        // Start rotation
        this.startRotation();
    }

    startRotation() {
        // Clear any existing interval
        if (this.interval) {
            clearInterval(this.interval);
        }

        // Start new rotation interval
        this.interval = setInterval(() => {
            this.rotate();
        }, 5000); // Rotate every 5 seconds
    }

    rotate() {
        const currentElement = this.elements[0];
        const nextElement = this.elements[1];
        
        // Update next background
        this.currentIndex = (this.currentIndex + 1) % this.backgrounds.length;
        nextElement.style.backgroundImage = `url(${this.backgrounds[this.currentIndex]})`;
        
        // Trigger transition
        currentElement.classList.remove('active');
        nextElement.classList.add('active');
        
        // Swap elements in array
        this.elements.reverse();
    }

    // Cleanup method
    destroy() {
        if (this.interval) {
            clearInterval(this.interval);
        }
        this.elements.forEach(el => el.remove());
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    const homeBackground = new HomeBackground();
    
    // Store instance for potential cleanup
    window.homeBackground = homeBackground;
});