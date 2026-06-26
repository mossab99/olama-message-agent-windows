using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // TLS 1.2 Security Protocol initialization
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Application UI initialization
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Handle unhandled UI thread exceptions
            Application.ThreadException += (sender, e) =>
            {
                Logger.Error("Unhandled UI thread exception caught.", e.Exception);
                MessageBox.Show(
                    "An unexpected error occurred. Details have been logged to the agent logs.",
                    "Unexpected Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            };

            // Handle unhandled background thread exceptions
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                Logger.Error("Unhandled AppDomain exception caught.", ex);
            };

            // Run the windowless Tray Application context
            Application.Run(new TrayApplicationContext());
        }
    }
}
