// Loading Manager 
const LoadingManager = {
    overlay: null,

    init() {
        this.createOverlay();
        this.attachNavigationListeners();
    },

    createOverlay() {
        this.overlay = document.createElement('div');
        this.overlay.id = 'loading-overlay';
        this.overlay.innerHTML = `
            <div class="loading-content">
                <div class="loading-spinner" id="loading-spinner"></div>
                <div class="loading-text" id="loading-text">Loading...</div>
            </div>
        `;
        document.body.appendChild(this.overlay);
    },

    attachNavigationListeners() {
        // Show loading on actual page navigation clicks
        document.addEventListener('click', (e) => {
            const link = e.target.closest('a');

            // Ignore if not a link
            if (!link) return;

            // Get href
            const href = link.getAttribute('href');

            
            // 1. Dropdown toggles
            if (link.classList.contains('dropdown-toggle')) return;
            if (link.hasAttribute('data-bs-toggle')) return;

            // 2. Fragment links (same page anchors)
            if (href && href.startsWith('#')) return;

            // 3. External links
            if (href && (href.startsWith('http://') || href.startsWith('https://'))) {
                // Only show loading for external links if they're not opening in new tab
                if (link.target === '_blank') return;
            }

            // 4. JavaScript void links
            if (href === 'javascript:void(0)' || href === 'javascript:;') return;

            // 5. Links with data-no-loading attribute
            if (link.hasAttribute('data-no-loading')) return;

            // 6. Modal triggers
            if (link.hasAttribute('data-bs-target')) return;

            // 7. Empty or null href
            if (!href || href === '') return;

            // SHOW LOADING for actual page navigations
            this.showHourglass('Loading page...');
        });

        // Show loading on form submissions (except AJAX forms)
        document.addEventListener('submit', (e) => {
            const form = e.target;

            // Don't show loading for AJAX forms
            if (form.hasAttribute('data-ajax')) return;
            if (form.classList.contains('ajax-form')) return;

            // Don't show for forms with data-no-loading
            if (form.hasAttribute('data-no-loading')) return;

            this.showHourglass('Processing...');
        });

        // Hide loading when page fully loads
        window.addEventListener('load', () => {
            this.hide();
        });

        // Hide loading if page navigation is cancelled
        window.addEventListener('pageshow', () => {
            this.hide();
        });
    },

    showHourglass(message = 'Loading...') {
        if (this.overlay) {
            const spinner = this.overlay.querySelector('#loading-spinner');
            const text = this.overlay.querySelector('#loading-text');

            // Use the BEST hourglass animation
            spinner.className = 'loading-spinner hourglass-best';  
            text.textContent = message;

            this.overlay.classList.add('active');
        }
    },

    showCart(message = 'Processing order...') {
        if (this.overlay) {
            const spinner = this.overlay.querySelector('#loading-spinner');
            const text = this.overlay.querySelector('#loading-text');

            // Set cart animation
            spinner.className = 'loading-spinner cart';
            text.textContent = message;

            this.overlay.classList.add('active');
        }
    },

    hide() {
        if (this.overlay) {
            this.overlay.classList.remove('active');
        }
    }
};

// Initialise when DOM is ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => LoadingManager.init());
} else {
    LoadingManager.init();
}