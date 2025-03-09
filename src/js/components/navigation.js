// src/js/components/navigation.js

class Navigation {
    constructor() {
        // Elements
        this.nav = document.querySelector('.nav');
        this.toggle = document.querySelector('.nav__toggle');
        this.menu = document.querySelector('.nav__menu');
        this.authLink = document.querySelector('#auth-link');
        this.dropdown = document.querySelector('.nav__dropdown');
        this.dropdownMenu = document.querySelector('#auth-dropdown');
        this.logoutLink = document.querySelector('#logout-link');
        this.authLinkText = document.querySelector('.nav__link-text');
        
        // Bind all methods
        this.bindMethods();

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
        this.authLink.removeEventListener('click', this.handleAuthLinkClick);
        
        // Add new click handlers
        this.authLink.addEventListener('click', (e) => {
            if (this.isLoggedIn()) {
                e.preventDefault();
                this.toggleDropdown();
            }
        });

        // Handle logout separately
        this.logoutLink.addEventListener('click', async (e) => {
            e.preventDefault();
            await this.handleLogout();
        });

        document.addEventListener('click', this.handleClickOutsideDropdown);
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


    handleClickOutside(event) {
        // Close menu if clicking outside when it's open
        if (this.nav.classList.contains('nav--open') && 
            !this.menu.contains(event.target) && 
            !this.toggle.contains(event.target)) {
            this.closeMenu();
        }
    }

    handleAuthLinkClick(e) {
        if (this.isLoggedIn()) {
            e.preventDefault();
            this.toggleDropdown();
        }
    }

    toggleDropdown() {
        const isOpen = this.dropdownMenu.classList.toggle('active');
        this.authLink.setAttribute('aria-expanded', isOpen);
    }

    closeDropdown() {
        this.dropdownMenu.classList.remove('active');
        this.authLink.setAttribute('aria-expanded', false);
    }

    handleClickOutsideDropdown(event) {
        if (this.isLoggedIn() && 
            !this.dropdown.contains(event.target)) {
            this.closeDropdown();
        }
    }

    handleAuthState() {
        const isLoggedIn = this.isLoggedIn();
        
        if (isLoggedIn) {
            const userName = this.getUserName();
            this.authLinkText.textContent = userName;
            this.authLink.setAttribute('aria-expanded', false);
            this.authLink.setAttribute('aria-haspopup', true);
            this.dropdownMenu.style.display = '';
        } else {
            this.authLinkText.textContent = this.authLink.dataset.loginText;
            this.authLink.removeAttribute('aria-expanded');
            this.authLink.removeAttribute('aria-haspopup');
            this.dropdownMenu.style.display = 'none';
        }

        // Update href
        this.authLink.href = isLoggedIn ? '#' : '/login.html';
    }

    closeMenu() {
        this.nav.classList.remove('nav--open');
        this.toggle.setAttribute('aria-expanded', false);
        document.body.style.overflow = '';
        this.closeDropdown(); // Also close dropdown when closing menu
    }

   
    async handleLogout() {
        try {
            await AuthAPI.logout();
            this.clearUserInfo();
            this.closeDropdown();
            this.handleAuthState();
            //window.location.href = '/';
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
