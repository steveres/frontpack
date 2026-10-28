// js/components/route-detail.js

class RouteDetail {
    constructor() {
        this.infoContainer = document.getElementById('route-info');
        this.loadingState = document.getElementById('loading-state');
        this.errorState = document.getElementById('error-state');
        this.mapContainer = document.getElementById('map');
        this.map = null;
        this.tour = null;
        this.childTourSummaries = null;
        this.currentChildRoute = null;
        this.currentPolylines = [];

        this.handleChildRouteClick = this.handleChildRouteClick.bind(this);
        this.handleToggleDescription = this.handleToggleDescription.bind(this);
        this.initialize();
    }

    async initialize() {
        try {
            // First load navigation
            await Navigation.loadNavigation();
            
            const params = new URLSearchParams(window.location.search);
            const routeId = params.get('routeId');
            
            if (!routeId) {
                throw new Error('No route ID provided');
            }

            this.showLoading();
            await this.loadRouteData(routeId);
            this.setLayoutType();
            this.initializeMap();
            
            if (this.isMultiRoute()) {
                await this.loadChildTourSummaries();
                this.renderInfo();

                this.currentChildRoute = this.tour.childTours[0];
                this.selectFirstCard();

                try {
                    await this.loadAndDrawChildRoute(this.currentChildRoute.tourID);
                } catch (error) {
                    this.showChildRouteError(0, 'Failed to load route data');
                }
            } else {
                this.renderInfo();

                if (this.tour.route && this.tour.route.tracks) {
                    this.drawTrack(this.tour.route.tracks);
                }
            }
            this.hideLoading();
        } catch (error) {
            this.showError('Failed to load route details');
            console.error('Error initializing route detail:', error);
        }
    }

    async loadRouteData(routeId) {
        try {
            const response = await RoutesAPI.getTour(routeId, true);
            this.tour = response.tour;
        } catch (error) {
            throw error;
        }
    }

    async loadChildTourSummaries() {
        try {
            const tourIds = this.tour.childTours.map(child => child.tourID);
            const response = await RoutesAPI.getToursByIds(tourIds);
            this.childTourSummaries = response.results;
            console.log('Loaded child tour summaries:', this.childTourSummaries);
        } catch (error) {
            throw error;
        }
    }

    setLayoutType() {
        const container = document.querySelector('.route-detail');
        if (this.isMultiRoute()) {
            container.classList.add('route-detail--multi');
            container.classList.remove('route-detail--single');
        } else {
            container.classList.add('route-detail--single');
            container.classList.remove('route-detail--multi');
        }
    }

    isMultiRoute() {
        return this.tour.childTours && this.tour.childTours.length > 0;
    }

    initializeMap() {
        const center = {
            lat: this.tour.mapcenter_Lat,
            lng: this.tour.mapcenter_Long
        };

        this.map = new google.maps.Map(this.mapContainer, {
            center: center,
            zoom: 12
        });
    }

    renderInfo() {
        let html = `
            <h1 class="route-info__title">${this.tour.name}</h1>
            <div class="route-info__description">${this.tour.descr || ''}</div>
        `;

        if (this.isMultiRoute()) {
            html += this.renderChildRoutes();
        }

        this.infoContainer.innerHTML = html;

        if (this.isMultiRoute()) {
            this.addChildRouteListeners();
        }
    }

    metersToMiles(meters) {
        const miles = meters * 0.000621371;
        return Math.round(miles);
    }

    renderChildRoutes() {
        const childTours = this.childTourSummaries;
        console.log('childTours:', childTours);
        return `
            <div class="route-info__child-routes">
                <h2 class="child-routes__title">Routes</h2>
                <ul class="child-routes__list">
                    ${childTours.map((childTour, index) => `
                        <li class="child-route__item">
                            <a href="#" class="child-route__link${index === 0 ? ' active' : ''}" data-route-index="${index}">
                                <div class="child-route__content">
                                    <div class="child-route__header">
                                        <div class="child-route__name">${childTour.name}</div>
                                    </div>
                                    <div class="child-route__metrics">
                                        <div class="child-route__metrics-left">
                                            ${childTour.logoURL ? `<img src="${childTour.logoURL}" alt="${childTour.name} logo" class="child-route__logo">` : ''}
                                            <div class="child-route__distance">
                                                ${this.metersToMiles(childTour.distance)} miles
                                            </div>
                                        </div>
                                        <button type="button" class="child-route__toggle" data-route-index="${index}">
                                            &#9660;
                                        </button>
                                    </div>
                                    <div class="child-route__descr" data-route-index="${index}">${childTour.descr || 'No description available.'}</div>
                                </div>
                            </a>
                        </li>
                    `).join('')}
                </ul>
            </div>
        `;
    }

    async loadAndDrawChildRoute(tourID) {
        const response = await RoutesAPI.getTour(tourID, true);
        const childTour = response.tour;
        
        if (childTour.route && childTour.route.tracks) {
            this.drawTrack(childTour.route.tracks);
        } else {
            throw new Error('No route data available');
        }
    }

    drawTrack(tracks) {
        const { Polyline, LatLngBounds } = google.maps;
        const bounds = new LatLngBounds();

        tracks.forEach(track => {
            const trackPath = track.trackpoints.map(point => {
                const latLng = { lat: point.latLong.lat, lng: point.latLong.lng };
                bounds.extend(latLng);
                return latLng;
            });

            let strokeColor = (!track.title)
                ? '#0000FF'
                : track.title.toLowerCase().includes('alt') ? '#0000FF' : '#FF00FF';

            const trackLine = new Polyline({
                path: trackPath,
                geodesic: true,
                strokeColor: strokeColor,
                strokeOpacity: 0.5,
                strokeWeight: 4 
            });

            trackLine.setMap(this.map);
            this.currentPolylines.push(trackLine);
        });

        this.map.fitBounds(bounds);
    }

    async handleChildRouteClick(event) {
        // Prevent route loading if the click was on the toggle button
        if (event.target.classList.contains('child-route__toggle')) {
            event.preventDefault();
            return;
        }
        
        event.preventDefault();
        const routeIndex = parseInt(event.currentTarget.dataset.routeIndex);
        const selectedTourID = this.tour.childTours[routeIndex].tourID;

        try {
            this.showChildRouteLoading(routeIndex);
            this.clearMap();
            await this.loadAndDrawChildRoute(selectedTourID);
            this.updateActiveChildRoute(routeIndex);
            this.hideChildRouteLoading(routeIndex);
        } catch (error) {
            console.error('Error loading child route:', error);
            this.showChildRouteError(routeIndex, 'Failed to load route');
        }
    }

    handleToggleDescription(event) {
        event.preventDefault();
        event.stopPropagation();
        
        const routeIndex = parseInt(event.target.dataset.routeIndex);
        const descElement = this.infoContainer.querySelector(`.child-route__descr[data-route-index="${routeIndex}"]`);
        const toggleBtn = event.target;
        
        if (descElement.classList.contains('expanded')) {
            descElement.classList.remove('expanded');
            toggleBtn.classList.remove('expanded');
            toggleBtn.innerHTML = '&#9660;'; // Down arrow
        } else {
            descElement.classList.add('expanded');
            toggleBtn.classList.add('expanded');
            toggleBtn.innerHTML = '&#9650;'; // Up arrow
        }
    }

    addChildRouteListeners() {
        const links = this.infoContainer.querySelectorAll('.child-route__link');
        links.forEach(link => {
            link.addEventListener('click', this.handleChildRouteClick);
        });

        const toggleButtons = this.infoContainer.querySelectorAll('.child-route__toggle');
        toggleButtons.forEach(button => {
            button.addEventListener('click', this.handleToggleDescription);
        });
    }

    clearMap() {
        this.currentPolylines.forEach(polyline => {
            polyline.setMap(null);
        });
        this.currentPolylines = [];
    }

    selectFirstCard() {
        const firstLink = this.infoContainer.querySelector('.child-route__link');
        if (firstLink) {
            firstLink.classList.add('active');
        }
    }

    updateActiveChildRoute(index) {
        const links = this.infoContainer.querySelectorAll('.child-route__link');
        links.forEach(link => link.classList.remove('active'));
        const activeLink = this.infoContainer.querySelector(`[data-route-index="${index}"]`);
        if (activeLink) {
            activeLink.classList.add('active');
        }
    }

    showLoading() {
        this.infoContainer.classList.add('hidden');
        this.errorState.classList.add('hidden');
        this.loadingState.classList.remove('hidden');
    }

    hideLoading() {
        this.loadingState.classList.add('hidden');
        if (!this.errorState.classList.contains('hidden')) {
            this.infoContainer.classList.add('hidden');
        } else {
            this.infoContainer.classList.remove('hidden');
        }
    }

    showError(message) {
        this.loadingState.classList.add('hidden');
        this.infoContainer.classList.add('hidden');
        this.errorState.textContent = message;
        this.errorState.classList.remove('hidden');
    }

    showChildRouteLoading(index) {
        const card = this.infoContainer.querySelector(`[data-route-index="${index}"]`)
            ?.closest('.child-route__item');
        if (card) {
            card.classList.add('route-card__loading');
            const errorElement = card.querySelector('.child-route__error');
            errorElement?.remove();
        }
    }

    hideChildRouteLoading(index) {
        const card = this.infoContainer.querySelector(`[data-route-index="${index}"]`)
            ?.closest('.child-route__item');
        if (card) {
            card.classList.remove('route-card__loading');
        }
    }

    showChildRouteError(index, message) {
        const card = this.infoContainer.querySelector(`[data-route-index="${index}"]`)
            ?.closest('.child-route__item');
        if (card) {
            card.classList.remove('route-card__loading');
            
            let errorElement = card.querySelector('.child-route__error');
            errorElement?.remove();
            
            errorElement = document.createElement('div');
            errorElement.className = 'child-route__error';
            errorElement.textContent = message;
            card.appendChild(errorElement);
        }
    }
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new RouteDetail();
});
