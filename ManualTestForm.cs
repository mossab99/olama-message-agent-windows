using System;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    public partial class ManualTestForm : Form
    {
        private AgentSettings _settings;

        public ManualTestForm()
        {
            InitializeComponent();
        }

        private void ManualTestForm_Load(object sender, EventArgs e)
        {
            _settings = SettingsManager.LoadSettings();

            if (string.IsNullOrEmpty(_settings.SiteUrl) || string.IsNullOrEmpty(_settings.AgentUuid) || string.IsNullOrEmpty(_settings.ApiKey))
            {
                AppendConsole("WARNING: Agent settings are incomplete. Please configure the agent first.");
                btnSend.Enabled = false;
                return;
            }

            if (!KdeCliHelper.DetectCli(_settings.KdeCliPath))
            {
                AppendConsole($"WARNING: KDE Connect CLI was not found at: {_settings.KdeCliPath}. Please install KDE Connect or configure the correct path in Settings.");
                btnSend.Enabled = false;
                return;
            }

            if (string.IsNullOrEmpty(_settings.KdeDeviceId))
            {
                AppendConsole("WARNING: No target KDE Device ID has been selected. Please open Settings and scan for paired devices.");
                btnSend.Enabled = false;
                return;
            }

            AppendConsole("System ready for manual diagnostic test SMS.");
            AppendConsole($"KDE CLI Path: {_settings.KdeCliPath}");
            AppendConsole($"Target Device ID: {_settings.KdeDeviceId}");
        }

        private void TxtMessageBody_TextChanged(object sender, EventArgs e)
        {
            lblCharCount.Text = $"{txtMessageBody.Text.Length} / 160";
        }

        private async void BtnSend_Click(object sender, EventArgs e)
        {
            string phone = txtPhoneNumber.Text.Trim();
            string message = txtMessageBody.Text.Trim();

            // Validate Jordanian E.164 Phone format: +9627[7|8|9]XXXXXXX
            if (!Regex.IsMatch(phone, @"^\+9627[789]\d{7}$"))
            {
                MessageBox.Show(
                    "Invalid phone number format. Target phone must match the Jordanian E.164 pattern: +9627XXXXXXXX (starting with +96277, +96278, or +96279 followed by 7 digits).",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                MessageBox.Show(
                    "Message body cannot be empty.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            if (message.Length > 160)
            {
                MessageBox.Show(
                    "Message body must not exceed 160 characters for Phase 3 test messaging.",
                    "Validation Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            btnSend.Enabled = false;
            btnClose.Enabled = false;
            txtConsoleOutput.Clear();

            AppendConsole($"[INIT] Dispatched diagnostic SMS test request.");
            AppendConsole($"[INFO] Target Mobile: {phone}");
            AppendConsole($"[INFO] Message: {message}");
            AppendConsole($"[INFO] Spawning CLI process without shell context...");

            // Execute CLI SMS dispatch safely (no shell shell injection possible)
            var sendResult = await System.Threading.Tasks.Task.Run(() => 
                KdeCliHelper.SendSms(_settings.KdeCliPath, _settings.KdeDeviceId, phone, message)
            );

            AppendConsole($"[PROCESS EXIT] Code: {sendResult.ExitCode}");
            
            if (!string.IsNullOrEmpty(sendResult.Stdout))
            {
                AppendConsole($"[STDOUT]\r\n{sendResult.Stdout}");
            }
            if (!string.IsNullOrEmpty(sendResult.Stderr))
            {
                AppendConsole($"[STDERR]\r\n{sendResult.Stderr}");
            }

            if (sendResult.Success)
            {
                AppendConsole("[SUCCESS] SMS command dispatched successfully by KDE CLI.");
            }
            else
            {
                AppendConsole("[FAILURE] KDE Connect CLI reported an execution error.");
            }

            AppendConsole("[REST] Uploading audit results to WordPress...");
            
            var uploadResult = await WordPressAgentClient.SendTestResultAsync(_settings, phone, message, sendResult);

            if (uploadResult.Success)
            {
                AppendConsole("[REST] WordPress logged result successfully.");
            }
            else
            {
                AppendConsole($"[REST ERROR] Upload failed: {uploadResult.Message}");
            }

            btnSend.Enabled = true;
            btnClose.Enabled = true;
        }

        private void AppendConsole(string text)
        {
            txtConsoleOutput.AppendText(text + Environment.NewLine);
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
