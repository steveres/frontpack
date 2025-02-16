// src/js/components/login.js

class LoginForm {
    constructor() {
        this.form = document.getElementById('login-form');
        this.errorMessage = document.getElementById('error-message');
        this.submitButton = this.form.querySelector('button[type="submit"]');
        
        this.init();
    }

    async init() {

        // First load navigation
        await Navigation.loadNavigation();

        this.form.addEventListener('submit', this.handleSubmit.bind(this));
    }

    showError(message) {
        this.errorMessage.textContent = message;
        this.errorMessage.classList.add('visible');
    }

    hideError() {
        this.errorMessage.textContent = '';
        this.errorMessage.classList.remove('visible');
    }

    setLoading(isLoading) {
        if (isLoading) {
            this.submitButton.disabled = true;
            this.submitButton.classList.add('loading');
        } else {
            this.submitButton.disabled = false;
            this.submitButton.classList.remove('loading');
        }
    }

    async handleSubmit(event) {
        event.preventDefault();
        this.hideError();

        const email = this.form.email.value.trim();
        const password = this.form.password.value;

        // Basic validation
        if (!email || !password) {
            this.showError('Please fill in all fields');
            return;
        }

        // Email format validation
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            this.showError('Please enter a valid email address');
            return;
        }

        try {
            this.setLoading(true);
            
            if (!window.navigationInstance) {
                throw new Error('Authentication service not available');
            }

            await window.navigationInstance.login(email, password);
            
            // Redirect to routes page on success
            window.location.href = '/routes.html';

        } catch (error) {
            this.showError(error.message || 'Login failed. Please try again.');
            this.setLoading(false);
        }
    }
}

// Initialize form when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new LoginForm();
});
