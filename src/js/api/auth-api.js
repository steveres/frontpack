// js/api/auth-apis.js

class AuthAPI {
    static API_URL = 'https://api2.thefrontpack.com';
    //static API_URL = 'https://localhost:44367';
    static USER_NAME_KEY = 'userName';
    static USER_ID_KEY = 'userID';

    static async login(email, password) {
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
            return await response.json();

        } catch (error) {
            console.error('Login error:', error);
            throw error;
        }
    }

    static async getUserInfo(email) {
        try {
            const response = await fetch(
                `${this.API_URL}/Profiles/email/${email}`,
                {
                    headers: {
                        ...window.authHelper.getAuthHeader()
                    }
                }
            );

            if (!response.ok) {
                throw new Error('Failed to fetch user info');
            }

            const data = await response.json();
            this.setUserName(data.name);
            this.setUserID(data.userID);
            return data;

        } catch (error) {
            console.error('Error fetching user info:', error);
            throw error;
        }
    }

    // Local storage management for user info
    static getUserName() {
        return localStorage.getItem(this.USER_NAME_KEY);
    }

    static getUserID() {
        return localStorage.getItem(this.USER_ID_KEY);
    }

    static setUserName(name) {
        localStorage.setItem(this.USER_NAME_KEY, name);
    }

    static setUserID(userID) {
        localStorage.setItem(this.USER_ID_KEY, userID);
    }

    static clearUserInfo() {
        localStorage.removeItem(this.USER_NAME_KEY);
        localStorage.removeItem(this.USER_ID_KEY);
    }
}
