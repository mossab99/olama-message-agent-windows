using System;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    public partial class LogViewerForm : Form
    {
        public LogViewerForm()
        {
            InitializeComponent();
        }

        private void LogViewerForm_Load(object sender, EventArgs e)
        {
            lblLogPath.Text = "Log Path: " + Logger.GetLogPath();
            ReloadLogs();

            // Subscribe to live log updates
            Logger.OnLogAdded += Logger_OnLogAdded;
        }

        private void Logger_OnLogAdded(string line)
        {
            if (this.IsDisposed) return;

            // Handle cross-thread calls safely since heartbeats trigger on background threads
            if (txtLogOutput.InvokeRequired)
            {
                txtLogOutput.BeginInvoke(new Action<string>(Logger_OnLogAdded), line);
            }
            else
            {
                txtLogOutput.AppendText(line + Environment.NewLine);
            }
        }

        private void LogViewerForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            // Unsubscribe to prevent memory leaks
            Logger.OnLogAdded -= Logger_OnLogAdded;
        }

        private void ReloadLogs()
        {
            txtLogOutput.Text = Logger.ReadAllLogs();
            
            // Scroll to the bottom
            txtLogOutput.SelectionStart = txtLogOutput.Text.Length;
            txtLogOutput.ScrollToCaret();
        }

        private void BtnRefresh_Click(object sender, EventArgs e)
        {
            ReloadLogs();
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to clear the local agent logs?", 
                                "Confirm Clear", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Logger.ClearLogs();
                ReloadLogs();
            }
        }

        private void BtnClose_Click(object sender, EventArgs e)
        {
            this.Close();
        }
    }
}
