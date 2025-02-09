# AI Assistant Guidance for Front Pack Website Development

## Current Implementation Focus: Navigation Component
Build the site incrementally in this order:
1. Navigation (current focus)
2. Authentication (next)
3. Route list
4. Map

### Navigation Requirements
When implementing the navigation component:

1. HTML Structure
```html
<!-- Example structure to follow -->
<nav class="nav">
  <div class="nav__container">
    <div class="nav__brand">
      <!-- Logo/Brand -->
    </div>
    <button class="nav__toggle">
      <!-- Mobile menu button -->
    </button>
    <ul class="nav__menu">
      <!-- Menu items -->
    </ul>
  </div>
</nav>
```

2. CSS Implementation Order:
   - Start with mobile layout (default)
   - Add desktop styles at @media (min-width: 768px)
   - Implement transitions for menu toggle
   - Style active and hover states

3. JavaScript Requirements:
   - Toggle mobile menu
   - Handle login state from localStorage
   - Update Login/Logout button text
   - Set active page indicator

### State Management
Use localStorage for:
```javascript
// Auth token
localStorage.setItem('authToken', token);
localStorage.getItem('authToken');

// User state
localStorage.setItem('isLoggedIn', 'true');
localStorage.getItem('isLoggedIn');
```

## Design System

### Colors
Use these specific color values:
```css
:root {
  --nav-bg: rgb(255, 255, 255);        /* White */
  --nav-text: rgb(40, 36, 40);         /* Dark Grey */
  --nav-active: rgb(44, 172, 227);     /* Light Blue */
  --nav-hover: rgb(149, 210, 243);     /* Pale Blue */
}
```

### Typography
Navigation text should use:
```css
.nav {
  font-family: "Helvetica Neue", Helvetica, Arial, sans-serif;
  font-size: 14px;
}
```

### Responsive Breakpoints
```css
/* Mobile (default) */
/* Tablet/Desktop */
@media (min-width: 768px) {
  /* Desktop navigation styles */
}
```

## Component Architecture

### CSS Methodology
Use this BEM structure for navigation:
```css
.nav {}                    /* Block: Main navigation */
.nav__container {}        /* Element: Container for nav contents */
.nav__brand {}           /* Element: Brand/logo area */
.nav__toggle {}          /* Element: Mobile menu button */
.nav__menu {}           /* Element: Menu list */
.nav__item {}           /* Element: Menu item */
.nav__link {}           /* Element: Menu link */
.nav__link--active {}   /* Modifier: Active state */
```

### JavaScript Patterns
Follow these patterns:
```javascript
// State management
const navState = {
  isLoggedIn: false,
  isMenuOpen: false
};

// Event handlers
function toggleMenu() {
  // Toggle mobile menu
}

function updateAuthState() {
  // Update login/logout button
}

// Event listeners
document.addEventListener('DOMContentLoaded', () => {
  // Initialize navigation
});
```

## File Structure
```
src/
├── css/
│   ├── base/
│   │   ├── reset.css
│   │   └── variables.css
│   └── components/
│       └── navigation.css
├── js/
│   ├── components/
│   │   └── navigation.js
│   └── utils/
│       └── auth-helpers.js
└── pages/
    └── index.html
```

## Error Handling
For navigation:
1. Handle missing localStorage values
2. Manage failed state updates
3. Handle mobile menu transition errors
4. Manage authentication state errors

## Testing Requirements
Test navigation for:
1. Mobile menu toggle
2. Responsive breakpoints
3. Login state persistence
4. Active page indication
5. Cross-browser compatibility

## Accessibility Requirements
1. Use semantic HTML5 elements
2. Implement proper ARIA attributes
3. Ensure keyboard navigation
4. Maintain sufficient color contrast
5. Provide screen reader support

## Performance Considerations
1. Minimize layout shifts during page load
2. Optimize menu transitions
3. Efficiently handle state changes
4. Minimize reflows and repaints

Would you like me to proceed with implementing the navigation component now?

