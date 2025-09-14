// Sortarr Remote Configuration JavaScript
// This file provides client-side functionality for the Sortarr web interface

document.addEventListener('DOMContentLoaded', function() {
    // Add any client-side form validation or UI enhancements here
    console.log('Sortarr remote configuration loaded');

    // Auto-refresh functionality for status updates
    const statusElements = document.querySelectorAll('.status-indicator');
    if (statusElements.length > 0) {
        setInterval(function() {
            // Could implement auto-refresh of status indicators here
            // For now, just log that the script is running
            console.log('Status check interval');
        }, 30000); // 30 seconds
    }

    // Form validation helpers
    function validatePaths() {
        const pathInputs = document.querySelectorAll('input[type="text"][name*="Path"], input[type="text"][name*="Folder"]');
        pathInputs.forEach(function(input) {
            input.addEventListener('blur', function() {
                if (this.value && !isValidPath(this.value)) {
                    this.style.borderColor = '#ff6b6b';
                } else {
                    this.style.borderColor = '';
                }
            });
        });
    }

    function isValidPath(path) {
        // Basic path validation - could be enhanced
        return path.length > 0 && !path.match(/[<>:"|?*]/);
    }

    // Initialize form enhancements
    validatePaths();

    // Add form submission handler
    const forms = document.querySelectorAll('form');
    forms.forEach(function(form) {
        form.addEventListener('submit', function(e) {
            console.log('Form submitted');
            // Could add additional validation or loading indicators here
        });
    });
});

// Utility functions for the web interface
window.SortarrUtils = {
    showNotification: function(message, type = 'info') {
        const notification = document.createElement('div');
        notification.className = `notification ${type}`;
        notification.textContent = message;
        notification.style.cssText = `
            position: fixed;
            top: 20px;
            right: 20px;
            padding: 15px 20px;
            border-radius: 4px;
            color: white;
            font-weight: bold;
            z-index: 1000;
            opacity: 0;
            transition: opacity 0.3s;
        `;

        if (type === 'error') {
            notification.style.backgroundColor = '#ff4444';
        } else if (type === 'success') {
            notification.style.backgroundColor = '#4caf50';
        } else {
            notification.style.backgroundColor = '#2196f3';
        }

        document.body.appendChild(notification);

        // Fade in
        setTimeout(() => notification.style.opacity = '1', 100);

        // Auto remove after 5 seconds
        setTimeout(() => {
            notification.style.opacity = '0';
            setTimeout(() => document.body.removeChild(notification), 300);
        }, 5000);
    },

    formatPath: function(path) {
        // Normalize path separators for display
        return path.replace(/\//g, '\\');
    }
};