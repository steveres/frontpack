// js/utils/nav-helpers.js

class NavHelper {

    constructor() {
    }

    isLoggedIn() {
        return this.getUserID() !== null & this.getUserName() !== null;
    }

    async login(email, password) {
        try {
            // First, authenticate
            const authResponse = await AuthAPI.login(email, password);
            console.log('Auth response:', authResponse);

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
            console.log('auth-helper Logging out...');
            await AuthAPI.logout();
            console.log('auth-helper Waiting delay...');
            await new Promise(resolve => setTimeout(resolve, 3000));
            this.clearUserInfo();
            console.log('auth-helper Cleared User info...');
        } catch (error) {
            console.error('Logout error:', error);
            throw error;
        }
    }

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
}

window.navHelper = new NavHelper();
