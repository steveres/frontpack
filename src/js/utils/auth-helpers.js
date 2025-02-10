// js/utils/auth-helpers.js

class AuthHelper {
    constructor() {
        this.TOKEN_KEY = 'authToken';
    }

    isLoggedIn() {
        return this.getToken() !== null;
    }

    getToken() {
        return localStorage.getItem(this.TOKEN_KEY);
    }

    setToken(token) {
        localStorage.setItem(this.TOKEN_KEY, token);
        this.notifyAuthChange();
    }

    removeToken() {
        localStorage.removeItem(this.TOKEN_KEY);
        AuthAPI.clearUserInfo(); // Clear user info on logout
        this.notifyAuthChange();
    }

    notifyAuthChange() {
        if (window.navigationInstance) {
            window.navigationInstance.updateAuthState();
        }
    }

    async login(email, password) {
        try {
            // First, authenticate
            const authResponse = await AuthAPI.login(email, password);
            this.setToken(authResponse.token);

            // Then, get user info
            await AuthAPI.getUserInfo(email);
            return true;

        } catch (error) {
            console.error('Login error:', error);
            throw error;
        }
    }

    async logout() {
        try {
            this.removeToken();
            return true;
        } catch (error) {
            console.error('Logout error:', error);
            throw error;
        }
    }

    getAuthHeader() {
        const token = this.getToken();
        return token ? { 'Authorization': `Bearer ${token}` } : {};
    }

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

    getUserName() {
        return localStorage.getItem(AuthAPI.USER_NAME_KEY);
    }

    getUserID() {
        try {
            return localStorage.getItem(AuthAPI.USER_ID_KEY);
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
}

window.authHelper = new AuthHelper();
