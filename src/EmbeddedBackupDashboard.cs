using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    internal static class BackupDashboardService
    {
        public static bool EnsureAvailable()
        {
            if (Probe()) return true;

            string dashboardDirectory = BackupMonitorPaths.DashboardDirectory;
            string server = Path.Combine(dashboardDirectory, "backup_dashboard_server.py");
            if (!File.Exists(server)) return false;

            string python = FindPython();
            if (String.IsNullOrWhiteSpace(python)) return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "-u \"" + server + "\"",
                    WorkingDirectory = dashboardDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
                return false;
            }

            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(250);
                if (Probe()) return true;
            }
            return false;
        }

        private static bool Probe()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(BackupMonitorPaths.DashboardUrl + "api/status");
                request.Method = "GET";
                request.Timeout = 900;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
            }
            catch
            {
                return false;
            }
        }

        private static string FindPython()
        {
            string preferred = @"C:\Users\z\AppData\Local\Programs\Python\Python311\python.exe";
            if (File.Exists(preferred)) return preferred;

            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string item in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(item.Trim(), "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "";
        }
    }

    internal class EmbeddedWebDashboard : UserControl
    {
        private readonly WebView2 browser;
        private readonly Label statusLabel;
        private readonly string dashboardUrl;
        private readonly Func<bool> ensureAvailable;
        private bool initialized;

        public EmbeddedWebDashboard(string url, Func<bool> ensureService)
        {
            dashboardUrl = url;
            ensureAvailable = ensureService;
            BackColor = Color.FromArgb(226, 235, 241);
            Padding = new Padding(0);

            statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = "准备载入备份监控台…",
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(77, 99, 113),
                BackColor = Color.FromArgb(226, 235, 241),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular)
            };
            browser = new WebView2
            {
                Dock = DockStyle.Fill,
                Visible = false,
                BackColor = Color.FromArgb(226, 235, 241)
            };
            Controls.Add(browser);
            Controls.Add(statusLabel);
        }

        public async Task LoadDashboardAsync()
        {
            statusLabel.Visible = true;
            browser.Visible = false;
            statusLabel.Text = "正在连接本地备份监控台…";

            bool available = await Task.Run(() => ensureAvailable == null || ensureAvailable());
            if (!available)
            {
                statusLabel.Text = "备份监控台服务未能启动。请确认本机 Python 与备份面板文件仍在，\n然后重新点击左侧“备份中心”。";
                return;
            }

            try
            {
                if (!initialized)
                {
                    string userData = Path.Combine(RepairCenterPaths.Root, "webview2");
                    CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData, null);
                    await browser.EnsureCoreWebView2Async(environment);
                    browser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    browser.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    initialized = true;
                }

                browser.CoreWebView2.Navigate(dashboardUrl);
                browser.Visible = true;
                statusLabel.Visible = false;
            }
            catch (Exception ex)
            {
                browser.Visible = false;
                statusLabel.Visible = true;
                statusLabel.Text = "备份监控台内嵌失败：" + ex.Message;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && browser != null) browser.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class EmbeddedBackupDashboard : EmbeddedWebDashboard
    {
        public EmbeddedBackupDashboard()
            : base(BackupMonitorPaths.DashboardUrl, BackupDashboardService.EnsureAvailable)
        {
        }
    }

    internal sealed class EmbeddedOpenClawDashboard : EmbeddedWebDashboard
    {
        public EmbeddedOpenClawDashboard()
            : base(OpenClawController.DashboardUrl, OpenClawController.EnsureDashboardAvailable)
        {
        }
    }
}
