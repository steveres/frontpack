// js/components/route-edit.js

class RouteEdit {
    constructor() {
        this.tabs = document.querySelectorAll('.tab-button');
        this.tabContents = document.querySelectorAll('.tab-content');
        this.routeId = new URLSearchParams(window.location.search).get('routeId');
        
        // Bind methods
        this.switchTab = this.switchTab.bind(this);
        this.loadData = this.loadData.bind(this);
        this.addRouteSection = this.addRouteSection.bind(this);
        this.addTag = this.addTag.bind(this);

        this.saveData = this.saveData.bind(this);
        this.handleDelete = this.handleDelete.bind(this);
        
        this.initialize();
    }

    async initialize() {
        try {
            
            // Add event listeners for add buttons
            document.getElementById('add-section').addEventListener('click', this.addRouteSection);
            document.getElementById('add-tag').addEventListener('click', this.addTag);
            document.querySelector('.save-button').addEventListener('click', this.saveData);
            document.querySelector('.delete-button-large').addEventListener('click', this.handleDelete);

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
            //console.log('Loading tour data for ID:', this.routeId);
            const tourData = await RoutesAPI.getTour(this.routeId, false);

            // Load available tours for route sections
            //console.log('Loading available tours for route sections');
            const myTours = await RoutesAPI.getMyTours();

            // Store the data for later use
            this.tour = tourData.tour;
            this.availableTours = myTours.results;
            console.log('Tour data loaded:', this.tour);
            //console.log('Available Tours loaded:', this.availableTours);

            // Populate form fields
            //console.log('Populating form fields:', this.tour);
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

    addRouteSection(selectedTourId = null) {
        const routeSectionsContainer = document.getElementById('route-sections');
        const sectionCount = routeSectionsContainer.children.length;
        
        const sectionDiv = document.createElement('div');
        sectionDiv.className = `form-group route-section `;
        
        // Create container for select and delete button
        const container = document.createElement('div');
        container.className = 'route-section-container';

        // Create select element
        const select = document.createElement('select');
        select.className = `form-select `;
        select.id = `route-section-${sectionCount}`;
        select.name = `route-section-${sectionCount}`;

        // Add options from available tours
        if (this.availableTours) {
            this.availableTours.forEach(tour => {
                const option = document.createElement('option');
                option.value = tour.tourID;
                option.textContent = tour.name;
                if (selectedTourId && tour.tourID === selectedTourId) {
                    option.selected = true;
                }
                select.appendChild(option);
            });
        }

        // Create delete button
        const deleteButton = document.createElement('button');
        deleteButton.type = 'button';
        deleteButton.className = 'delete-button';
        deleteButton.innerHTML = '×';
        deleteButton.onclick = () => sectionDiv.remove();

        container.appendChild(select);
        container.appendChild(deleteButton);
        sectionDiv.appendChild(container);
        routeSectionsContainer.appendChild(sectionDiv);
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

        // Clear existing sections
        const routeSectionsContainer = document.getElementById('route-sections');
        routeSectionsContainer.innerHTML = '';

        // Add existing route sections
        if (this.tour.childTours && this.tour.childTours.length > 0) {
            this.tour.childTours.forEach(childTour => {
                this.addRouteSection(childTour.tourID);
            });
        }
    }

    addTag(tagValue = '') {
        const tagsContainer = document.getElementById('tags-container');
        const tagCount = tagsContainer.children.length;
        
        const tagDiv = document.createElement('div');
        tagDiv.className = 'form-group tag-item';
        
        // Create container for input and delete button
        const container = document.createElement('div');
        container.className = 'tag-container';

        // Create input for tag
        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'form-input';
        input.id = `tag-${tagCount}`;
        input.name = `tag-${tagCount}`;
        input.value = (typeof tagValue === 'object' && tagValue !== null) 
            ? ''
            : tagValue;
        
        // Create delete button
        const deleteButton = document.createElement('button');
        deleteButton.type = 'button';
        deleteButton.className = 'delete-button';
        deleteButton.innerHTML = '×';
        deleteButton.onclick = () => tagDiv.remove();

        container.appendChild(input);
        container.appendChild(deleteButton);
        tagDiv.appendChild(container);
        tagsContainer.appendChild(tagDiv);
    }

    populateTagsTab() {
        // Clear existing tags
        const tagsContainer = document.getElementById('tags-container');
        tagsContainer.innerHTML = '';

        // Add existing tags
        if (this.tour.tags && this.tour.tags.length > 0) {
            this.tour.tags.forEach(tag => {
                this.addTag(this.tagToText(tag));
            });
        }
    }

    populateBrandingTab() {
        // Logo preview
        const logoPreview = document.getElementById('logo-preview');
        if (this.tour.logoURL) {
            logoPreview.src = this.tour.logoURL;
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

    tagToText(tag) {
        return (tag)
            ? tag.name === 'tag' ? tag.value : `${tag.name} = ${tag.value}`
            : '';
    }

    async saveData() {
        console.log('Starting save operation');
        const saveButton = document.querySelector('.save-button');
        saveButton.disabled = true;
        saveButton.textContent = 'Saving...';
        
        try {
            // Initialize tour object
            let tour = this.routeId ? { ...this.tour } : {
                owner_UserID: window.navHelper.getUserID(),
                createdDate: new Date().toISOString(),
                visibility: 0
            };
    
            // Log initial tour state
            console.log('Initial tour state:', { 
                isNew: !this.routeId,
                tourId: tour.tourID,
                userId: tour.owner_UserID 
            });
    
            // Basic info
            tour.name = document.getElementById('tour-name').value;
            tour.descr = document.getElementById('tour-description').value;
            tour.visibility = document.getElementById('tour-privacy').value === 'private' ? 1 : 0;
    
            // Handle file uploads one at a time to prevent any race conditions
            const routeFile = document.getElementById('route-file').files[0];
            if (routeFile) {
                console.log('Processing route file:', routeFile.name);
                tour.routeXML = await window.fileHelper.readFileAsText(routeFile);
                console.log('Route file processed, length:', tour.routeXML.length);
            }
    
            const logoFile = document.getElementById('logo-file').files[0];
            if (logoFile) {
                console.log('Processing logo file:', logoFile.name);
                tour.logo = await window.fileHelper.readFileAsBinary(logoFile);
                console.log('Logo file processed, length:', tour.logo.length);
            }
    
            const coverFile = document.getElementById('cover-file').files[0];
            if (coverFile) {
                console.log('Processing cover file:', coverFile.name);
                tour.coverPic = await window.fileHelper.readFileAsBinary(coverFile);
                console.log('Cover file processed, length:', tour.coverPic.length);
            }
    
            const catalogFile = document.getElementById('catalog-file').files[0];
            if (catalogFile) {
                console.log('Processing catalog file:', catalogFile.name);
                tour.catalogPic = await window.fileHelper.readFileAsBinary(catalogFile);
                console.log('Catalog file processed, length:', tour.catalogPic.length);
            }
    
            // Handle route sections
            const routeSections = document.querySelectorAll('.route-section select');
            tour.childTours = Array.from(routeSections).map(select => ({
                tourID: parseInt(select.value)
            }));
            console.log('Route sections processed:', tour.childTours);
    
            // Handle tags
            const tagInputs = document.querySelectorAll('.tag-item input');
            tour.tags = Array.from(tagInputs)
                .map(input => input.value.trim())
                .filter(tag => tag !== '')
                .map(tag => {
                    const [name, value] = tag.includes('=') 
                        ? tag.split('=').map(part => part.trim())
                        : ['tag', tag];
                    return { name, value };
                });
            console.log('Tags processed:', tour.tags);
    
            // Save the tour
            const method = this.routeId ? 'PUT' : 'POST';
            console.log(`Saving tour with method ${method}`);
            
            const result = await RoutesAPI.postTour(tour, method);
    
            if (result.success) {
                console.log('Save successful:', result);
                window.location.href = `/edit.html?routeId=${result.tourId}`;
            } else {
                throw new Error(result.message || 'Failed to save route');
            }
    
        } catch (error) {
            console.error('Error saving tour:', error);
            alert(`Failed to save route: ${error.message}`);
        } finally {
            saveButton.disabled = false;
            saveButton.textContent = 'Save';
        }
    }

    async handleDelete() {
        if (confirm('Are you sure you want to delete this Route? This action cannot be undone')) {
            console.log('Deleting route:', this);
            const result = await RoutesAPI.deleteTour(this.tour.tourID);
            if (result.success) {
                alert(`Deleteted route: ${this.tour.name}`); 
            } else {
                alert(`Error deleteing route: ${result.message}`); 
            }
        }
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.routeEdit = new RouteEdit();
});