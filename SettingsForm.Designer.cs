namespace OlamaMessageAgent
{
    partial class SettingsForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.lblSiteUrl = new System.Windows.Forms.Label();
            this.txtSiteUrl = new System.Windows.Forms.TextBox();
            this.lblAgentUuid = new System.Windows.Forms.Label();
            this.txtAgentUuid = new System.Windows.Forms.TextBox();
            this.lblApiKey = new System.Windows.Forms.Label();
            this.txtApiKey = new System.Windows.Forms.TextBox();
            this.lblHeartbeatInterval = new System.Windows.Forms.Label();
            this.numHeartbeatInterval = new System.Windows.Forms.NumericUpDown();
            this.lblIntervalUnit = new System.Windows.Forms.Label();
            this.grpKdeSettings = new System.Windows.Forms.GroupBox();
            this.cmbKdeDevices = new System.Windows.Forms.ComboBox();
            this.lblKdeDeviceId = new System.Windows.Forms.Label();
            this.btnDetectKde = new System.Windows.Forms.Button();
            this.txtKdeCliPath = new System.Windows.Forms.TextBox();
            this.lblKdeCliPath = new System.Windows.Forms.Label();
            this.btnTestConnection = new System.Windows.Forms.Button();
            this.btnSave = new System.Windows.Forms.Button();
            this.btnCancel = new System.Windows.Forms.Button();
            this.lblStatusHeader = new System.Windows.Forms.Label();
            this.lblStatus = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.numHeartbeatInterval)).BeginInit();
            this.grpKdeSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // lblSiteUrl
            // 
            this.lblSiteUrl.AutoSize = true;
            this.lblSiteUrl.Location = new System.Drawing.Point(12, 15);
            this.lblSiteUrl.Name = "lblSiteUrl";
            this.lblSiteUrl.Size = new System.Drawing.Size(107, 15);
            this.lblSiteUrl.TabIndex = 0;
            this.lblSiteUrl.Text = "WordPress Site URL:";
            // 
            // txtSiteUrl
            // 
            this.txtSiteUrl.Location = new System.Drawing.Point(135, 12);
            this.txtSiteUrl.Name = "txtSiteUrl";
            this.txtSiteUrl.Size = new System.Drawing.Size(327, 23);
            this.txtSiteUrl.TabIndex = 1;
            // 
            // lblAgentUuid
            // 
            this.lblAgentUuid.AutoSize = true;
            this.lblAgentUuid.Location = new System.Drawing.Point(12, 47);
            this.lblAgentUuid.Name = "lblAgentUuid";
            this.lblAgentUuid.Size = new System.Drawing.Size(71, 15);
            this.lblAgentUuid.TabIndex = 2;
            this.lblAgentUuid.Text = "Agent UUID:";
            // 
            // txtAgentUuid
            // 
            this.txtAgentUuid.Location = new System.Drawing.Point(135, 44);
            this.txtAgentUuid.Name = "txtAgentUuid";
            this.txtAgentUuid.Size = new System.Drawing.Size(327, 23);
            this.txtAgentUuid.TabIndex = 3;
            // 
            // lblApiKey
            // 
            this.lblApiKey.AutoSize = true;
            this.lblApiKey.Location = new System.Drawing.Point(12, 79);
            this.lblApiKey.Name = "lblApiKey";
            this.lblApiKey.Size = new System.Drawing.Size(83, 15);
            this.lblApiKey.TabIndex = 4;
            this.lblApiKey.Text = "API Secret Key:";
            // 
            // txtApiKey
            // 
            this.txtApiKey.Location = new System.Drawing.Point(135, 76);
            this.txtApiKey.Name = "txtApiKey";
            this.txtApiKey.PasswordChar = '●';
            this.txtApiKey.Size = new System.Drawing.Size(327, 23);
            this.txtApiKey.TabIndex = 5;
            // 
            // lblHeartbeatInterval
            // 
            this.lblHeartbeatInterval.AutoSize = true;
            this.lblHeartbeatInterval.Location = new System.Drawing.Point(12, 111);
            this.lblHeartbeatInterval.Name = "lblHeartbeatInterval";
            this.lblHeartbeatInterval.Size = new System.Drawing.Size(104, 15);
            this.lblHeartbeatInterval.TabIndex = 6;
            this.lblHeartbeatInterval.Text = "Heartbeat Interval:";
            // 
            // numHeartbeatInterval
            // 
            this.numHeartbeatInterval.Location = new System.Drawing.Point(135, 109);
            this.numHeartbeatInterval.Maximum = new decimal(new int[] {
            3600,
            0,
            0,
            0});
            this.numHeartbeatInterval.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.numHeartbeatInterval.Name = "numHeartbeatInterval";
            this.numHeartbeatInterval.Size = new System.Drawing.Size(80, 23);
            this.numHeartbeatInterval.TabIndex = 7;
            this.numHeartbeatInterval.Value = new decimal(new int[] {
            60,
            0,
            0,
            0});
            // 
            // lblIntervalUnit
            // 
            this.lblIntervalUnit.AutoSize = true;
            this.lblIntervalUnit.Location = new System.Drawing.Point(221, 111);
            this.lblIntervalUnit.Name = "lblIntervalUnit";
            this.lblIntervalUnit.Size = new System.Drawing.Size(50, 15);
            this.lblIntervalUnit.TabIndex = 8;
            this.lblIntervalUnit.Text = "seconds";
            // 
            // grpKdeSettings
            // 
            this.grpKdeSettings.Controls.Add(this.cmbKdeDevices);
            this.grpKdeSettings.Controls.Add(this.lblKdeDeviceId);
            this.grpKdeSettings.Controls.Add(this.btnDetectKde);
            this.grpKdeSettings.Controls.Add(this.txtKdeCliPath);
            this.grpKdeSettings.Controls.Add(this.lblKdeCliPath);
            this.grpKdeSettings.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.grpKdeSettings.Location = new System.Drawing.Point(15, 148);
            this.grpKdeSettings.Name = "grpKdeSettings";
            this.grpKdeSettings.Size = new System.Drawing.Size(447, 140);
            this.grpKdeSettings.TabIndex = 9;
            this.grpKdeSettings.TabStop = false;
            this.grpKdeSettings.Text = "KDE Connect Local Options";
            // 
            // cmbKdeDevices
            // 
            this.cmbKdeDevices.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.cmbKdeDevices.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.cmbKdeDevices.FormattingEnabled = true;
            this.cmbKdeDevices.Location = new System.Drawing.Point(120, 68);
            this.cmbKdeDevices.Name = "cmbKdeDevices";
            this.cmbKdeDevices.Size = new System.Drawing.Size(315, 23);
            this.cmbKdeDevices.TabIndex = 4;
            // 
            // lblKdeDeviceId
            // 
            this.lblKdeDeviceId.AutoSize = true;
            this.lblKdeDeviceId.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblKdeDeviceId.Location = new System.Drawing.Point(12, 71);
            this.lblKdeDeviceId.Name = "lblKdeDeviceId";
            this.lblKdeDeviceId.Size = new System.Drawing.Size(91, 15);
            this.lblKdeDeviceId.TabIndex = 3;
            this.lblKdeDeviceId.Text = "Target Device ID:";
            // 
            // btnDetectKde
            // 
            this.btnDetectKde.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnDetectKde.Location = new System.Drawing.Point(120, 102);
            this.btnDetectKde.Name = "btnDetectKde";
            this.btnDetectKde.Size = new System.Drawing.Size(160, 25);
            this.btnDetectKde.TabIndex = 2;
            this.btnDetectKde.Text = "Detect CLI & Paired Devices";
            this.btnDetectKde.UseVisualStyleBackColor = true;
            this.btnDetectKde.Click += new System.EventHandler(this.BtnDetectKde_Click);
            // 
            // txtKdeCliPath
            // 
            this.txtKdeCliPath.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.txtKdeCliPath.Location = new System.Drawing.Point(120, 31);
            this.txtKdeCliPath.Name = "txtKdeCliPath";
            this.txtKdeCliPath.Size = new System.Drawing.Size(315, 23);
            this.txtKdeCliPath.TabIndex = 1;
            // 
            // lblKdeCliPath
            // 
            this.lblKdeCliPath.AutoSize = true;
            this.lblKdeCliPath.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblKdeCliPath.Location = new System.Drawing.Point(12, 34);
            this.lblKdeCliPath.Name = "lblKdeCliPath";
            this.lblKdeCliPath.Size = new System.Drawing.Size(78, 15);
            this.lblKdeCliPath.TabIndex = 0;
            this.lblKdeCliPath.Text = "KDE CLI Path:";
            // 
            // btnTestConnection
            // 
            this.btnTestConnection.Location = new System.Drawing.Point(15, 303);
            this.btnTestConnection.Name = "btnTestConnection";
            this.btnTestConnection.Size = new System.Drawing.Size(120, 25);
            this.btnTestConnection.TabIndex = 10;
            this.btnTestConnection.Text = "Test Connection";
            this.btnTestConnection.UseVisualStyleBackColor = true;
            this.btnTestConnection.Click += new System.EventHandler(this.BtnTestConnection_Click);
            // 
            // btnSave
            // 
            this.btnSave.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnSave.Location = new System.Drawing.Point(286, 303);
            this.btnSave.Name = "btnSave";
            this.btnSave.Size = new System.Drawing.Size(85, 25);
            this.btnSave.TabIndex = 11;
            this.btnSave.Text = "Save";
            this.btnSave.UseVisualStyleBackColor = true;
            this.btnSave.Click += new System.EventHandler(this.BtnSave_Click);
            // 
            // btnCancel
            // 
            this.btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.btnCancel.Location = new System.Drawing.Point(377, 303);
            this.btnCancel.Name = "btnCancel";
            this.btnCancel.Size = new System.Drawing.Size(85, 25);
            this.btnCancel.TabIndex = 12;
            this.btnCancel.Text = "Cancel";
            this.btnCancel.UseVisualStyleBackColor = true;
            this.btnCancel.Click += new System.EventHandler(this.BtnCancel_Click);
            // 
            // lblStatusHeader
            // 
            this.lblStatusHeader.AutoSize = true;
            this.lblStatusHeader.Location = new System.Drawing.Point(15, 340);
            this.lblStatusHeader.Name = "lblStatusHeader";
            this.lblStatusHeader.Size = new System.Drawing.Size(105, 15);
            this.lblStatusHeader.TabIndex = 13;
            this.lblStatusHeader.Text = "Connection Status:";
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lblStatus.ForeColor = System.Drawing.Color.DarkGray;
            this.lblStatus.Location = new System.Drawing.Point(135, 340);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(78, 15);
            this.lblStatus.TabIndex = 14;
            this.lblStatus.Text = "Unconfigured";
            // 
            // SettingsForm
            // 
            this.AcceptButton = this.btnSave;
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.btnCancel;
            this.ClientSize = new System.Drawing.Size(477, 372);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.lblStatusHeader);
            this.Controls.Add(this.btnCancel);
            this.Controls.Add(this.btnSave);
            this.Controls.Add(this.btnTestConnection);
            this.Controls.Add(this.grpKdeSettings);
            this.Controls.Add(this.lblIntervalUnit);
            this.Controls.Add(this.numHeartbeatInterval);
            this.Controls.Add(this.lblHeartbeatInterval);
            this.Controls.Add(this.txtApiKey);
            this.Controls.Add(this.lblApiKey);
            this.Controls.Add(this.txtAgentUuid);
            this.Controls.Add(this.lblAgentUuid);
            this.Controls.Add(this.txtSiteUrl);
            this.Controls.Add(this.lblSiteUrl);
            this.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "SettingsForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Olama Message Agent — Configuration";
            this.Load += new System.EventHandler(this.SettingsForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numHeartbeatInterval)).EndInit();
            this.grpKdeSettings.ResumeLayout(false);
            this.grpKdeSettings.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label lblSiteUrl;
        private System.Windows.Forms.TextBox txtSiteUrl;
        private System.Windows.Forms.Label lblAgentUuid;
        private System.Windows.Forms.TextBox txtAgentUuid;
        private System.Windows.Forms.Label lblApiKey;
        private System.Windows.Forms.TextBox txtApiKey;
        private System.Windows.Forms.Label lblHeartbeatInterval;
        private System.Windows.Forms.NumericUpDown numHeartbeatInterval;
        private System.Windows.Forms.Label lblIntervalUnit;
        private System.Windows.Forms.GroupBox grpKdeSettings;
        private System.Windows.Forms.Label lblKdeCliPath;
        private System.Windows.Forms.TextBox txtKdeCliPath;
        private System.Windows.Forms.Button btnDetectKde;
        private System.Windows.Forms.ComboBox cmbKdeDevices;
        private System.Windows.Forms.Label lblKdeDeviceId;
        private System.Windows.Forms.Button btnTestConnection;
        private System.Windows.Forms.Button btnSave;
        private System.Windows.Forms.Button btnCancel;
        private System.Windows.Forms.Label lblStatusHeader;
        private System.Windows.Forms.Label lblStatus;
    }
}
