// js/components/routes-list.js

class RoutesList {
    constructor() {
        this.container = document.getElementById('routes-list');
        this.loadingState = document.getElementById('loading-state');
        this.errorState = document.getElementById('error-state');
        this.tagFilter = document.getElementById('tag-filter');
        this.routes = [];
        
        // Bind methods
        this.handleTagChange = this.handleTagChange.bind(this);
        
        this.initialize();
    }

    async initialize() {
        try {
            // First load navigation
            await Navigation.loadNavigation();

            // Show/hide add route button based on user login status
            this.showHideAddRouteButton();

            // Load tags with default 'usbrs'
            await this.loadTags('usbrs');
            
            // Load routes
            this.showLoading();
            await this.fetchRoutes('usbrs');
            this.hideLoading();
            this.render();
            
            // Add event listener for tag changes
            this.tagFilter.addEventListener('change', this.handleTagChange);
        } catch (error) {
            this.showError();
        }
    }

    async loadTags(tag) {
        try {
            const tags = await RoutesAPI.getTags();
            
            // Clear loading option
            this.tagFilter.innerHTML = '';
            
            // Add tags to dropdown
            tags.forEach(tagOption => {
                const option = document.createElement('option');
                option.value = tagOption;
                option.textContent = tagOption;
                this.tagFilter.appendChild(option);
            });
            
            // Set selection to provided tag
            this.tagFilter.value = tag;
        } catch (error) {
            console.error('Error loading catalog:', error);
            this.tagFilter.innerHTML = '<option value="">Error loading catalog</option>';
        }
    }

    async handleTagChange(event) {
        const selectedTag = event.target.value;
        try {
            this.showLoading();
            await this.fetchRoutes(selectedTag);
            this.hideLoading();
            this.render();
        } catch (error) {
            this.showError();
        }
    }

    async fetchRoutes(tag) {
        try {
            let data = await RoutesAPI.getTours(tag);
            this.routes = data.results;
        } catch (error) {
            throw error;
        }
    }

    render() {
        // Clear existing routes
        this.container.innerHTML = '';

        // Create route cards
        const routesHTML = this.routes.map(route => this.createRouteCard(route)).join('');
        this.container.innerHTML = routesHTML;
    }

    createRouteCard(route) {
        // Check if current user is the owner
        const isOwner = route.owner_UserID == window.navigationInstance.getUserID();
        //console.log('isOwner:', route.owner_UserID, window.navigationInstance.getUserID(), isOwner);
        const editIconHTML = isOwner & window.navigationInstance.isLoggedIn()
            ? `<img src="/images/edit-icon.svg" 
                   alt="Edit" 
                   class="route-card__edit-icon" 
                   onclick="window.location.href='/edit.html?routeId=${route.tourID}'">`
            : '';
        
        return `
            <div class="route-card">
                ${editIconHTML}
                <img src="${route.mapPreviewURL}" alt="${route.name}" class="route-card__image">
                <div class="route-card__content">
                    <h2 class="route-card__title">
                        <a href="/route.html?routeId=${route.tourID}" class="route-card__link">${route.name}</a>
                    </h2>
                    <p class="route-card__description">${route.descr || ''}</p>
                </div>
            </div>
        `;
    }

    showHideAddRouteButton() {
        try {
            const addRouteButton = document.getElementById('add-route-button');
            //console.log('isLoggedIn:', window.navigationInstance.isLoggedIn());
            if (window.navigationInstance.isLoggedIn()) {
                addRouteButton.classList.remove('hidden');
            } else {
                addRouteButton.classList.add('hidden');
            }
        } catch (error) {
            console.error('Error showing/hiding add route button:', error);
        }
    }

    showLoading() {
        this.loadingState.classList.remove('hidden');
        this.errorState.classList.add('hidden');
    }

    hideLoading() {
        this.loadingState.classList.add('hidden');
    }

    showError() {
        this.loadingState.classList.add('hidden');
        this.errorState.classList.remove('hidden');
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new RoutesList();
});