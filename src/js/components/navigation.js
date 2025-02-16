// src/js/components/navigation.js

class Navigation {
    constructor() {
        // Elements
        this.nav = document.querySelector('.nav');
        this.toggle = document.querySelector('.nav__toggle');
        this.menu = document.querySelector('.nav__menu');
        this.authLink = document.querySelector('#auth-link');
        
        // Bind all methods
        this.bindMethods();
        console.log('navigation constructor binded all methods' );

        // Initialize
        this.init();
        console.log('navigation constructor complete' );
    }

    init() {
        // Add event listeners
        this.toggle.addEventListener('click', this.toggleMenu);
        document.addEventListener('click', this.handleClickOutside);
        window.addEventListener('resize', this.closeMenu);

        // Set initial auth state
        this.handleAuthState();

        // Add click handler for auth link
        this.authLink.addEventListener('click', async (e) => {
            console.log('navigation isLoggedIn:', window.navigationInstance.isLoggedIn());
            if (window.navigationInstance.isLoggedIn()) {
                e.preventDefault();
                await this.handleLogout();
            }
        });
    }

    bindMethods() {
        const methods = Object.getOwnPropertyNames(Object.getPrototypeOf(this));
        methods.forEach(method => {
            if (typeof this[method] === 'function' && method !== 'constructor') {
                this[method] = this[method].bind(this);
            }
        });
    }

    // Menu functions
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

    // state management functions
    handleAuthState() {
        console.log('navigation handleAuthState');
        const isLoggedIn = this.isLoggedIn();
        
        // Update auth link text
        this.authLink.textContent = isLoggedIn 
        ? this.authLink.dataset.logoutText
        : this.authLink.dataset.loginText;

        // Update href
        this.authLink.href = isLoggedIn ? '#' : '/login.html';
    }

    async handleLogout() {

        try {

            await AuthAPI.logout();

            this.clearUserInfo();
    
            // Update UI
            this.handleAuthState();
            
            // Redirect to home page
            window.location.href = '/';

        } catch (error) {
            console.error('Logout error:', error);
            throw error;
        }
    }

    isLoggedIn() {
        return this.getUserID() !== null & this.getUserName() !== null;
    }

    async login(email, password) {
        try {
            // First, authenticate
            const authResponse = await AuthAPI.login(email, password);

            // Then, get user info
            await AuthAPI.getUserInfo(email);

        } catch (error) {
            console.error('Login error:', error);
            throw error;
        }
    }


    // Local storage management for user info
    getUserName() {
        return localStorage.getItem(AuthAPI.USER_NAME_KEY);
    }

    getUserID() {
        try {
            return parseInt(localStorage.getItem(AuthAPI.USER_ID_KEY), 10);
        } catch (error) {
            console.error('Error getting user ID:', error);
            return null;
        }
    }

    setUserName(name) {
        localStorage.setItem(AuthAPI.USER_NAME_KEY, name);
    }

    setUserID(userID) {
        localStorage.setItem(AuthAPI.USER_ID_KEY, userID);
    }

    clearUserInfo() {
        localStorage.removeItem(AuthAPI.USER_NAME_KEY);
        localStorage.removeItem(AuthAPI.USER_ID_KEY);
    }

    static async loadNavigation() {
        try {
            const response = await fetch('/components/navigation.html');
            const html = await response.text();
            document.getElementById('navigation-placeholder').innerHTML = html;
            
            // Initialize navigation and return the instance
            window.initNavigation();
            return window.navigationInstance;
        } catch (error) {
            console.error('Error loading navigation:', error);
            throw error;
        }
    }

}

// Initialize navigation when DOM is loaded
window.initNavigation = function() {
    const navigation = new Navigation();
    // Make navigation instance available globally if needed
    window.navigationInstance = navigation;
};
