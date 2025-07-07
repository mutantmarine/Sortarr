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

        public Sortarr()
        {
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
                    LogMessage("Error: No profiles found for automated mode.");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: No profiles found for automated mode.\n\n");
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
                    LogMessage("Error: Validation failed for automated mode. Check profile settings.");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Error: Validation failed for automated mode. Check profile settings in {profileSelector.Text}.txt\n\n");
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
                LogMessage($"Automated mode error: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Automated Mode Error: {ex.Message}\n\n");
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
                httpListener = new HttpListener();
                httpListener.Prefixes.Add("http://localhost:6969/");
                httpListener.Start();
                isServerRunning = true;
                LogMessage("HTTP server started on http://localhost:6969/");
                Task.Run(() => HandleHttpRequests());
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to start HTTP server: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HTTP Server Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to start HTTP server: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                checkboxEnableRemoteConfig.Checked = false;
            }
        }

        private void StopHttpServer()
        {
            if (!isServerRunning) return;

            try
            {
                httpListener.Stop();
                httpListener.Close();
                isServerRunning = false;
                LogMessage("HTTP server stopped.");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to stop HTTP server: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HTTP Server Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to stop HTTP server: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private async void HandleHttpRequests()
        {
            while (isServerRunning)
            {
                try
                {
                    var context = await httpListener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    if (request.HttpMethod == "GET")
                    {
                        string html = GenerateConfigHtml();
                        byte[] buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    else if (request.HttpMethod == "POST")
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

                    response.Close();
                }
                catch (Exception ex)
                {
                    LogMessage($"HTTP request error: {ex.Message}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] HTTP Request Error: {ex.Message}\n\n");
                }
            }
        }

        private string GenerateConfigHtml(string message = "")
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>Sortarr Configuration</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body {");
            sb.AppendLine("    font-family: Arial, sans-serif;");
            sb.AppendLine("    margin: 0;");
            sb.AppendLine("    padding: 40px;");
            sb.AppendLine("    background-color: #2b2b2b;");
            sb.AppendLine("    color: #f0f0f0;");
            sb.AppendLine("}");
            sb.AppendLine("h1 { color: #ffffff; }");
            sb.AppendLine("form { max-width: 600px; margin: auto; }");
            sb.AppendLine("label { display: block; margin: 15px 0 5px; }");
            sb.AppendLine("input[type=text] {");
            sb.AppendLine("    width: 100%;");
            sb.AppendLine("    padding: 10px;");
            sb.AppendLine("    border: none;");
            sb.AppendLine("    border-radius: 4px;");
            sb.AppendLine("    background-color: #444;");
            sb.AppendLine("    color: #f0f0f0;");
            sb.AppendLine("}");
            sb.AppendLine("input[type=submit] {");
            sb.AppendLine("    margin-top: 20px;");
            sb.AppendLine("    padding: 12px 24px;");
            sb.AppendLine("    background-color: #4caf50;");
            sb.AppendLine("    color: white;");
            sb.AppendLine("    border: none;");
            sb.AppendLine("    border-radius: 4px;");
            sb.AppendLine("    cursor: pointer;");
            sb.AppendLine("}");
            sb.AppendLine("input[type=submit]:hover { background-color: #45a049; }");
            sb.AppendLine(".message { color: #80ff80; margin-bottom: 20px; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>Sortarr Configuration</h1>");
            if (!string.IsNullOrEmpty(message))
                sb.AppendLine($"<p class='message'>{message}</p>");
            sb.AppendLine("<form method='post'>");

            sb.AppendLine("<label>FileBot Path:</label>");
            sb.AppendLine($"<input type='text' name='filebotPath' value='{sourceFilebotFolder.Text}'><br>");

            sb.AppendLine("<label>Downloads Folder:</label>");
            sb.AppendLine($"<input type='text' name='downloadsFolder' value='{sourceDownloadsFolder.Text}'><br>");

            sb.AppendLine("<label>Movies Folder 1 (Default):</label>");
            sb.AppendLine($"<input type='text' name='hdMovie1' value='{(sourceFolderMovies1.Text == "Default" ? "" : sourceFolderMovies1.Text)}'><br>");

            sb.AppendLine("<label>4K Movies Folder 1 (Default):</label>");
            sb.AppendLine($"<input type='text' name='4kMovie1' value='{(sourceFolder4kMovies1.Text == "Default" ? "" : sourceFolder4kMovies1.Text)}'><br>");

            sb.AppendLine("<label>TV Shows Folder 1 (Default):</label>");
            sb.AppendLine($"<input type='text' name='hdTV1' value='{(sourceFolderTVShows1.Text == "Default" ? "" : sourceFolderTVShows1.Text)}'><br>");

            sb.AppendLine("<label>4K TV Shows Folder 1 (Default):</label>");
            sb.AppendLine($"<input type='text' name='4kTV1' value='{(sourceFolder4kTVShows1.Text == "Default" ? "" : sourceFolder4kTVShows1.Text)}'><br>");

            sb.AppendLine("<label>Override Movies Format:</label>");
            sb.AppendLine($"<input type='text' name='overrideMovies' value='{overrideMoviesTextBox.Text}'><br>");

            sb.AppendLine("<label>Override TV Shows Format:</label>");
            sb.AppendLine($"<input type='text' name='overrideTV' value='{overrideTVShowsTextBox.Text}'><br>");

            sb.AppendLine("<input type='submit' value='Save Changes'>");
            sb.AppendLine("</form></body></html>");
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
                LogMessage($"Error checking scheduled task: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task Check Error: {ex.Message}\n\n");
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
                    LogMessage(errorMessage);
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task Creation Error: {errorMessage}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show(errorMessage, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
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
                    LogMessage(errorMessage);
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Task Creation Error: {errorMessage}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"{errorMessage}\nRight-click Sortarr.exe and select 'Run as administrator'.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    return;
                }

                if (IsTaskScheduled())
                {
                    LogMessage("Scheduled task 'Sortarr_AutoSort' already exists.");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show("A scheduled task already exists. Remove it first to create a new one.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning)));
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
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Scheduled task created to run Sortarr every {numericUpDownSchedule.Value} minute(s) indefinitely, starting now.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));

                    automateBtn.Enabled = false;
                    removeSortarrAutomation.Enabled = true;
                    removeSortarrAutomation.Visible = true;
                }
            }
            catch (Exception ex)
            {
                string errorMessage = $"Failed to create scheduled task: {ex.Message}\nEnsure Sortarr is running as administrator.";
                LogMessage(errorMessage);
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Scheduled Task Creation Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"{errorMessage}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
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
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.paypal.com/donate/?business=WBHFP3TMYUHS8&amount=5&no_recurring=1&item_name=Thank+you+for+trying+my+program.+It+took+many+hours+and+late+nights+to+get+it+up+and+running.+Your+donations+are+appreciated%21¤cy_code=USD",
                    UseShellExecute = true
                });
                LogMessage("Opened donation link: https://www.paypal.com");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to open donation link: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Donation Link Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to open donation link: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
            }
        }

        private void openLocalHostBtn_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://localhost:6969/",
                    UseShellExecute = true
                });
                LogMessage("Opened localhost: http://localhost:6969/");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to open localhost: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Localhost Error: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to open http://localhost:6969/: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
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
            if (!isAutomated && IsHandleCreated)
            {
                BeginInvoke((SystemAction)(() =>
                {
                    logBox.Items.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
                    logBox.TopIndex = logBox.Items.Count - 1;
                }));
            }
            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n\n");
        }

        private void browseFilebotLocationBtn_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "Executable files (*.exe)|*.exe";
                if (dialog.ShowDialog() == DialogResult.OK)
                    sourceFilebotFolder.Text = dialog.FileName;
            }
            ValidateSetup();
        }

        private void browseDownloadsLocationBtn_Click(object sender, EventArgs e)
        {
            BrowseFolderIntoTextBox(sourceDownloadsFolder);
            ValidateSetup();
        }

        private void BrowseFolderIntoTextBox(TextBox targetBox)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                    targetBox.Text = dialog.SelectedPath;
            }
            ValidateSetup();
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
                "RemoteConfigEnabled=" + checkboxEnableRemoteConfig.Checked
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
            string pattern = @"[sS]\d{1,2}[eE]\d{1,2}|[sS]eason\s*\d+.*[eE]pisode\s*\d+|\d{1,2}x\d{1,2}";
            return Regex.IsMatch(Path.GetFileNameWithoutExtension(filename), pattern);
        }

        private bool Is4K(string filename)
        {
            string pattern = @"4[kK]|2160[pP]";
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
            Directory.CreateDirectory(tempBasePath);

            var tempFolders = new Dictionary<string, string>
            {
                { "HDMovie", Path.Combine(tempBasePath, "HDMovies") },
                { "4KMovie", Path.Combine(tempBasePath, "4KMovies") },
                { "HDTVShow", Path.Combine(tempBasePath, "HDTVShows") },
                { "4KTVShow", Path.Combine(tempBasePath, "4KTVShows") }
            };

            foreach (var folder in tempFolders.Values)
                Directory.CreateDirectory(folder);

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
                File.WriteAllText(logFilePath, $"FileBot Log - {DateTime.Now}\n\n");
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to initialize log file: {ex.Message}");
                File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Failed to initialize log file: {ex.Message}\n\n");
                if (!isAutomated && IsHandleCreated)
                    BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to initialize log file: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                return;
            }

            // Phase 1: Add New Media
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

                    LogMessage($"Determined file type: {(isTvShow ? (is4k ? "4K TV Show" : "HD TV Show") : (is4k ? "4K Movie" : "HD Movie"))}, Destination: {tempDest}");

                    var beforeFiles = Directory.GetFiles(tempDest, "*.*", SearchOption.AllDirectories).ToList();

                    string format = isTvShow
                        ? (checkboxOverrideSortarrParameters.Checked && !string.IsNullOrWhiteSpace(overrideTVShowsTextBox.Text)
                            ? overrideTVShowsTextBox.Text
                            : "{n} ({y})\\{n} - S{s.pad(2)}E{e.pad(2)} - {t}")
                        : (checkboxOverrideSortarrParameters.Checked && !string.IsNullOrWhiteSpace(overrideMoviesTextBox.Text)
                            ? overrideMoviesTextBox.Text
                            : "Movies\\{n.colon(' - ')} ({y})");
                    string db = isTvShow ? "TheTVDB" : "TheMovieDB";
                    string seriesName = isTvShow ? Path.GetFileNameWithoutExtension(filename).Split('.')[0] : "";
                    string args = isTvShow
                        ? $"-rename \"{file}\" --db {db} --format \"{format}\" --output \"{tempDest}\" --action move -non-strict --lang en --def order=airdate" + (string.IsNullOrEmpty(seriesName) ? "" : $" --def series=\"{seriesName}\"")
                        : $"-rename \"{file}\" --db {db} --format \"{format}\" --output \"{tempDest}\" --action move --lang en";

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

                        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] File: {filename}\nCommand: {filebotPath} {args}\nOutput: {output}\nError: {error}\nExitCode: {exitCode}\n\n");
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
                        // Fallback: Correct episode naming from SE or 1x01 to SxxExx for TV shows
                        if (isTvShow)
                        {
                            string newFileName = Path.GetFileName(newFile);
                            // Correct year (e.g., 1998 to 1999 for SpongeBob SquarePants)
                            if (newFileName.Contains("(1998)"))
                            {
                                string correctedName = newFileName.Replace("(1998)", "(1999)");
                                string correctedPath = Path.Combine(Path.GetDirectoryName(newFile), correctedName);
                                try
                                {
                                    File.Move(newFile, correctedPath);
                                    newFile = correctedPath;
                                    LogMessage($"Corrected year: {newFileName} to {correctedName}");
                                }
                                catch (Exception ex)
                                {
                                    LogMessage($"Failed to correct year for {newFileName}: {ex.Message}");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Year Correction Error: {newFileName}\nException: {ex.Message}\n\n");
                                }
                            }
                            // Correct episode format (e.g., SE or 1x01 to S01E01)
                            if (Regex.IsMatch(newFileName, @"[sS][eE]\s*-|\d+x\d+"))
                            {
                                string correctedName = Regex.Replace(newFileName, @"[sS][eE]\s*-", match =>
                                {
                                    var matchInfo = Regex.Match(newFileName, @"[sS](\d{1,2})[eE](\d{1,2})");
                                    if (matchInfo.Success)
                                        return $"S{matchInfo.Groups[1].Value.PadLeft(2, '0')}E{matchInfo.Groups[2].Value.PadLeft(2, '0')} - ";
                                    return match.Value; // Fallback
                                });
                                correctedName = Regex.Replace(correctedName, @"(\d+)x(\d+)", "S$1E$2");
                                string correctedPath = Path.Combine(Path.GetDirectoryName(newFile), correctedName);
                                try
                                {
                                    File.Move(newFile, correctedPath);
                                    newFile = correctedPath;
                                    LogMessage($"Corrected episode naming: {newFileName} to {correctedName}");
                                }
                                catch (Exception ex)
                                {
                                    LogMessage($"Failed to rename {newFileName} to {correctedName}: {ex.Message}");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Episode Naming Error: {newFileName}\nException: {ex.Message}\n\n");
                                }
                            }
                        }
                        // Fallback: Ensure colons are replaced with " - " for movies
                        else
                        {
                            string newFileName = Path.GetFileName(newFile);
                            if (newFileName.Contains(":"))
                            {
                                string correctedName = newFileName.Replace(":", " - ");
                                string correctedPath = Path.Combine(Path.GetDirectoryName(newFile), correctedName);
                                try
                                {
                                    File.Move(newFile, correctedPath);
                                    newFile = correctedPath;
                                    LogMessage($"Manually corrected filename: {newFileName} to {correctedName}");
                                }
                                catch (Exception ex)
                                {
                                    LogMessage($"Failed to rename {newFileName} to {correctedName}: {ex.Message}");
                                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Filename Correction Error: {newFileName}\nException: {ex.Message}\n\n");
                                }
                            }
                        }
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

            // Phase 2: Move files to final destinations
            foreach (var mapping in fileMappings)
            {
                string original = mapping.Original;
                string renamed = mapping.Renamed;
                string filename = Path.GetFileName(renamed);
                bool isTvShow = IsTVShow(filename);
                bool is4k = Is4K(filename);
                string mediaType = isTvShow ? (is4k ? "4KTVShow" : "HDTVShow") : (is4k ? "4KMovie" : "HDMovie");
                string[] finalDestinations = GetEnabledDirectories(mediaType);

                if (finalDestinations.Length == 0)
                {
                    LogMessage($"No valid destination folders for {mediaType}. Skipping move for {filename}.");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] No valid destination folders for {mediaType}. File: {filename}\n\n");
                    continue;
                }

                string finalDest = finalDestinations.First();
                string finalPath = Path.Combine(finalDest, Path.GetFileName(renamed));

                try
                {
                    File.Move(renamed, finalPath);
                    LogMessage($"Moved {filename} to {finalPath}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Moved {filename} to {finalPath}\n\n");
                }
                catch (Exception ex)
                {
                    LogMessage($"Failed to move {filename} to {finalPath}: {ex.Message}");
                    File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Move Error: {filename} to {finalPath}\nException: {ex.Message}\n\n");
                    if (!isAutomated && IsHandleCreated)
                        BeginInvoke((SystemAction)(() => MessageBox.Show($"Failed to move {filename}: {ex.Message}\nCheck {logFilePath} for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
            }

            // Log contents of temp folders for debugging
            foreach (var folder in tempFolders)
            {
                var files = Directory.GetFiles(folder.Value, "*.*", SearchOption.AllDirectories);
                if (files.Any())
                {
                    LogMessage($"Files in {folder.Key} temp folder ({folder.Value}):");
                    foreach (var file in files)
                        LogMessage($"  - {Path.GetFileName(file)}");
                }
                else
                {
                    LogMessage($"No files in {folder.Key} temp folder ({folder.Value}).");
                }
            }

            LogMessage("Sortarr process completed.");
            File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Sortarr process completed.\n\n");

            if (!isAutomated && IsHandleCreated)
                BeginInvoke((SystemAction)(() => MessageBox.Show("Sortarr process completed!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        }
    }
}