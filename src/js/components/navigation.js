// src/js/components/navigation.js

class Navigation {
    constructor() {
        // Elements
        this.nav = document.querySelector('.nav');
        this.toggle = document.querySelector('.nav__toggle');
        this.menu = document.querySelector('.nav__menu');
        this.authLink = document.querySelector('#auth-link');
        
        // Bind methods
        this.toggleMenu = this.toggleMenu.bind(this);
        this.closeMenu = this.closeMenu.bind(this);
        this.handleAuthState = this.handleAuthState.bind(this);
        this.handleClickOutside = this.handleClickOutside.bind(this);
        this.handleLogout = this.handleLogout.bind(this);
        
        // Initialize
        this.init();
    }

    init() {
        // Add event listeners
        this.toggle.addEventListener('click', this.toggleMenu);
        document.addEventListener('click', this.handleClickOutside);
        window.addEventListener('resize', this.closeMenu);

        // Set initial auth state
        this.handleAuthState();

        // Add click handler for auth link
        this.authLink.addEventListener('click', (e) => {
            console.log('navigation isLoggedIn:', window.navHelper.isLoggedIn());
            if (window.navHelper.isLoggedIn()) {
                e.preventDefault();
                this.handleLogout();
            }
        });
    }

    toggleMenu() {
        const isOpen = this.nav.classList.toggle('nav--open');
        this.toggle.setAttribute('aria-expanded', isOpen);
        
        // Prevent body scroll when menu is open
        document.body.style.overflow = isOpen ? 'hidden' : '';
    }

    closeMenu() {
        this.nav.classList.remove('nav--open');
        this.toggle.setAttribute('aria-expanded', false);
        document.body.style.overflow = '';
    }

    handleClickOutside(event) {
        // Close menu if clicking outside when it's open
        if (this.nav.classList.contains('nav--open') && 
            !this.menu.contains(event.target) && 
            !this.toggle.contains(event.target)) {
            this.closeMenu();
        }
    }

    handleAuthState() {
        console.log('navigation handleAuthState');
        const isLoggedIn = window.navHelper.isLoggedIn();
        
        // Update auth link text
        this.authLink.textContent = isLoggedIn 
        ? this.authLink.dataset.logoutText
        : this.authLink.dataset.loginText;

        // Update href
        //this.authLink.href = isLoggedIn ? '#' : '/login.html';
    }

    handleLogout() {

        console.log('navigation handleLogout');
        // Clear storage
        window.navHelper.logout();
        
        // Update UI
        this.handleAuthState();
        console.log('navigation updated state');
        
        // Redirect to home page
        //window.location.href = '/';
    }

    // Public method to update auth state from outside
    updateAuthState() {
        this.handleAuthState();
    }
}

// Initialize navigation when DOM is loaded
window.initNavigation = function() {
    const navigation = new Navigation();
    // Make navigation instance available globally if needed
    window.navigationInstance = navigation;
};
