// js/api/routes-api.js

class RoutesAPI {
    static API_URL = 'https://api2.thefrontpack.com';

    static filterByTag(tag) {
        document.getElementById('search').value = '';
        return JSON.stringify({
            pageSize: 50,
            tags: tag,
            "includeGroupTypePersonal": true,
            "includeGroupTypeLocal": true,
            "includeGroupTypeTour": true,
            "includeGroupTypeEvent": true
        });
    }

    static async getTours(tag = 'usbrs') {
        try {
            const response = await fetch(`${this.API_URL}/Tours/Search`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...window.authHelper.getAuthHeader()
                },
                body: this.filterByTag(tag)
            });

            if (!response.ok) {
                if (response.status === 401) {
                    window.location.href = '/login.html';
                    return;
                }
                throw new Error('Failed to fetch tours');
            }

            const data = await response.json();
            return data;

        } catch (error) {
            console.error('Error fetching tours:', error);
            throw error;
        }
    }

    static async getTour(tourID, includeRoute) {
        try {
            let url = `${this.API_URL}/Tours/${tourID}?IncludeRoute=${includeRoute}`;
            const response = await fetch(url, 
                {
                    headers: {
                        ...window.authHelper.getAuthHeader()
                    }
                }
            );

            if (!response.ok) {
                if (response.status === 401) {
                    window.location.href = '/login.html';
                    return;
                }
                throw new Error('Failed to fetch tour details');
            }

            return await response.json();

        } catch (error) {
            console.error('Error fetching tour details:', error);
            throw error;
        }
    }

    static async getMyTours() {
        try {
            const body = JSON.stringify({ 
                pageSize: 200, 
                "includeGroupTypePersonal": false,
                "includeGroupTypeLocal": true,
                "includeGroupTypeTour": false,
                "includeGroupTypeEvent": false,
                "expandLinkedTrips": true,
                "owner_UserID": window.authHelper.getUserID(),
                "tourDetail": 0
             });
        
            console.log(body);
            const response = await fetch(`${this.API_URL}/Tours/Search`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    ...window.authHelper.getAuthHeader()
                },
                body: body
            });

            if (!response.ok) {
                if (response.status === 401) {
                    window.location.href = '/login.html';
                    return;
                }
                throw new Error('Failed to fetch tours');
            }

            const data = await response.json();
            return data;

        } catch (error) {
            console.error('Error fetching tours:', error);
            throw error;
        }
    }


    static async getTags() {
        try {
            const response = await fetch(`${this.API_URL}/Tours/tags`, {
                headers: {
                    ...window.authHelper.getAuthHeader()
                }
            });

            if (!response.ok) {
                if (response.status === 401) {
                    window.location.href = '/login.html';
                    return;
                }
                throw new Error('Failed to fetch tags');
            }

            return await response.json();

        } catch (error) {
            console.error('Error fetching tags:', error);
            throw error;
        }
    }
}
