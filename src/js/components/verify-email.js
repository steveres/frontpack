// src/js/components/verify-email.js

class EmailVerification {
    constructor() {
        this.messageContainer = document.getElementById('message-container');
        this.messageText = document.getElementById('message-text');
        this.countdown = document.getElementById('countdown');
        this.init();
    }

    async init() {
        // First load navigation
        await Navigation.loadNavigation();

        // Get URL parameters
        const urlParams = new URLSearchParams(window.location.search);
        const email = urlParams.get('email');
        const code = urlParams.get('code');

        if (!email || !code) {
            this.showError('Invalid verification link. Please check your email for the correct link.');
            return;
        }

        this.verifyEmail(email, code);
    }

    showSuccess(message) {
        this.messageContainer.classList.remove('error');
        this.messageContainer.classList.add('success', 'visible');
        this.messageText.textContent = message;
    }

    showError(message) {
        this.messageContainer.classList.remove('success');
        this.messageContainer.classList.add('error', 'visible');
        this.messageText.textContent = message;
    }

    async startCountdown() {
        let secondsLeft = 10;
        
        const updateCountdown = () => {
            this.countdown.textContent = ` Redirecting in ${secondsLeft} seconds`;
            secondsLeft--;

            if (secondsLeft < 0) {
                window.location.href = '/';
            } else {
                setTimeout(updateCountdown, 1000);
            }
        };

        updateCountdown();
    }

    async verifyEmail(email, code) {
        try {
            const isVerified = await AuthAPI.verifyEmail(email, code);
            
            if (isVerified) {
                this.showSuccess('Thank you for verifying your email. You\'re ready to ride.');
                this.startCountdown();
            } else {
                this.showError('The code you entered on the URL does not match our confirmation information. Please register again.');
            }
        } catch (error) {
            this.showError('An error occurred during verification. Please try again.');
        }
    }
}

// Initialize verification when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new EmailVerification();
});