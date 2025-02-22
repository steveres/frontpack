// src/js/components/register.js

class RegisterForm {
    constructor() {
        this.registerOptions = document.getElementById('register-options');
        this.emailSignupButton = document.getElementById('email-signup-button');
        this.form = document.getElementById('register-form');
        this.errorMessage = document.getElementById('error-message');
        this.successMessage = document.getElementById('success-message');
        this.submitButton = this.form.querySelector('button[type="submit"]');
        this.backButton = document.getElementById('back-button');
                
        this.init();
    }

    async init() {
        // First load navigation
        await Navigation.loadNavigation();

        // Add event listeners
        this.emailSignupButton.addEventListener('click', this.showEmailForm.bind(this));
        this.backButton.addEventListener('click', this.showOptionsForm.bind(this));
        this.form.addEventListener('submit', this.handleSubmit.bind(this));

        // Add event listeners for social login buttons (to be implemented)
        const googleButton = document.querySelector('.google-button');
        const appleButton = document.querySelector('.apple-button');
        
        googleButton.addEventListener('click', () => {
            this.showError('Google sign-in will be available soon');
        });
        
        appleButton.addEventListener('click', () => {
            this.showError('Apple sign-in will be available soon');
        });
    }

    showEmailForm() {
        this.registerOptions.style.display = 'none';
        this.form.classList.remove('hidden');
        this.backButton.classList.remove('hidden');
        this.hideMessages();
    }

    showOptionsForm() {
        this.registerOptions.style.display = 'flex';
        this.form.classList.add('hidden');
        this.backButton.classList.add('hidden');
        this.hideMessages();
        this.form.reset(); 
    }

    showError(message) {
        this.errorMessage.textContent = message;
        this.errorMessage.classList.add('visible');
    }

    hideError() {
        this.errorMessage.textContent = '';
        this.errorMessage.classList.remove('visible');
    }

    showSuccess(message) {
        this.successMessage.textContent = message;
        this.successMessage.classList.add('visible');
    }

    hideSuccess() {
        this.successMessage.textContent = '';
        this.successMessage.classList.remove('visible');
    }

    hideMessages() {
        this.hideError();
        this.hideSuccess();
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

    setEmailComplete(message) {
        this.showSuccess(message);
        this.submitButton.classList.remove('loading');
        this.backButton.classList.add('hidden');
    }


    validatePassword(password) {
        // Minimum 8 characters, at least one uppercase letter, one lowercase letter, and one number
        const minLength = password.length >= 8;
        const hasUpperCase = /[A-Z]/.test(password);
        const hasLowerCase = /[a-z]/.test(password);
        const hasNumber = /\d/.test(password);

        if (!minLength) {
            return 'Password must be at least 8 characters long';
        }
        if (!hasUpperCase || !hasLowerCase || !hasNumber) {
            return 'Password must contain at least one uppercase letter, one lowercase letter, and one number';
        }
        return null;
    }

    async handleSubmit(event) {
        event.preventDefault();
        this.hideError();

        const name = this.form.name.value.trim();
        const email = this.form.email.value.trim();
        const password = this.form.password.value;

        // Basic validation
        if (!name || !email || !password) {
            this.showError('Please fill in all fields');
            return;
        }

        // Name validation
        if (name.length < 2) {
            this.showError('Please enter your full name');
            return;
        }

        // Email format validation
        const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        if (!emailRegex.test(email)) {
            this.showError('Please enter a valid email address');
            return;
        }

        // Password validation
        const passwordError = this.validatePassword(password);
        if (passwordError) {
            this.showError(passwordError);
            return;
        }

        try {
            this.setLoading(true);
            
            await AuthAPI.register(name, email, password);
            
            // After successful registration, login the user
            //await AuthAPI.login(email, password);
            
            // Show success message
            this.setEmailComplete('Thank you for registering. Please check your inbox for a confirmation email and follow the instructions to complete registration.');

            // Redirect to home page on success
            //window.location.href = '/';

        } catch (error) {
            this.showError(error.message || 'Registration failed. Please try again.');
            this.setLoading(false);
        }
    }
}

// Initialize form when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new RegisterForm();
});