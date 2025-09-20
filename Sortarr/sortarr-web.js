// Sortarr Web Interface JavaScript
// Comprehensive functionality for the web interface

class SortarrWeb {
    constructor() {
        this.isRunning = false;
        this.logUpdateInterval = null;
        this.configPollingTimer = null;
        this.currentPath = 'C\\\\';
        this.browserTarget = null;
        this.browserMode = 'folder'; // 'folder' or 'file'
        this.currentProfile = '';

        this.init();
    }

    async init() {
        this.setupTabs();
        this.setupEventListeners();
        this.registerLifecycleHandlers();
        await this.loadSetupData();
        this.startLogPolling();
        this.startConfigPolling();
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
        const profileSelect = document.getElementById('profileSelect');
        if (profileSelect) {
            profileSelect.addEventListener('change', () => this.handleProfileSelection());
        }

        const bindClick = (id, handler) => {
            const element = document.getElementById(id);
            if (element) {
                element.addEventListener('click', handler);
            }
        };

        const bindChange = (id, handler) => {
            const element = document.getElementById(id);
            if (element) {
                element.addEventListener('change', handler);
            }
        };

        // Profile Management
        bindClick('loadProfileBtn', () => this.loadProfile());
        bindClick('saveProfileBtn', () => this.saveProfile());
        bindClick('deleteProfileBtn', () => this.deleteProfile());

        // Operations
        bindClick('runSortarrBtn', () => this.runSortarr());
        bindClick('stopSortarrBtn', () => this.stopSortarr());

        // Log Controls
        bindClick('clearLogBtn', () => this.clearLog());
        bindClick('refreshLogBtn', () => this.refreshLog());
        const autoRefreshLog = document.getElementById('autoRefreshLog');
        if (autoRefreshLog) {
            autoRefreshLog.addEventListener('change', (e) => {
                if (e.target.checked) {
                    this.startLogPolling();
                } else {
                    this.stopLogPolling();
                }
            });
        }

        // Configuration
        bindClick('saveConfigBtn', () => this.saveConfiguration());

        // Advanced Controls
        const enableScheduling = document.getElementById('enableScheduling');
        if (enableScheduling) {
            enableScheduling.addEventListener('change', (e) => {
                const schedulingControls = document.getElementById('schedulingControls');
                if (schedulingControls) {
                    schedulingControls.style.display = e.target.checked ? 'block' : 'none';
                }
            });
        }

        const enableOverrides = document.getElementById('enableOverrides');
        if (enableOverrides) {
            enableOverrides.addEventListener('change', (e) => {
                const overrideControls = document.getElementById('overrideControls');
                if (overrideControls) {
                    overrideControls.style.display = e.target.checked ? 'block' : 'none';
                }
            });
        }

        bindClick('createTaskBtn', () => this.createScheduledTask());
        bindClick('removeTaskBtn', () => this.removeScheduledTask());

        // Support
        bindClick('donateBtn', () => this.openDonationLink());
        bindClick('openLocalBtn', () => this.openLocalHost());

        // Folder count changes
        bindChange('hdMovieCount', (e) => {
            const newCount = parseInt(e.target.value, 10) || 1;
            const existing = this.collectCurrentFolderValues('hdMovies');
            this.updateFolderInputs('hdMovies', newCount, existing);
        });
        bindChange('movieCount4K', (e) => {
            const newCount = parseInt(e.target.value, 10) || 1;
            const existing = this.collectCurrentFolderValues('4kMovies');
            this.updateFolderInputs('4kMovies', newCount, existing);
        });
        bindChange('hdTVCount', (e) => {
            const newCount = parseInt(e.target.value, 10) || 1;
            const existing = this.collectCurrentFolderValues('hdTV');
            this.updateFolderInputs('hdTV', newCount, existing);
        });
        bindChange('tvCount4K', (e) => {
            const newCount = parseInt(e.target.value, 10) || 1;
            const existing = this.collectCurrentFolderValues('4kTV');
            this.updateFolderInputs('4kTV', newCount, existing);
        });
    }



    // API Communication
    async apiCall(endpoint, method = 'GET', data = null) {
        const options = {
            method,
            headers: {
                'Content-Type': 'application/json',
            },
        };

        if (data !== null && data !== undefined) {
            options.body = JSON.stringify(data);
        }

        try {
            const response = await fetch(`/api/${endpoint}`, options);
            const status = response.status;
            let payload = null;

            if (status !== 204) {
                const textBody = await response.text();
                if (textBody) {
                    try {
                        payload = JSON.parse(textBody);
                    } catch (parseError) {
                        console.warn('Failed to parse response JSON', parseError);
                    }
                }
            }

            if (!response.ok) {
                const message = (payload && payload.error) ? payload.error : `Request failed (${status})`;
                this.showNotification(message, 'error');
                return null;
            }

            return payload;
        } catch (error) {
            console.error('API call failed:', error);
            this.showNotification('Connection error', 'error');
            return null;
        }
    }
    registerLifecycleHandlers() {
        window.addEventListener('beforeunload', () => {
            this.stopLogPolling();
            this.stopConfigPolling();
        });
    }

    async loadSetupData() {
        const setup = await this.apiCall('setup');
        if (!setup) {
            await this.loadProfiles();
            await this.refreshConfig(true);
            return;
        }

        this.populateProfiles(setup.profiles || [], setup.currentProfile || '');

        if (setup.config) {
            this.populateForm(setup.config, { silent: true });
        } else {
            await this.refreshConfig(true);
        }

        await this.refreshScheduleStatus();
    }

    populateProfiles(profiles = [], selectedProfile = '') {
        const profileSelect = document.getElementById('profileSelect');
        const list = Array.isArray(profiles) ? profiles : [];

        if (!profileSelect) {
            this.currentProfile = selectedProfile || this.currentProfile || '';
            this.updateProfileBanner();
            return;
        }

        profileSelect.innerHTML = '<option value="">Select Profile...</option>';

        list.forEach(profile => {
            const option = document.createElement('option');
            option.value = profile;
            option.textContent = profile;
            profileSelect.appendChild(option);
        });

        this.setProfileSelection(selectedProfile);
    }

    setProfileSelection(profileName = undefined) {
        const profileSelect = document.getElementById('profileSelect');
        let targetProfile = profileName;

        if (!profileSelect) {
            this.currentProfile = targetProfile || this.currentProfile || '';
            this.updateProfileBanner();
            return;
        }

        const optionValues = Array.from(profileSelect.options).map(opt => opt.value);
        if (targetProfile && optionValues.includes(targetProfile)) {
            profileSelect.value = targetProfile;
        } else if (profileSelect.options.length > 1) {
            profileSelect.selectedIndex = 1;
            targetProfile = profileSelect.value;
        } else {
            profileSelect.selectedIndex = 0;
            targetProfile = '';
        }

        this.currentProfile = targetProfile || '';
        this.updateProfileBanner();
    }

    async handleProfileSelection() {
        const profileSelect = document.getElementById('profileSelect');
        const profileName = profileSelect ? profileSelect.value : '';
        if (!profileName) {
            return;
        }

        const result = await this.apiCall('profiles/select', 'POST', { profileName });
        if (result && result.success) {
            const appliedProfile = result.currentProfile || profileName;
            this.setProfileSelection(appliedProfile);

            if (result.config) {
                this.populateForm(result.config, { silent: true });
            } else {
                await this.refreshConfig(true);
            }

            this.showNotification(`Profile '${appliedProfile}' loaded`, 'success');
        }
    }

    startConfigPolling() {
        if (this.configPollingTimer) {
            return;
        }
        this.configPollingTimer = setInterval(() => this.refreshConfig(), 5000);
    }

    stopConfigPolling() {
        if (!this.configPollingTimer) {
            return;
        }

        clearInterval(this.configPollingTimer);
        this.configPollingTimer = null;
    }

    async refreshConfig(force = false) {
        if (!force && this.isUserEditingSetup()) {
            return;
        }

        const config = await this.apiCall('config');
        if (config) {
            this.populateForm(config, { silent: !force });
        }
    }

    isUserEditingSetup() {
        const active = document.activeElement;
        if (!active) {
            return false;
        }

        const tag = active.tagName;
        if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') {
            return active.closest('#setup-tab') !== null;
        }

        return false;
    }

    collectCurrentFolderValues(type) {
        const container = document.getElementById(`${type}Folders`) ||
                           document.getElementById(`${type.replace('Movies', 'Movie')}Folders`) ||
                           document.getElementById(`${type.replace('TV', '')}Folders`);
        if (!container) {
            return [];
        }

        const inputs = container.querySelectorAll('input');
        return Array.from(inputs).map((input, index) => {
            const value = (input.value || '').trim();
            if (index === 0 && value.toLowerCase() === 'default') {
                return '';
            }
            return value;
        });
    }

    updateProfileBanner() {
        const banner = document.getElementById('activeProfileDisplay');
        if (!banner) {
            return;
        }

        if (this.currentProfile) {
            banner.textContent = this.currentProfile;
            banner.classList.remove('empty');
        } else {
            banner.textContent = 'No profile selected';
            banner.classList.add('empty');
        }
    }



    // Profile Management
    async loadProfiles(selectedProfile = null) {
        const profiles = await this.apiCall('profiles');
        const requestedProfile = selectedProfile ?? this.currentProfile;
        this.populateProfiles(profiles || [], requestedProfile);
    }


    async loadProfile() {
        const profileSelect = document.getElementById('profileSelect');
        const profileName = profileSelect ? profileSelect.value : '';
        if (!profileName) {
            this.showNotification('Please select a profile', 'warning');
            return;
        }

        await this.handleProfileSelection();
    }


    async saveProfile() {
        const profileSelect = document.getElementById('profileSelect');
        const profileName = document.getElementById('newProfileName').value.trim() || (profileSelect ? profileSelect.value : '');

        if (!profileName) {
            this.showNotification('Please enter a profile name', 'warning');
            return;
        }

        const config = this.getFormData();
        const result = await this.apiCall(`profiles/${encodeURIComponent(profileName)}`, 'POST', config);

        if (result && result.success) {
            document.getElementById('newProfileName').value = '';
            await this.loadProfiles(profileName);
            await this.refreshConfig(true);
            this.showNotification('Profile saved successfully', 'success');
        }
    }


    async deleteProfile() {
        const profileSelect = document.getElementById('profileSelect');
        const profileName = profileSelect ? profileSelect.value : '';
        if (!profileName) {
            this.showNotification('Please select a profile to delete', 'warning');
            return;
        }

        if (confirm(`Are you sure you want to delete profile "${profileName}"?`)) {
            const result = await this.apiCall(`profiles/${encodeURIComponent(profileName)}`, 'DELETE');
            if (result && result.success) {
                await this.loadProfiles();
                await this.refreshConfig(true);
                this.showNotification('Profile deleted successfully', 'success');
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
        } else if (result && result.error) {
            this.showNotification(result.error, 'error');
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
        } else if (result && result.error) {
            this.showNotification(result.error, 'warning');
        }
    }

    // Configuration
    async loadConfiguration() {
        await this.refreshConfig(true);
    }


    async saveConfiguration() {
        const config = this.getFormData();
        const result = await this.apiCall('config', 'POST', config);

        if (result && result.success) {
            if (result.config) {
                this.populateForm(result.config, { silent: true });
            } else {
                await this.refreshConfig(true);
            }

            this.showNotification('Configuration saved successfully', 'success');
            await this.refreshScheduleStatus();
        }
    }


    getFormData() {
        const hdMovieCount = parseInt(document.getElementById('hdMovieCount').value, 10) || 1;
        const movie4kCount = parseInt(document.getElementById('movieCount4K').value, 10) || 1;
        const hdTvCount = parseInt(document.getElementById('hdTVCount').value, 10) || 1;
        const tv4kCount = parseInt(document.getElementById('tvCount4K').value, 10) || 1;

        return {
            currentProfile: this.currentProfile,
            filebotPath: document.getElementById('filebotPath').value,
            downloadsFolder: document.getElementById('downloadsFolder').value,

            // HD Movies
            enableHDMovies: document.getElementById('enableHDMovies').checked,
            hdMovieCount,
            hdMovieFolders: this.getFolderValues('hdMovies', hdMovieCount),

            // 4K Movies
            enable4KMovies: document.getElementById('enable4KMovies').checked,
            movieCount4K: movie4kCount,
            movieFolders4K: this.getFolderValues('4kMovies', movie4kCount),

            // HD TV
            enableHDTV: document.getElementById('enableHDTV').checked,
            hdTVCount: hdTvCount,
            hdTVFolders: this.getFolderValues('hdTV', hdTvCount),

            // 4K TV
            enable4KTV: document.getElementById('enable4KTV').checked,
            tvCount4K: tv4kCount,
            tvFolders4K: this.getFolderValues('4kTV', tv4kCount),

            // Advanced
            enableScheduling: document.getElementById('enableScheduling').checked,
            scheduleInterval: parseInt(document.getElementById('scheduleInterval').value, 10) || 1,
            enableOverrides: document.getElementById('enableOverrides').checked,
            movieFormatOverride: document.getElementById('movieFormatOverride').value,
            tvFormatOverride: document.getElementById('tvFormatOverride').value,
            enableSystemTray: document.getElementById('enableSystemTray').checked
        };
    }


    getFolderValues(type, count) {
        const safeCount = Math.max(1, count || 1);
        const values = [];

        for (let i = 1; i <= safeCount; i++) {
            const element = document.getElementById(`${type}Folder${i}`) ||
                           document.getElementById(`${type.replace('Movies', 'Movie')}Folder${i}`) ||
                           document.getElementById(`${type.replace('TV', '')}Folder${i}`);

            let value = element ? element.value.trim() : '';
            if (i === 1 && value.toLowerCase() === 'default') {
                value = '';
            }

            values.push(value);
        }

        return values;
    }


    populateForm(config = {}, options = {}) {
        if (!config) {
            return;
        }

        const { silent = false } = options;

        const setValue = (id, value) => {
            const element = document.getElementById(id);
            if (!element || value === undefined || value === null) {
                return;
            }

            if (element.tagName === 'SELECT') {
                element.value = String(value);
            } else {
                element.value = value;
            }
        };

        const setCheckbox = (id, value) => {
            if (typeof value !== 'boolean') {
                return;
            }
            const element = document.getElementById(id);
            if (element) {
                element.checked = value;
            }
        };

        const sanitizeCount = (value, fallback) => {
            const parsed = parseInt(value, 10);
            if (Number.isFinite(parsed) && parsed > 0) {
                return parsed;
            }
            return Math.max(1, fallback || 1);
        };

        if ('filebotPath' in config) setValue('filebotPath', config.filebotPath || '');
        if ('downloadsFolder' in config) setValue('downloadsFolder', config.downloadsFolder || '');

        setCheckbox('enableHDMovies', config.enableHDMovies);
        setCheckbox('enable4KMovies', config.enable4KMovies);
        setCheckbox('enableHDTV', config.enableHDTV);
        setCheckbox('enable4KTV', config.enable4KTV);
        setCheckbox('enableScheduling', config.enableScheduling);
        setCheckbox('enableOverrides', config.enableOverrides);
        setCheckbox('enableSystemTray', config.enableSystemTray);

        const currentHdMovies = parseInt(document.getElementById('hdMovieCount').value, 10) || 1;
        const current4kMovies = parseInt(document.getElementById('movieCount4K').value, 10) || 1;
        const currentHdTv = parseInt(document.getElementById('hdTVCount').value, 10) || 1;
        const current4kTv = parseInt(document.getElementById('tvCount4K').value, 10) || 1;

        const hdMovieCount = sanitizeCount(config.hdMovieCount, currentHdMovies);
        const movie4kCount = sanitizeCount(config.movieCount4K, current4kMovies);
        const hdTvCount = sanitizeCount(config.hdTVCount, currentHdTv);
        const tv4kCount = sanitizeCount(config.tvCount4K, current4kTv);

        setValue('hdMovieCount', hdMovieCount);
        setValue('movieCount4K', movie4kCount);
        setValue('hdTVCount', hdTvCount);
        setValue('tvCount4K', tv4kCount);

        const hdMovieFolders = Array.isArray(config.hdMovieFolders) ? config.hdMovieFolders : this.collectCurrentFolderValues('hdMovies');
        const movie4kFolders = Array.isArray(config.movieFolders4K) ? config.movieFolders4K : this.collectCurrentFolderValues('4kMovies');
        const hdTvFolders = Array.isArray(config.hdTVFolders) ? config.hdTVFolders : this.collectCurrentFolderValues('hdTV');
        const tv4kFolders = Array.isArray(config.tvFolders4K) ? config.tvFolders4K : this.collectCurrentFolderValues('4kTV');

        this.updateFolderInputs('hdMovies', hdMovieCount, hdMovieFolders);
        this.updateFolderInputs('4kMovies', movie4kCount, movie4kFolders);
        this.updateFolderInputs('hdTV', hdTvCount, hdTvFolders);
        this.updateFolderInputs('4kTV', tv4kCount, tv4kFolders);

        if ('scheduleInterval' in config) {
            const interval = Math.max(1, parseInt(config.scheduleInterval, 10) || 1);
            setValue('scheduleInterval', interval);
        }
        if ('movieFormatOverride' in config) setValue('movieFormatOverride', config.movieFormatOverride || '');
        if ('tvFormatOverride' in config) setValue('tvFormatOverride', config.tvFormatOverride || '');
        const schedulingControls = document.getElementById('schedulingControls');
        if (schedulingControls) {
            schedulingControls.style.display = document.getElementById('enableScheduling').checked ? 'block' : 'none';
        }

        const overrideControls = document.getElementById('overrideControls');
        if (overrideControls) {
            overrideControls.style.display = document.getElementById('enableOverrides').checked ? 'block' : 'none';
        }

        this.updateTaskStatus(config.isTaskScheduled, config.scheduleInterval);

        if (config.currentProfile !== undefined) {
            this.setProfileSelection(config.currentProfile);
        } else if (!silent) {
            this.updateProfileBanner();
        }
    }


    // Folder Management
    updateFolderInputs(type, count, values = []) {
        const container = document.getElementById(`${type}Folders`) ||
                           document.getElementById(`${type.replace('Movies', 'Movie')}Folders`) ||
                           document.getElementById(`${type.replace('TV', '')}Folders`);
        if (!container) {
            return;
        }

        const safeCount = Math.max(1, count || 1);
        const normalizedValues = Array.isArray(values) ? values : [];
        container.innerHTML = '';

        for (let i = 1; i <= safeCount; i++) {
            const div = document.createElement('div');
            div.className = 'input-group';

            const typeLabel = type.replace(/([A-Z])/g, ' $1').replace(/^./, (str) => str.toUpperCase());
            const inputId = `${type}Folder${i}`;
            div.innerHTML = `
                <label>${typeLabel} Folder ${i}:</label>
                <div class="path-input-group">
                    <input type="text" id="${inputId}" placeholder="Select destination folder...">
                    <button class="btn btn-browse" onclick="browseFolder('${inputId}')">Browse</button>
                </div>
            `;

            container.appendChild(div);

            const input = div.querySelector('input');
            const value = normalizedValues[i - 1];
            if (value) {
                input.value = value;
            } else if (i === 1) {
                input.value = 'Default';
            }
        }
    }


    // Scheduling
    async createScheduledTask() {
        const interval = Math.max(1, parseInt(document.getElementById('scheduleInterval').value, 10) || 1);
        const result = await this.apiCall('schedule/create', 'POST', { interval });

        if (result && result.success) {
            this.showNotification('Scheduled task created successfully', 'success');
            await this.refreshScheduleStatus();
        }
    }

    async removeScheduledTask() {
        const result = await this.apiCall('schedule/remove', 'POST');

        if (result && result.success) {
            this.showNotification('Scheduled task removed successfully', 'success');
            await this.refreshScheduleStatus();
        }
    }

    async refreshScheduleStatus() {
        const status = await this.apiCall('schedule/status');
        if (!status || !status.success) {
            return;
        }

        if (typeof status.interval === 'number') {
            const intervalInput = document.getElementById('scheduleInterval');
            if (intervalInput) {
                intervalInput.value = Math.max(1, status.interval);
            }
        }

        if (typeof status.schedulingEnabled === 'boolean') {
            const enableScheduling = document.getElementById('enableScheduling');
            if (enableScheduling) {
                enableScheduling.checked = status.schedulingEnabled;
            }
        }

        const schedulingControls = document.getElementById('schedulingControls');
        if (schedulingControls) {
            const enableScheduling = document.getElementById('enableScheduling');
            const isEnabled = enableScheduling ? enableScheduling.checked : false;
            schedulingControls.style.display = (status.isScheduled || isEnabled) ? 'block' : 'none';
        }

        this.updateTaskStatus(status.isScheduled, status.interval);
    }

    updateTaskStatus(isScheduled, interval) {
        const taskStatus = document.getElementById('taskStatus');
        if (!taskStatus) {
            return;
        }

        if (isScheduled) {
            const minutes = Math.max(1, parseInt(interval, 10) || 1);
            taskStatus.innerHTML = `<span class="status-dot active"></span> Task scheduled to run every ${minutes} minutes`;
        } else {
            taskStatus.innerHTML = '';
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
            if (!progress) {
                return;
            }

            if (typeof progress.percentage === 'number') {
                this.updateProgress(progress.percentage, progress.currentFile);
            }

            if (progress.status) {
                const normalized = progress.status.toLowerCase();
                const statusType = normalized.includes('error') ? 'error' : (progress.isRunning ? 'processing' : 'ready');
                this.updateStatus(statusType, progress.status);
            }

            if (progress.isRunning) {
                this.isRunning = true;
                document.getElementById('runSortarrBtn').style.display = 'none';
                document.getElementById('stopSortarrBtn').style.display = 'inline-block';
                document.getElementById('progressContainer').style.display = 'block';
            } else {
                this.isRunning = false;
                document.getElementById('runSortarrBtn').style.display = 'inline-block';
                document.getElementById('stopSortarrBtn').style.display = 'none';
                document.getElementById('progressContainer').style.display = 'none';
                if (!progress.status) {
                    this.updateStatus('ready', 'Ready');
                }
                this.stopProgressPolling();
            }

            if (progress.completed) {
                this.stopProgressPolling();
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
    const value = parseInt(count, 10) || 1;
    const existing = window.sortarrWeb.collectCurrentFolderValues(type);
    window.sortarrWeb.updateFolderInputs(type, value, existing);
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

