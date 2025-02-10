// js/components/route-edit.js

class RouteEdit {
    constructor() {
        this.tabs = document.querySelectorAll('.tab-button');
        this.tabContents = document.querySelectorAll('.tab-content');
        this.routeId = new URLSearchParams(window.location.search).get('routeId');
        
        // Bind methods
        this.switchTab = this.switchTab.bind(this);
        this.loadData = this.loadData.bind(this);
        
        this.initialize();
    }

    async initialize() {
        try {
            // Check authentication
            if (!window.authHelper.isLoggedIn()) {
                window.location.href = '/login.html';
                return;
            }

            // Initialize tab switching
            this.tabs.forEach(tab => {
                tab.addEventListener('click', (event) => {
                    const tabName = event.target.dataset.tab;
                    this.switchTab(tabName);
                });
            });

            // Show first tab by default
            if (this.tabs.length > 0) {
                const firstTabName = this.tabs[0].dataset.tab;
                this.switchTab(firstTabName);
            }

            // Load data if editing existing route
            if (this.routeId) {
                await this.loadData();
            }

        } catch (error) {
            console.error('Error initializing route edit:', error);
        }
    }

    async loadData() {
        try {
            // Load the tour data
            console.log('Loading tour data for ID:', this.routeId);
            const tourData = await RoutesAPI.getTour(this.routeId, false);

            // Load available tours for route sections
            console.log('Loading available tours for route sections');
            const myTours = await RoutesAPI.getMyTours();

            // Store the data for later use
            this.tour = tourData.tour;
            this.availableTours = myTours.results;
            console.log('Tour data loaded:', this.tour);
            console.log('Available Tours loaded:', this.availableTours);

            // Populate form fields
            console.log('Populating form fields:', this.tour);
            this.populateInfoTab();
            this.populateItineraryTab();
            this.populateBrandingTab();
            this.populateTagsTab();
            console.log('Form fields populated');

        } catch (error) {
            console.error('Error loading route data:', error);
            alert('Error loading route data. Please try again.');
        }
    }

    populateInfoTab() {
        // Basic info fields
        document.getElementById('tour-name').value = this.tour.name || '';
        document.getElementById('tour-description').value = this.tour.descr || '';
        document.getElementById('tour-privacy').value = this.tour.visibility === 1 ? 'private' : 'public';
    }

    populateItineraryTab() {
        // Route preview image
        const routePreview = document.getElementById('route-preview');
        if (this.tour.mapPreviewURL) {
            routePreview.src = this.tour.mapPreviewURL;
            routePreview.classList.remove('hidden');
        } else {
            routePreview.classList.add('hidden');
        }

        // Route sections
        const routeSectionsContainer = document.getElementById('route-sections');
        routeSectionsContainer.innerHTML = ''; // Clear existing sections

        if (this.tour.childTours && this.tour.childTours.length > 0) {
            this.tour.childTours.forEach((childTour, index) => {
                const sectionDiv = document.createElement('div');
                sectionDiv.className = 'form-group';
                
                // Create select element
                const select = document.createElement('select');
                select.className = 'form-select';
                select.id = `route-section-${index}`;
                select.name = `route-section-${index}`;

                // Add options from available tours
                if (this.availableTours) {
                    this.availableTours.forEach(tour => {
                        const option = document.createElement('option');
                        option.value = tour.tourID;
                        option.textContent = tour.name;
                        option.selected = tour.tourID === childTour.tourID;
                        select.appendChild(option);
                    });
                }

                sectionDiv.appendChild(select);
                routeSectionsContainer.appendChild(sectionDiv);
            });
        }
    }

    populateBrandingTab() {
        // Logo preview
        const logoPreview = document.getElementById('logo-preview');
        if (this.tour.logo) {
            logoPreview.src = this.tour.logo;
            logoPreview.classList.remove('hidden');
        } else {
            logoPreview.classList.add('hidden');
        }

        // Cover preview
        const coverPreview = document.getElementById('cover-preview');
        if (this.tour.coverURL) {
            coverPreview.src = this.tour.coverURL;
            coverPreview.classList.remove('hidden');
        } else {
            coverPreview.classList.add('hidden');
        }

        // Catalog preview
        const catalogPreview = document.getElementById('catalog-preview');
        if (this.tour.catalogURL) {
            catalogPreview.src = this.tour.catalogURL;
            catalogPreview.classList.remove('hidden');
        } else {
            catalogPreview.classList.add('hidden');
        }
    }

    populateTagsTab() {
        const tagsContainer = document.getElementById('tags-container');
        tagsContainer.innerHTML = ''; // Clear existing tags

        if (this.tour.tags && this.tour.tags.length > 0) {
            this.tour.tags.forEach((tag, index) => {
                const tagDiv = document.createElement('div');
                tagDiv.className = 'form-group';
                
                const input = document.createElement('input');
                input.type = 'text';
                input.className = 'form-input';
                input.id = `tag-${index}`;
                input.name = `tag-${index}`;
                input.value = this.tagToText (tag);

                tagDiv.appendChild(input);
                tagsContainer.appendChild(tagDiv);
            });
        }
    }

    switchTab(tabName) {
        try {
            // Remove active class from all tabs and contents
            this.tabs.forEach(tab => tab.classList.remove('active'));
            this.tabContents.forEach(content => content.classList.remove('active'));

            // Add active class to selected tab and content
            const selectedTab = document.querySelector(`.tab-button[data-tab="${tabName}"]`);
            const selectedContent = document.getElementById(`${tabName}-tab`);

            if (!selectedTab || !selectedContent) {
                throw new Error(`Tab ${tabName} not found`);
            }

            selectedTab.classList.add('active');
            selectedContent.classList.add('active');

        } catch (error) {
            console.error('Error switching tabs:', error);
            // Show the first tab as fallback
            if (this.tabs.length > 0 && this.tabContents.length > 0) {
                this.tabs[0].classList.add('active');
                this.tabContents[0].classList.add('active');
            }
        }
    }

    tagToText (tag) {
        return (tag)
            ? tag.name === 'tag' ? tag.value : `${tag.name} = ${tag.value}`
            : '';
        
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new RouteEdit();
});