// js/components/routes-list.js

class RoutesList {
    constructor() {
        this.container = document.getElementById('routes-list');
        this.loadingState = document.getElementById('loading-state');
        this.errorState = document.getElementById('error-state');
        this.tagFilter = document.getElementById('tag-filter');
        this.searchFilter = document.getElementById('search-filter');
        this.routes = [];
        this.filteredRoutes = [];
        this.currentTag = 'usbrs';
        
        // Bind methods
        this.handleTagChange = this.handleTagChange.bind(this);
        this.handleSearchInput = this.handleSearchInput.bind(this);
        
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
            this.filteredRoutes = [...this.routes]; // Initialize filtered routes with all routes
            this.hideLoading();
            this.render();
            
            // Add event listeners for filtering
            this.tagFilter.addEventListener('change', this.handleTagChange);
            this.searchFilter.addEventListener('input', this.handleSearchInput);
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
        this.currentTag = selectedTag;
        try {
            this.showLoading();
            await this.fetchRoutes(selectedTag);
            this.filteredRoutes = [...this.routes];
            this.hideLoading();
            this.render();
        } catch (error) {
            this.showError();
        }
    }

    handleSearchInput(event) {
        const searchTerm = event.target.value.toLowerCase().trim();
        
        if (searchTerm === '') {
            // If search is empty, show all routes for current tag
            this.filteredRoutes = [...this.routes];
        } else {
            // Filter routes by name and description
            this.filteredRoutes = this.routes.filter(route => {
                const name = (route.name || '').toLowerCase();
                const description = (route.descr || '').toLowerCase();
                return name.includes(searchTerm) || description.includes(searchTerm);
            });
        }
        
        this.render();
    }

    async fetchRoutes(tag) {
        try {
            this.searchFilter.value = '';
            let data = await RoutesAPI.getTours(tag);
            this.routes = data.results;
            this.filteredRoutes = [...this.routes];
        } catch (error) {
            throw error;
        }
    }

    render() {
        // Clear existing routes
        this.container.innerHTML = '';

        // Show message if no routes match filters
        if (this.filteredRoutes.length === 0) {
            this.container.innerHTML = '<div class="no-results">No routes found matching your filters</div>';
            return;
        }

        // Create route cards
        const routesHTML = this.filteredRoutes.map(route => this.createRouteCard(route)).join('');
        this.container.innerHTML = routesHTML;
    }

    createRouteCard(route) {
        // Check if current user is the owner
        const isOwner = route.owner_UserID == window.navigationInstance.getUserID();
        const editIconHTML = isOwner & window.navigationInstance.isLoggedIn()
            ? `<img src="/images/edit-icon.svg" 
                   alt="Edit" 
                   class="route-card__edit-icon" 
                   onclick="window.location.href='/edit.html?routeId=${route.tourID}'">`
            : '';
        
        let imgSrc = route.mapPreviewURL;
        if (this.tagFilter.value !== 'usbrs') { 
            imgSrc = route.logoURL ?? route.mapPreviewURL;
        }

        return `
            <div class="route-card">
                ${editIconHTML}
                <img src="${imgSrc}" alt="${route.name}" class="route-card__image">
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