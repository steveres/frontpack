// js/api/auth-apis.js

class AuthAPI {
    static API_URL = 'https://api2.thefrontpack.com';
    //static API_URL = 'https://localhost:44367';
    static USER_NAME_KEY = 'userName';
    static USER_ID_KEY = 'userID';

    static async login(email, password) {
        try {
            const response = await fetch(`${this.API_URL}/authenticate/login2`, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ email, password })
            });
            
            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || 'Login failed');
            }
    
        } catch (error) {
            console.error('Login error:', error);
            throw error;
        }
    }

    static async register(name, email, password, profileType = 0) {
        try {
            const response = await fetch(`${this.API_URL}/Authenticate/register`, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ 
                    name,
                    email, 
                    password,
                    profileType
                })
            });
            
            if (!response.ok) {
                const error = await response.json();
                const reason = error.detail ? ': ' + error.detail : ''
                throw new Error(error.message || `Registration failed ${reason}`);
            }

        } catch (error) {
            console.error('Registration error:', error);
            throw error;
        }
    }

    static async verifyEmail(email, code) {
        try {
            const response = await fetch(`${this.API_URL}/authenticate/confirm`, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ email, code })
            });
            console.log('Email verification response:', response);
            return response.status === 200;
    
        } catch (error) {
            console.error('Email verification error:', error);
            return false;
        }
    }

    static async getUserInfo(email) {
        try {
            console.log('Fetching user info for:', email);
            const response = await fetch(`${this.API_URL}/Profiles/email/${email}`, {
                credentials: 'include',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                }
            });
    
            if (!response.ok) {
                throw new Error('Failed to fetch user info');
            }
    
            const data = await response.json();
            this.setUserName(data.name);
            this.setUserID(data.userID);
    
        } catch (error) {
            console.error('Error fetching user info:', error);
            throw error;
        }
    }

    static async logout() {
        try {
            console.log('AuthAPI Logging out...');

            const response = await fetch(`${this.API_URL}/authenticate/logout`, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json',
                },
            });
            console.log('Logout complete, status:', response.status);

            if (!response.ok) {
                const error = await response.json();
                throw new Error(error.message || 'Logout failed');
            }
    
        } catch (error) {
            console.error('Logout error:', error);
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
