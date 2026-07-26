using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test")
            {
                File.WriteAllText(args[1], RecoveryEngine.BuildSelfTest(), Encoding.UTF8);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class PackageState
    {
        public bool Installed;
        public string Status = "NotInstalled";
        public string InstallLocation = "";
        public string PackageFullName = "";
        public bool IsOk { get { return Installed && Status.Equals("Ok", StringComparison.OrdinalIgnoreCase); } }
    }

    internal sealed class CommandResult
    {
        public int ExitCode;
        public string Output = "";
        public bool TimedOut;
    }

    internal static class RecoveryEngine
    {
        public const string PackageName = "OpenAI.Codex";
        public const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
        public const string StoreProductId = "9PLM9XGG6VKS";

        public static string LogRoot
        {
            get
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CodexRecoveryCenter", "logs");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static PackageState GetPackageState()
        {
            CommandResult result = GetPackageProbe();
            string line = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value =>
                    value.StartsWith("INSTALLED|||", StringComparison.Ordinal) ||
                    value.Equals("NOT_INSTALLED", StringComparison.Ordinal)) ?? "";
            if (!line.StartsWith("INSTALLED|||", StringComparison.Ordinal))
                return new PackageState();

            string[] parts = line.Split(new[] { "|||" }, StringSplitOptions.None);
            return new PackageState
            {
                Installed = true,
                Status = parts.Length > 1 ? parts[1].Trim() : "Unknown",
                InstallLocation = parts.Length > 2 ? parts[2].Trim() : "",
                PackageFullName = parts.Length > 3 ? parts[3].Trim() : ""
            };
        }

        private static CommandResult GetPackageProbe()
        {
            string script =
                "$p=Get-AppxPackage -Name 'OpenAI.Codex'|Select-Object -First 1;" +
                "if($null-eq$p){'NOT_INSTALLED'}else{" +
                "'INSTALLED|||'+$p.Status+'|||'+$p.InstallLocation+'|||'+$p.PackageFullName}";
            return RunPowerShell(script, 20000);
        }

        public static bool IsCodexRunning()
        {
            return Process.GetProcessesByName("ChatGPT").Length > 0;
        }

        public static void StopCodex()
        {
            foreach (string name in new[] { "ChatGPT", "codex" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try { process.Kill(); process.WaitForExit(5000); }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            Thread.Sleep(1500);
        }

        public static bool LaunchSafe(PackageState state)
        {
            if (!state.IsOk || String.IsNullOrWhiteSpace(state.InstallLocation))
                return false;
            string exe = Path.Combine(state.InstallLocation, "app", "ChatGPT.exe");
            if (!File.Exists(exe))
                return false;
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "--disable-gpu",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)
            });
            return true;
        }

        public static CommandResult RepairRegistration()
        {
            string script =
                "Add-AppxPackage -RegisterByFamilyName " +
                "-MainPackage 'OpenAI.Codex_2p2nqsd0c76g0' " +
                "-ForceTargetApplicationShutdown -ErrorAction Stop";
            return RunPowerShell(script, 60000);
        }

        public static CommandResult RestageFromMicrosoftStore()
        {
            string winget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            if (!File.Exists(winget))
                return new CommandResult { ExitCode = -2, Output = "winget.exe is unavailable." };

            string args =
                "install --id " + StoreProductId +
                " --source msstore --force --silent --disable-interactivity" +
                " --accept-package-agreements --accept-source-agreements";
            return RunProcess(winget, args, 300000);
        }

        public static PackageState WaitForOk(int seconds, Action<string> progress)
        {
            PackageState state = new PackageState();
            for (int elapsed = 0; elapsed <= seconds; elapsed += 3)
            {
                state = GetPackageState();
                progress("等待 Windows 完成处理：" + state.Status + "（" + elapsed + "/" + seconds + " 秒）");
                if (state.IsOk)
                    return state;
                Thread.Sleep(3000);
            }
            return state;
        }

        public static CommandResult RunPowerShell(string script, int timeoutMs)
        {
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            return RunProcess(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell",
                    "v1.0", "powershell.exe"),
                "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                timeoutMs);
        }

        public static CommandResult RunProcess(string file, string args, int timeoutMs)
        {
            var result = new CommandResult();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                var output = new StringBuilder();
                process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(timeoutMs))
                {
                    result.TimedOut = true;
                    try { process.Kill(); } catch { }
                }
                else
                {
                    process.WaitForExit();
                    result.ExitCode = process.ExitCode;
                }
                result.Output = output.ToString();
            }
            return result;
        }

        public static string BuildSelfTest()
        {
            PackageState state = GetPackageState();
            string winget = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "winget.exe");
            return String.Join(Environment.NewLine, new[]
            {
                "time=" + DateTime.Now.ToString("O"),
                "packageInstalled=" + state.Installed,
                "packageStatus=" + state.Status,
                "packageFullName=" + state.PackageFullName,
                "executableExists=" + File.Exists(Path.Combine(state.InstallLocation ?? "", "app", "ChatGPT.exe")),
                "wingetExists=" + File.Exists(winget),
                "codexRunning=" + IsCodexRunning()
            });
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label status;
        private readonly ProgressBar progress;
        private readonly TextBox log;
        private readonly Button checkButton;
        private readonly Button safeButton;
        private readonly Button repairButton;
        private readonly Button storeButton;
        private readonly string sessionLog;

        public MainForm()
        {
            Text = "Codex 恢复中心";
            Width = 760;
            Height = 570;
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(242, 245, 248);
            Font = new Font("Microsoft YaHei UI", 10F);

            var title = new Label
            {
                Text = "Codex 恢复中心",
                Font = new Font("Microsoft YaHei UI", 20F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(28, 22)
            };
            var desc = new Label
            {
                Text = "不清空聊天数据。先安全启动；包注册异常时修复；仍异常时从微软商店官方源重新暂存。",
                AutoSize = true,
                ForeColor = Color.FromArgb(70, 78, 88),
                Location = new Point(31, 67)
            };
            status = new Label
            {
                Text = "正在检查……",
                Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(31, 105)
            };
            progress = new ProgressBar
            {
                Location = new Point(33, 137),
                Width = 680,
                Height = 8,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 25
            };

            checkButton = NewButton("检查状态", 32, 168, 145, Color.FromArgb(90, 103, 120));
            safeButton = NewButton("安全启动", 190, 168, 145, Color.FromArgb(56, 112, 91));
            repairButton = NewButton("一键修复并启动", 348, 168, 190, Color.FromArgb(47, 94, 151));
            storeButton = NewButton("打开官方商店页", 551, 168, 162, Color.FromArgb(116, 92, 130));

            log = new TextBox
            {
                Location = new Point(32, 222),
                Width = 681,
                Height = 270,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5F)
            };
            var note = new Label
            {
                Text = "提示：一键修复会关闭正在运行的 Codex。请先保存任务；只有官方商店重新暂存失败时才需要手工处理。",
                AutoSize = true,
                ForeColor = Color.FromArgb(120, 70, 48),
                Location = new Point(32, 505)
            };

            Controls.AddRange(new Control[]
                { title, desc, status, progress, checkButton, safeButton, repairButton, storeButton, log, note });

            checkButton.Click += async (s, e) => await CheckAsync();
            safeButton.Click += async (s, e) => await SafeLaunchAsync();
            repairButton.Click += async (s, e) => await RepairAsync();
            storeButton.Click += (s, e) =>
                Process.Start("ms-windows-store://pdp/?ProductId=" + RecoveryEngine.StoreProductId);

            sessionLog = Path.Combine(RecoveryEngine.LogRoot,
                "recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            Shown += async (s, e) => await CheckAsync();
        }

        private Button NewButton(string text, int x, int y, int width, Color color)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Width = width,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                BackColor = color,
                ForeColor = Color.White,
                FlatAppearance = { BorderSize = 0 },
                Cursor = Cursors.Hand
            };
        }

        private void WriteLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(WriteLog), text);
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            log.AppendText(line + Environment.NewLine);
            try { File.AppendAllText(sessionLog, line + Environment.NewLine, Encoding.UTF8); } catch { }
        }

        private void SetBusy(bool busy, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string>(SetBusy), busy, text);
                return;
            }
            checkButton.Enabled = safeButton.Enabled = repairButton.Enabled = storeButton.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            status.Text = text;
        }

        private async Task CheckAsync()
        {
            SetBusy(true, "正在检查安装与运行状态……");
            PackageState state = await Task.Run(() => RecoveryEngine.GetPackageState());
            bool running = RecoveryEngine.IsCodexRunning();
            WriteLog("安装状态：" + state.Status + "；正在运行：" + (running ? "是" : "否"));
            SetBusy(false, state.IsOk ? "安装包正常（Ok）" : "安装包需要修复：" + state.Status);
        }

        private async Task SafeLaunchAsync()
        {
            if (RecoveryEngine.IsCodexRunning())
            {
                DialogResult answer = MessageBox.Show(
                    "Codex 当前仍在运行。安全模式必须完全关闭后重新启动。\n\n是否关闭所有 Codex 进程并安全启动？",
                    "确认安全重启", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            SetBusy(true, "正在以 GPU 安全模式启动……");
            bool launched = await Task.Run(() =>
            {
                RecoveryEngine.StopCodex();
                return RecoveryEngine.LaunchSafe(RecoveryEngine.GetPackageState());
            });
            WriteLog(launched ? "已发送 GPU 安全模式启动命令。" : "安全启动失败：安装包状态不是 Ok。");
            SetBusy(false, launched ? "安全模式已启动" : "请执行一键修复");
        }

        private async Task RepairAsync()
        {
            DialogResult answer = MessageBox.Show(
                "修复过程会关闭所有 Codex 窗口，但不会主动重置或清空应用数据。\n\n是否继续？",
                "开始修复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "正在修复，请不要重复点击……");
            await Task.Run(() =>
            {
                WriteLog("关闭残留 Codex 进程。");
                RecoveryEngine.StopCodex();
                PackageState state = RecoveryEngine.GetPackageState();
                WriteLog("初始包状态：" + state.Status);

                if (!state.IsOk)
                {
                    WriteLog("执行当前用户注册修复。");
                    CommandResult register = RecoveryEngine.RepairRegistration();
                    WriteLog("注册修复退出码：" + register.ExitCode);
                    state = RecoveryEngine.WaitForOk(45, WriteLog);
                }

                if (!state.IsOk)
                {
                    WriteLog("注册修复不足，改用微软商店官方源重新暂存。");
                    CommandResult store = RecoveryEngine.RestageFromMicrosoftStore();
                    WriteLog("微软商店源退出码：" + store.ExitCode +
                        (store.TimedOut ? "（超时）" : ""));
                    if (!String.IsNullOrWhiteSpace(store.Output))
                        WriteLog(store.Output.Trim());
                    state = RecoveryEngine.WaitForOk(240, WriteLog);
                }

                if (!state.IsOk)
                {
                    SetBusy(false, "自动修复未完成，请打开官方商店页");
                    WriteLog("最终状态仍为：" + state.Status);
                    BeginInvoke(new Action(() =>
                    {
                        MessageBox.Show(
                            "自动修复未能把包恢复为 Ok。日志已经保存。\n请点击“打开官方商店页”完成更新。",
                            "仍需商店处理", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }));
                    return;
                }

                WriteLog("包状态已经恢复为 Ok，启动 GPU 安全模式。");
                bool launched = RecoveryEngine.LaunchSafe(state);
                WriteLog(launched ? "修复完成，已启动。" : "包已恢复，但启动命令失败。");
                SetBusy(false, launched ? "修复完成，安全模式已启动" : "包已恢复，请点击安全启动");
            });
        }
    }
}
