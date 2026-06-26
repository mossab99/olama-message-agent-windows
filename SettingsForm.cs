using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    public partial class SettingsForm : Form
    {
        private AgentSettings _currentSettings;

        public class DeviceItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool Reachable { get; set; }
            public override string ToString()
            {
                return $"{Name} ({Id}){(Reachable ? " [Connected]" : " [Offline]")}";
            }
        }

        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            _currentSettings = SettingsManager.LoadSettings();

            txtSiteUrl.Text = _currentSettings.SiteUrl;
            txtAgentUuid.Text = _currentSettings.AgentUuid;
            txtApiKey.Text = _currentSettings.ApiKey; // Decrypted on-the-fly
            numHeartbeatInterval.Value = Math.Max(10, Math.Min(3600, _currentSettings.HeartbeatIntervalSeconds));
            txtKdeCliPath.Text = _currentSettings.KdeCliPath;

            // Load saved target device
            if (!string.IsNullOrEmpty(_currentSettings.KdeDeviceId))
            {
                cmbKdeDevices.Items.Add(_currentSettings.KdeDeviceId);
                cmbKdeDevices.SelectedIndex = 0;
            }

            lblStatus.Text = "Loaded settings.";
            lblStatus.ForeColor = Color.DimGray;
        }

        private async void BtnDetectKde_Click(object sender, EventArgs e)
        {
            string cliPath = txtKdeCliPath.Text.Trim();
            
            if (string.IsNullOrEmpty(cliPath) || !KdeCliHelper.DetectCli(cliPath))
            {
                MessageBox.Show(
                    "KDE Connect CLI executable was not found at the specified path. Please verify the file path.",
                    "CLI Path Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            btnDetectKde.Enabled = false;
            lblStatus.Text = "Scanning paired devices...";
            lblStatus.ForeColor = Color.Orange;

            // Run discovery on a background thread task to keep UI responsive
            var devices = await System.Threading.Tasks.Task.Run(() => 
                KdeCliHelper.DiscoverDevices(cliPath)
            );

            cmbKdeDevices.Items.Clear();

            if (devices.Count == 0)
            {
                lblStatus.Text = "No paired devices found.";
                lblStatus.ForeColor = Color.Red;
                MessageBox.Show(
                    "No paired devices detected. Please check that your Android phone is paired and connected inside the KDE Connect desktop app.",
                    "No Devices Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                btnDetectKde.Enabled = true;
                return;
            }

            DeviceItem selectedItem = null;
            foreach (var d in devices)
            {
                var item = new DeviceItem
                {
                    Id = d.Id,
                    Name = d.Name,
                    Reachable = d.Reachable
                };
                cmbKdeDevices.Items.Add(item);

                // Auto-select previously saved device ID if it matches
                if (d.Id == _currentSettings.KdeDeviceId)
                {
                    selectedItem = item;
                }
            }

            if (selectedItem != null)
            {
                cmbKdeDevices.SelectedItem = selectedItem;
            }
            else if (cmbKdeDevices.Items.Count > 0)
            {
                cmbKdeDevices.SelectedIndex = 0;
            }

            lblStatus.Text = $"Found {devices.Count} device(s). Submitting diagnostics...";
            lblStatus.ForeColor = Color.DarkGreen;

            // Submit detailed diagnostics to WordPress
            var testSettings = new AgentSettings
            {
                SiteUrl = WordPressAgentClient.NormalizeBaseUrl(txtSiteUrl.Text),
                AgentUuid = txtAgentUuid.Text.Trim(),
                ApiKey = txtApiKey.Text.Trim(),
                KdeCliPath = cliPath,
                KdeDeviceId = GetSelectedDeviceId()
            };

            if (!string.IsNullOrEmpty(testSettings.SiteUrl) && 
                !string.IsNullOrEmpty(testSettings.AgentUuid) && 
                !string.IsNullOrEmpty(testSettings.ApiKey))
            {
                await WordPressAgentClient.SendDiagnosticsAsync(testSettings, true, devices);
                lblStatus.Text = $"Scan finished. Diagnostics uploaded.";
            }
            else
            {
                lblStatus.Text = $"Scan finished. Diagnostics local only (Site details missing).";
            }

            btnDetectKde.Enabled = true;
        }

        private async void BtnTestConnection_Click(object sender, EventArgs e)
        {
            btnTestConnection.Enabled = false;
            lblStatus.Text = "Testing connection...";
            lblStatus.ForeColor = Color.Orange;

            var testSettings = new AgentSettings
            {
                SiteUrl = WordPressAgentClient.NormalizeBaseUrl(txtSiteUrl.Text),
                AgentUuid = txtAgentUuid.Text.Trim(),
                ApiKey = txtApiKey.Text.Trim(),
                KdeCliPath = txtKdeCliPath.Text.Trim(),
                KdeDeviceId = GetSelectedDeviceId()
            };

            if (string.IsNullOrEmpty(testSettings.SiteUrl) || 
                string.IsNullOrEmpty(testSettings.AgentUuid) || 
                string.IsNullOrEmpty(testSettings.ApiKey))
            {
                lblStatus.Text = "Validation Failed: Site URL, UUID, and Key are required.";
                lblStatus.ForeColor = Color.Red;
                btnTestConnection.Enabled = true;
                return;
            }

            // Perform simple light heartbeat (no process execution)
            bool cliFound = KdeCliHelper.DetectCli(testSettings.KdeCliPath);
            var result = await WordPressAgentClient.SendHeartbeatAsync(
                testSettings, 
                cliFound, 
                string.Empty, 
                false
            );

            if (result.Success)
            {
                lblStatus.Text = $"Success! Status: Online ({result.StatusCode})";
                lblStatus.ForeColor = Color.DarkGreen;
            }
            else if (result.StatusCode == 403 || result.StatusCode == 401)
            {
                lblStatus.Text = $"Authentication Failed: Rejected by Server ({result.StatusCode})";
                lblStatus.ForeColor = Color.DarkGoldenrod;
            }
            else
            {
                lblStatus.Text = $"Failed: {result.Message}";
                lblStatus.ForeColor = Color.Red;
            }

            btnTestConnection.Enabled = true;
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string normalizedUrl = WordPressAgentClient.NormalizeBaseUrl(txtSiteUrl.Text);
            string uuid = txtAgentUuid.Text.Trim();
            string key = txtApiKey.Text.Trim();

            if (string.IsNullOrEmpty(normalizedUrl) || string.IsNullOrEmpty(uuid) || string.IsNullOrEmpty(key))
            {
                MessageBox.Show(
                    "WordPress Site URL, Agent UUID, and API Secret Key are required configuration fields.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            _currentSettings.SiteUrl = normalizedUrl;
            _currentSettings.AgentUuid = uuid;
            _currentSettings.ApiKey = key; // Encrypts automatically
            _currentSettings.HeartbeatIntervalSeconds = (int)numHeartbeatInterval.Value;
            _currentSettings.KdeCliPath = txtKdeCliPath.Text.Trim();
            _currentSettings.KdeDeviceId = GetSelectedDeviceId();

            if (SettingsManager.SaveSettings(_currentSettings))
            {
                if (ApplicationContextInstance.Current != null)
                {
                    ApplicationContextInstance.Current.ReloadSettings();
                }

                MessageBox.Show(
                    "Settings saved successfully. Heartbeat and discovery timer has been re-initialized.",
                    "Success",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                MessageBox.Show(
                    "Failed to save settings to disk. See logs for details.",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private string GetSelectedDeviceId()
        {
            if (cmbKdeDevices.SelectedItem == null)
            {
                return cmbKdeDevices.Text.Trim();
            }

            if (cmbKdeDevices.SelectedItem is DeviceItem di)
            {
                return di.Id;
            }

            return cmbKdeDevices.SelectedItem.ToString().Trim();
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
