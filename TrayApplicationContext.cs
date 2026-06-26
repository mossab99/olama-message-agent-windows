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

        private AgentSettings _settings;
        private bool _heartbeatInProgress;
        private Icon _currentIcon;

        // Telemetry Cache
        private bool _kdeCliFound;
        private string _kdeDeviceName = string.Empty;
        private bool _kdeDeviceReachable;
        private DateTime _lastDiagnosticsTime = DateTime.MinValue;

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

            // Start timer if configured
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

                // Run slow discovery diagnostics periodically (every 300 seconds)
                var elapsed = DateTime.Now - _lastDiagnosticsTime;
                if (elapsed.TotalSeconds >= 300)
                {
                    _lastDiagnosticsTime = DateTime.Now;
                    // Trigger discovery on a background thread pool task so we do not block heartbeat network POST
                    _ = Task.Run(async () => await RunDiagnosticsAsync());
                }

                // Dispatch heartbeat carrying last known cached states
                var result = await WordPressAgentClient.SendHeartbeatAsync(
                    _settings,
                    _kdeCliFound,
                    _kdeDeviceName,
                    _kdeDeviceReachable
                );

                if (result.Success)
                {
                    UpdateStatusIndicator("Online", Color.Green);
                    UpdateMenuStates(true);
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

                var devices = KdeCliHelper.DiscoverDevices(_settings.KdeCliPath);
                
                bool targetDeviceFound = false;
                if (!string.IsNullOrEmpty(_settings.KdeDeviceId) && devices.Count > 0)
                {
                    foreach (var d in devices)
                    {
                        if (d.Id == _settings.KdeDeviceId)
                        {
                            _kdeDeviceName = d.Name;
                            _kdeDeviceReachable = d.Reachable;
                            targetDeviceFound = true;
                            break;
                        }
                    }
                }

                if (!targetDeviceFound)
                {
                    _kdeDeviceReachable = false;
                    if (!string.IsNullOrEmpty(_settings.KdeDeviceId))
                    {
                        Logger.Warn($"Target Device ID '{_settings.KdeDeviceId}' was not found in discovered devices.");
                    }
                }

                // POST diagnostics to WordPress
                await WordPressAgentClient.SendDiagnosticsAsync(_settings, cliFound, devices);

                // Update UI thread menu states
                UpdateMenuStates(true);
            }
            catch (Exception ex)
            {
                Logger.Error("Error executing background discovery diagnostics.", ex);
            }
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
            _notifyIcon.Visible = false;
            
            if (_currentIcon != null)
            {
                DestroyIcon(_currentIcon.Handle);
                _currentIcon.Dispose();
            }
            
            _notifyIcon.Dispose();
            Application.Exit();
        }
    }
}
