// Sortarr Web Interface JavaScript
// Comprehensive functionality for the web interface

class SortarrWeb {
    constructor() {
        this.isRunning = false;
        this.logUpdateInterval = null;
        this.currentPath = 'C:\\';
        this.browserTarget = null;
        this.browserMode = 'folder'; // 'folder' or 'file'

        this.init();
    }

    init() {
        this.setupTabs();
        this.setupEventListeners();
        this.loadProfiles();
        this.loadConfiguration();
        this.startLogPolling();
        this.updateStatus();
    }

    // Tab Management
    setupTabs() {
        const tabButtons = document.querySelectorAll('.tab-btn');
        const tabContents = document.querySelectorAll('.tab-content');

        tabButtons.forEach(button => {
            button.addEventListener('click', () => {
                const targetTab = button.getAttribute('data-tab');

                // Remove active class from all tabs and contents
                tabButtons.forEach(btn => btn.classList.remove('active'));
                tabContents.forEach(content => content.classList.remove('active'));

                // Add active class to clicked tab and corresponding content
                button.classList.add('active');
                document.getElementById(`${targetTab}-tab`).classList.add('active');
            });
        });
    }

    // Event Listeners
    setupEventListeners() {
        // Profile Management
        document.getElementById('loadProfileBtn').addEventListener('click', () => this.loadProfile());
        document.getElementById('saveProfileBtn').addEventListener('click', () => this.saveProfile());
        document.getElementById('deleteProfileBtn').addEventListener('click', () => this.deleteProfile());

        // Operations
        document.getElementById('runSortarrBtn').addEventListener('click', () => this.runSortarr());
        document.getElementById('stopSortarrBtn').addEventListener('click', () => this.stopSortarr());

        // Log Controls
        document.getElementById('clearLogBtn').addEventListener('click', () => this.clearLog());
        document.getElementById('refreshLogBtn').addEventListener('click', () => this.refreshLog());
        document.getElementById('autoRefreshLog').addEventListener('change', (e) => {
            if (e.target.checked) {
                this.startLogPolling();
            } else {
                this.stopLogPolling();
            }
        });

        // Configuration
        document.getElementById('saveConfigBtn').addEventListener('click', () => this.saveConfiguration());

        // Advanced Controls
        document.getElementById('enableScheduling').addEventListener('change', (e) => {
            document.getElementById('schedulingControls').style.display = e.target.checked ? 'block' : 'none';
        });

        document.getElementById('enableOverrides').addEventListener('change', (e) => {
            document.getElementById('overrideControls').style.display = e.target.checked ? 'block' : 'none';
        });

        document.getElementById('enableRemoteConfig').addEventListener('change', (e) => {
            document.getElementById('remoteControls').style.display = e.target.checked ? 'block' : 'none';
        });

        document.getElementById('createTaskBtn').addEventListener('click', () => this.createScheduledTask());
        document.getElementById('removeTaskBtn').addEventListener('click', () => this.removeScheduledTask());

        // Support
        document.getElementById('donateBtn').addEventListener('click', () => this.openDonationLink());
        document.getElementById('openLocalBtn').addEventListener('click', () => this.openLocalHost());

        // Folder count changes
        document.getElementById('hdMovieCount').addEventListener('change', (e) => {
            this.updateFolderInputs('hdMovies', e.target.value);
        });
        document.getElementById('movieCount4K').addEventListener('change', (e) => {
            this.updateFolderInputs('4kMovies', e.target.value);
        });
        document.getElementById('hdTVCount').addEventListener('change', (e) => {
            this.updateFolderInputs('hdTV', e.target.value);
        });
        document.getElementById('tvCount4K').addEventListener('change', (e) => {
            this.updateFolderInputs('4kTV', e.target.value);
        });
    }

    // API Communication
    async apiCall(endpoint, method = 'GET', data = null) {
        const options = {
            method: method,
            headers: {
                'Content-Type': 'application/json',
            }
        };

        if (data) {
            options.body = JSON.stringify(data);
        }

        try {
            const response = await fetch(`/api/${endpoint}`, options);
            return await response.json();
        } catch (error) {
            console.error('API call failed:', error);
            this.showNotification('Connection error', 'error');
            return null;
        }
    }

    // Profile Management
    async loadProfiles() {
        const profiles = await this.apiCall('profiles');
        const profileSelect = document.getElementById('profileSelect');

        profileSelect.innerHTML = '<option value="">Select Profile...</option>';

        if (profiles) {
            profiles.forEach(profile => {
                const option = document.createElement('option');
                option.value = profile;
                option.textContent = profile;
                profileSelect.appendChild(option);
            });
        }
    }

    async loadProfile() {
        const profileName = document.getElementById('profileSelect').value;
        if (!profileName) {
            this.showNotification('Please select a profile', 'warning');
            return;
        }

        const config = await this.apiCall(`profiles/${profileName}`);
        if (config) {
            this.populateForm(config);
            this.showNotification('Profile loaded successfully', 'success');
        }
    }

    async saveProfile() {
        const profileName = document.getElementById('newProfileName').value.trim() ||
                           document.getElementById('profileSelect').value;

        if (!profileName) {
            this.showNotification('Please enter a profile name', 'warning');
            return;
        }

        const config = this.getFormData();
        const result = await this.apiCall(`profiles/${profileName}`, 'POST', config);

        if (result && result.success) {
            this.showNotification('Profile saved successfully', 'success');
            this.loadProfiles();
            document.getElementById('newProfileName').value = '';
        }
    }

    async deleteProfile() {
        const profileName = document.getElementById('profileSelect').value;
        if (!profileName) {
            this.showNotification('Please select a profile to delete', 'warning');
            return;
        }

        if (confirm(`Are you sure you want to delete profile "${profileName}"?`)) {
            const result = await this.apiCall(`profiles/${profileName}`, 'DELETE');
            if (result && result.success) {
                this.showNotification('Profile deleted successfully', 'success');
                this.loadProfiles();
            }
        }
    }

    // Operations
    async runSortarr() {
        if (this.isRunning) return;

        const result = await this.apiCall('run', 'POST');
        if (result && result.success) {
            this.isRunning = true;
            document.getElementById('runSortarrBtn').style.display = 'none';
            document.getElementById('stopSortarrBtn').style.display = 'inline-block';
            document.getElementById('progressContainer').style.display = 'block';
            this.updateStatus('processing', 'Processing...');
            this.startProgressPolling();
        }
    }

    async stopSortarr() {
        const result = await this.apiCall('stop', 'POST');
        if (result && result.success) {
            this.isRunning = false;
            document.getElementById('runSortarrBtn').style.display = 'inline-block';
            document.getElementById('stopSortarrBtn').style.display = 'none';
            document.getElementById('progressContainer').style.display = 'none';
            this.updateStatus('ready', 'Ready');
            this.stopProgressPolling();
        }
    }

    // Configuration
    async loadConfiguration() {
        const config = await this.apiCall('config');
        if (config) {
            this.populateForm(config);
        }
    }

    async saveConfiguration() {
        const config = this.getFormData();
        const result = await this.apiCall('config', 'POST', config);

        if (result && result.success) {
            this.showNotification('Configuration saved successfully', 'success');
        }
    }

    getFormData() {
        return {
            filebotPath: document.getElementById('filebotPath').value,
            downloadsFolder: document.getElementById('downloadsFolder').value,

            // HD Movies
            enableHDMovies: document.getElementById('enableHDMovies').checked,
            hdMovieCount: parseInt(document.getElementById('hdMovieCount').value),
            hdMovieFolders: this.getFolderValues('hdMovies'),

            // 4K Movies
            enable4KMovies: document.getElementById('enable4KMovies').checked,
            movieCount4K: parseInt(document.getElementById('movieCount4K').value),
            movieFolders4K: this.getFolderValues('4kMovies'),

            // HD TV
            enableHDTV: document.getElementById('enableHDTV').checked,
            hdTVCount: parseInt(document.getElementById('hdTVCount').value),
            hdTVFolders: this.getFolderValues('hdTV'),

            // 4K TV
            enable4KTV: document.getElementById('enable4KTV').checked,
            tvCount4K: parseInt(document.getElementById('tvCount4K').value),
            tvFolders4K: this.getFolderValues('4kTV'),

            // Advanced
            enableScheduling: document.getElementById('enableScheduling').checked,
            scheduleInterval: parseInt(document.getElementById('scheduleInterval').value),
            enableOverrides: document.getElementById('enableOverrides').checked,
            movieFormatOverride: document.getElementById('movieFormatOverride').value,
            tvFormatOverride: document.getElementById('tvFormatOverride').value,
            enableRemoteConfig: document.getElementById('enableRemoteConfig').checked,
            serverPort: parseInt(document.getElementById('serverPort').value),
            enableSystemTray: document.getElementById('enableSystemTray').checked
        };
    }

    getFolderValues(type) {
        const count = parseInt(document.getElementById(`${type}Count`).value) ||
                     parseInt(document.getElementById(`${type.replace('Movies', 'Movie')}Count`).value) ||
                     parseInt(document.getElementById(`${type.replace('TV', '')}Count`).value) || 1;

        const folders = [];
        for (let i = 1; i <= count; i++) {
            const element = document.getElementById(`${type}Folder${i}`) ||
                           document.getElementById(`${type.replace('Movies', 'Movie')}Folder${i}`) ||
                           document.getElementById(`${type}Folder${i}`);
            if (element && element.value) {
                folders.push(element.value);
            }
        }
        return folders;
    }

    populateForm(config) {
        // Basic paths
        if (config.filebotPath) document.getElementById('filebotPath').value = config.filebotPath;
        if (config.downloadsFolder) document.getElementById('downloadsFolder').value = config.downloadsFolder;

        // HD Movies
        if (config.enableHDMovies !== undefined) document.getElementById('enableHDMovies').checked = config.enableHDMovies;
        if (config.hdMovieCount) document.getElementById('hdMovieCount').value = config.hdMovieCount;

        // 4K Movies
        if (config.enable4KMovies !== undefined) document.getElementById('enable4KMovies').checked = config.enable4KMovies;
        if (config.movieCount4K) document.getElementById('movieCount4K').value = config.movieCount4K;

        // HD TV
        if (config.enableHDTV !== undefined) document.getElementById('enableHDTV').checked = config.enableHDTV;
        if (config.hdTVCount) document.getElementById('hdTVCount').value = config.hdTVCount;

        // 4K TV
        if (config.enable4KTV !== undefined) document.getElementById('enable4KTV').checked = config.enable4KTV;
        if (config.tvCount4K) document.getElementById('tvCount4K').value = config.tvCount4K;

        // Advanced
        if (config.enableScheduling !== undefined) {
            document.getElementById('enableScheduling').checked = config.enableScheduling;
            document.getElementById('schedulingControls').style.display = config.enableScheduling ? 'block' : 'none';
        }
        if (config.scheduleInterval) document.getElementById('scheduleInterval').value = config.scheduleInterval;

        if (config.enableOverrides !== undefined) {
            document.getElementById('enableOverrides').checked = config.enableOverrides;
            document.getElementById('overrideControls').style.display = config.enableOverrides ? 'block' : 'none';
        }
        if (config.movieFormatOverride) document.getElementById('movieFormatOverride').value = config.movieFormatOverride;
        if (config.tvFormatOverride) document.getElementById('tvFormatOverride').value = config.tvFormatOverride;

        if (config.enableRemoteConfig !== undefined) {
            document.getElementById('enableRemoteConfig').checked = config.enableRemoteConfig;
            document.getElementById('remoteControls').style.display = config.enableRemoteConfig ? 'block' : 'none';
        }
        if (config.serverPort) document.getElementById('serverPort').value = config.serverPort;
        if (config.enableSystemTray !== undefined) document.getElementById('enableSystemTray').checked = config.enableSystemTray;

        // Update folder inputs
        this.updateFolderInputs('hdMovies', config.hdMovieCount || 1);
        this.updateFolderInputs('4kMovies', config.movieCount4K || 1);
        this.updateFolderInputs('hdTV', config.hdTVCount || 1);
        this.updateFolderInputs('4kTV', config.tvCount4K || 1);

        // Populate folder values after creating inputs
        setTimeout(() => {
            // HD Movies folders
            if (config.hdMovieFolders) {
                config.hdMovieFolders.forEach((folder, index) => {
                    const input = document.getElementById(`hdMoviesFolder${index + 1}`);
                    if (input) input.value = folder;
                });
            }

            // 4K Movies folders
            if (config.movieFolders4K) {
                config.movieFolders4K.forEach((folder, index) => {
                    const input = document.getElementById(`4kMoviesFolder${index + 1}`);
                    if (input) input.value = folder;
                });
            }

            // HD TV folders
            if (config.hdTVFolders) {
                config.hdTVFolders.forEach((folder, index) => {
                    const input = document.getElementById(`hdTVFolder${index + 1}`);
                    if (input) input.value = folder;
                });
            }

            // 4K TV folders
            if (config.tvFolders4K) {
                config.tvFolders4K.forEach((folder, index) => {
                    const input = document.getElementById(`4kTVFolder${index + 1}`);
                    if (input) input.value = folder;
                });
            }
        }, 100); // Small delay to ensure DOM elements are created
    }

    // Folder Management
    updateFolderInputs(type, count) {
        const container = document.getElementById(`${type}Folders`);
        if (!container) return;

        container.innerHTML = '';

        for (let i = 1; i <= count; i++) {
            const div = document.createElement('div');
            div.className = 'input-group';

            const typeLabel = type.replace(/([A-Z])/g, ' $1').replace(/^./, str => str.toUpperCase());

            div.innerHTML = `
                <label>${typeLabel} Folder ${i}:</label>
                <div class="path-input-group">
                    <input type="text" id="${type}Folder${i}" placeholder="Select destination folder...">
                    <button class="btn btn-browse" onclick="browseFolder('${type}Folder${i}')">Browse</button>
                </div>
            `;

            container.appendChild(div);
        }
    }

    // Scheduling
    async createScheduledTask() {
        const interval = document.getElementById('scheduleInterval').value;
        const result = await this.apiCall('schedule/create', 'POST', { interval: parseInt(interval) });

        if (result && result.success) {
            this.showNotification('Scheduled task created successfully', 'success');
            document.getElementById('taskStatus').innerHTML =
                `<span class="status-dot active"></span> Task scheduled to run every ${interval} minutes`;
        }
    }

    async removeScheduledTask() {
        const result = await this.apiCall('schedule/remove', 'POST');

        if (result && result.success) {
            this.showNotification('Scheduled task removed successfully', 'success');
            document.getElementById('taskStatus').innerHTML = '';
        }
    }

    // Log Management
    async refreshLog() {
        const logs = await this.apiCall('logs');
        if (logs) {
            const logContent = document.getElementById('logContent');
            logContent.innerHTML = logs.map(log => `<div class="log-entry">${log}</div>`).join('');
            logContent.scrollTop = logContent.scrollHeight;
        }
    }

    clearLog() {
        document.getElementById('logContent').innerHTML =
            '<div class="log-entry">Log cleared</div>';
    }

    startLogPolling() {
        if (this.logUpdateInterval) return;

        this.logUpdateInterval = setInterval(() => {
            if (document.getElementById('autoRefreshLog').checked) {
                this.refreshLog();
            }
        }, 2000);
    }

    stopLogPolling() {
        if (this.logUpdateInterval) {
            clearInterval(this.logUpdateInterval);
            this.logUpdateInterval = null;
        }
    }

    // Progress Monitoring
    startProgressPolling() {
        this.progressInterval = setInterval(async () => {
            const progress = await this.apiCall('progress');
            if (progress) {
                this.updateProgress(progress.percentage, progress.currentFile);

                if (progress.completed) {
                    this.isRunning = false;
                    document.getElementById('runSortarrBtn').style.display = 'inline-block';
                    document.getElementById('stopSortarrBtn').style.display = 'none';
                    document.getElementById('progressContainer').style.display = 'none';
                    this.updateStatus('ready', 'Completed');
                    this.stopProgressPolling();
                }
            }
        }, 1000);
    }

    stopProgressPolling() {
        if (this.progressInterval) {
            clearInterval(this.progressInterval);
            this.progressInterval = null;
        }
    }

    updateProgress(percentage, currentFile) {
        document.getElementById('progressFill').style.width = `${percentage}%`;
        document.getElementById('progressText').textContent =
            `${percentage}% - ${currentFile || 'Processing...'}`;
    }

    // Status Management
    updateStatus(status = 'ready', text = 'Ready') {
        const statusDot = document.getElementById('statusDot');
        const statusText = document.getElementById('statusText');

        statusDot.className = `status-dot ${status}`;
        statusText.textContent = text;
    }

    // File Browser
    async browseFile(targetId) {
        this.browserTarget = targetId;
        this.browserMode = 'file';
        document.getElementById('browserTitle').textContent = 'Select File';
        await this.openFileBrowser();
    }

    async browseFolder(targetId) {
        this.browserTarget = targetId;
        this.browserMode = 'folder';
        document.getElementById('browserTitle').textContent = 'Select Folder';
        await this.openFileBrowser();
    }

    async openFileBrowser() {
        document.getElementById('fileBrowserModal').style.display = 'block';
        await this.loadBrowserContent();
    }

    closeFileBrowser() {
        document.getElementById('fileBrowserModal').style.display = 'none';
    }

    async loadBrowserContent() {
        const items = await this.apiCall(`browse?path=${encodeURIComponent(this.currentPath)}&type=${this.browserMode}`);
        const container = document.getElementById('browserContent');

        document.getElementById('currentPath').textContent = this.currentPath;
        container.innerHTML = '';

        if (items) {
            items.forEach(item => {
                const div = document.createElement('div');
                div.className = 'browser-item';
                div.onclick = () => this.selectBrowserItem(div, item);

                const icon = item.isDirectory ? '📁' : '📄';
                div.innerHTML = `
                    <span class="browser-item-icon">${icon}</span>
                    <span>${item.name}</span>
                `;
                div.dataset.path = item.path;
                div.dataset.isDirectory = item.isDirectory;

                container.appendChild(div);
            });
        }
    }

    selectBrowserItem(element, item) {
        // Remove previous selection
        document.querySelectorAll('.browser-item').forEach(el => el.classList.remove('selected'));

        if (item.isDirectory && this.browserMode === 'folder') {
            element.classList.add('selected');
        } else if (!item.isDirectory && this.browserMode === 'file') {
            element.classList.add('selected');
        } else if (item.isDirectory) {
            // Navigate into directory
            this.currentPath = item.path;
            this.loadBrowserContent();
        }
    }

    async navigateUp() {
        const parentPath = await this.apiCall(`browse/parent?path=${encodeURIComponent(this.currentPath)}`);
        if (parentPath) {
            this.currentPath = parentPath;
            await this.loadBrowserContent();
        }
    }

    selectPath() {
        const selected = document.querySelector('.browser-item.selected');
        if (selected && this.browserTarget) {
            const path = selected.dataset.path;
            document.getElementById(this.browserTarget).value = path;
            this.closeFileBrowser();
        }
    }

    // External Links
    openDonationLink() {
        window.open('https://www.paypal.com/donate/?business=WBHFP3TMYUHS8&amount=5&no_recurring=1&item_name=Thank+you+for+trying+my+program.+It+took+many+hours+and+late+nights+to+get+it+up+and+running.+Your+donations+are+appreciated%21¤cy_code=USD', '_blank');
    }

    openLocalHost() {
        window.open('http://localhost:6969/', '_blank');
    }

    // Notifications
    showNotification(message, type = 'info') {
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
            z-index: 2000;
            opacity: 0;
            transition: opacity 0.3s;
        `;

        if (type === 'error') {
            notification.style.backgroundColor = '#f44336';
        } else if (type === 'success') {
            notification.style.backgroundColor = '#4caf50';
        } else if (type === 'warning') {
            notification.style.backgroundColor = '#ff9800';
        } else {
            notification.style.backgroundColor = '#2196f3';
        }

        document.body.appendChild(notification);

        setTimeout(() => notification.style.opacity = '1', 100);

        setTimeout(() => {
            notification.style.opacity = '0';
            setTimeout(() => document.body.removeChild(notification), 300);
        }, 5000);
    }
}

// Global functions for inline event handlers
function browseFile(targetId) {
    window.sortarrWeb.browseFile(targetId);
}

function browseFolder(targetId) {
    window.sortarrWeb.browseFolder(targetId);
}

function updateFolderInputs(type, count) {
    window.sortarrWeb.updateFolderInputs(type, count);
}

function closeFileBrowser() {
    window.sortarrWeb.closeFileBrowser();
}

function navigateUp() {
    window.sortarrWeb.navigateUp();
}

function selectPath() {
    window.sortarrWeb.selectPath();
}

// Initialize when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    window.sortarrWeb = new SortarrWeb();
});