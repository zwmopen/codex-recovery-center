using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    internal sealed class MainForm : ThemedForm
    {
        private readonly Label statusTitle;
        private readonly Label statusDetail;
        private readonly Label statusDot;
        private readonly ProgressBar progress;
        private readonly SoftButton checkButton;
        private readonly SoftButton safeButton;
        private readonly SoftButton repairButton;
        private readonly SoftButton storeButton;
        private readonly SoftButton settingsButton;
        private readonly SoftButton aboutButton;
        private readonly SoftButton logButton;
        private readonly string sessionLog;
        private readonly StringBuilder logBuffer = new StringBuilder();
        private AppSettings appSettings;
        private bool lastPackageOk = true;

        public MainForm()
        {
            appSettings = SettingsStore.Load();
            Text = "Codex 恢复中心";
            ClientSize = new Size(800, 570);
            MinimumSize = new Size(700, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            TrySetIcon();

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(36, 26, 36, 22),
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            Controls.Add(root);

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                BackColor = Color.Transparent
            };
            root.Controls.Add(header, 0, 0);

            var appTitle = MakeLabel("Codex 恢复中心", 0, 1, 16F, FontStyle.Bold, "title");
            var identity = MakeLabel(
                "WINDOWS · 独立恢复工具", 2, 32, 8F, FontStyle.Bold, "accent");
            var version = MakeLabel(
                "v" + ProductInfo.Version, 0, 13, 8.5F, FontStyle.Regular, "muted");
            settingsButton = MakeButton("设置", 0, 4, 72, 36, ButtonVisualRole.Ghost);
            aboutButton = MakeButton("关于", 0, 4, 72, 36, ButtonVisualRole.Ghost);
            header.Controls.AddRange(new Control[]
                { appTitle, identity, version, settingsButton, aboutButton });
            header.Layout += (s, e) =>
            {
                aboutButton.Left = header.ClientSize.Width - aboutButton.Width;
                settingsButton.Left = aboutButton.Left - settingsButton.Width - 6;
                version.Left = settingsButton.Left - version.Width - 14;
            };

            var section = MakeLabel(
                "让 Codex 重新正常打开", 0, 0, 11F, FontStyle.Bold, "title");
            section.Dock = DockStyle.Fill;
            section.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(section, 0, 1);

            var description = MakeLabel(
                "先判断问题在哪一层，再执行最小修复；只有必要时才调用微软商店官方源。",
                0, 0, 9F, FontStyle.Regular, "muted");
            description.Dock = DockStyle.Fill;
            description.TextAlign = ContentAlignment.TopLeft;
            root.Controls.Add(description, 0, 2);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 8)
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210F));
            root.Controls.Add(body, 0, 3);

            var statusCard = new SoftPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 24, 0),
                CornerRadius = 22,
                Tag = "card"
            };
            body.Controls.Add(statusCard, 0, 0);

            statusDot = MakeLabel("●", 28, 27, 18F, FontStyle.Regular, "warning");
            statusTitle = MakeLabel(
                "正在检查当前状态", 66, 27, 13F, FontStyle.Bold, "title");
            statusDetail = MakeLabel(
                "通常几秒钟就能完成", 68, 59, 9.5F, FontStyle.Regular, "muted");
            progress = new ProgressBar
            {
                Location = new Point(68, 89),
                Width = 400,
                Height = 5,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 22
            };

            var pathTitle = MakeLabel("恢复路径", 30, 128, 9.5F, FontStyle.Bold, "title");
            var step1 = MakeLabel(
                "01   检查安装与运行状态", 31, 160, 9F, FontStyle.Regular, "muted");
            var step2 = MakeLabel(
                "02   修复当前用户注册", 31, 190, 9F, FontStyle.Regular, "muted");
            var step3 = MakeLabel(
                "03   必要时从微软商店重新暂存", 31, 220, 9F, FontStyle.Regular, "muted");
            var safety = MakeLabel(
                "不会主动重置应用数据", 31, 254, 8.5F, FontStyle.Bold, "accent");
            logButton = MakeButton(
                "查看处理记录", 24, 0, 148, 36, ButtonVisualRole.Ghost);
            logButton.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            statusCard.Controls.AddRange(new Control[]
            {
                statusDot, statusTitle, statusDetail, progress, pathTitle,
                step1, step2, step3, safety, logButton
            });
            statusCard.Layout += (s, e) =>
                logButton.Top = statusCard.ClientSize.Height - logButton.Height - 16;

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 4,
                Margin = new Padding(0)
            };
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 178F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.Controls.Add(actions, 1, 0);

            repairButton = MakeButton(
                "一键恢复并启动", 0, 0, 210, 52, ButtonVisualRole.Primary);
            repairButton.Dock = DockStyle.Fill;
            repairButton.Margin = new Padding(8, 0, 8, 0);
            repairButton.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            actions.Controls.Add(repairButton, 0, 0);

            var actionGroup = new SoftPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                CornerRadius = 20,
                Tag = "card"
            };
            actions.Controls.Add(actionGroup, 0, 2);
            safeButton = MakeButton(
                "安全模式启动", 20, 18, 170, 40, ButtonVisualRole.Secondary);
            checkButton = MakeButton(
                "重新检查", 20, 67, 170, 40, ButtonVisualRole.Secondary);
            storeButton = MakeButton(
                "微软商店修复", 20, 116, 170, 40, ButtonVisualRole.Secondary);
            safeButton.Anchor = checkButton.Anchor = storeButton.Anchor =
                AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            actionGroup.Controls.AddRange(new Control[] { safeButton, checkButton, storeButton });
            actionGroup.Resize += (s, e) =>
            {
                int width = Math.Max(120, actionGroup.ClientSize.Width - 40);
                safeButton.Width = checkButton.Width = storeButton.Width = width;
            };

            var footer = MakeLabel(
                "本地运行 · 不上传聊天与凭据 · 修复前会先关闭所有 Codex 窗口",
                0, 0, 8.5F, FontStyle.Regular, "muted");
            footer.Dock = DockStyle.Fill;
            footer.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(footer, 0, 4);

            sessionLog = Path.Combine(RecoveryEngine.LogRoot,
                "recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");

            checkButton.Click += async (s, e) => await CheckAsync();
            safeButton.Click += async (s, e) => await SafeLaunchAsync();
            repairButton.Click += async (s, e) => await RepairAsync();
            storeButton.Click += (s, e) =>
                Process.Start("ms-windows-store://pdp/?ProductId=" + RecoveryEngine.StoreProductId);
            settingsButton.Click += (s, e) => OpenSettings();
            aboutButton.Click += (s, e) =>
            {
                using (var about = new AboutForm(appSettings.Theme))
                    about.ShowDialog(this);
            };
            logButton.Click += (s, e) =>
            {
                using (var logs = new LogForm(appSettings.Theme, logBuffer.ToString(), sessionLog))
                    logs.ShowDialog(this);
            };

            ThemeManager.Apply(this, appSettings.Theme);
            Shown += async (s, e) =>
            {
                await CheckAsync();
                if (appSettings.AutoCheckUpdates)
                    await CheckUpdatesAsync(false);
            };
        }

        private ThemePalette CurrentPalette
        {
            get { return ThemeManager.Get(appSettings.Theme); }
        }

        private Label MakeLabel(string text, int x, int y, float size,
            FontStyle style, string role)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                Font = new Font("Microsoft YaHei UI", size, style),
                BackColor = Color.Transparent,
                Tag = role
            };
        }

        private SoftButton MakeButton(string text, int x, int y, int width, int height,
            ButtonVisualRole role)
        {
            return new SoftButton
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                VisualRole = role,
                Font = new Font("Microsoft YaHei UI", 9F,
                    role == ButtonVisualRole.Primary ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        private void TrySetIcon()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) Icon = icon;
            }
            catch { }
        }

        private void OpenSettings()
        {
            using (var settings = new SettingsForm(
                appSettings, ApplySettings, () => CheckUpdatesAsync(true)))
                settings.ShowDialog(this);
        }

        private void ApplySettings(AppSettings updated)
        {
            appSettings = updated;
            ThemeManager.Apply(this, appSettings.Theme);
            statusDot.ForeColor = lastPackageOk
                ? CurrentPalette.Accent : CurrentPalette.Danger;
            WriteLog("设置已保存；主题：" +
                (appSettings.Theme == VisualTheme.Neumorphic ? "拟态悬浮" : "克制玻璃") + "。");
        }

        private void WriteLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(WriteLog), text);
                return;
            }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            logBuffer.AppendLine(line);
            try { File.AppendAllText(sessionLog, line + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }

        private void SetBusy(bool busy, string title, string detail)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool, string, string>(SetBusy), busy, title, detail);
                return;
            }
            checkButton.Enabled = safeButton.Enabled = repairButton.Enabled =
                storeButton.Enabled = settingsButton.Enabled = aboutButton.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            progress.Visible = busy;
            statusTitle.Text = title;
            if (!String.IsNullOrWhiteSpace(detail))
                statusDetail.Text = detail;
            if (busy) statusDot.ForeColor = CurrentPalette.Warning;
        }

        private void SetStatusDetail(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatusDetail), text);
                return;
            }
            statusDetail.Text = text;
        }

        private async Task CheckAsync()
        {
            SetBusy(true, "正在检查当前状态", "正在读取安装与运行状态……");
            PackageState state = await Task.Run(() => RecoveryEngine.GetPackageState());
            bool running = RecoveryEngine.IsCodexRunning();
            lastPackageOk = state.IsOk;
            WriteLog("安装状态：" + state.Status + "；正在运行：" + (running ? "是" : "否"));
            statusDot.ForeColor = state.IsOk ? CurrentPalette.Accent : CurrentPalette.Danger;
            SetBusy(false,
                state.IsOk ? "现在状态正常" : "需要修复",
                state.IsOk
                    ? (running ? "安装正常，Codex 当前正在运行" : "安装正常，需要时可以直接启动")
                    : "Windows 检测到安装注册异常，建议立即恢复");
        }

        private async Task SafeLaunchAsync()
        {
            if (RecoveryEngine.IsCodexRunning())
            {
                DialogResult answer = MessageBox.Show(
                    "Codex 当前仍在运行。安全模式必须完全关闭后重新启动。\n\n" +
                    "是否关闭所有 Codex 进程并安全启动？",
                    "确认安全重启", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            SetBusy(true, "正在安全启动", "关闭 GPU 加速后重新打开 Codex……");
            bool launched = await Task.Run(() =>
            {
                RecoveryEngine.StopCodex();
                return RecoveryEngine.LaunchSafe(RecoveryEngine.GetPackageState());
            });
            WriteLog(launched ? "已发送 GPU 安全模式启动命令。" :
                "安全启动失败：安装包状态不是 Ok。");
            statusDot.ForeColor = launched ? CurrentPalette.Accent : CurrentPalette.Danger;
            SetBusy(false,
                launched ? "安全模式已启动" : "请先执行一键恢复",
                launched ? "已关闭 GPU 加速，用来降低多窗口崩溃风险" :
                    "安装状态异常，先恢复后再启动");
        }

        private async Task RepairAsync()
        {
            DialogResult answer = MessageBox.Show(
                "修复会关闭所有 Codex 窗口，但不会主动重置或清空应用数据。\n\n是否继续？",
                "开始修复", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;

            SetBusy(true, "正在开始修复", "请不要重复点击");
            await Task.Run(() =>
            {
                WriteLog("关闭残留 Codex 进程。");
                RecoveryEngine.StopCodex();
                PackageState state = RecoveryEngine.GetPackageState();
                WriteLog("初始包状态：" + state.Status);

                if (!state.IsOk)
                {
                    SetBusy(true, "正在修复应用注册", "先处理当前用户的安装注册");
                    CommandResult register = RecoveryEngine.RepairRegistration();
                    WriteLog("注册修复退出码：" + register.ExitCode);
                    if (register.ExitCode == 0 && !register.TimedOut)
                    {
                        int wait = state.Status.IndexOf(
                            "NeedsRemediation", StringComparison.OrdinalIgnoreCase) >= 0 ? 12 : 30;
                        state = RecoveryEngine.WaitForOk(wait, WriteLog);
                    }
                }

                if (!state.IsOk)
                {
                    SetBusy(true, "正在从微软商店恢复",
                        "Windows 正在重新校验程序包，通常需要 2–4 分钟");
                    int lastReported = -1;
                    CommandResult store = RecoveryEngine.RestageFromMicrosoftStore(elapsed =>
                    {
                        SetStatusDetail("微软商店处理中：已用 " + elapsed + " 秒（通常需要 2–4 分钟）");
                        if (elapsed >= 15 && elapsed / 15 != lastReported)
                        {
                            lastReported = elapsed / 15;
                            WriteLog("微软商店仍在处理，已用 " + elapsed + " 秒。");
                        }
                    });
                    WriteLog("微软商店源退出码：" + store.ExitCode +
                        (store.TimedOut ? "（超时）" : ""));
                    if (!String.IsNullOrWhiteSpace(store.Output))
                        WriteLog(store.Output.Trim());
                    state = RecoveryEngine.WaitForOk(240, WriteLog);
                }

                if (!state.IsOk)
                {
                    statusDot.ForeColor = CurrentPalette.Danger;
                    SetBusy(false, "自动恢复尚未完成",
                        "处理记录已经保留，可继续使用微软商店官方修复");
                    BeginInvoke(new Action(() => MessageBox.Show(
                        "自动恢复尚未把状态恢复为 Ok。\n请点击“微软商店修复”完成更新。",
                        "仍需商店处理", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                    return;
                }

                WriteLog("包状态已经恢复为 Ok，启动 GPU 安全模式。");
                bool launched = RecoveryEngine.LaunchSafe(state);
                WriteLog(launched ? "修复完成，已启动。" : "包已恢复，但启动命令失败。");
                statusDot.ForeColor = CurrentPalette.Accent;
                SetBusy(false, launched ? "恢复完成" : "安装已经恢复",
                    launched ? "已经用更稳妥的模式重新启动" : "现在可以再次启动");
            });
        }

        private async Task CheckUpdatesAsync(bool userInitiated)
        {
            bool restartScheduled = false;
            try
            {
                if (userInitiated)
                    SetBusy(true, "正在检查软件更新", "连接公开更新清单，通常几秒钟完成");

                UpdateInfo latest = await Task.Run(() => UpdateService.CheckLatest());
                WriteLog("更新检查：本机 v" + ProductInfo.Version + "；最新 v" + latest.Version + "。");
                if (!latest.IsNewerThan(ProductInfo.Version))
                {
                    if (userInitiated)
                        MessageBox.Show("当前已经是最新版 v" + ProductInfo.Version + "。",
                            "检查更新", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (!appSettings.AutoDownloadUpdates)
                {
                    DialogResult open = MessageBox.Show(
                        "发现新版 v" + latest.Version + "。\n\n是否打开 GitHub 发布页？",
                        "发现新版本", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (open == DialogResult.Yes) Process.Start(latest.ReleaseUrl);
                }
                else
                {
                    SetBusy(true, "正在安全下载 v" + latest.Version,
                        "下载后会核对公开清单中的 SHA-256");
                    string downloaded = await Task.Run(() => UpdateService.DownloadAndVerify(latest));
                    WriteLog("新版已下载并通过 SHA-256 校验：" + downloaded);
                    DialogResult install = MessageBox.Show(
                        "v" + latest.Version + " 已下载并通过 SHA-256 校验。\n\n" +
                        "安装只会关闭恢复中心，不会关闭 Codex。是否现在更新？",
                        "安全更新已就绪", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (install == DialogResult.Yes)
                    {
                        restartScheduled = true;
                        UpdateService.ScheduleReplaceAndRestart(downloaded);
                        Application.Exit();
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("检查更新失败：" + ex.Message);
                if (userInitiated)
                    MessageBox.Show("暂时无法完成更新检查。\n\n" + ex.Message,
                        "检查更新失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            if (!restartScheduled && !IsDisposed)
                await CheckAsync();
        }
    }

    internal sealed class LogForm : ThemedForm
    {
        public LogForm(VisualTheme theme, string content, string path)
        {
            Text = "处理记录 · Codex 恢复中心";
            ClientSize = new Size(650, 430);
            MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.None;

            var title = new Label
            {
                Text = "处理记录",
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(32, 25),
                BackColor = Color.Transparent,
                Tag = "title"
            };
            var hint = new Label
            {
                Text = "日志只保存在本机：" + path,
                AutoSize = true,
                Location = new Point(34, 58),
                BackColor = Color.Transparent,
                Tag = "muted"
            };
            var card = new SoftPanel
            {
                Location = new Point(28, 88),
                Size = new Size(594, 286),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom |
                    AnchorStyles.Left | AnchorStyles.Right,
                Padding = new Padding(24, 22, 24, 22),
                Tag = "card"
            };
            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                Text = String.IsNullOrWhiteSpace(content) ? "本次还没有处理记录。" : content,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            card.Controls.Add(box);
            var close = new SoftButton
            {
                Text = "关闭",
                Size = new Size(120, 40),
                Location = new Point(502, 382),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
                VisualRole = ButtonVisualRole.Primary
            };
            close.Click += (s, e) => Close();
            Controls.AddRange(new Control[] { title, hint, card, close });
            ThemeManager.Apply(this, theme);
        }
    }
}
