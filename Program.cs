using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace OlamaMessageAgent
{
    internal static class Program
    {
        private const string SingleInstanceMutexName = @"Local\OlamaMessageAgent.SingleInstance";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Olama Message Agent is already running in the notification area.",
                        "Olama Message Agent",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return;
                }

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
                GC.KeepAlive(singleInstanceMutex);
            }
        }
    }
}
