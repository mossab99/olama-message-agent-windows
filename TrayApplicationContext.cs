using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace OlamaMessageAgent
{
    public static class ApplicationContextInstance
    {
        public static TrayApplicationContext Current { get; set; }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _menuStatus;
        private readonly ToolStripMenuItem _menuTestSms;
        private readonly System.Windows.Forms.Timer _heartbeatTimer;

        // ── Dispatcher fields (Phase 4 Run 4C) ───────────────────────────────
        private DispatcherService _dispatcher;
        private readonly System.Windows.Forms.Timer _dispatcherTimer;
        private ToolStripMenuItem _menuDispatcherStatus;
        private ToolStripMenuItem _menuDispatcherEnable;
        private ToolStripMenuItem _menuDispatcherDisable;
        private ToolStripMenuItem _menuDispatcherPollNow;
        private bool _dispatcherPollInProgress;

        private AgentSettings _settings;
        private bool _heartbeatInProgress;
        private Icon _currentIcon;

        // Telemetry Cache
        private bool _kdeCliFound;
        private string _kdeDeviceName = string.Empty;
        private bool _kdeDeviceReachable;
        private DateTime _lastDiagnosticsTime = DateTime.MinValue;

        /// <summary>
        /// Exposes the last known KDE device reachability for the dispatcher's
        /// capability reporting without running a new discovery process.
        /// </summary>
        public bool LastKnownDeviceReachable => _kdeDeviceReachable;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public TrayApplicationContext()
        {
            ApplicationContextInstance.Current = this;

            Logger.Info("Olama Message Agent started.");

            // Load initial settings
            _settings = SettingsManager.LoadSettings();

            // Set TLS 1.2 initialization
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

            // Initialize Context Menu
            _contextMenu = new ContextMenuStrip();
            
            _menuStatus = new ToolStripMenuItem("Status: Initializing...") { Enabled = false };
            _contextMenu.Items.Add(_menuStatus);
            _contextMenu.Items.Add(new ToolStripSeparator());

            var menuSettings = new ToolStripMenuItem("Open Settings", null, (s, e) => ShowSettingsForm());
            _contextMenu.Items.Add(menuSettings);

            _menuTestSms = new ToolStripMenuItem("Send Test SMS...", null, (s, e) => ShowManualTestForm()) { Enabled = false };
            _contextMenu.Items.Add(_menuTestSms);

            var menuLogs = new ToolStripMenuItem("View Agent Logs", null, (s, e) => ShowLogsForm());
            _contextMenu.Items.Add(menuLogs);

            var menuHeartbeatNow = new ToolStripMenuItem("Heartbeat Now", null, async (s, e) => await TriggerHeartbeatAsync());
            _contextMenu.Items.Add(menuHeartbeatNow);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // ── Dispatcher section (Phase 4 Run 4C) ──────────────────────────
            _menuDispatcherStatus = new ToolStripMenuItem("Dispatcher: Disabled") { Enabled = false };
            _contextMenu.Items.Add(_menuDispatcherStatus);

            _menuDispatcherEnable = new ToolStripMenuItem("Enable Dispatcher", null, (s, e) => EnableDispatcher());
            _contextMenu.Items.Add(_menuDispatcherEnable);

            _menuDispatcherDisable = new ToolStripMenuItem("Disable Dispatcher", null, (s, e) => DisableDispatcher());
            _menuDispatcherDisable.Enabled = false;
            _contextMenu.Items.Add(_menuDispatcherDisable);

            _menuDispatcherPollNow = new ToolStripMenuItem("Poll Now", null, async (s, e) => await TriggerDispatcherPollAsync());
            _menuDispatcherPollNow.Enabled = false;
            _contextMenu.Items.Add(_menuDispatcherPollNow);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var menuExit = new ToolStripMenuItem("Exit Agent", null, (s, e) => ExitApplication());
            _contextMenu.Items.Add(menuExit);

            // Initialize Tray Icon
            _notifyIcon = new NotifyIcon
            {
                ContextMenuStrip = _contextMenu,
                Text = "Olama Message Agent",
                Visible = true
            };

            // Set initial state icon (Gray/Unconfigured)
            UpdateStatusIndicator("Unconfigured", Color.Gray);

            // Double click opens settings
            _notifyIcon.DoubleClick += (s, e) => ShowSettingsForm();

            // Initialize Heartbeat Timer
            _heartbeatTimer = new System.Windows.Forms.Timer();
            _heartbeatTimer.Tick += async (s, e) => await PerformHeartbeatTickAsync();

            // Initialize Dispatcher Timer
            _dispatcherTimer = new System.Windows.Forms.Timer();
            _dispatcherTimer.Tick += async (s, e) => await RunDispatcherTickAsync();
            // Default interval; will be updated from loaded config
            _dispatcherTimer.Interval = 30000; // 30 seconds

            // Initialize dispatcher service and restore the operator's persisted choice.
            _dispatcher = new DispatcherService(_settings);

            bool autoEnableRequested = string.Equals(
                Environment.GetEnvironmentVariable("OLAMA_AGENT_AUTO_ENABLE_DISPATCHER"),
                "true",
                StringComparison.OrdinalIgnoreCase);
            if (_settings.DispatcherEnabled || autoEnableRequested)
            {
                Logger.Info("Restoring enabled dispatcher state at startup.");
                _dispatcher.Enable();
                // Let the UI timer trigger the first poll so all continuations stay on the UI thread.
                _dispatcherTimer.Interval = 1000;
                _dispatcherTimer.Start();
            }

            // Start heartbeat timer if configured
            ReloadTimer();

            // Perform initial heartbeat
            Task.Run(async () => await PerformHeartbeatTickAsync());
        }

        public void ReloadSettings()
        {
            Logger.Info("Settings updated. Reloading agent configuration.");
            _settings = SettingsManager.LoadSettings();

            // Force immediate diagnostics discovery run on next tick
            _lastDiagnosticsTime = DateTime.MinValue;

            // Rebuild dispatcher with fresh settings and restore its persisted state.
            bool wasEnabled = _dispatcher != null && _dispatcher.IsEnabled;
            if (wasEnabled)
            {
                _dispatcher.Disable();
                _dispatcherTimer.Stop();
                Logger.Info("Dispatcher paused while settings reload.");
            }
            _dispatcher = new DispatcherService(_settings);

            if (_settings.DispatcherEnabled || wasEnabled)
            {
                _dispatcher.Enable();
                _dispatcherTimer.Interval = 1000;
                _dispatcherTimer.Start();
            }

            ReloadTimer();
            Task.Run(async () => await PerformHeartbeatTickAsync());
        }

        private void ReloadTimer()
        {
            _heartbeatTimer.Stop();
            int intervalMs = _settings.HeartbeatIntervalSeconds * 1000;
            _heartbeatTimer.Interval = Math.Max(10000, intervalMs); // Minimum 10 seconds guard
            _heartbeatTimer.Start();
            Logger.Info($"Heartbeat timer configured to run every {_settings.HeartbeatIntervalSeconds} seconds.");
        }

        private async Task PerformHeartbeatTickAsync()
        {
            if (_heartbeatInProgress)
            {
                Logger.Warn("Heartbeat tick skipped because a previous heartbeat request is still in progress.");
                return;
            }

            try
            {
                _heartbeatInProgress = true;

                if (string.IsNullOrEmpty(_settings.SiteUrl) || string.IsNullOrEmpty(_settings.AgentUuid) || string.IsNullOrEmpty(_settings.ApiKey))
                {
                    UpdateStatusIndicator("Unconfigured", Color.Gray);
                    UpdateMenuStates(false);
                    return;
                }

                // Check CLI file exists (light check, no process execution)
                _kdeCliFound = KdeCliHelper.DetectCli(_settings.KdeCliPath);

                // Refresh reachability before every heartbeat. A five-minute cached
                // value is unsafe for an SMS sender because phones disconnect often.
                var devices = await Task.Run(() => RefreshKdeDeviceState());

                // Keep the detailed diagnostics audit on its original five-minute
                // cadence, reusing the fresh discovery result from this heartbeat.
                var elapsed = DateTime.Now - _lastDiagnosticsTime;
                if (elapsed.TotalSeconds >= 300)
                {
                    _lastDiagnosticsTime = DateTime.Now;
                    _ = WordPressAgentClient.SendDiagnosticsAsync(_settings, _kdeCliFound, devices);
                }

                // Dispatch heartbeat carrying last known cached states
                var result = await WordPressAgentClient.SendHeartbeatAsync(
                    _settings,
                    _kdeCliFound,
                    _kdeDeviceName,
                    _kdeDeviceReachable,
                    _dispatcher
                );

                if (result.Success)
                {
                    if (_kdeDeviceReachable)
                    {
                        UpdateStatusIndicator("Online", Color.Green);
                        UpdateMenuStates(true);
                    }
                    else
                    {
                        UpdateStatusIndicator("Device Disconnected", Color.OrangeRed);
                        UpdateMenuStates(false);
                    }
                }
                else if (result.StatusCode == 403 || result.StatusCode == 401)
                {
                    UpdateStatusIndicator("Auth Failed", Color.Gold);
                    UpdateMenuStates(false);
                }
                else
                {
                    UpdateStatusIndicator("Offline", Color.Red);
                    UpdateMenuStates(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception inside heartbeat loop.", ex);
                UpdateStatusIndicator("Offline", Color.Red);
                UpdateMenuStates(false);
            }
            finally
            {
                _heartbeatInProgress = false;
            }
        }

        public async Task RunDiagnosticsAsync()
        {
            try
            {
                Logger.Info("Running background KDE device discovery diagnostics...");
                bool cliFound = KdeCliHelper.DetectCli(_settings.KdeCliPath);
                _kdeCliFound = cliFound;

                var devices = RefreshKdeDeviceState();

                // POST diagnostics to WordPress
                await WordPressAgentClient.SendDiagnosticsAsync(_settings, cliFound, devices);

                // Update UI thread menu states
                UpdateMenuStates(_kdeDeviceReachable);
            }
            catch (Exception ex)
            {
                Logger.Error("Error executing background discovery diagnostics.", ex);
            }
        }

        private System.Collections.Generic.List<KdeDevice> RefreshKdeDeviceState()
        {
            var devices = KdeCliHelper.DiscoverDevices(_settings.KdeCliPath);
            bool targetDeviceFound = false;
            _kdeDeviceReachable = false;

            foreach (var device in devices)
            {
                if (string.Equals(device.Id, _settings.KdeDeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _kdeDeviceName = device.Name;
                    _kdeDeviceReachable = device.Reachable;
                    targetDeviceFound = true;
                    break;
                }
            }

            if (!targetDeviceFound && !string.IsNullOrEmpty(_settings.KdeDeviceId))
                Logger.Warn($"Target Device ID '{_settings.KdeDeviceId}' is disconnected or was not found.");

            return devices;
        }

        private void UpdateMenuStates(bool isOnline)
        {
            if (_notifyIcon.ContextMenuStrip.InvokeRequired)
            {
                _notifyIcon.ContextMenuStrip.BeginInvoke(new Action<bool>(UpdateMenuStates), isOnline);
                return;
            }
            // Enable manual test SMS option only when online and a device ID is set
            _menuTestSms.Enabled = isOnline && !string.IsNullOrEmpty(_settings.KdeDeviceId);
        }

        private async Task TriggerHeartbeatAsync()
        {
            Logger.Info("Manual heartbeat check requested by user.");
            await PerformHeartbeatTickAsync();
        }

        private void UpdateStatusIndicator(string status, Color color)
        {
            // Thread-safe UI update
            if (_notifyIcon.ContextMenuStrip.InvokeRequired)
            {
                _notifyIcon.ContextMenuStrip.BeginInvoke(new Action<string, Color>(UpdateStatusIndicator), status, color);
                return;
            }

            // Update status text
            _menuStatus.Text = $"Status: {status}";
            _notifyIcon.Text = $"Olama Message Agent - {status}";

            // Draw status icon programmatically
            Icon oldIcon = _currentIcon;
            _currentIcon = CreateProgrammaticIcon(color);
            _notifyIcon.Icon = _currentIcon;

            // Dispose old icon handle to prevent GDI leak
            if (oldIcon != null)
            {
                DestroyIcon(oldIcon.Handle);
                oldIcon.Dispose();
            }
        }

        private Icon CreateProgrammaticIcon(Color color)
        {
            using (var bitmap = new Bitmap(16, 16))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                
                // Draw a sleek indicator dot
                using (var brush = new SolidBrush(color))
                {
                    g.FillEllipse(brush, 1, 1, 14, 14);
                }

                using (var pen = new Pen(Color.White, 1))
                {
                    g.DrawEllipse(pen, 1, 1, 14, 14);
                }

                // Get icon from bitmap handle
                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        private void ShowSettingsForm()
        {
            // Check if form is already open
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm is SettingsForm)
                {
                    openForm.BringToFront();
                    return;
                }
            }

            var frm = new SettingsForm();
            frm.Show();
        }

        private void ShowManualTestForm()
        {
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm is ManualTestForm)
                {
                    openForm.BringToFront();
                    return;
                }
            }

            var frm = new ManualTestForm();
            frm.Show();
        }

        private void ShowLogsForm()
        {
            // Check if form is already open
            foreach (Form openForm in Application.OpenForms)
            {
                if (openForm is LogViewerForm)
                {
                    openForm.BringToFront();
                    return;
                }
            }

            var frm = new LogViewerForm();
            frm.Show();
        }

        private void ExitApplication()
        {
            Logger.Info("Exit requested. Terminating agent application.");
            _heartbeatTimer.Stop();
            _dispatcherTimer.Stop();
            if (_dispatcher != null && _dispatcher.IsEnabled)
            {
                _dispatcher.Disable();
            }
            _notifyIcon.Visible = false;
            
            if (_currentIcon != null)
            {
                DestroyIcon(_currentIcon.Handle);
                _currentIcon.Dispose();
            }
            
            _notifyIcon.Dispose();
            Application.Exit();
        }

        // ── Dispatcher operator controls (Phase 4 Run 4C) ────────────────────

        private async void EnableDispatcher()
        {
            string error = _dispatcher.Enable();
            if (error != null)
            {
                MessageBox.Show(
                    $"Cannot enable dispatcher:\n\n{error}",
                    "Dispatcher Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                UpdateDispatcherMenuState();
                return;
            }

            // Start dispatcher timer using loaded config interval
            _dispatcherTimer.Interval = 30000; // Will be updated after first config load
            _dispatcherTimer.Start();
            _settings.DispatcherEnabled = true;
            SettingsManager.SaveSettings(_settings);
            UpdateDispatcherMenuState();

            // Immediately kick off first poll
            await TriggerDispatcherPollAsync();
        }

        private void DisableDispatcher()
        {
            _dispatcherTimer.Stop();
            _dispatcher.Disable();
            _settings.DispatcherEnabled = false;
            SettingsManager.SaveSettings(_settings);
            UpdateDispatcherMenuState();
        }

        private async Task TriggerDispatcherPollAsync()
        {
            Logger.Info("Manual dispatcher poll requested by operator.");
            await RunDispatcherTickAsync();
        }

        private async Task RunDispatcherTickAsync()
        {
            if (_dispatcherPollInProgress)
            {
                Logger.Warn("Dispatcher tick skipped — previous poll still in progress.");
                return;
            }

            if (!_dispatcher.IsEnabled) return;

            try
            {
                _dispatcherPollInProgress = true;
                _dispatcherTimer.Stop();
                UpdateDispatcherMenuState();

                var result = await _dispatcher.PollOnceAsync();

                // Update dispatcher timer interval if config was loaded
                if (result.Success)
                {
                    // Re-read interval from dispatcher's config each cycle
                    _dispatcherTimer.Interval = Math.Max(10000,
                        GetDispatcherPollIntervalMs());
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception in dispatcher tick.", ex);
            }
            finally
            {
                _dispatcherPollInProgress = false;
                if (_dispatcher.IsEnabled)
                {
                    _dispatcherTimer.Start();
                }
                UpdateDispatcherMenuState();
            }
        }

        private int GetDispatcherPollIntervalMs()
        {
            if (_dispatcher == null) return 30000;
            return _dispatcher.PollIntervalSeconds * 1000;
        }

        private void UpdateDispatcherMenuState()
        {
            if (_contextMenu.InvokeRequired)
            {
                _contextMenu.BeginInvoke(new Action(UpdateDispatcherMenuState));
                return;
            }

            bool enabled = _dispatcher != null && _dispatcher.IsEnabled;
            DispatcherState state = _dispatcher != null ? _dispatcher.CurrentState : DispatcherState.Disabled;

            string stateLabel;
            switch (state)
            {
                case DispatcherState.Idle:     stateLabel = "Idle";     break;
                case DispatcherState.Polling:  stateLabel = "Polling";  break;
                case DispatcherState.Reserved: stateLabel = "Reserved"; break;
                case DispatcherState.DryRun:   stateLabel = "Dry Run";  break;
                case DispatcherState.Error:    stateLabel = "Error";    break;
                default:                       stateLabel = "Disabled"; break;
            }

            _menuDispatcherStatus.Text    = $"Dispatcher: {stateLabel}";
            _menuDispatcherEnable.Enabled = !enabled;
            _menuDispatcherDisable.Enabled = enabled;
            _menuDispatcherPollNow.Enabled = enabled && !_dispatcherPollInProgress;
        }
    }
}
