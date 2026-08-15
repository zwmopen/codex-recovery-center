using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    /// <summary>
    /// A user-first collection of small, repeatable actions. It deliberately
    /// reuses the diagnostic modules instead of creating a second repair path.
    /// </summary>
    internal sealed class QuickToolsForm : ThemedForm
    {
        private readonly VisualTheme visualTheme;
        private readonly ColorMode colorMode;
        private readonly Func<Task> runScan;
        private readonly Action<string> openModule;
        private readonly Action showMemory;
        private readonly Action<string> writeLog;
        private readonly List<SoftButton> actionButtons = new List<SoftButton>();
        private readonly Label statusLabel;
        private bool busy;

        public QuickToolsForm(VisualTheme theme, ColorMode mode, Func<Task> scan,
            Action<string> openModuleAction, Action memoryAction, Action<string> logAction)
        {
            visualTheme = theme;
            colorMode = mode;
            runScan = scan;
            openModule = openModuleAction;
            showMemory = memoryAction;
            writeLog = logAction;

            Text = "快捷工具 · Windows 修复中心";
            ClientSize = new Size(780, 650);
            MinimumSize = new Size(700, 560);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = LabelAt("快捷工具", 32, 24, 19F, FontStyle.Bold, "title");
            var subtitle = LabelAt("把重复的小事变成一键动作；有影响的操作会先说明并确认。",
                34, 60, 9.5F, FontStyle.Regular, "muted");
            var checkButton = ButtonAt("一键安全体检", 600, 24, 148, 42, ButtonVisualRole.Primary);
            checkButton.Click += async (s, e) => await RunScanAsync(checkButton);
            actionButtons.Add(checkButton);

            statusLabel = LabelAt("每项工具都会写入本机处理记录。", 34, 88, 9F,
                FontStyle.Regular, "muted");

            var panel = new FlowLayoutPanel
            {
                Location = new Point(28, 120),
                Size = new Size(724, 470),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };

            AddTool(panel, "清理可重建缓存", "预览开发、浏览器和临时缓存，只清理可重新生成的内容。",
                "检查并清理", () => RunRepairToolAsync("清理可重建缓存", new PowerSpaceRepairModule()));
            AddTool(panel, "刷新 DNS 缓存", "只刷新本机 DNS 缓存，不修改路由器和网络账号。",
                "刷新 DNS", () => RunRepairToolAsync("刷新 DNS 缓存", new NetworkRepairModule()));
            AddTool(panel, "修复蓝牙音频", "检查蓝牙耳机端点与 Windows 音频服务，必要时短暂刷新服务。",
                "检查并修复", () => RunRepairToolAsync("修复蓝牙音频", new BluetoothRepairModule()));
            AddTool(panel, "释放内存", "列出占用较高的程序，关闭前会显示名称和预计影响。",
                "打开释放工具", () => RunMemoryAsync());
            AddTool(panel, "启动小龙虾", "复用本机控制脚本启动 OpenClaw，不复制配置或密钥。",
                "启动小龙虾", () => RunOpenClawAsync());
            AddTool(panel, "打开存储分析", "查看磁盘容量、缓存候选和用户资料边界，先看清再处理。",
                "查看分析", () => OpenModuleAsync("storage"));
            AddTool(panel, "查看弹窗审计", "追查 PowerShell/CMD 的任务、父进程、命令行和事件证据。",
                "查看审计", () => OpenModuleAsync("popup-audit"));
            AddTool(panel, "查看启动项", "检查 Startup、Run/RunOnce、任务和桌面入口，不会直接删除。",
                "查看启动项", () => OpenModuleAsync("startup"));

            var closeButton = ButtonAt("关闭", 638, 604, 110, 38, ButtonVisualRole.Ghost);
            closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            closeButton.Click += (s, e) => Close();

            Controls.AddRange(new Control[] { title, subtitle, checkButton, statusLabel, panel, closeButton });
            ThemeManager.Apply(this, visualTheme, colorMode);
        }

        private void AddTool(FlowLayoutPanel panel, string title, string description,
            string actionText, Func<Task> action)
        {
            var card = new SoftPanel
            {
                Size = new Size(348, 128),
                Margin = new Padding(0, 0, 16, 16),
                Padding = new Padding(16, 12, 16, 12),
                Tag = "card"
            };
            var titleLabel = LabelAt(title, 16, 12, 10.5F, FontStyle.Bold, null);
            var descriptionLabel = LabelAt(description, 16, 39, 310, 38, 8.5F,
                FontStyle.Regular, "muted");
            var button = ButtonAt(actionText, 16, 84, 132, 34, ButtonVisualRole.Secondary);
            button.Click += async (s, e) => await RunActionAsync(button, action);
            actionButtons.Add(button);
            card.Controls.AddRange(new Control[] { titleLabel, descriptionLabel, button });
            panel.Controls.Add(card);
        }

        private async Task RunScanAsync(SoftButton source)
        {
            await RunActionAsync(source, async () =>
            {
                statusLabel.Text = "正在检查本机状态……";
                await runScan();
                statusLabel.Text = "检查完成；可回到主界面查看证据和建议。";
            });
        }

        private async Task RunRepairToolAsync(string title, IRepairModule module)
        {
            RepairFinding finding = await Task.Run(() => module.Detect());
            if (finding == null)
            {
                ShowResult(title, "没有收到检测结果。", false);
                return;
            }
            if (!finding.CanRepair)
            {
                ShowResult(title, finding.Summary +
                    (String.IsNullOrWhiteSpace(finding.Detail) ? "" : "\n\n" + finding.Detail), false);
                return;
            }

            string detail = String.IsNullOrWhiteSpace(finding.Detail) ? "" : "\n\n" + finding.Detail;
            DialogResult answer = MessageBox.Show(this,
                finding.Summary + detail + "\n\n风险：" + finding.Risk + "\n\n是否继续？",
                "确认" + title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                statusLabel.Text = "已取消" + title + "。";
                return;
            }

            statusLabel.Text = "正在处理" + title + "……";
            RepairFinding result = await Task.Run(() => module.Repair());
            string summary = result == null ? "没有收到修复结果。" : result.Summary;
            WriteLog(title + "：" + summary);
            ShowResult(title, summary, true);
        }

        private async Task RunOpenClawAsync()
        {
            if (!OpenClawController.IsAvailable)
            {
                ShowResult("启动小龙虾", "未找到本机小龙虾控制脚本，未执行任何修改。", false);
                return;
            }
            statusLabel.Text = "正在启动小龙虾……";
            CommandResult result = await Task.Run(() => OpenClawController.Run("Start", null, 240000));
            string summary = OpenClawController.Describe(result);
            WriteLog("启动小龙虾：" + summary);
            ShowResult("启动小龙虾", summary, result != null && result.ExitCode == 0);
        }

        private Task RunMemoryAsync()
        {
            showMemory();
            statusLabel.Text = "已打开释放内存工具。";
            return Task.CompletedTask;
        }

        private Task OpenModuleAsync(string id)
        {
            openModule(id);
            statusLabel.Text = "已切换到“" + id + "”模块。";
            return Task.CompletedTask;
        }

        private async Task RunActionAsync(SoftButton source, Func<Task> action)
        {
            if (busy) return;
            SetBusy(true, source);
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                statusLabel.Text = "操作未完成：" + ex.Message;
                MessageBox.Show(this, ex.Message, "快捷工具", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, source);
            }
        }

        private void SetBusy(bool value, SoftButton source)
        {
            busy = value;
            foreach (SoftButton button in actionButtons)
                button.Enabled = !value || button == source;
            if (value && source != null)
                source.Enabled = false;
        }

        private void ShowResult(string title, string message, bool success)
        {
            statusLabel.Text = (success ? "完成：" : "结果：") + title;
            MessageBox.Show(this, message, title,
                MessageBoxButtons.OK,
                success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        private Label LabelAt(string text, int x, int y, float size, FontStyle style, string role)
        {
            return LabelAt(text, x, y, 540, 28, size, style, role);
        }

        private Label LabelAt(string text, int x, int y, int width, int height,
            float size, FontStyle style, string role)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Microsoft YaHei UI", size, style),
                Tag = role,
                AutoEllipsis = false
            };
        }

        private SoftButton ButtonAt(string text, int x, int y, int width, int height,
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

        private void WriteLog(string text)
        {
            if (writeLog != null) writeLog(text);
        }
    }
}
