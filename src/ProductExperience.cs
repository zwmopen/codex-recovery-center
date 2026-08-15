using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    internal static class ProductInfo
    {
        public const string ProductName = "个人聚合工具箱";
        public const string Version = "2.10.0";
        public const string RepositoryUrl = "https://github.com/zwmopen/codex-recovery-center";
        public const string ReleasesUrl = RepositoryUrl + "/releases";
        public const string UpdateManifestUrl =
            "https://github.com/zwmopen/codex-recovery-center/releases/latest/download/manifest.json";
    }

    internal enum VisualTheme
    {
        Neumorphic,
        RestrainedGlass
    }

    internal enum ColorMode
    {
        Light,
        Dark
    }

    internal enum ButtonVisualRole
    {
        Primary,
        Secondary,
        Ghost,
        Danger
    }

    internal sealed class AppSettings
    {
        public VisualTheme Theme = VisualTheme.Neumorphic;
        public ColorMode ColorMode = ColorMode.Light;
        public bool AutoCheckUpdates = true;
        public bool AutoDownloadUpdates = true;
        public bool ResourceGuardEnabled;

        public AppSettings Clone()
        {
            return new AppSettings
            {
                Theme = Theme,
                ColorMode = ColorMode,
                AutoCheckUpdates = AutoCheckUpdates,
                AutoDownloadUpdates = AutoDownloadUpdates,
                ResourceGuardEnabled = ResourceGuardEnabled
            };
        }
    }

    internal static class SettingsStore
    {
        public static string SettingsPath
        {
            get
            {
                string root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WindowsRepairCenter");
                Directory.CreateDirectory(root);
                return Path.Combine(root, "settings.ini");
            }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            string sourcePath = SettingsPath;
            if (!File.Exists(sourcePath))
            {
                string legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CodexRecoveryCenter", "settings.ini");
                if (File.Exists(legacyPath))
                    sourcePath = legacyPath;
            }
            if (!File.Exists(sourcePath))
                return settings;

            try
            {
                foreach (string raw in File.ReadAllLines(sourcePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (key.Equals("Theme", StringComparison.OrdinalIgnoreCase))
                    {
                        VisualTheme parsed;
                        if (Enum.TryParse(value, true, out parsed))
                            settings.Theme = parsed;
                    }
                    else if (key.Equals("ColorMode", StringComparison.OrdinalIgnoreCase))
                    {
                        ColorMode parsed;
                        if (Enum.TryParse(value, true, out parsed))
                            settings.ColorMode = parsed;
                    }
                    else if (key.Equals("ResourceGuardEnabled", StringComparison.OrdinalIgnoreCase))
                    {
                        bool parsed;
                        if (bool.TryParse(value, out parsed))
                            settings.ResourceGuardEnabled = parsed;
                    }
                }
            }
            catch { }
            settings.AutoCheckUpdates = true;
            settings.AutoDownloadUpdates = true;
            settings.ResourceGuardEnabled = ResourceGuard.IsScheduled();
            return settings;
        }

        public static void Save(AppSettings settings)
        {
            string[] lines =
            {
                "Theme=" + settings.Theme,
                "ColorMode=" + settings.ColorMode,
                "ResourceGuardEnabled=" + settings.ResourceGuardEnabled
            };
            File.WriteAllLines(SettingsPath, lines, Encoding.UTF8);
        }
    }

    internal sealed class ThemePalette
    {
        public Color Canvas;
        public Color WindowTop;
        public Color WindowBottom;
        public Color Card;
        public Color CardBorder;
        public Color Highlight;
        public Color Shadow;
        public Color Ink;
        public Color Muted;
        public Color Accent;
        public Color AccentHover;
        public Color Secondary;
        public Color SecondaryHover;
        public Color Warning;
        public Color Danger;
    }

    internal static class ThemeManager
    {
        public static ThemePalette Get(VisualTheme theme)
        {
            return Get(theme, ColorMode.Light);
        }

        public static ThemePalette Get(VisualTheme theme, ColorMode colorMode)
        {
            if (colorMode == ColorMode.Dark)
            {
                if (theme == VisualTheme.RestrainedGlass)
                {
                    return new ThemePalette
                    {
                        Canvas = Color.FromArgb(20, 27, 35),
                        WindowTop = Color.FromArgb(31, 42, 53),
                        WindowBottom = Color.FromArgb(17, 23, 30),
                        Card = Color.FromArgb(29, 39, 49),
                        CardBorder = Color.FromArgb(66, 83, 100),
                        Highlight = Color.FromArgb(42, 255, 255, 255),
                        Shadow = Color.FromArgb(60, 0, 0, 0),
                        Ink = Color.FromArgb(232, 240, 247),
                        Muted = Color.FromArgb(157, 174, 189),
                        Accent = Color.FromArgb(78, 161, 255),
                        AccentHover = Color.FromArgb(105, 180, 255),
                        Secondary = Color.FromArgb(48, 61, 74),
                        SecondaryHover = Color.FromArgb(61, 77, 92),
                        Warning = Color.FromArgb(255, 194, 105),
                        Danger = Color.FromArgb(255, 124, 113)
                    };
                }

                return new ThemePalette
                {
                    Canvas = Color.FromArgb(24, 31, 40),
                    WindowTop = Color.FromArgb(30, 39, 49),
                    WindowBottom = Color.FromArgb(16, 22, 29),
                    Card = Color.FromArgb(29, 39, 50),
                    CardBorder = Color.FromArgb(58, 74, 91),
                    Highlight = Color.FromArgb(30, 255, 255, 255),
                    Shadow = Color.FromArgb(70, 0, 0, 0),
                    Ink = Color.FromArgb(234, 241, 247),
                    Muted = Color.FromArgb(153, 171, 188),
                    Accent = Color.FromArgb(74, 157, 255),
                    AccentHover = Color.FromArgb(101, 177, 255),
                    Secondary = Color.FromArgb(45, 58, 71),
                    SecondaryHover = Color.FromArgb(57, 73, 88),
                    Warning = Color.FromArgb(255, 190, 102),
                    Danger = Color.FromArgb(255, 122, 112)
                };
            }

            if (theme == VisualTheme.RestrainedGlass)
            {
                return new ThemePalette
                {
                    Canvas = Color.FromArgb(223, 234, 242),
                    WindowTop = Color.FromArgb(235, 243, 248),
                    WindowBottom = Color.FromArgb(207, 222, 233),
                    Card = Color.FromArgb(237, 244, 248),
                    CardBorder = Color.FromArgb(158, 184, 201),
                    Highlight = Color.FromArgb(180, 255, 255, 255),
                    Shadow = Color.FromArgb(35, 105, 132, 151),
                    Ink = Color.FromArgb(16, 43, 69),
                    Muted = Color.FromArgb(97, 122, 145),
                    Accent = Color.FromArgb(47, 127, 245),
                    AccentHover = Color.FromArgb(23, 109, 231),
                    Secondary = Color.FromArgb(235, 244, 249),
                    SecondaryHover = Color.FromArgb(220, 233, 242),
                    Warning = Color.FromArgb(164, 112, 62),
                    Danger = Color.FromArgb(164, 72, 67)
                };
            }

            return new ThemePalette
            {
                Canvas = Color.FromArgb(232, 237, 243),
                WindowTop = Color.FromArgb(229, 237, 243),
                WindowBottom = Color.FromArgb(209, 220, 229),
                Card = Color.FromArgb(226, 235, 241),
                CardBorder = Color.FromArgb(195, 208, 220),
                Highlight = Color.FromArgb(125, 255, 255, 255),
                Shadow = Color.FromArgb(42, 112, 130, 150),
                Ink = Color.FromArgb(28, 41, 56),
                Muted = Color.FromArgb(96, 111, 128),
                Accent = Color.FromArgb(48, 126, 255),
                AccentHover = Color.FromArgb(66, 142, 255),
                Secondary = Color.FromArgb(238, 244, 248),
                SecondaryHover = Color.FromArgb(224, 235, 243),
                Warning = Color.FromArgb(177, 119, 62),
                Danger = Color.FromArgb(171, 76, 70)
            };
        }

        public static void Apply(Control root, VisualTheme theme, ColorMode colorMode = ColorMode.Light)
        {
            ThemePalette palette = Get(theme, colorMode);
            ApplyRecursive(root, palette, theme, colorMode);
            root.Invalidate(true);
        }

        private static void ApplyRecursive(Control control, ThemePalette palette, VisualTheme theme, ColorMode colorMode)
        {
            Form form = control as Form;
            if (form != null)
                form.BackColor = palette.WindowBottom;

            ThemedForm themedForm = control as ThemedForm;
            if (themedForm != null)
            {
                themedForm.VisualTheme = theme;
                themedForm.ColorMode = colorMode;
            }

            SoftPanel panel = control as SoftPanel;
            if (panel != null)
            {
                panel.BackColor = Color.Transparent;
                panel.FillColor = palette.Card;
                panel.BorderColor = palette.CardBorder;
                panel.HighlightColor = palette.Highlight;
                panel.ShadowColor = palette.Shadow;
            }
            else if (control is Panel)
            {
                control.BackColor = Color.Transparent;
            }

            Label label = control as Label;
            if (label != null)
            {
                label.BackColor = Color.Transparent;
                string role = Convert.ToString(label.Tag);
                if (role == "accent") label.ForeColor = palette.Accent;
                else if (role == "muted") label.ForeColor = palette.Muted;
                else if (role == "warning") label.ForeColor = palette.Warning;
                else if (role == "danger") label.ForeColor = palette.Danger;
                else label.ForeColor = palette.Ink;
            }

            SoftButton button = control as SoftButton;
            if (button != null)
            {
                if (button.VisualRole == ButtonVisualRole.Primary)
                {
                    button.FillColor = palette.Accent;
                    button.HoverColor = palette.AccentHover;
                    button.TextColor = Color.White;
                }
                else if (button.VisualRole == ButtonVisualRole.Danger)
                {
                    button.FillColor = palette.Danger;
                    button.HoverColor = ControlPaint.Dark(palette.Danger);
                    button.TextColor = Color.White;
                }
                else if (button.VisualRole == ButtonVisualRole.Ghost)
                {
                    button.FillColor = palette.Canvas;
                    button.HoverColor = palette.SecondaryHover;
                    button.TextColor = palette.Muted;
                }
                else
                {
                    button.FillColor = palette.Secondary;
                    button.HoverColor = palette.SecondaryHover;
                    button.TextColor = palette.Ink;
                }
                SoftPanel parentPanel = button.Parent as SoftPanel;
                button.SurfaceColor = parentPanel == null
                    ? palette.WindowBottom : parentPanel.FillColor;
                button.HighlightColor = palette.Highlight;
                button.ShadowColor = palette.Shadow;
            }

            TextBox textBox = control as TextBox;
            if (textBox != null)
            {
                textBox.BackColor = palette.Card;
                textBox.ForeColor = palette.Ink;
            }
            CheckBox checkBox = control as CheckBox;
            if (checkBox != null)
            {
                checkBox.BackColor = Color.Transparent;
                checkBox.ForeColor = palette.Ink;
            }
            RadioButton radio = control as RadioButton;
            if (radio != null)
            {
                radio.BackColor = Color.Transparent;
                radio.ForeColor = palette.Ink;
            }
            ComboBox combo = control as ComboBox;
            if (combo != null)
            {
                combo.BackColor = palette.Card;
                combo.ForeColor = palette.Ink;
            }

            foreach (Control child in control.Controls)
                ApplyRecursive(child, palette, theme, colorMode);
        }
    }

    internal class ThemedForm : Form
    {
        public VisualTheme VisualTheme { get; set; }
        public ColorMode ColorMode { get; set; }

        public ThemedForm()
        {
            VisualTheme = VisualTheme.Neumorphic;
            ColorMode = ColorMode.Light;
            DoubleBuffered = true;
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null) Icon = icon;
            }
            catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            ThemePalette palette = ThemeManager.Get(VisualTheme, ColorMode);
            using (var brush = new LinearGradientBrush(
                ClientRectangle, palette.WindowTop, palette.WindowBottom, 90F))
                e.Graphics.FillRectangle(brush, ClientRectangle);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int decorationAlpha = ColorMode == ColorMode.Dark ? 14 : 28;
            using (var blue = new SolidBrush(Color.FromArgb(
                VisualTheme == VisualTheme.Neumorphic ? decorationAlpha : Math.Max(10, decorationAlpha - 8), 48, 126, 255)))
            using (var white = new SolidBrush(Color.FromArgb(ColorMode == ColorMode.Dark ? 12 : 42, 255, 255, 255)))
            using (var cyan = new SolidBrush(Color.FromArgb(ColorMode == ColorMode.Dark ? 10 : 18, 65, 213, 207)))
            {
                e.Graphics.FillEllipse(blue, new Rectangle(Width - 260, -130, 330, 270));
                e.Graphics.FillEllipse(white, new Rectangle(-150, Height - 230, 300, 270));
                e.Graphics.FillEllipse(cyan, new Rectangle(Width - 240, Height - 160, 170, 140));
            }
        }
    }

    internal sealed class UpdateInfo
    {
        public string Version = "";
        public string ReleaseUrl = "";
        public string DownloadUrl = "";
        public string Digest = "";
        public long Size;

        public bool IsNewerThan(string currentVersion)
        {
            System.Version latest;
            System.Version current;
            return System.Version.TryParse(Version.TrimStart('v', 'V'), out latest) &&
                System.Version.TryParse(currentVersion.TrimStart('v', 'V'), out current) &&
                latest > current;
        }
    }

    internal static class UpdateService
    {
        public static string BuildSelfTest()
        {
            UpdateInfo latest = CheckLatest();
            string downloaded = DownloadAndVerify(latest);
            AppSettings settings = SettingsStore.Load();
            return String.Join(Environment.NewLine, new[]
            {
                "time=" + DateTime.Now.ToString("O"),
                "currentVersion=" + ProductInfo.Version,
                "latestVersion=" + latest.Version,
                "releaseUrl=" + latest.ReleaseUrl,
                "downloadDigestPresent=" + (!String.IsNullOrWhiteSpace(latest.Digest)),
                "downloadVerified=" + File.Exists(downloaded),
                "theme=" + settings.Theme,
                "colorMode=" + settings.ColorMode,
                "autoCheckUpdates=" + settings.AutoCheckUpdates,
                "autoDownloadUpdates=" + settings.AutoDownloadUpdates
            });
        }

        public static UpdateInfo CheckLatest()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string json;
            using (var client = NewClient())
                json = client.DownloadString(ProductInfo.UpdateManifestUrl +
                    "?client=" + Uri.EscapeDataString(ProductInfo.Version) +
                    "&time=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
                throw new InvalidDataException("GitHub 返回内容无法识别。");

            var info = new UpdateInfo
            {
                Version = Convert.ToString(root["version"]).TrimStart('v', 'V')
            };
            info.ReleaseUrl = ProductInfo.ReleasesUrl + "/tag/v" + info.Version;

            object[] assets = root["artifacts"] as object[];
            if (assets != null)
            {
                foreach (object raw in assets)
                {
                    var asset = raw as Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = Convert.ToString(asset["file"]);
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    info.DownloadUrl = ProductInfo.ReleasesUrl + "/download/v" + info.Version +
                        "/" + name;
                    object sha256;
                    if (asset.TryGetValue("sha256", out sha256))
                        info.Digest = "sha256:" + Convert.ToString(sha256);
                    object size;
                    if (!asset.TryGetValue("size", out size))
                        asset.TryGetValue("bytes", out size);
                    if (size != null)
                        info.Size = Convert.ToInt64(size);
                    break;
                }
            }
            return info;
        }

        public static string DownloadAndVerify(UpdateInfo info)
        {
            if (String.IsNullOrWhiteSpace(info.DownloadUrl))
                throw new InvalidDataException("最新版没有可用的 EXE 附件。");
            if (String.IsNullOrWhiteSpace(info.Digest) ||
                !info.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("最新版缺少 SHA-256 校验值，已拒绝自动安装。");

            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WindowsRepairCenter", "updates");
            Directory.CreateDirectory(root);
            string target = Path.Combine(root,
                "Windows-Repair-Center-v" + info.Version + ".exe");

            using (var client = NewClient())
                client.DownloadFile(info.DownloadUrl, target);

            string expected = info.Digest.Substring("sha256:".Length);
            string actual;
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(target))
                actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(target); } catch { }
                throw new InvalidDataException("更新文件校验失败，已删除下载文件。");
            }
            return target;
        }

        public static void ScheduleReplaceAndRestart(string downloadedPath)
        {
            string current = Application.ExecutablePath;
            int pid = Process.GetCurrentProcess().Id;
            string log = Path.Combine(RecoveryEngine.LogRoot,
                "update-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            string script =
                "$ErrorActionPreference='Stop';" +
                "try{Wait-Process -Id " + pid + " -Timeout 20 -ErrorAction SilentlyContinue;" +
                "Start-Sleep -Milliseconds 600;" +
                "Copy-Item -LiteralPath '" + Escape(downloadedPath) + "' -Destination '" +
                    Escape(current) + "' -Force;" +
                "Start-Process -FilePath '" + Escape(current) + "';" +
                "'updated'|Out-File -LiteralPath '" + Escape(log) + "' -Encoding utf8}" +
                "catch{($_|Out-String)|Out-File -LiteralPath '" + Escape(log) +
                    "' -Encoding utf8;Start-Process -FilePath '" + Escape(downloadedPath) + "'}";
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            string powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }

        private static WebClient NewClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] =
                "Windows-Repair-Center/" + ProductInfo.Version;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return client;
        }

        private static string Escape(string value)
        {
            return value.Replace("'", "''");
        }
    }

    internal sealed class SettingsForm : ThemedForm
    {
        private readonly SoftButton neoThemeButton;
        private readonly SoftButton glassThemeButton;
        private readonly SoftButton modeButton;
        private readonly Label hint;
        private readonly SoftButton saveButton;
        private readonly SoftButton checkButton;
        private readonly SoftButton guardButton;
        private readonly AppSettings working;
        private readonly Action<AppSettings> applySettings;
        private readonly Func<Task> checkUpdates;

        public SettingsForm(AppSettings current, Action<AppSettings> apply, Func<Task> check,
            ColorMode colorMode = ColorMode.Light)
        {
            working = current.Clone();
            working.ColorMode = colorMode;
            applySettings = apply;
            checkUpdates = check;

            Text = "设置 · " + ProductInfo.ProductName;
            ClientSize = new Size(600, 675);
            MinimumSize = new Size(560, 645);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = LabelAt("设置", 30, 25, 20F, FontStyle.Bold, null);

            var themeCard = CardAt(30, 82, 540, 126);
            themeCard.Controls.Add(LabelAt("视觉语言", 20, 16, 11F, FontStyle.Bold, null));
            themeCard.Controls.Add(LabelAt("两套都是你的长期偏好，可随时原地切换。", 20, 45, 9F,
                FontStyle.Regular, "muted"));
            neoThemeButton = ButtonAt("拟态悬浮", 20, 75, 240, 40,
                working.Theme == VisualTheme.Neumorphic
                    ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary);
            glassThemeButton = ButtonAt("克制玻璃", 280, 75, 240, 40,
                working.Theme == VisualTheme.RestrainedGlass
                    ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary);
            neoThemeButton.Click += (s, e) => SelectTheme(VisualTheme.Neumorphic);
            glassThemeButton.Click += (s, e) => SelectTheme(VisualTheme.RestrainedGlass);
            themeCard.Controls.Add(neoThemeButton);
            themeCard.Controls.Add(glassThemeButton);

            var modeCard = CardAt(30, 222, 540, 104);
            modeCard.Controls.Add(LabelAt("显示模式", 20, 14, 11F, FontStyle.Bold, null));
            modeCard.Controls.Add(LabelAt("切换界面明暗，不改变检查和修复逻辑。", 20, 45, 9F,
                FontStyle.Regular, "muted"));
            modeButton = ButtonAt(working.ColorMode == ColorMode.Dark ? "深色模式" : "浅色模式",
                398, 51, 122, 36, working.ColorMode == ColorMode.Dark
                    ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary);
            modeButton.Click += (s, e) => ToggleColorMode();
            modeCard.Controls.Add(modeButton);

            var guardCard = CardAt(30, 340, 540, 104);
            guardCard.Controls.Add(LabelAt("资源守卫", 20, 14, 11F, FontStyle.Bold, null));
            guardCard.Controls.Add(LabelAt("每 2 分钟拦截失控进程与高风险孤儿服务，不关闭正常任务。",
                20, 45, 9F, FontStyle.Regular, "muted"));
            guardButton = ButtonAt(working.ResourceGuardEnabled ? "已开启" : "未开启",
                398, 51, 122, 36, working.ResourceGuardEnabled
                    ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary);
            guardButton.Click += (s, e) => ToggleResourceGuard();
            guardCard.Controls.Add(guardButton);

            var updateCard = CardAt(30, 458, 540, 92);
            updateCard.Controls.Add(LabelAt("软件更新", 20, 15, 11F, FontStyle.Bold, null));
            checkButton = ButtonAt("检查版本", 374, 43, 146, 36, ButtonVisualRole.Secondary);
            updateCard.Controls.Add(checkButton);

            hint = LabelAt("设置保存在本机，不会同步到云端。", 32, 568, 9F,
                FontStyle.Regular, "muted");
            hint.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            saveButton = ButtonAt("保存设置", 420, 596, 150, 42, ButtonVisualRole.Primary);
            saveButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            var cancelButton = ButtonAt("取消", 314, 596, 92, 42, ButtonVisualRole.Ghost);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            Controls.AddRange(new Control[]
                { title, themeCard, modeCard, guardCard, updateCard, hint, saveButton, cancelButton });

            checkButton.Click += async (s, e) =>
            {
                checkButton.Enabled = false;
                checkButton.Text = "检查中……";
                try { await checkUpdates(); }
                finally
                {
                    checkButton.Text = "检查版本";
                    checkButton.Enabled = true;
                }
            };
            saveButton.Click += (s, e) =>
            {
                working.AutoCheckUpdates = true;
                working.AutoDownloadUpdates = true;
                CommandResult guardResult = ResourceGuard.SetScheduledGuardEnabled(
                    working.ResourceGuardEnabled);
                if (guardResult.ExitCode != 0 &&
                    !(guardResult.ExitCode == 1 && !working.ResourceGuardEnabled))
                {
                    MessageBox.Show(this,
                        "资源守卫设置失败：" + guardResult.Output,
                        "无法保存", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SettingsStore.Save(working);
                applySettings(working.Clone());
                DialogResult = DialogResult.OK;
                Close();
            };
            cancelButton.Click += (s, e) => Close();

            ThemeManager.Apply(this, working.Theme, working.ColorMode);
        }

        private void ToggleResourceGuard()
        {
            working.ResourceGuardEnabled = !working.ResourceGuardEnabled;
            guardButton.Text = working.ResourceGuardEnabled ? "已开启" : "未开启";
            guardButton.VisualRole = working.ResourceGuardEnabled
                ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary;
            ThemeManager.Apply(this, working.Theme, working.ColorMode);
        }

        private void ToggleColorMode()
        {
            working.ColorMode = working.ColorMode == ColorMode.Dark
                ? ColorMode.Light : ColorMode.Dark;
            modeButton.Text = working.ColorMode == ColorMode.Dark ? "深色模式" : "浅色模式";
            modeButton.VisualRole = working.ColorMode == ColorMode.Dark
                ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary;
            ThemeManager.Apply(this, working.Theme, working.ColorMode);
        }

        private void SelectTheme(VisualTheme theme)
        {
            working.Theme = theme;
            neoThemeButton.VisualRole = theme == VisualTheme.Neumorphic
                ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary;
            glassThemeButton.VisualRole = theme == VisualTheme.RestrainedGlass
                ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary;
            ThemeManager.Apply(this, working.Theme, working.ColorMode);
        }

        private SoftPanel CardAt(int x, int y, int width, int height)
        {
            return new SoftPanel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Tag = "card"
            };
        }

        private Label LabelAt(string text, int x, int y, float size, FontStyle style, string role)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                Font = new Font("Microsoft YaHei UI", size, style),
                Tag = role
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
                Font = new Font("Microsoft YaHei UI", 9.5F,
                    role == ButtonVisualRole.Primary ? FontStyle.Bold : FontStyle.Regular)
            };
        }
    }

    internal sealed class AboutForm : ThemedForm
    {
        public AboutForm(VisualTheme theme, ColorMode colorMode = ColorMode.Light)
        {
            Text = "关于 · " + ProductInfo.ProductName;
            ClientSize = new Size(620, 535);
            MinimumSize = new Size(580, 500);
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;

            var title = NewLabel(ProductInfo.ProductName, 32, 26, 20F, FontStyle.Bold, null);
            var version = NewLabel("v" + ProductInfo.Version + " · Windows 本机诊断与修复工具",
                34, 68, 9.5F, FontStyle.Bold, "accent");
            var intro = NewLabel(
                "面向 Windows 本机问题：启动、蓝牙、弹窗来源、网络、存储和资源压力。Codex 只是其中一个模块。",
                34, 105, 540, 46, 9.5F, FontStyle.Regular, "muted");

            var idea = new SoftPanel
            {
                Location = new Point(30, 168),
                Size = new Size(560, 154),
                Tag = "card"
            };
            idea.Controls.Add(NewLabel("设计思路", 20, 16, 11F, FontStyle.Bold, null));
            idea.Controls.Add(NewLabel(
                "1  先判断安装状态，不对正常包反复修复\n2  注册异常时做最小修复，并给 Windows 留出处理时间\n3  仍异常才调用微软商店官方源，不主动重置聊天数据",
                20, 48, 510, 82, 9.5F, FontStyle.Regular, "muted"));

            var privacy = new SoftPanel
            {
                Location = new Point(30, 338),
                Size = new Size(560, 106),
                Tag = "card"
            };
            privacy.Controls.Add(NewLabel("数据与隐私", 20, 14, 11F, FontStyle.Bold, null));
            privacy.Controls.Add(NewLabel(
                "设置和诊断日志只保存在本机。不会上传聊天内容、账号凭据或私人日志。修复逻辑已内置在 EXE，外部脚本只用于源码开发和热更新。源码采用 MIT License。",
                20, 46, 510, 45, 9F, FontStyle.Regular, "muted"));

            var repoButton = NewButton("打开开源仓库", 30, 466, 170, 42, ButtonVisualRole.Secondary);
            var releaseButton = NewButton("查看版本更新", 214, 466, 170, 42, ButtonVisualRole.Secondary);
            var closeButton = NewButton("关闭", 454, 466, 136, 42, ButtonVisualRole.Primary);
            repoButton.Click += (s, e) => Process.Start(ProductInfo.RepositoryUrl);
            releaseButton.Click += (s, e) => Process.Start(ProductInfo.ReleasesUrl);
            closeButton.Click += (s, e) => Close();

            Controls.AddRange(new Control[]
                { title, version, intro, idea, privacy, repoButton, releaseButton, closeButton });
            ThemeManager.Apply(this, theme, colorMode);
        }

        private Label NewLabel(string text, int x, int y, float size, FontStyle style, string role)
        {
            return NewLabel(text, x, y, 540, 28, size, style, role);
        }

        private Label NewLabel(string text, int x, int y, int width, int height,
            float size, FontStyle style, string role)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                Font = new Font("Microsoft YaHei UI", size, style),
                Tag = role
            };
        }

        private SoftButton NewButton(string text, int x, int y, int width, int height,
            ButtonVisualRole role)
        {
            return new SoftButton
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                VisualRole = role,
                Font = new Font("Microsoft YaHei UI", 9.5F,
                    role == ButtonVisualRole.Primary ? FontStyle.Bold : FontStyle.Regular)
            };
        }
    }
}
