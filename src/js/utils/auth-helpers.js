// src/js/utils/auth-helpers.js

class AuthHelper {
    constructor() {
        this.API_URL = 'https://api2.thefrontpack.com';
        this.TOKEN_KEY = 'authToken';
    }

    // Check if user is logged in
    isLoggedIn() {
        return this.getToken() !== null;
    }

    // Get token from localStorage
    getToken() {
        return localStorage.getItem(this.TOKEN_KEY);
    }

    // Save token to localStorage
    setToken(token) {
        localStorage.setItem(this.TOKEN_KEY, token);
        this.notifyAuthChange();
    }

    // Remove token from localStorage
    removeToken() {
        localStorage.removeItem(this.TOKEN_KEY);
        this.notifyAuthChange();
    }

    // Notify navigation of auth state change
    notifyAuthChange() {
        if (window.navigationInstance) {
            window.navigationInstance.updateAuthState();
        }
    }

    // Handle login
    async login(email, password) {
        try {
            const response = await fetch(`${this.API_URL}/authenticate/login`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ email, password })
            });

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || 'Login failed');
            }

            const data = await response.json();
            this.setToken(data.token);
            return true;

        } catch (error) {
            console.error('Login error:', error);
            throw error;
        }
    }

    // Handle logout
    async logout() {
        try {
            // Call logout endpoint if API requires it
            // const response = await fetch(`${this.API_URL}/auth/logout`, {
            //     method: 'POST',
            //     headers: {
            //         'Authorization': `Bearer ${this.getToken()}`
            //     }
            // });

            this.removeToken();
            return true;

        } catch (error) {
            console.error('Logout error:', error);
            throw error;
        }
    }

    // Get authorization header
    getAuthHeader() {
        const token = this.getToken();
        return token ? { 'Authorization': `Bearer ${token}` } : {};
    }

    // Check if token is expired
    isTokenExpired() {
        const token = this.getToken();
        if (!token) return true;

        try {
            const payload = JSON.parse(atob(token.split('.')[1]));
            return payload.exp * 1000 < Date.now();
        } catch (error) {
            console.error('Token validation error:', error);
            return true;
        }
    }
}

// Create and export a single instance
window.authHelper = new AuthHelper(); // Make available globally
