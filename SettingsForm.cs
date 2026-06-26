using System;
using System.Drawing;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    public partial class SettingsForm : Form
    {
        private AgentSettings _currentSettings;

        public SettingsForm()
        {
            InitializeComponent();
        }

        private void SettingsForm_Load(object sender, EventArgs e)
        {
            _currentSettings = SettingsManager.LoadSettings();

            txtSiteUrl.Text = _currentSettings.SiteUrl;
            txtAgentUuid.Text = _currentSettings.AgentUuid;
            txtApiKey.Text = _currentSettings.ApiKey; // Decrypted on-the-fly by helper property
            numHeartbeatInterval.Value = Math.Max(10, Math.Min(3600, _currentSettings.HeartbeatIntervalSeconds));
            txtKdeCliPath.Text = _currentSettings.KdeCliPath;
            txtKdeDeviceId.Text = _currentSettings.KdeDeviceId;

            lblStatus.Text = "Loaded settings.";
            lblStatus.ForeColor = Color.DimGray;
        }

        private async void BtnTestConnection_Click(object sender, EventArgs e)
        {
            btnTestConnection.Enabled = false;
            lblStatus.Text = "Testing connection...";
            lblStatus.ForeColor = Color.Orange;

            // Temp settings from input values (with URL normalization)
            var testSettings = new AgentSettings
            {
                SiteUrl = WordPressAgentClient.NormalizeBaseUrl(txtSiteUrl.Text),
                AgentUuid = txtAgentUuid.Text.Trim(),
                ApiKey = txtApiKey.Text.Trim(), // Automatically encrypted in property
                KdeCliPath = txtKdeCliPath.Text.Trim(),
                KdeDeviceId = txtKdeDeviceId.Text.Trim()
            };

            // Double check validation
            if (string.IsNullOrEmpty(testSettings.SiteUrl) || 
                string.IsNullOrEmpty(testSettings.AgentUuid) || 
                string.IsNullOrEmpty(testSettings.ApiKey))
            {
                lblStatus.Text = "Validation Failed: Site URL, UUID, and Key are required.";
                lblStatus.ForeColor = Color.Red;
                btnTestConnection.Enabled = true;
                return;
            }

            var result = await WordPressAgentClient.SendHeartbeatAsync(testSettings);

            if (result.Success)
            {
                lblStatus.Text = $"Success! Status: Online ({result.StatusCode})";
                lblStatus.ForeColor = Color.DarkGreen;
            }
            else if (result.StatusCode == 403)
            {
                lblStatus.Text = $"Authentication Failed: Rejected by Server (403)";
                lblStatus.ForeColor = Color.DarkGoldenrod;
            }
            else if (result.StatusCode == 401)
            {
                lblStatus.Text = $"Authentication Failed: Missing credentials headers (401)";
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
            _currentSettings.KdeDeviceId = txtKdeDeviceId.Text.Trim();

            if (SettingsManager.SaveSettings(_currentSettings))
            {
                // Force parent Context context to load new values and reset timer
                if (ApplicationContextInstance.Current != null)
                {
                    ApplicationContextInstance.Current.ReloadSettings();
                }

                MessageBox.Show(
                    "Settings saved successfully. Background heartbeat has been initialized.",
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

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
