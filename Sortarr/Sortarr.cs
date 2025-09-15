using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using TaskScheduler = Microsoft.Win32.TaskScheduler;
using SystemAction = System.Action;

namespace Sortarr
{
    public partial class Sortarr : Form
    {
        private string profilePath = Path.Combine(Application.StartupPath, "profiles");
        private string[] mediaExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".m4v" };
        private List<(string Original, string Renamed)> fileMappings = new List<(string Original, string Renamed)>();
        private string logFilePath = Path.Combine(Application.StartupPath, "filebot_log.txt");
        private bool isAutomated = false;
        private HttpListener httpListener;
        private bool isServerRunning = false;

        // Dictionary to map media types to their controls
        private Dictionary<string, (CheckBox CheckBox, NumericUpDown UpDown, TextBox[] TextBoxes, Button[] BrowseButtons, Label LocationLabel)> mediaControls;
        // List of Advanced tab checkboxes
        private CheckBox[] advancedCheckboxes;
        // Logger instance
        private BufferedLogger logger;
        // System tray components
        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayContextMenu;
        private bool minimizeToTray = false;
        private bool allowVisible = true;

        // Buffered logging class to optimize file I/O operations
        private class BufferedLogger : IDisposable
        {
            private readonly StreamWriter writer;
            private readonly object lockObj = new object();
            private string cachedTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            private DateTime lastTimestampUpdate = DateTime.Now;

            public BufferedLogger(string filePath)
            {
                writer = new StreamWriter(filePath, append: true, encoding: Encoding.UTF8) { AutoFlush = false };
            }

            public string GetTimestamp()
            {
                var now = DateTime.Now;
                if ((now - lastTimestampUpdate).TotalSeconds >= 1)
                {
                    cachedTimestamp = now.ToString("yyyy-MM-dd HH:mm:ss");
                    lastTimestampUpdate = now;
                }
                return cachedTimestamp;
            }

            public void Log(string message)
            {
                lock (lockObj)
                {
                    writer.WriteLine($"[{GetTimestamp()}] {message}");
                }
            }

            public void LogError(string context, string error)
            {
                lock (lockObj)
                {
                    writer.WriteLine($"[{GetTimestamp()}] {context}\nException: {error}");
                    writer.WriteLine();
                }
            }

            public void Flush()
            {
                lock (lockObj)
                {
                    writer?.Flush();
                }
            }

            public void Dispose()
            {
                lock (lockObj)
                {
                    writer?.Flush();
                    writer?.Dispose();
                }
            }
        }

        public Sortarr()
        {
            // Initialize logger first
            logger = new BufferedLogger(logFilePath);

            // Check for command-line arguments early
            string[] args = Environment.GetCommandLineArgs();
            isAutomated = args.Contains("--auto");
            if (isAutomated)
            {
                this.Visible = false; // Hide UI immediately
                this.ShowInTaskbar = false; // Prevent taskbar icon
                this.WindowState = FormWindowState.Minimized; // Start minimized in automated mode
                LogMessage("Running in automated mode (--auto), window minimized.");
            }

            InitializeComponent();
            Directory.CreateDirectory(profilePath);

            // Initialize system tray
            SetupSystemTray();

            // Hook up form closing event
            this.FormClosing += Sortarr_FormClosing;

            // Configure NumericUpDown for scheduling
            numericUpDownSchedule.Minimum = 1;
            numericUpDownSchedule.Maximum = 30;
            numericUpDownSchedule.Value = 1;
            numericUpDownSchedule.Enabled = false;
            numericUpDownSchedule.Visible = false;
            runEveryTxt.Visible = false;
            minsTxt.Visible = false;
            automateBtn.Enabled = false;
            automateBtn.Visible = false;
            removeSortarrAutomation.Enabled = false;
            removeSortarrAutomation.Visible = false;

            // Configure override controls
            overrideMoviesTxt.Visible = false;
            overrideTVShowsTxt.Visible = false;
            overrideMoviesTextBox.Visible = false;
            overrideTVShowsTextBox.Visible = false;
            overrideMoviesTextBox.Enabled = false;
            overrideTVShowsTextBox.Enabled = false;

            // Configure remote config controls
            openLocalHostBtn.Enabled = false;
            openLocalHostBtn.Visible = true;
            sortarrPortTxt.Visible = false; // Hide label initially

            // Set default placeholder text
            sourceFolderMovies1.Text = "Default";
            sourceFolder4kMovies1.Text = "Default";
            sourceFolderTVShows1.Text = "Default";
            sourceFolder4kTVShows1.Text = "Default";

            // Initialize media controls dictionary
            mediaControls = new Dictionary<string, (CheckBox, NumericUpDown, TextBox[], Button[], Label)>
            {
                { "HDMovie", (checkboxMovie, upDownMovie,
                    new[] { sourceFolderMovies1, sourceFolderMovies2, sourceFolderMovies3, sourceFolderMovies4, sourceFolderMovies5 },
                    new[] { browseMovieLocationBtn1, browseMovieLocationBtn2, browseMovieLocationBtn3, browseMovieLocationBtn4, browseMovieLocationBtn5 },
                    movieLocationTxt) },
                { "4KMovie", (checkbox4KMovie, upDown4KMovie,
                    new[] { sourceFolder4kMovies1, sourceFolder4kMovies2, sourceFolder4kMovies3, sourceFolder4kMovies4, sourceFolder4kMovies5 },
                    new[] { browse4kMovieLocationBtn1, browse4kMovieLocationBtn2, browse4kMovieLocationBtn3, browse4kMovieLocationBtn4, browse4kMovieLocationBtn5 },
                    movieLocation4kTxt) },
                { "HDTVShow", (checkboxTVShow, upDownTVShow,
                    new[] { sourceFolderTVShows1, sourceFolderTVShows2, sourceFolderTVShows3, sourceFolderTVShows4, sourceFolderTVShows5 },
                    new[] { browseTVShowLocationBtn1, browseTVShowLocationBtn2, browseTVShowLocationBtn3, browseTVShowLocationBtn4, browseTVShowLocationBtn5 },
                    tvShowLocationTxt) },
                { "4KTVShow", (checkbox4KTVShow, upDown4KTVShow,
                    new[] { sourceFolder4kTVShows1, sourceFolder4kTVShows2, sourceFolder4kTVShows3, sourceFolder4kTVShows4, sourceFolder4kTVShows5 },
                    new[] { browseTVShowLocation4kBtn1, browseTVShowLocation4kBtn2, browseTVShowLocation4kBtn3, browseTVShowLocation4kBtn4, browseTVShowLocation4kBtn5 },
                    tvShowLocation4kTxt) }
            };

            // Configure NumericUpDown controls for media types
            foreach (var mediaType in mediaControls)
            {
                mediaType.Value.UpDown.Minimum = 1;
                mediaType.Value.UpDown.Maximum = 5;
                mediaType.Value.UpDown.Value = 1;
                mediaType.Value.UpDown.ValueChanged += (s, e) => UpdateControlVisibility();
            }

            // Initialize advanced checkboxes
            advancedCheckboxes = new[] { checkboxScheduleTask, checkboxOverrideSortarrParameters, checkboxEnableRemoteConfig };
            foreach (var checkbox in advancedCheckboxes)
                checkbox.Enabled = false;

            // Disable button initially
            sortarrBtn.Enabled = false;

            LoadProfilesToDropdown();

            // Handle automated mode
            if (isAutomated)
            {
                // Run automated mode logic asynchronously
                Task.Run(async () => await HandleAutomatedModeAsync());
            }
            else
            {
                HookUpEventHandlers();
                UpdateControlVisibility();
                ValidateSetup();

                // Start HTTP server if remote config is enabled
                if (checkboxEnableRemoteConfig.Checked)
                    StartHttpServer();
            }
        }

        private async Task HandleAutomatedModeAsync()
        {
            try
            {
                var profiles = Directory.GetFiles(profilePath, "*.txt").Select(Path.GetFileNameWithoutExtension).OrderBy(p => p).ToList();
                if (!profiles.Any())
                {
                    LogError("No profiles found for automated mode", "");
                    logger?.Flush(); // Ensure log is written before exit
                    await Task.Delay(1000); // Allow logging
                    Application.Exit();
                    return;
                }

                profileSelector.Text = profiles.First();
                loadProfileBtn_Click(this, EventArgs.Empty);
                LogMessage($"Automatically loaded first profile: {profileSelector.Text}");

                // Validate inputs
                if (!ValidateInputs())
                {
                    LogError($"Validation failed for automated mode. Check profile settings in {profileSelector.Text}.txt", "");
                    logger?.Flush(); // Ensure log is written before exit
                    await Task.Delay(1000); // Allow logging
                    StopHttpServer();
                    Application.Exit();
                    return;
                }

                await RunSortarrProcess();

                // Extended delay to ensure all operations complete
                await Task.Delay(5000);

                StopHttpServer();
                Application.Exit();
            }
            catch (Exception ex)
            {
                LogError("Automated mode error", ex);
                logger?.Flush(); // Ensure log is written before exit
                await Task.Delay(1000);
                Application.Exit();
            }
        }

        private void HookUpEventHandlers()
        {
            // Browse button handlers
            browseMovieLocationBtn1.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderMovies1);
            browseMovieLocationBtn2.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderMovies2);
            browseMovieLocationBtn3.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderMovies3);
            browseMovieLocationBtn4.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderMovies4);
            browseMovieLocationBtn5.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderMovies5);
            browse4kMovieLocationBtn1.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kMovies1);
            browse4kMovieLocationBtn2.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kMovies2);
            browse4kMovieLocationBtn3.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kMovies3);
            browse4kMovieLocationBtn4.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kMovies4);
            browse4kMovieLocationBtn5.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kMovies5);
            browseTVShowLocationBtn1.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderTVShows1);
            browseTVShowLocationBtn2.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderTVShows2);
            browseTVShowLocationBtn3.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderTVShows3);
            browseTVShowLocationBtn4.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderTVShows4);
            browseTVShowLocationBtn5.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolderTVShows5);
            browseTVShowLocation4kBtn1.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kTVShows1);
            browseTVShowLocation4kBtn2.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kTVShows2);
            browseTVShowLocation4kBtn3.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kTVShows3);
            browseTVShowLocation4kBtn4.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kTVShows4);
            browseTVShowLocation4kBtn5.Click += (s, e) => BrowseFolderIntoTextBox(sourceFolder4kTVShows5);
            browseFilebotLocationBtn.Click += browseFilebotLocationBtn_Click;
            browseDownloadsLocationBtn.Click += browseDownloadsLocationBtn_Click;
            saveProfileBtn.Click += saveProfileBtn_Click;
            loadProfileBtn.Click += loadProfileBtn_Click;
            deleteProfileBtn.Click += deleteProfileBtn_Click;
            sortarrBtn.Click += sortarrBtn_Click;
            checkboxScheduleTask.CheckedChanged += checkboxScheduleTask_CheckedChanged;
            checkboxOverrideSortarrParameters.CheckedChanged += checkboxOverrideSortarrParameters_CheckedChanged;
            checkboxEnableRemoteConfig.CheckedChanged += checkboxEnableRemoteConfig_CheckedChanged;
            automateBtn.Click += automateBtn_Click;
            removeSortarrAutomation.Click += removeSortarrAutomation_Click;
            donateBtn.Click += donateBtn_Click;
            openLocalHostBtn.Click += openLocalHostBtn_Click;

            // Add CheckedChanged handlers for media checkboxes
            checkboxMovie.CheckedChanged += (s, e) => UpdateControlVisibility();
            checkbox4KMovie.CheckedChanged += (s, e) => UpdateControlVisibility();
            checkboxTVShow.CheckedChanged += (s, e) => UpdateControlVisibility();
            checkbox4KTVShow.CheckedChanged += (s, e) => UpdateControlVisibility();

            // Add placeholder event handlers for default directories
            sourceFolderMovies1.Enter += (s, e) => TextBox_Enter(sourceFolderMovies1, "Default");
            sourceFolderMovies1.Leave += (s, e) => TextBox_Leave(sourceFolderMovies1, "Default");
            sourceFolder4kMovies1.Enter += (s, e) => TextBox_Enter(sourceFolder4kMovies1, "Default");
            sourceFolder4kMovies1.Leave += (s, e) => TextBox_Leave(sourceFolder4kMovies1, "Default");
            sourceFolderTVShows1.Enter += (s, e) => TextBox_Enter(sourceFolderTVShows1, "Default");
            sourceFolderTVShows1.Leave += (s, e) => TextBox_Leave(sourceFolderTVShows1, "Default");
            sourceFolder4kTVShows1.Enter += (s, e) => TextBox_Enter(sourceFolder4kTVShows1, "Default");
            sourceFolder4kTVShows1.Leave += (s, e) => TextBox_Leave(sourceFolder4kTVShows1, "Default");
        }

        private void TextBox_Enter(TextBox textBox, string placeholder)
        {
            if (textBox.Text == placeholder)
            {
                textBox.Text = "";
            }
        }

        private void TextBox_Leave(TextBox textBox, string placeholder)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = placeholder;
            }
        }

        private void checkboxScheduleTask_CheckedChanged(object sender, EventArgs e)
        {
            bool isSetupValid = ValidateSetup();
            numericUpDownSchedule.Visible = checkboxScheduleTask.Checked;
            runEveryTxt.Visible = checkboxScheduleTask.Checked;
            minsTxt.Visible = checkboxScheduleTask.Checked;
            numericUpDownSchedule.Enabled = checkboxScheduleTask.Checked && isSetupValid;
            automateBtn.Enabled = checkboxScheduleTask.Checked && isSetupValid && !IsTaskScheduled();
            automateBtn.Visible = checkboxScheduleTask.Checked && isSetupValid;
            removeSortarrAutomation.Enabled = IsTaskScheduled();
            removeSortarrAutomation.Visible = IsTaskScheduled();
        }

        private void checkboxOverrideSortarrParameters_CheckedChanged(object sender, EventArgs e)
        {
            bool isSetupValid = ValidateSetup();
            overrideMoviesTxt.Visible = checkboxOverrideSortarrParameters.Checked;
            overrideTVShowsTxt.Visible = checkboxOverrideSortarrParameters.Checked;
            overrideMoviesTextBox.Visible = checkboxOverrideSortarrParameters.Checked;
            overrideTVShowsTextBox.Visible = checkboxOverrideSortarrParameters.Checked;
            overrideMoviesTextBox.Enabled = checkboxOverrideSortarrParameters.Checked && isSetupValid;
            overrideTVShowsTextBox.Enabled = checkboxOverrideSortarrParameters.Checked && isSetupValid;
        }

        private void checkboxEnableRemoteConfig_CheckedChanged(object sender, EventArgs e)
        {
            openLocalHostBtn.Enabled = checkboxEnableRemoteConfig.Checked;
            sortarrPortTxt.Visible = checkboxEnableRemoteConfig.Checked;
            if (checkboxEnableRemoteConfig.Checked)
                StartHttpServer();
            else
                StopHttpServer();
        }

        private void StartHttpServer()
        {
            if (isServerRunning) return;

            try
            {
                // Clean up any existing listener first
                if (httpListener != null)
                {
                    try
                    {
                        httpListener.Stop();
                        httpListener.Close();
                    }
                    catch (ObjectDisposedException) { }
                    httpListener = null;
                }

                httpListener = new HttpListener();
                httpListener.Prefixes.Add("http://localhost:6969/");
                httpListener.Start();
                isServerRunning = true;
                LogMessage("HTTP server started on http://localhost:6969/");
                Task.Run(() => HandleHttpRequests());
            }
            catch (Exception ex)
            {
                LogError("Failed to start HTTP server", ex);
                ShowErrorMessage($"Failed to start HTTP server: {ex.Message}");
                checkboxEnableRemoteConfig.Checked = false;
                httpListener = null;
                isServerRunning = false;
            }
        }

        private void StopHttpServer()
        {
            if (!isServerRunning || httpListener == null) return;

            try
            {
                isServerRunning = false; // Set flag first to stop the loop

                // Give the async loop time to exit gracefully
                System.Threading.Thread.Sleep(100);

                if (httpListener != null)
                {
                    httpListener.Stop();
                    httpListener.Close();
                    httpListener = null;
                }
                LogMessage("HTTP server stopped.");
            }
            catch (ObjectDisposedException)
            {
                // HTTP listener was already disposed, which is fine
                LogMessage("HTTP server stopped (already disposed).");
            }
            catch (Exception ex)
            {
                LogError("Failed to stop HTTP server", ex);
                ShowErrorMessage($"Failed to stop HTTP server: {ex.Message}");
            }
        }

        private async void HandleHttpRequests()
        {
            while (isServerRunning && httpListener != null)
            {
                try
                {
                    var context = await httpListener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    string url = request.Url.AbsolutePath;

                    if (request.HttpMethod == "GET")
                    {
                        // Handle static file requests
                        if (url.EndsWith(".css"))
                        {
                            await ServeStaticFile(response, url, "text/css");
                        }
                        else if (url.EndsWith(".js"))
                        {
                            await ServeStaticFile(response, url, "application/javascript");
                        }
                        else if (url.StartsWith("/api/"))
                        {
                            await HandleApiRequest(request, response);
                        }
                        else
                        {
                            // For any main page request (/, /index.html, etc.), serve the comprehensive web interface
                            string html = GenerateConfigHtml();
                            byte[] buffer = Encoding.UTF8.GetBytes(html);
                            response.ContentType = "text/html";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }
                    else if (request.HttpMethod == "POST")
                    {
                        if (url.StartsWith("/api/"))
                        {
                            await HandleApiRequest(request, response);
                        }
                        else
                        {
                            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                            {
                                string postData = await reader.ReadToEndAsync();
                                UpdateConfigFromPost(postData);
                            }
                            string html = GenerateConfigHtml("Configuration updated successfully.");
                            byte[] buffer = Encoding.UTF8.GetBytes(html);
                            response.ContentType = "text/html";
                            response.ContentLength64 = buffer.Length;
                            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                        }
                    }

                    response.Close();
                }
                catch (ObjectDisposedException)
                {
                    // HttpListener was disposed, exit loop gracefully
                    break;
                }
                catch (HttpListenerException)
                {
                    // HttpListener was stopped, exit loop gracefully
                    break;
                }
                catch (Exception ex)
                {
                    LogError("HTTP request error", ex);
                }
            }
        }

        private async Task ServeStaticFile(HttpListenerResponse response, string url, string contentType)
        {
            try
            {
                string fileName = url.TrimStart('/');
                string filePath = Path.Combine(Application.StartupPath, fileName);

                if (File.Exists(filePath))
                {
                    byte[] fileBytes = File.ReadAllBytes(filePath);
                    response.ContentType = contentType;
                    response.ContentLength64 = fileBytes.Length;
                    await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
                }
                else
                {
                    response.StatusCode = 404;
                    string notFound = "File not found";
                    byte[] buffer = Encoding.UTF8.GetBytes(notFound);
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error serving static file {url}", ex);
                response.StatusCode = 500;
            }
        }

        private async Task HandleApiRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            try
            {
                string url = request.Url.AbsolutePath;
                string jsonResponse = "";

                // Add CORS headers for web interface
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");
                response.ContentType = "application/json";

                LogMessage($"API request: {request.HttpMethod} {url}");

                if (url == "/api/config" && request.HttpMethod == "GET")
                {
                    jsonResponse = GetCurrentConfigAsJson();
                    LogMessage("Returning current configuration via API");
                }
                else if (url == "/api/profiles" && request.HttpMethod == "GET")
                {
                    jsonResponse = GetProfilesAsJson();
                }
                else if (url.StartsWith("/api/profiles/") && request.HttpMethod == "GET")
                {
                    string profileName = url.Substring("/api/profiles/".Length);
                    jsonResponse = GetProfileAsJson(profileName);
                }
                else if (url == "/api/logs" && request.HttpMethod == "GET")
                {
                    jsonResponse = GetLogsAsJson();
                }
                else
                {
                    response.StatusCode = 404;
                    jsonResponse = "{\"error\": \"API endpoint not found\"}";
                    LogMessage($"API endpoint not found: {url}");
                }

                byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                LogError("API request error", ex);
                response.StatusCode = 500;
                string errorJson = "{\"error\": \"Internal server error\"}";
                byte[] buffer = Encoding.UTF8.GetBytes(errorJson);
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            }
        }

        private string GetCurrentConfigAsJson()
        {
            try
            {
                var config = new
                {
                    filebotPath = sourceFilebotFolder.Text,
                    downloadsFolder = sourceDownloadsFolder.Text,
                    enableHDMovies = checkboxMovie.Checked,
                    hdMovieCount = (int)upDownMovie.Value,
                    hdMovieFolders = new[] { sourceFolderMovies1.Text, sourceFolderMovies2.Text, sourceFolderMovies3.Text, sourceFolderMovies4.Text, sourceFolderMovies5.Text }.Where(x => !string.IsNullOrEmpty(x) && x != "Default").ToArray(),
                    enable4KMovies = checkbox4KMovie.Checked,
                    movieCount4K = (int)upDown4KMovie.Value,
                    movieFolders4K = new[] { sourceFolder4kMovies1.Text, sourceFolder4kMovies2.Text, sourceFolder4kMovies3.Text, sourceFolder4kMovies4.Text, sourceFolder4kMovies5.Text }.Where(x => !string.IsNullOrEmpty(x) && x != "Default").ToArray(),
                    enableHDTV = checkboxTVShow.Checked,
                    hdTVCount = (int)upDownTVShow.Value,
                    hdTVFolders = new[] { sourceFolderTVShows1.Text, sourceFolderTVShows2.Text, sourceFolderTVShows3.Text, sourceFolderTVShows4.Text, sourceFolderTVShows5.Text }.Where(x => !string.IsNullOrEmpty(x) && x != "Default").ToArray(),
                    enable4KTV = checkbox4KTVShow.Checked,
                    tvCount4K = (int)upDown4KTVShow.Value,
                    tvFolders4K = new[] { sourceFolder4kTVShows1.Text, sourceFolder4kTVShows2.Text, sourceFolder4kTVShows3.Text, sourceFolder4kTVShows4.Text, sourceFolder4kTVShows5.Text }.Where(x => !string.IsNullOrEmpty(x) && x != "Default").ToArray(),
                    enableScheduling = checkboxScheduleTask.Checked,
                    scheduleInterval = (int)numericUpDownSchedule.Value,
                    enableOverrides = checkboxOverrideSortarrParameters.Checked,
                    movieFormatOverride = overrideMoviesTextBox.Text,
                    tvFormatOverride = overrideTVShowsTextBox.Text,
                    enableRemoteConfig = checkboxEnableRemoteConfig.Checked,
                    serverPort = 6969,
                    enableSystemTray = minimizeToTray
                };

                string json = System.Text.Json.JsonSerializer.Serialize(config);
                LogMessage($"API config data: FileBot={config.filebotPath}, Downloads={config.downloadsFolder}, HD Movies={config.enableHDMovies}, HD Movie folders={config.hdMovieFolders.Length}");
                return json;
            }
            catch (Exception ex)
            {
                LogError("Error serializing configuration to JSON", ex);
                return "{\"error\": \"Failed to serialize configuration\"}";
            }
        }

        private string GetProfilesAsJson()
        {
            try
            {
                var profiles = Directory.GetFiles(profilePath, "*.txt")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(p => p)
                    .ToArray();

                return System.Text.Json.JsonSerializer.Serialize(profiles);
            }
            catch
            {
                return "[]";
            }
        }

        private string GetProfileAsJson(string profileName)
        {
            // This would return profile-specific configuration
            // For now, return current config
            return GetCurrentConfigAsJson();
        }

        private string GetLogsAsJson()
        {
            try
            {
                if (File.Exists(logFilePath))
                {
                    var allLines = File.ReadAllLines(logFilePath);
                    var lines = allLines.Length > 50 ? allLines.Skip(allLines.Length - 50).ToArray() : allLines;
                    return System.Text.Json.JsonSerializer.Serialize(lines);
                }
                return "[\"No log entries available\"]";
            }
            catch
            {
                return "[\"Error reading log file\"]";
            }
        }

        private string GenerateConfigHtml(string message = "")
        {
            // Load and return the comprehensive web interface
            string htmlPath = Path.Combine(Application.StartupPath, "web-interface.html");

            try
            {
                if (File.Exists(htmlPath))
                {
                    string html = File.ReadAllText(htmlPath);

                    // If there's a message, inject it into the HTML
                    if (!string.IsNullOrEmpty(message))
                    {
                        html = html.Replace("Ready", message);
                    }

                    LogMessage($"Serving web-interface.html from: {htmlPath}");
                    return html;
                }
                else
                {
                    LogMessage($"web-interface.html not found at: {htmlPath}");
                    // Fallback to basic HTML if file not found
                    return GenerateBasicConfigHtml(message);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error loading web-interface.html from {htmlPath}", ex);
                return GenerateBasicConfigHtml(message);
            }
        }

        private string GenerateBasicConfigHtml(string message = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>Sortarr Configuration</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: Arial, sans-serif; margin: 0; padding: 30px; background-color: #2b2b2b; color: #f0f0f0; }");
            sb.AppendLine("h1 { color: #4caf50; text-align: center; }");
            sb.AppendLine("form { max-width: 800px; margin: auto; }");
            sb.AppendLine("label { display: block; margin: 15px 0 5px; }");
            sb.AppendLine("input[type=text], input[type=number] { width: 100%; padding: 10px; border: none; border-radius: 4px; background-color: #444; color: #f0f0f0; }");
            sb.AppendLine("input[type=submit], button { margin-top: 20px; padding: 12px 24px; background-color: #4caf50; color: white; border: none; border-radius: 4px; cursor: pointer; }");
            sb.AppendLine("input[type=submit]:hover, button:hover { background-color: #45a049; }");
            sb.AppendLine(".message { color: #80ff80; margin-bottom: 20px; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>Sortarr Configuration</h1>");
            if (!string.IsNullOrEmpty(message))
                sb.AppendLine($"<p class='message'>{message}</p>");
            sb.AppendLine("<p>Comprehensive web interface not available. Please check installation.</p>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        private void UpdateConfigFromPost(string postData)
        {
            var pairs = postData.Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace("+", " ")));
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() =>
                {
                    if (pairs.ContainsKey("filebotPath")) sourceFilebotFolder.Text = pairs["filebotPath"];
                    if (pairs.ContainsKey("downloadsFolder")) sourceDownloadsFolder.Text = pairs["downloadsFolder"];
                    if (pairs.ContainsKey("hdMovie1")) sourceFolderMovies1.Text = string.IsNullOrWhiteSpace(pairs["hdMovie1"]) ? "Default" : pairs["hdMovie1"];
                    if (pairs.ContainsKey("4kMovie1")) sourceFolder4kMovies1.Text = string.IsNullOrWhiteSpace(pairs["4kMovie1"]) ? "Default" : pairs["4kMovie1"];
                    if (pairs.ContainsKey("hdTV1")) sourceFolderTVShows1.Text = string.IsNullOrWhiteSpace(pairs["hdTV1"]) ? "Default" : pairs["hdTV1"];
                    if (pairs.ContainsKey("4kTV1")) sourceFolder4kTVShows1.Text = string.IsNullOrWhiteSpace(pairs["4kTV1"]) ? "Default" : pairs["4kTV1"];
                    if (pairs.ContainsKey("overrideMovies")) overrideMoviesTextBox.Text = pairs["overrideMovies"];
                    if (pairs.ContainsKey("overrideTV")) overrideTVShowsTextBox.Text = pairs["overrideTV"];
                    ValidateSetup();
                    LogMessage("Configuration updated via web interface.");
                }));
            }
            else
            {
                LogMessage("Skipping UI update in automated mode or handle not created.");
            }
        }

        private bool IsTaskScheduled()
        {
            try
            {
                using (TaskScheduler.TaskService ts = new TaskScheduler.TaskService())
                {
                    var task = ts.FindTask("Sortarr_AutoSort", false);
                    bool exists = task != null && task.Enabled;
                    LogMessage($"Checking task 'Sortarr_AutoSort': {(exists ? "Found" : "Not found")}");
                    return exists;
                }
            }
            catch (Exception ex)
            {
                LogError("Error checking scheduled task", ex);
                return false;
            }
        }

        private void automateBtn_Click(object sender, EventArgs e)
        {
            try
            {
                // Check if at least one profile exists
                if (!Directory.GetFiles(profilePath, "*.txt").Any())
                {
                    string errorMessage = "Cannot create scheduled task: No profiles found. Please save a profile first.";
                    LogError("Task Creation Error", errorMessage);
                    ShowErrorMessage(errorMessage);
                    return;
                }

                bool isElevated;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }

                if (!isElevated)
                {
                    string errorMessage = "Sortarr must be run as administrator to create scheduled tasks.";
                    LogError("Task Creation Error", errorMessage);
                    ShowErrorMessage(errorMessage, "Right-click Sortarr.exe and select 'Run as administrator'.");
                    return;
                }

                if (IsTaskScheduled())
                {
                    LogMessage("Scheduled task 'Sortarr_AutoSort' already exists.");
                    ShowWarningMessage("A scheduled task already exists. Remove it first to create a new one.");
                    return;
                }

                using (TaskScheduler.TaskService ts = new TaskScheduler.TaskService())
                {
                    TaskScheduler.TaskDefinition td = ts.NewTask();
                    td.RegistrationInfo.Description = "Automatically runs Sortarr to process and sort media files.";
                    td.Principal.RunLevel = TaskScheduler.TaskRunLevel.Highest;
                    td.Settings.Enabled = true; // Ensure task is enabled
                    td.Settings.Hidden = true; // Hide task window

                    TaskScheduler.DailyTrigger trigger = new TaskScheduler.DailyTrigger
                    {
                        StartBoundary = DateTime.Now, // Start immediately
                        Repetition = new TaskScheduler.RepetitionPattern(TimeSpan.FromMinutes((double)numericUpDownSchedule.Value), TimeSpan.Zero),
                        Enabled = true
                    };
                    td.Triggers.Add(trigger);

                    string exePath = Application.ExecutablePath;
                    TaskScheduler.ExecAction action = new TaskScheduler.ExecAction(exePath, "--auto", Application.StartupPath);
                    td.Actions.Add(action);

                    ts.RootFolder.RegisterTaskDefinition("Sortarr_AutoSort", td);

                    LogMessage($"Scheduled task 'Sortarr_AutoSort' created to run every {numericUpDownSchedule.Value} minute(s) indefinitely, starting now.");
                    ShowSuccessMessage($"Scheduled task created to run Sortarr every {numericUpDownSchedule.Value} minute(s) indefinitely, starting now.");

                    automateBtn.Enabled = false;
                    removeSortarrAutomation.Enabled = true;
                    removeSortarrAutomation.Visible = true;
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to create scheduled task: {ex.Message}\nEnsure Sortarr is running as administrator.";
                LogError("Scheduled Task Creation Error", ex);
                ShowErrorMessage(errorMessage, $"Check {logFilePath} for details.");
            }
        }

        private void removeSortarrAutomation_Click(object sender, EventArgs e)
        {
            try
            {
                bool isElevated;
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
                }

                if (!isElevated)
                {
                    string errorMessage = "Sortarr must be run as administrator to remove scheduled tasks.";
                    LogMessage(errorMessage);
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task Removal Error: {errorMessage}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"{errorMessage}\nRight-click Sortarr.exe and select 'Run as administrator'.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    return;
                }

                using (TaskScheduler.TaskService ts = new TaskScheduler.TaskService())
                {
                    string taskName = "Sortarr_AutoSort";
                    var task = ts.FindTask(taskName, false);
                    if (task != null)
                    {
                        ts.RootFolder.DeleteTask(taskName, false);
                        LogMessage($"Scheduled task '{taskName}' removed successfully.");
                        if (!isAutomated && IsHandleCreated)
                            BeginInvoke((SystemAction)(() => MessageBox.Show($"Scheduled task '{taskName}' removed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));

                        bool isSetupValid = ValidateSetup();
                        automateBtn.Enabled = checkboxScheduleTask.Checked && isSetupValid;
                        removeSortarrAutomation.Enabled = false;
                        removeSortarrAutomation.Visible = false;
                    }
                    else
                    {
                        LogMessage($"Scheduled task '{taskName}' not found.");
                        if (!isAutomated && IsHandleCreated)
                            BeginInvoke((SystemAction)(() => MessageBox.Show($"Scheduled task '{taskName}' not found.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                    }
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to remove scheduled task: {ex.Message}\nEnsure Sortarr is running as administrator.";
                LogMessage(errorMessage);
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Scheduled Task Removal Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"{errorMessage}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void donateBtn_Click(object sender, EventArgs e)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.paypal.com/donate/?business=WBHFP3TMYUHS8&amount=5&no_recurring=1&item_name=Thank+you+for+trying+my+program.+It+took+many+hours+and+late+nights+to+get+it+up+and+running.+Your+donations+are+appreciated%21¤cy_code=USD",
                    UseShellExecute = true
                })) { }
                LogMessage("Opened donation link: https://www.paypal.com");
            }
            catch (Exception ex)
            {
                LogError("Failed to open donation link", ex);
                ShowErrorMessage($"Failed to open donation link: {ex.Message}");
            }
        }

        private void openLocalHostBtn_Click(object sender, EventArgs e)
        {
            try
            {
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = "http://localhost:6969/",
                    UseShellExecute = true
                })) { }
                LogMessage("Opened localhost: http://localhost:6969/");
            }
            catch (Exception ex)
            {
                LogError("Failed to open localhost", ex);
                ShowErrorMessage($"Failed to open http://localhost:6969/: {ex.Message}");
            }
        }

        private void deleteProfileBtn_Click(object sender, EventArgs e)
        {
            string profileName = profileSelector.Text.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                LogMessage("No profile selected to delete.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Please select a profile to delete.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                return;
            }

            string path = Path.Combine(profilePath, profileName + ".txt");
            if (!File.Exists(path))
            {
                LogMessage($"Profile '{profileName}' not found.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Profile '{profileName}' not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            try
            {
                File.Delete(path);
                LogMessage($"Profile '{profileName}' deleted successfully.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Profile '{profileName}' deleted successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));
                LoadProfilesToDropdown();
                profileSelector.Text = "";
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to delete profile '{profileName}': {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Profile Deletion Error: {profileName}\nException: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to delete profile '{profileName}': {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void UpdateControlVisibility()
        {
            foreach (var mediaType in mediaControls)
            {
                var (checkBox, upDown, textBoxes, browseButtons, locationLabel) = mediaType.Value;
                bool isChecked = checkBox.Checked;
                int count = (int)upDown.Value;

                upDown.Enabled = isChecked;
                locationLabel.Visible = isChecked;
                for (int i = 0; i < textBoxes.Length; i++)
                {
                    bool shouldBeVisible = isChecked && i < count;
                    textBoxes[i].Visible = shouldBeVisible;
                    browseButtons[i].Visible = shouldBeVisible;
                    textBoxes[i].Enabled = shouldBeVisible;
                    browseButtons[i].Enabled = shouldBeVisible;
                }
            }
            ValidateSetup();
        }

        private bool ValidateSetup()
        {
            bool isValid = true;

            if (string.IsNullOrWhiteSpace(sourceFilebotFolder.Text) || string.IsNullOrWhiteSpace(sourceDownloadsFolder.Text))
            {
                isValid = false;
            }
            else
            {
                bool hasValidMediaType = false;
                foreach (var mediaType in mediaControls)
                {
                    if (mediaType.Value.CheckBox.Checked)
                    {
                        int count = (int)mediaType.Value.UpDown.Value;
                        for (int i = 0; i < count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(mediaType.Value.TextBoxes[i].Text) || mediaType.Value.TextBoxes[i].Text == "Default")
                            {
                                isValid = false;
                                break;
                            }
                        }
                        hasValidMediaType = true;
                    }
                    if (!isValid) break;
                }
                if (!hasValidMediaType) isValid = false;
            }

            sortarrBtn.Enabled = isValid;
            setupStatusLabel.Visible = isValid;

            // Update Advanced tab checkboxes
            foreach (var checkbox in advancedCheckboxes)
                checkbox.Enabled = isValid;

            // Update scheduling controls
            if (checkboxScheduleTask.Checked)
            {
                numericUpDownSchedule.Enabled = isValid;
                numericUpDownSchedule.Visible = isValid;
                runEveryTxt.Visible = isValid;
                minsTxt.Visible = isValid;
                automateBtn.Enabled = isValid && !IsTaskScheduled();
                automateBtn.Visible = isValid;
                removeSortarrAutomation.Enabled = IsTaskScheduled();
                removeSortarrAutomation.Visible = IsTaskScheduled();
            }
            else
            {
                numericUpDownSchedule.Enabled = false;
                numericUpDownSchedule.Visible = false;
                runEveryTxt.Visible = false;
                minsTxt.Visible = false;
                automateBtn.Enabled = false;
                automateBtn.Visible = false;
                removeSortarrAutomation.Enabled = false;
                removeSortarrAutomation.Visible = false;
            }

            // Update override textboxes
            if (checkboxOverrideSortarrParameters.Checked)
            {
                overrideMoviesTextBox.Enabled = isValid;
                overrideTVShowsTextBox.Enabled = isValid;
            }
            else
            {
                overrideMoviesTextBox.Enabled = false;
                overrideTVShowsTextBox.Enabled = false;
            }

            return isValid;
        }

        private void LogMessage(string message)
        {
            // Log to file using buffered logger
            logger?.Log(message);
            logger?.Log(""); // Add blank line for readability

            // Update UI if not in automated mode
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() =>
                {
                    logBox.Items.Add($"[{logger.GetTimestamp()}] {message}");
                    logBox.TopIndex = logBox.Items.Count - 1;
                }));
            }
        }

        private void LogError(string context, Exception ex)
        {
            string message = $"Error: {context}";
            LogMessage(message);
            logger?.LogError(context, ex.Message);
        }

        private void LogError(string context, string error)
        {
            string message = $"Error: {context}";
            LogMessage(message);
            logger?.LogError(context, error);
        }

        // Helper methods for UI operations
        private void ShowErrorMessage(string message, string details = null)
        {
            if (!isAutomated && IsHandleCreated)
            {
                string fullMessage = string.IsNullOrEmpty(details) ? message : $"{message}\n{details}";
                BeginInvoke((SystemAction)(() => MessageBox.Show(fullMessage, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void ShowWarningMessage(string message)
        {
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() => MessageBox.Show(message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
            }
        }

        private void ShowInfoMessage(string message)
        {
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() => MessageBox.Show(message, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
        }

        private void ShowSuccessMessage(string message)
        {
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() => MessageBox.Show(message, "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
        }

        // Generic browse handlers
        private void BrowseForFile(TextBox targetTextBox, string filter = "All Files (*.*)|*.*")
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = filter;
                if (dialog.ShowDialog() == DialogResult.OK)
                    targetTextBox.Text = dialog.FileName;
            }
            ValidateSetup();
        }

        private void BrowseForFolder(TextBox targetTextBox)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    targetTextBox.Text = dialog.SelectedPath;
            }
            ValidateSetup();
        }

        // System Tray Setup and Management
        private void SetupSystemTray()
        {
            // Create context menu
            trayContextMenu = new ContextMenuStrip();
            trayContextMenu.Items.Add("Show Sortarr", null, TrayShow_Click);
            trayContextMenu.Items.Add("Run Now", null, TrayRun_Click);
            trayContextMenu.Items.Add("-"); // Separator
            trayContextMenu.Items.Add("Exit", null, TrayExit_Click);

            // Create notify icon
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = this.Icon; // Use the form's icon
            notifyIcon.Text = "Sortarr - Media File Organizer";
            notifyIcon.ContextMenuStrip = trayContextMenu;
            notifyIcon.Visible = false; // Initially hidden

            // Double-click to show
            notifyIcon.DoubleClick += TrayShow_Click;
        }

        private void TrayShow_Click(object sender, EventArgs e)
        {
            ShowFromTray();
        }

        private void TrayRun_Click(object sender, EventArgs e)
        {
            // Trigger Sortarr run from tray
            sortarrBtn_Click(this, EventArgs.Empty);
        }

        private void TrayExit_Click(object sender, EventArgs e)
        {
            allowVisible = false;
            minimizeToTray = false;
            notifyIcon.Visible = false;
            Application.Exit();
        }

        private void ShowFromTray()
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
            notifyIcon.Visible = false;
        }

        private void MinimizeToTray()
        {
            this.Hide();
            this.ShowInTaskbar = false;
            notifyIcon.Visible = true;
            notifyIcon.ShowBalloonTip(2000, "Sortarr", "Application minimized to system tray", ToolTipIcon.Info);
        }

        private void browseFilebotLocationBtn_Click(object sender, EventArgs e)
        {
            BrowseForFile(sourceFilebotFolder, "Executable files (*.exe)|*.exe");
        }

        private void browseDownloadsLocationBtn_Click(object sender, EventArgs e)
        {
            BrowseForFolder(sourceDownloadsFolder);
        }

        private void BrowseFolderIntoTextBox(TextBox targetBox)
        {
            BrowseForFolder(targetBox);
        }

        private void saveProfileBtn_Click(object sender, EventArgs e)
        {
            string profileName = profileSelector.Text.Trim();
            if (string.IsNullOrWhiteSpace(profileName))
            {
                LogMessage("No profile name entered for saving.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Enter a profile name to save.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                return;
            }

            string path = Path.Combine(profilePath, profileName + ".txt");
            var lines = new List<string>
            {
                "HDMovieEnabled=" + checkboxMovie.Checked,
                "HDMovieCount=" + upDownMovie.Value,
                "HDMovie1=" + (sourceFolderMovies1.Text == "Default" ? "" : sourceFolderMovies1.Text),
                "HDMovie2=" + sourceFolderMovies2.Text,
                "HDMovie3=" + sourceFolderMovies3.Text,
                "HDMovie4=" + sourceFolderMovies4.Text,
                "HDMovie5=" + sourceFolderMovies5.Text,
                "4KMovieEnabled=" + checkbox4KMovie.Checked,
                "4KMovieCount=" + upDown4KMovie.Value,
                "4KMovie1=" + (sourceFolder4kMovies1.Text == "Default" ? "" : sourceFolder4kMovies1.Text),
                "4KMovie2=" + sourceFolder4kMovies2.Text,
                "4KMovie3=" + sourceFolder4kMovies3.Text,
                "4KMovie4=" + sourceFolder4kMovies4.Text,
                "4KMovie5=" + sourceFolder4kMovies5.Text,
                "HDTVEnabled=" + checkboxTVShow.Checked,
                "HDTVCount=" + upDownTVShow.Value,
                "HDTV1=" + (sourceFolderTVShows1.Text == "Default" ? "" : sourceFolderTVShows1.Text),
                "HDTV2=" + sourceFolderTVShows2.Text,
                "HDTV3=" + sourceFolderTVShows3.Text,
                "HDTV4=" + sourceFolderTVShows4.Text,
                "HDTV5=" + sourceFolderTVShows5.Text,
                "4KTVEnabled=" + checkbox4KTVShow.Checked,
                "4KTVCount=" + upDown4KTVShow.Value,
                "4KTV1=" + (sourceFolder4kTVShows1.Text == "Default" ? "" : sourceFolder4kTVShows1.Text),
                "4KTV2=" + sourceFolder4kTVShows2.Text,
                "4KTV3=" + sourceFolder4kTVShows3.Text,
                "4KTV4=" + sourceFolder4kTVShows4.Text,
                "4KTV5=" + sourceFolder4kTVShows5.Text,
                "FileBot=" + sourceFilebotFolder.Text,
                "Downloads=" + sourceDownloadsFolder.Text,
                "OverrideParametersEnabled=" + checkboxOverrideSortarrParameters.Checked,
                "OverrideMoviesFormat=" + overrideMoviesTextBox.Text,
                "OverrideTVShowsFormat=" + overrideTVShowsTextBox.Text,
                "RemoteConfigEnabled=" + checkboxEnableRemoteConfig.Checked,
                "MinimizeToTray=" + minimizeToTray
            };

            try
            {
                File.WriteAllLines(path, lines);
                LoadProfilesToDropdown();
                LogMessage($"Profile '{profileName}' saved successfully.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Profile saved!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to save profile '{profileName}': {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Profile Save Error: {profileName}\nException: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to save profile '{profileName}': {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void loadProfileBtn_Click(object sender, EventArgs e)
        {
            string profileName = profileSelector.Text.Trim();
            string path = Path.Combine(profilePath, profileName + ".txt");

            if (!File.Exists(path))
            {
                LogMessage($"Profile '{profileName}' not found.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Profile Load Error: {profileName} not found.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Profile not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            try
            {
                Dictionary<string, string> settings = new Dictionary<string, string>();
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts.Length == 2)
                        settings[parts[0]] = parts[1];
                }

                checkboxMovie.Checked = settings.ContainsKey("HDMovieEnabled") && bool.Parse(settings["HDMovieEnabled"]);
                upDownMovie.Value = settings.ContainsKey("HDMovieCount") ? int.Parse(settings["HDMovieCount"]) : 1;
                sourceFolderMovies1.Text = settings.ContainsKey("HDMovie1") && !string.IsNullOrEmpty(settings["HDMovie1"]) ? settings["HDMovie1"] : "Default";
                sourceFolderMovies2.Text = settings.ContainsKey("HDMovie2") ? settings["HDMovie2"] : "";
                sourceFolderMovies3.Text = settings.ContainsKey("HDMovie3") ? settings["HDMovie3"] : "";
                sourceFolderMovies4.Text = settings.ContainsKey("HDMovie4") ? settings["HDMovie4"] : "";
                sourceFolderMovies5.Text = settings.ContainsKey("HDMovie5") ? settings["HDMovie5"] : "";
                checkbox4KMovie.Checked = settings.ContainsKey("4KMovieEnabled") && bool.Parse(settings["4KMovieEnabled"]);
                upDown4KMovie.Value = settings.ContainsKey("4KMovieCount") ? int.Parse(settings["4KMovieCount"]) : 1;
                sourceFolder4kMovies1.Text = settings.ContainsKey("4KMovie1") && !string.IsNullOrEmpty(settings["4KMovie1"]) ? settings["4KMovie1"] : "Default";
                sourceFolder4kMovies2.Text = settings.ContainsKey("4KMovie2") ? settings["4KMovie2"] : "";
                sourceFolder4kMovies3.Text = settings.ContainsKey("4KMovie3") ? settings["4KMovie3"] : "";
                sourceFolder4kMovies4.Text = settings.ContainsKey("4KMovie4") ? settings["4KMovie4"] : "";
                sourceFolder4kMovies5.Text = settings.ContainsKey("4KMovie5") ? settings["4KMovie5"] : "";
                checkboxTVShow.Checked = settings.ContainsKey("HDTVEnabled") && bool.Parse(settings["HDTVEnabled"]);
                upDownTVShow.Value = settings.ContainsKey("HDTVCount") ? int.Parse(settings["HDTVCount"]) : 1;
                sourceFolderTVShows1.Text = settings.ContainsKey("HDTV1") && !string.IsNullOrEmpty(settings["HDTV1"]) ? settings["HDTV1"] : "Default";
                sourceFolderTVShows2.Text = settings.ContainsKey("HDTV2") ? settings["HDTV2"] : "";
                sourceFolderTVShows3.Text = settings.ContainsKey("HDTV3") ? settings["HDTV3"] : "";
                sourceFolderTVShows4.Text = settings.ContainsKey("HDTV4") ? settings["HDTV4"] : "";
                sourceFolderTVShows5.Text = settings.ContainsKey("HDTV5") ? settings["HDTV5"] : "";
                checkbox4KTVShow.Checked = settings.ContainsKey("4KTVEnabled") && bool.Parse(settings["4KTVEnabled"]);
                upDown4KTVShow.Value = settings.ContainsKey("4KTVCount") ? int.Parse(settings["4KTVCount"]) : 1;
                sourceFolder4kTVShows1.Text = settings.ContainsKey("4KTV1") && !string.IsNullOrEmpty(settings["4KTV1"]) ? settings["4KTV1"] : "Default";
                sourceFolder4kTVShows2.Text = settings.ContainsKey("4KTV2") ? settings["4KTV2"] : "";
                sourceFolder4kTVShows3.Text = settings.ContainsKey("4KTV3") ? settings["4KTV3"] : "";
                sourceFolder4kTVShows4.Text = settings.ContainsKey("4KTV4") ? settings["4KTV4"] : "";
                sourceFolder4kTVShows5.Text = settings.ContainsKey("4KTV5") ? settings["4KTV5"] : "";
                sourceFilebotFolder.Text = settings.ContainsKey("FileBot") ? settings["FileBot"] : "";
                sourceDownloadsFolder.Text = settings.ContainsKey("Downloads") ? settings["Downloads"] : "";
                checkboxOverrideSortarrParameters.Checked = settings.ContainsKey("OverrideParametersEnabled") && bool.Parse(settings["OverrideParametersEnabled"]);
                overrideMoviesTextBox.Text = settings.ContainsKey("OverrideMoviesFormat") ? settings["OverrideMoviesFormat"] : "";
                overrideTVShowsTextBox.Text = settings.ContainsKey("OverrideTVShowsFormat") ? settings["OverrideTVShowsFormat"] : "";
                checkboxEnableRemoteConfig.Checked = settings.ContainsKey("RemoteConfigEnabled") && bool.Parse(settings["RemoteConfigEnabled"]);

                // Load tray preference
                if (settings.ContainsKey("MinimizeToTray"))
                {
                    minimizeToTray = bool.Parse(settings["MinimizeToTray"]);
                }

                // Update sortarrPortTxt visibility based on checkboxEnableRemoteConfig
                sortarrPortTxt.Visible = checkboxEnableRemoteConfig.Checked;

                UpdateControlVisibility();
                ValidateSetup();
                LogMessage($"Profile '{profileName}' loaded successfully.");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Profile loaded.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information)));
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to load profile '{profileName}': {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Profile Load Error: {profileName}\nException: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to load profile '{profileName}': {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void LoadProfilesToDropdown()
        {
            profileSelector.Items.Clear();
            var files = Directory.GetFiles(profilePath, "*.txt");
            foreach (string file in files)
            {
                string profileName = Path.GetFileNameWithoutExtension(file);
                if (!profileSelector.Items.Contains(profileName))
                    profileSelector.Items.Add(profileName);
            }
        }

        private bool ValidateInputs()
        {
            if (!File.Exists(sourceFilebotFolder.Text))
            {
                LogMessage("Error: FileBot executable not found.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: FileBot executable not found at {sourceFilebotFolder.Text}.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("FileBot executable not found at the specified path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return false;
            }

            if (!Directory.Exists(sourceDownloadsFolder.Text))
            {
                LogMessage("Error: Downloads folder not found.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: Downloads folder not found at {sourceDownloadsFolder.Text}.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("Downloads folder not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return false;
            }

            bool hasValidMediaType = false;
            foreach (var mediaType in mediaControls)
            {
                if (mediaType.Value.CheckBox.Checked)
                {
                    int count = (int)mediaType.Value.UpDown.Value;
                    for (int i = 0; i < count; i++)
                    {
                        if (!Directory.Exists(mediaType.Value.TextBoxes[i].Text) || mediaType.Value.TextBoxes[i].Text == "Default")
                        {
                            LogMessage($"Error: {mediaType.Key} folder {i + 1} not found or is set to 'Default'.");
                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: {mediaType.Key} folder {i + 1} not found or is set to 'Default' at {mediaType.Value.TextBoxes[i].Text}.\n\n");
                            if (!isAutomated && IsHandleCreated)
                                BeginInvoke((SystemAction)(() => MessageBox.Show($"{mediaType.Key} folder {i + 1} not found or is set to 'Default'.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            return false;
                        }
                    }
                    hasValidMediaType = true;
                }
            }

            if (!hasValidMediaType)
            {
                LogMessage("Error: At least one media type must be enabled with valid folders.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: At least one media type must be enabled with valid folders.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("At least one media type must be enabled with valid folders.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return false;
            }

            return true;
        }

        private bool IsTVShow(string filename)
        {
            string pattern = @"[sS]\d{2}[eE]\d{2}";
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(filename), pattern);
        }

        private bool Is4K(string filename)
        {
            string pattern = @"2160[pP]";
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(filename), pattern);
        }

        private string[] GetEnabledDirectories(string mediaType)
        {
            var (checkBox, upDown, textBoxes, _, _) = mediaControls[mediaType];
            if (!checkBox.Checked) return new string[0];
            int count = (int)upDown.Value;
            return textBoxes.Take(count)
                .Select(tb => tb.Text)
                .Where(tb => Directory.Exists(tb) && tb != "Default")
                .ToArray();
        }

        private async void sortarrBtn_Click(object sender, EventArgs e)
        {
            if (!ValidateInputs()) return;

            await RunSortarrProcess();
        }

        private async Task RunSortarrProcess()
        {
            LogMessage("Starting Sortarr process (Transfer and Sort)...");

            // Initialize
            fileMappings.Clear();
            string downloadsFolder = sourceDownloadsFolder.Text;
            string filebotPath = sourceFilebotFolder.Text;

            if (!File.Exists(filebotPath))
            {
                LogMessage("Error: FileBot executable not found.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: FileBot executable not found at {filebotPath}.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("FileBot executable not found at the specified path.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            string tempBasePath = Path.Combine(Application.StartupPath, "FilebotMedia");
            try
            {
                Directory.CreateDirectory(tempBasePath);
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to create base temporary folder {tempBasePath}: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Temp Base Folder Creation Error: {tempBasePath}\nException: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to create base temporary folder: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            var tempFolders = new Dictionary<string, string>
            {
                { "HDMovie", Path.Combine(tempBasePath, "HDMovies") },
                { "4KMovie", Path.Combine(tempBasePath, "4KMovies") },
                { "HDTVShow", Path.Combine(tempBasePath, "HDTVShows") },
                { "4KTVShow", Path.Combine(tempBasePath, "4KTVShows") }
            };

            foreach (var folder in tempFolders.Values)
            {
                try
                {
                    Directory.CreateDirectory(folder);
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to create temporary folder {folder}: {ex.Message}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Temp Folder Creation Error: {folder}\nException: {ex.Message}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to create temporary folder {folder}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    return;
                }
            }

            var mediaFiles = Directory.GetFiles(downloadsFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => mediaExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

            if (mediaFiles.Count == 0)
            {
                LogMessage("No media files found in the downloads folder.");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No media files found in the downloads folder.\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show("No media files found in the downloads folder.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
                return;
            }

            LogMessage($"Found {mediaFiles.Count} media files to process.");

            try
            {
                // Initialize new log session
                logger?.Log($"FileBot Log - {DateTime.Now}");
                logger?.Log("");
            }
            catch (Exception ex)
            {
                LogError("Failed to initialize log file", ex);
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to initialize log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            // Phase 1: Process media files with FileBot AMC script
            foreach (var file in mediaFiles)
            {
                string filename = Path.GetFileName(file);
                LogMessage($"Processing file: {filename}");

                try
                {
                    // Verify file exists and is accessible
                    if (!File.Exists(file))
                    {
                        LogMessage($"Error: File {filename} does not exist.");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] File Missing: {filename}\n\n");
                        continue;
                    }

                    try
                    {
                        using (File.OpenRead(file)) { }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Error: File {filename} is inaccessible: {ex.Message}");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] File: {filename}\nException: {ex.Message}\n\n");
                        if (!isAutomated && IsHandleCreated)
                            BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to access {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                        continue;
                    }

                    bool isTvShow = IsTVShow(filename);
                    bool is4k = Is4K(filename);
                    string tempDest = isTvShow
                        ? (is4k ? tempFolders["4KTVShow"] : tempFolders["HDTVShow"])
                        : (is4k ? tempFolders["4KMovie"] : tempFolders["HDMovie"]);

                    // Verify temporary destination exists
                    if (!Directory.Exists(tempDest))
                    {
                        LogMessage($"Error: Temporary destination folder {tempDest} does not exist.");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Temp Folder Missing: {tempDest}\n\n");
                        if (!isAutomated && IsHandleCreated)
                            BeginInvoke((SystemAction)(() => MessageBox.Show($"Temporary folder {tempDest} does not exist.\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                        continue;
                    }

                    LogMessage($"Determined file type: {(isTvShow ? (is4k ? "4K TV Show" : "HD TV Show") : (is4k ? "4K Movie" : "HD Movie"))}, Temporary Destination: {tempDest}");

                    var beforeFiles = Directory.GetFiles(tempDest, "*.*", SearchOption.AllDirectories).ToList();

                    string movieFormat = checkboxOverrideSortarrParameters.Checked && !string.IsNullOrWhiteSpace(overrideMoviesTextBox.Text)
                        ? overrideMoviesTextBox.Text
                        : "Movies\\{n.colon(' - ')} ({y})";
                    string seriesFormat = checkboxOverrideSortarrParameters.Checked && !string.IsNullOrWhiteSpace(overrideTVShowsTextBox.Text)
                        ? overrideTVShowsTextBox.Text
                        : "{n} ({y})\\{n} - {s00e00} - {t}";

                    string args = $"-script fn:amc \"{file}\" --output \"{tempDest}\" --action duplicate -non-strict --log-file amc_{(isTvShow ? "tv" : "movies")}.log --def excludeList=amc.txt --def ut_kind=multi" +
                        (isTvShow ? $" --def seriesFormat=\"{seriesFormat}\"" : $" --def movieFormat=\"{movieFormat}\"");

                    LogMessage($"Executing FileBot command: {filebotPath} {args}");

                    // Add delay to ensure file is ready
                    await Task.Delay(500);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = filebotPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    string output, error;
                    int exitCode;
                    using (Process proc = Process.Start(psi))
                    {
                        output = proc.StandardOutput.ReadToEnd();
                        error = proc.StandardError.ReadToEnd();
                        proc.WaitForExit();
                        exitCode = proc.ExitCode;

                        logger?.Log($"File: {filename}");
                        logger?.Log($"Command: {filebotPath} {args}");
                        logger?.Log($"Output: {output}");
                        logger?.Log($"Error: {error}");
                        logger?.Log($"ExitCode: {exitCode}");
                        logger?.Log("");
                    }

                    if (exitCode != 0)
                    {
                        LogMessage($"FileBot failed for {filename}: {error}");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FileBot Error: {filename}\nError: {error}\n\n");
                        if (!isAutomated && IsHandleCreated)
                            BeginInvoke((SystemAction)(() => MessageBox.Show($"FileBot failed for {filename}: {error}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                        continue;
                    }

                    LogMessage($"FileBot successfully processed {filename}");

                    var afterFiles = Directory.GetFiles(tempDest, "*.*", SearchOption.AllDirectories).ToList();
                    var newFiles = afterFiles.Except(beforeFiles).ToList();

                    if (newFiles.Any())
                    {
                        string newFile = newFiles.First();
                        fileMappings.Add((Original: file, Renamed: newFile));
                        LogMessage($"Renamed file to: {Path.GetFileName(newFile)}");
                    }
                    else
                    {
                        LogMessage($"No new file detected for {filename}. FileBot output: {output}");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No new file detected for {filename}. Output: {output}\n\n");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Unexpected error processing {filename}: {ex.Message}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unexpected Error: {filename}\nException: {ex.Message}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Unexpected error processing {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
            }

            // Phase 1.5: Clean up folder names in temp folders (fix cases like "Bluey (2018) (2016)")
            foreach (var mediaType in tempFolders.Keys.Where(k => k.Contains("TVShow")))
            {
                string tempDest = tempFolders[mediaType];
                var subfolders = Directory.GetDirectories(tempDest);
                foreach (var subfolder in subfolders)
                {
                    string subfolderName = Path.GetFileName(subfolder);
                    // Match folder names with multiple years, e.g., "Bluey (2018) (2016)"
                    string pattern = @"(.+\s\(\d{4}\))\s\(\d{4}\)";
                    if (Regex.IsMatch(subfolderName, pattern))
                    {
                        string newSubfolderName = Regex.Replace(subfolderName, @"\s\(\d{4}\)$", "");
                        string newSubfolderPath = Path.Combine(tempDest, newSubfolderName);
                        try
                        {
                            Directory.Move(subfolder, newSubfolderPath);
                            LogMessage($"Cleaned up folder name: {subfolderName} to {newSubfolderName}");
                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Cleaned up folder name: {subfolderName} to {newSubfolderName}\n\n");

                            // Update fileMappings to reflect the new folder path
                            for (int i = 0; i < fileMappings.Count; i++)
                            {
                                if (fileMappings[i].Renamed.StartsWith(subfolder))
                                {
                                    fileMappings[i] = (fileMappings[i].Original, fileMappings[i].Renamed.Replace(subfolder, newSubfolderPath));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Failed to rename folder {subfolderName} to {newSubfolderName}: {ex.Message}");
                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Folder Rename Error: {subfolderName} to {newSubfolderName}\nException: {ex.Message}\n\n");
                            if (!isAutomated && IsHandleCreated)
                                BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to rename folder {subfolderName}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                        }
                    }
                }
            }

            // Phase 2: Move files/folders to final destinations with duplicate checks
            foreach (var mediaType in tempFolders.Keys)
            {
                string tempDest = tempFolders[mediaType];
                string[] finalDestinations = GetEnabledDirectories(mediaType);
                bool isMovie = mediaType.Contains("Movie");

                if (finalDestinations.Length == 0)
                {
                    LogMessage($"No valid destination folders for {mediaType}. Skipping move.");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No valid destination folders for {mediaType}.\n\n");
                    continue;
                }

                var files = Directory.GetFiles(tempDest, "*.*", SearchOption.AllDirectories)
                    .Where(f => mediaExtensions.Contains(Path.GetExtension(f).ToLower())).ToList();

                foreach (var file in files)
                {
                    string filename = Path.GetFileName(file);
                    string filenameNoExt = Path.GetFileNameWithoutExtension(filename);

                    if (isMovie)
                    {
                        // Movies: Check for duplicates in all final destinations
                        bool foundDuplicate = false;
                        foreach (var folder in finalDestinations)
                        {
                            try
                            {
                                var existingFiles = Directory.GetFiles(folder, "*.*", SearchOption.TopDirectoryOnly);
                                foreach (var existing in existingFiles)
                                {
                                    string existingNoExt = Path.GetFileNameWithoutExtension(existing);
                                    if (string.Equals(existingNoExt, filenameNoExt, StringComparison.OrdinalIgnoreCase))
                                    {
                                        LogMessage($"Duplicate movie found: {filename} in {folder} - deleting temp file");
                                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Duplicate movie found: {filename} in {folder}\n\n");
                                        File.Delete(file);
                                        foundDuplicate = true;
                                        break;
                                    }
                                }
                                if (foundDuplicate) break;
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error checking duplicates in {folder}: {ex.Message}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Duplicate Check Error: {folder}\nException: {ex.Message}\n\n");
                            }
                        }

                        if (!foundDuplicate)
                        {
                            // Move to first available destination
                            string finalDestRoot = finalDestinations.First();
                            string finalPath = Path.Combine(finalDestRoot, filename);
                            try
                            {
                                if (File.Exists(finalPath))
                                {
                                    File.Delete(finalPath);
                                    LogMessage($"Deleted existing movie file at {finalPath} to allow overwrite.");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleted existing movie file at {finalPath} to allow overwrite.\n\n");
                                }
                                if (!File.Exists(file))
                                {
                                    LogMessage($"Error: Source file {file} not found for moving to {finalPath}.");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Move Error: Source file {file} not found.\n\n");
                                    if (!isAutomated && IsHandleCreated)
                                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Source file {file} not found for moving.\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                                    continue;
                                }
                                File.Move(file, finalPath);
                                LogMessage($"Moved movie {filename} to {finalPath}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Moved movie {filename} to {finalPath}\n\n");
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Failed to move movie {filename} to {finalPath}: {ex.Message}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Movie Move Error: {filename} to {finalPath}\nException: {ex.Message}\n\n");
                                if (!isAutomated && IsHandleCreated)
                                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to move movie {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            }
                        }
                    }
                    else
                    {
                        // TV Shows: Match base folder name
                        string showFolderName = Path.GetFileName(Path.GetDirectoryName(file));
                        bool matchFound = false;

                        foreach (var folder in finalDestinations)
                        {
                            try
                            {
                                var existingFolders = Directory.GetDirectories(folder);
                                foreach (var existing in existingFolders)
                                {
                                    if (string.Equals(Path.GetFileName(existing), showFolderName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        string finalPath = Path.Combine(existing, filename);
                                        try
                                        {
                                            if (File.Exists(finalPath))
                                            {
                                                File.Delete(finalPath);
                                                LogMessage($"Deleted existing TV show file at {finalPath} to allow overwrite.");
                                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleted existing TV show file at {finalPath} to allow overwrite.\n\n");
                                            }
                                            if (!File.Exists(file))
                                            {
                                                LogMessage($"Error: Source file {file} not found for moving to {finalPath}.");
                                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Move Error: Source file {file} not found.\n\n");
                                                if (!isAutomated && IsHandleCreated)
                                                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Source file {file} not found for moving.\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                                                continue;
                                            }
                                            File.Move(file, finalPath);
                                            LogMessage($"Moved TV show file {filename} to existing folder {finalPath}");
                                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Moved TV show file {filename} to {finalPath}\n\n");
                                            matchFound = true;
                                        }
                                        catch (Exception ex)
                                        {
                                            LogMessage($"Failed to move TV show file {filename} to {finalPath}: {ex.Message}");
                                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TV Show Move Error: {filename} to {finalPath}\nException: {ex.Message}\n\n");
                                            if (!isAutomated && IsHandleCreated)
                                                BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to move TV show file {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                                        }
                                        break;
                                    }
                                }
                                if (matchFound) break;
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Error checking TV show folders in {folder}: {ex.Message}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TV Show Folder Check Error: {folder}\nException: {ex.Message}\n\n");
                            }
                        }

                        if (!matchFound)
                        {
                            // Create new folder in first available destination
                            string finalDestRoot = finalDestinations.First();
                            string newShowPath = Path.Combine(finalDestRoot, showFolderName);
                            string finalPath = Path.Combine(newShowPath, filename);
                            try
                            {
                                if (!Directory.Exists(newShowPath))
                                {
                                    Directory.CreateDirectory(newShowPath);
                                    LogMessage($"Created new TV show folder: {newShowPath}");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Created new TV show folder: {newShowPath}\n\n");
                                }
                                if (File.Exists(finalPath))
                                {
                                    File.Delete(finalPath);
                                    LogMessage($"Deleted existing TV show file at {finalPath} to allow overwrite.");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleted existing TV show file at {finalPath} to allow overwrite.\n\n");
                                }
                                if (!File.Exists(file))
                                {
                                    LogMessage($"Error: Source file {file} not found for moving to {finalPath}.");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Move Error: Source file {file} not found.\n\n");
                                    if (!isAutomated && IsHandleCreated)
                                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Source file {file} not found for moving.\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                                    continue;
                                }
                                File.Move(file, finalPath);
                                LogMessage($"Moved TV show file {filename} to new folder {finalPath}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Moved TV show file {filename} to {finalPath}\n\n");
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"Failed to move TV show file {filename} to {finalPath}: {ex.Message}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TV Show Move Error: {filename} to {finalPath}\nException: {ex.Message}\n\n");
                                if (!isAutomated && IsHandleCreated)
                                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to move TV show file {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                            }
                        }
                    }
                }
            }

            // Phase 3: Clean up original media files and empty folders in downloads folder
            try
            {
                LogMessage("Cleaning up original media files and empty folders in downloads folder...");
                if (!Directory.Exists(sourceDownloadsFolder.Text))
                {
                    LogMessage($"Downloads folder not found: {sourceDownloadsFolder.Text}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Downloads Folder Not Found: {sourceDownloadsFolder.Text}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Downloads folder not found: {sourceDownloadsFolder.Text}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                else
                {
                    // Delete media files in downloads folder
                    foreach (string mediaFile in Directory.GetFiles(sourceDownloadsFolder.Text, "*.*", SearchOption.AllDirectories)
                        .Where(f => mediaExtensions.Contains(Path.GetExtension(f).ToLower())))
                    {
                        try
                        {
                            // Check if this file was processed (in fileMappings)
                            if (fileMappings.Any(m => m.Original == mediaFile))
                            {
                                File.Delete(mediaFile);
                                LogMessage($"Deleted original media file: {mediaFile}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleted original media file: {mediaFile}\n\n");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Failed to delete original media file {mediaFile}: {ex.Message}");
                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Delete Error: {mediaFile}\nException: {ex.Message}\n\n");
                            if (!isAutomated && IsHandleCreated)
                                BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to delete original media file {mediaFile}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                        }
                    }

                    // Delete empty folders recursively (bottom-up)
                    foreach (string dir in Directory.GetDirectories(sourceDownloadsFolder.Text, "*", SearchOption.AllDirectories)
                        .OrderByDescending(d => d.Length))
                    {
                        try
                        {
                            if (!Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Any() &&
                                !Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).Any())
                            {
                                Directory.Delete(dir, true);
                                LogMessage($"Deleted empty folder: {dir}");
                                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Deleted empty folder: {dir}\n\n");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Failed to delete empty folder {dir}: {ex.Message}");
                            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Empty Folder Delete Error: {dir}\nException: {ex.Message}\n\n");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to clean up downloads folder: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Downloads Cleanup Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to clean up downloads folder: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }

            // Phase 4: Clean up temporary folders
            foreach (var folder in tempFolders.Values)
            {
                try
                {
                    if (Directory.Exists(folder) && !Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories).Any())
                    {
                        Directory.Delete(folder, true);
                        LogMessage($"Cleaned up empty temporary folder: {folder}");
                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Cleaned up empty temporary folder: {folder}\n\n");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to clean up temporary folder {folder}: {ex.Message}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Temp Folder Cleanup Error: {folder}\nException: {ex.Message}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to clean up temporary folder {folder}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
            }

            // Finalize
            LogMessage("Sortarr process completed.");
            logger?.Flush(); // Ensure all logs are written
            if (!isAutomated && IsHandleCreated)
                BeginInvoke((SystemAction)(() => MessageBox.Show("Sortarr process completed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        }

        private void Sortarr_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (allowVisible && e.CloseReason == CloseReason.UserClosing)
            {
                if (minimizeToTray)
                {
                    // User has enabled minimize to tray, so minimize instead of closing
                    e.Cancel = true;
                    MinimizeToTray();
                }
                else
                {
                    // Show dialog to ask user preference
                    DialogResult result = MessageBox.Show(
                        "Do you want to minimize Sortarr to the system tray instead of closing?\n\n" +
                        "Click 'Yes' to minimize to tray (this will enable the setting)\n" +
                        "Click 'No' to exit the application\n" +
                        "Click 'Cancel' to return to the application",
                        "Minimize to System Tray?",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question);

                    if (result == DialogResult.Yes)
                    {
                        // Enable minimize to tray and minimize
                        minimizeToTray = true;
                        e.Cancel = true;
                        MinimizeToTray();
                        LogMessage("Minimize to tray enabled and application minimized.");
                    }
                    else if (result == DialogResult.Cancel)
                    {
                        // Cancel closing
                        e.Cancel = true;
                    }
                    // DialogResult.No will allow normal closing
                }
            }

            if (!e.Cancel)
            {
                // Proceed with normal closing
                allowVisible = false;
                notifyIcon.Visible = false;
            }
        }

        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(allowVisible && value);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Unsubscribe from events
                this.FormClosing -= Sortarr_FormClosing;

                // Stop HTTP server properly before disposal
                StopHttpServer();

                // Dispose system tray components
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
                trayContextMenu?.Dispose();

                // Dispose logger
                logger?.Flush();
                logger?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}