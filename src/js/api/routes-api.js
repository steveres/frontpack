// js/api/routes-api.js

class RoutesAPI {
    static API_URL = 'https://api2.thefrontpack.com';
    //static API_URL = 'https://localhost:44367';

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
                credentials: 'include',
                headers: {
                    'Content-Type': 'application/json'
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
            //console.log('Fetched tours:', data);
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
                    credentials: 'include',
                    headers: {
                        'Accept': 'application/json',
                        'Content-Type': 'application/json'
                    },
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
                "owner_UserID": window.navigationInstance.getUserID(),
                "tourDetail": 0
             });
        
            const response = await fetch(`${this.API_URL}/Tours/Search`, {
                method: 'POST',
                credentials: 'include',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
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
                credentials: 'include',
            });

            console.log('Fetched tags (response):', response);
            if (!response.ok) {
                if (response.status === 401) {
                    window.location.href = '/login.html';
                    return;
                }
                throw new Error('Failed to fetch tags');
            }

            const data = await response.json();
            //console.log('Fetched tags (data):', data);
            return data;

        } catch (error) {
            console.error('Error fetching tags:', error);
            throw error;
        }
    }

    static async postTour(tour, method) {
        try {

            let response = await this.postTour_internal(tour, method);

            if (!response.ok) {
                // Get the error message from the response if possible
                let errorMessage;
                try {
                    const errorData = await response.json();
                    errorMessage = errorData.message || response.statusText;
                } catch {
                    errorMessage = response.statusText;
                }
                
                return {
                    message: `Server error: ${errorMessage}`,
                    tourId: null,
                    success: false
                };
            }

            if (method === 'PUT') {
                return {
                    message: 'Route saved',
                    tourId: tour.tourID,
                    success: true
                };
            } else {
                const responseBody = await response.json();
                return {
                    message: 'Route created',
                    tourId: responseBody.tourID,
                    success: true
                };
            }

        } catch (error) {
            console.error('Error in postTour:', error);
            return {
                message: error.message,
                tourId: null,
                success: false
            };
        }
    }    
    
    static async postTour_internal(tour, method) {
        const body = JSON.stringify(tour);
        console.log('Sending tour data:', {
            url: `${this.API_URL}/Tours`,
            method: method,
            tourId: tour.tourID,
            bodyLength: body.length
        });

        return await fetch(`${this.API_URL}/Tours`, {
            method: method,
            credentials: 'include',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            },
            body: body
        });
    }

    static async deleteTour(tourID) {

        try {
            const url = `${this.API_URL}/Tours/${tourID}`;
            const method = 'DELETE';
    
            const response = await fetch(url, {
                method: method,
                credentials: 'include',
                headers: {
                    'Accept': 'application/json',
                    'Content-Type': 'application/json'
                },
            });
    
            if (!response.ok) {
                // Get the error message from the response if possible
                let errorMessage;
                try {
                    const errorData = await response.json();
                    errorMessage = errorData.message || response.statusText;
                } catch {
                    errorMessage = response.statusText;
                }
                
                return {
                    message: `Server error: ${errorMessage}`,
                    success: false
                };
            }

            return {
                message: 'Route deleted',
                success: true
            };

        } catch (error) {
            console.error('Error in postTour:', error);
            return {
                message: error.message,
                success: false
            };
        }
    }    
}
