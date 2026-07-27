using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexRecoveryCenter
{
    internal static class Program
    {
        private const string SingleInstanceName =
            @"Local\CodexRecoveryCenter.SingleInstance";

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test")
            {
                File.WriteAllText(args[1], RecoveryEngine.BuildSelfTest(), Encoding.UTF8);
                return;
            }
            if (args.Length == 2 && args[0] == "--update-self-test")
            {
                try
                {
                    File.WriteAllText(args[1], UpdateService.BuildSelfTest(), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1], "updateSelfTestError=" + ex, Encoding.UTF8);
                    Environment.ExitCode = 2;
                }
                return;
            }

            bool createdNew;
            using (var singleInstance = new Mutex(true, SingleInstanceName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingWindow();
                    return;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm());
                }
                finally
                {
                    try { singleInstance.ReleaseMutex(); } catch { }
                }
            }
        }

        private static void ActivateExistingWindow()
        {
            int currentId = Process.GetCurrentProcess().Id;
            string processName = Process.GetCurrentProcess().ProcessName;
            for (int attempt = 0; attempt < 15; attempt++)
            {
                foreach (Process process in Process.GetProcessesByName(processName))
                {
                    try
                    {
                        if (process.Id == currentId || process.MainWindowHandle == IntPtr.Zero)
                            continue;
                        ShowWindow(process.MainWindowHandle, 9);
                        SetForegroundWindow(process.MainWindowHandle);
                        return;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                Thread.Sleep(100);
            }

            MessageBox.Show(
                "恢复中心已经在运行或正在处理。\n\n请查看已有窗口，不要重复启动修复。",
                "恢复正在进行", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    internal sealed class MemoryState
    {
        public double TotalPhysicalGb;
        public double AvailablePhysicalGb;
        public double TotalVirtualGb;
        public double AvailableVirtualGb;
        public double CommitPressurePercent;
        public bool IsHighPressure
        {
            get
            {
                return AvailableVirtualGb < 6.0 || AvailablePhysicalGb < 1.5 ||
                    CommitPressurePercent >= 85.0;
            }
        }
        public bool IsCritical
        {
            get { return AvailableVirtualGb < 2.0; }
        }
    }

    internal enum CrashKind
    {
        Unknown,
        OutOfMemory,
        GpuRenderer,
        Abort,
        Other
    }

    internal sealed class CrashInfo
    {
        public bool Found;
        public DateTime Time;
        public string App = "";
        public string ExceptionCode = "";
        public string ConversationId = "";
        public string Detail = "";
        public CrashKind Kind = CrashKind.Unknown;

        public bool IsRecent
        {
            get { return Found && (DateTime.Now - Time).TotalHours <= 48.0; }
        }

        public string KindText
        {
            get
            {
                switch (Kind)
                {
                    case CrashKind.OutOfMemory: return "内存耗尽";
                    case CrashKind.GpuRenderer: return "GPU / 会话渲染崩溃";
                    case CrashKind.Abort: return "程序中止（多为资源不足）";
                    case CrashKind.Other: return "其他异常";
                    default: return "未知";
                }
            }
        }
    }

    internal sealed class ProcessGroup
    {
        public string Name = "";
        public int Count;
        public double CommitMb;
        public bool IsCodex;
        public List<int> Ids = new List<int>();

        public string DisplayText
        {
            get
            {
                return Name + "  ×" + Count + "　—　" + CommitMb.ToString("F0") + " MB" +
                    (IsCodex ? "　（Codex 本体）" : "");
            }
        }
    }

    internal static class RecoveryEngine
    {
        public const string PackageName = "OpenAI.Codex";
        public const string PackageFamily = "OpenAI.Codex_2p2nqsd0c76g0";
        public const string StoreProductId = "9PLM9XGG6VKS";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(
            [In, Out] MemoryStatus buffer);

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

        public static MemoryState GetMemoryState()
        {
            var raw = new MemoryStatus();
            if (!GlobalMemoryStatusEx(raw))
                return new MemoryState();

            const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
            double committed = raw.TotalPageFile - raw.AvailablePageFile;
            return new MemoryState
            {
                TotalPhysicalGb = raw.TotalPhysical / bytesPerGb,
                AvailablePhysicalGb = raw.AvailablePhysical / bytesPerGb,
                TotalVirtualGb = raw.TotalPageFile / bytesPerGb,
                AvailableVirtualGb = raw.AvailablePageFile / bytesPerGb,
                CommitPressurePercent = raw.TotalPageFile == 0
                    ? 0 : committed * 100.0 / raw.TotalPageFile
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
            bool killedAny = false;
            foreach (string name in new[] { "ChatGPT", "codex" })
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try { process.Kill(); process.WaitForExit(5000); killedAny = true; }
                    catch { }
                    finally { process.Dispose(); }
                }
            }
            if (killedAny)
                Thread.Sleep(1500);
        }

        public static CrashInfo GetLastCodexCrash()
        {
            CrashInfo info = GetLastApplicationCrash();
            CrashInfo gpu = GetLastGpuRendererCrash();
            return gpu.Found && (!info.Found || gpu.Time > info.Time) ? gpu : info;
        }

        private static CrashInfo GetLastApplicationCrash()
        {
            var info = new CrashInfo();
            try
            {
                using (var log = new EventLog("Application"))
                {
                    EventLogEntryCollection entries = log.Entries;
                    int scanned = 0;
                    for (int i = entries.Count - 1; i >= 0 && scanned < 400; i--, scanned++)
                    {
                        EventLogEntry entry;
                        try { entry = entries[i]; }
                        catch { continue; }
                        if ((entry.InstanceId & 0xFFFF) != 1000)
                            continue;
                        if (!"Application Error".Equals(entry.Source, StringComparison.OrdinalIgnoreCase))
                            continue;
                        string[] parts = entry.ReplacementStrings;
                        if (parts == null || parts.Length < 11)
                            continue;
                        if (parts[10].IndexOf("OpenAI.Codex", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        info.Found = true;
                        info.Time = entry.TimeGenerated;
                        info.App = Path.GetFileName(parts[0]);
                        info.ExceptionCode = parts[6];
                        info.Kind = Classify(info.ExceptionCode, info.Time);
                        break;
                    }
                }
            }
            catch { }
            return info;
        }

        private static CrashInfo GetLastGpuRendererCrash()
        {
            var newest = new CrashInfo();
            try
            {
                string root = GetDesktopLogRoot();
                if (!Directory.Exists(root))
                    return newest;

                IEnumerable<string> files = Directory.GetFiles(root, "*.log", SearchOption.AllDirectories)
                    .OrderByDescending(File.GetLastWriteTime)
                    .Take(24);
                foreach (string file in files)
                {
                    string text = ReadTail(file, 2 * 1024 * 1024);
                    if (String.IsNullOrEmpty(text) ||
                        text.IndexOf("processType=GPU", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string activeConversation = "";
                    DateTime activeTime = DateTime.MinValue;
                    string missingConversation = "";
                    DateTime missingTime = DateTime.MinValue;
                    foreach (string line in text.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        DateTime lineTime;
                        if (!TryParseDesktopLogTime(line, out lineTime))
                            continue;

                        if (line.IndexOf("thread_stream_view_activity_changed active=true",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            activeConversation = ExtractLogValue(line, "conversationId");
                            activeTime = lineTime;
                            continue;
                        }

                        if (line.IndexOf("Conversation state not found",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            missingConversation = ExtractLogValue(line, "conversationId");
                            missingTime = lineTime;
                            continue;
                        }

                        bool gpuCrashed =
                            line.IndexOf("Recoverable Chromium child process gone",
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            line.IndexOf("processType=GPU", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            line.IndexOf("reason=crashed", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool gpuLaunchFailed =
                            line.IndexOf("Recoverable Chromium child process gone",
                                StringComparison.OrdinalIgnoreCase) >= 0 &&
                            line.IndexOf("processType=GPU", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            line.IndexOf("reason=launch-failed", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (gpuLaunchFailed && newest.Found &&
                            newest.Kind == CrashKind.GpuRenderer &&
                            lineTime >= newest.Time &&
                            (lineTime - newest.Time).TotalSeconds <= 5.0)
                            continue;
                        if ((!gpuCrashed && !gpuLaunchFailed) ||
                            (newest.Found && lineTime <= newest.Time))
                            continue;

                        string conversation = "";
                        if (activeTime != DateTime.MinValue &&
                            lineTime >= activeTime &&
                            (lineTime - activeTime).TotalMinutes <= 10.0)
                            conversation = activeConversation;
                        if (!String.IsNullOrEmpty(missingConversation) &&
                            missingTime != DateTime.MinValue &&
                            lineTime >= missingTime &&
                            (lineTime - missingTime).TotalMinutes <= 2.0)
                            conversation = missingConversation;

                        newest = new CrashInfo
                        {
                            Found = true,
                            Time = lineTime,
                            App = "Chromium GPU",
                            ExceptionCode = ExtractLogValue(line, "exitCode"),
                            ConversationId = conversation,
                            Detail = !String.IsNullOrEmpty(missingConversation) &&
                                (lineTime - missingTime).TotalMinutes <= 2.0
                                ? "恢复会话状态后 GPU 进程崩溃"
                                : "GPU 进程崩溃",
                            Kind = CrashKind.GpuRenderer
                        };
                    }
                }
            }
            catch { }
            return newest;
        }

        private static readonly string[] OutOfMemoryCodes =
        {
            "e0000008", // Chromium kOomExceptionCode：ChatGPT.exe 宿主内存耗尽
            "c0000017", // STATUS_NO_MEMORY
            "c00000fd"  // STATUS_STACK_OVERFLOW，内存压力下的常见伴随失败
        };

        private static CrashKind Classify(string code, DateTime when)
        {
            foreach (string oom in OutOfMemoryCodes)
                if (code.IndexOf(oom, StringComparison.OrdinalIgnoreCase) >= 0)
                    return CrashKind.OutOfMemory;
            if (DesktopLogsShowOom(when))
                return CrashKind.OutOfMemory;
            if (code.IndexOf("c0000409", StringComparison.OrdinalIgnoreCase) >= 0)
                return CrashKind.Abort;
            return CrashKind.Other;
        }

        private static bool DesktopLogsShowOom(DateTime around)
        {
            try
            {
                string root = GetDesktopLogRoot();
                for (int back = 0; back <= 1; back++)
                {
                    DateTime day = around.Date.AddDays(-back);
                    string dir = Path.Combine(root, day.ToString("yyyy"),
                        day.ToString("MM"), day.ToString("dd"));
                    if (!Directory.Exists(dir))
                        continue;
                    foreach (string file in Directory.GetFiles(dir, "*.log"))
                    {
                        DateTime touched = File.GetLastWriteTime(file);
                        if (touched < around.AddHours(-12) || touched > around.AddHours(1))
                            continue;
                        if (TailContains(file, "memory allocation of"))
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static string GetDesktopLogRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", PackageFamily, "LocalCache", "Local", "Codex", "Logs");
        }

        private static bool TryParseDesktopLogTime(string line, out DateTime localTime)
        {
            localTime = DateTime.MinValue;
            if (String.IsNullOrEmpty(line) || line.Length < 24)
                return false;
            DateTime parsed;
            if (!DateTime.TryParse(line.Substring(0, 24), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out parsed))
                return false;
            localTime = parsed.ToLocalTime();
            return true;
        }

        private static string ExtractLogValue(string line, string name)
        {
            string marker = name + "=";
            int start = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                return "";
            start += marker.Length;
            int end = line.IndexOf(' ', start);
            if (end < 0)
                end = line.Length;
            return line.Substring(start, end - start).Trim();
        }

        private static string ReadTail(string file, int maxTail)
        {
            try
            {
                using (var stream = new FileStream(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (stream.Length > maxTail)
                        stream.Seek(-maxTail, SeekOrigin.End);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        return reader.ReadToEnd();
                }
            }
            catch { return ""; }
        }

        private static bool TailContains(string file, string needle)
        {
            return ReadTail(file, 131072)
                .IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static readonly string[] ProtectedProcesses =
        {
            "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
            "services", "lsass", "svchost", "dwm", "fontdrvhost", "explorer",
            "memory compression", "audiodg", "conhost", "runtimebroker", "sihost",
            "taskhostw", "ctfmon", "searchhost", "startmenuexperiencehost",
            "shellexperiencehost", "applicationframehost", "textinputhost",
            "securityhealthservice", "msmpeng", "wudfhost", "spoolsv"
        };

        public static List<ProcessGroup> GetTopCommitGroups(int maxGroups)
        {
            var groups = new Dictionary<string, ProcessGroup>(StringComparer.OrdinalIgnoreCase);
            int selfId;
            string selfName;
            using (Process self = Process.GetCurrentProcess())
            {
                selfId = self.Id;
                selfName = self.ProcessName;
            }
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName;
                    if (process.Id == selfId ||
                        name.Equals(selfName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Array.IndexOf(ProtectedProcesses, name.ToLowerInvariant()) >= 0)
                        continue;
                    long commit = process.PagedMemorySize64;
                    if (commit <= 0)
                        continue;
                    ProcessGroup group;
                    if (!groups.TryGetValue(name, out group))
                        groups[name] = group = new ProcessGroup
                        {
                            Name = name,
                            IsCodex = name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) ||
                                name.Equals("codex", StringComparison.OrdinalIgnoreCase)
                        };
                    group.Count++;
                    group.CommitMb += commit / (1024.0 * 1024.0);
                    group.Ids.Add(process.Id);
                }
                catch { }
                finally { process.Dispose(); }
            }
            return groups.Values
                .OrderByDescending(group => group.CommitMb)
                .Take(maxGroups)
                .ToList();
        }

        public static double CloseProcessGroups(
            IEnumerable<ProcessGroup> groups, Action<string> progress)
        {
            double freedMb = 0;
            foreach (ProcessGroup group in groups)
            {
                int closed = 0;
                int skipped = 0;
                foreach (int id in group.Ids)
                {
                    try
                    {
                        using (Process process = Process.GetProcessById(id))
                        {
                            if (!process.ProcessName.Equals(group.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                skipped++;
                                continue;
                            }
                            long commit = process.PagedMemorySize64;
                            process.Kill();
                            process.WaitForExit(4000);
                            freedMb += commit / (1024.0 * 1024.0);
                            closed++;
                        }
                    }
                    catch { skipped++; }
                }
                if (progress != null)
                    progress("已关闭 " + group.Name + " ×" + closed +
                        (skipped > 0 ? "（跳过 " + skipped + " 个已退出或已变化的进程）" : "") + "。");
            }
            return freedMb;
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

        public static CommandResult RestageFromMicrosoftStore(Action<int> elapsedProgress)
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
            return RunProcess(winget, args, 300000, elapsedProgress);
        }

        public static PackageState WaitForOk(int seconds, Action<string> progress)
        {
            PackageState state = new PackageState();
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (true)
            {
                state = GetPackageState();
                int elapsed = Math.Min(seconds, (int)stopwatch.Elapsed.TotalSeconds);
                progress("等待 Windows 完成处理：" + state.Status + "（" + elapsed + "/" + seconds + " 秒）");
                if (state.IsOk)
                    return state;
                if (stopwatch.Elapsed.TotalSeconds >= seconds)
                    return state;
                int remainingMs = Math.Max(200, (int)((seconds - stopwatch.Elapsed.TotalSeconds) * 1000));
                Thread.Sleep(Math.Min(2000, remainingMs));
            }
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
            return RunProcess(file, args, timeoutMs, null);
        }

        public static CommandResult RunProcess(string file, string args, int timeoutMs,
            Action<int> elapsedProgress)
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
                Stopwatch stopwatch = Stopwatch.StartNew();
                bool exited = false;
                while (!(exited = process.WaitForExit(1000)))
                {
                    int elapsed = (int)stopwatch.Elapsed.TotalSeconds;
                    if (elapsedProgress != null)
                        elapsedProgress(elapsed);
                    if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                        break;
                }
                if (!exited)
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
            MemoryState memory = GetMemoryState();
            CrashInfo crash = GetLastCodexCrash();
            List<ProcessGroup> hogs = GetTopCommitGroups(3);
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
                "codexRunning=" + IsCodexRunning(),
                "virtualMemoryFreeGb=" + memory.AvailableVirtualGb.ToString("F2"),
                "commitPressurePercent=" + memory.CommitPressurePercent.ToString("F1"),
                "memoryPressureHigh=" + memory.IsHighPressure,
                "memoryCritical=" + memory.IsCritical,
                "lastCrashFound=" + crash.Found,
                "lastCrashTime=" + (crash.Found ? crash.Time.ToString("O") : ""),
                "lastCrashApp=" + crash.App,
                "lastCrashCode=" + crash.ExceptionCode,
                "lastCrashKind=" + crash.Kind,
                "lastCrashConversationId=" + crash.ConversationId,
                "lastCrashDetail=" + crash.Detail,
                "topCommitGroups=" + String.Join("; ", hogs.Select(group =>
                    group.Name + "x" + group.Count + "=" +
                    group.CommitMb.ToString("F0") + "MB"))
            });
        }
    }

    internal sealed class SoftButton : Button
    {
        public Color FillColor { get; set; }
        public Color HoverColor { get; set; }
        public Color TextColor { get; set; }
        public Color SurfaceColor { get; set; }
        public Color HighlightColor { get; set; }
        public Color ShadowColor { get; set; }
        public int CornerRadius { get; set; }
        public ButtonVisualRole VisualRole { get; set; }
        private bool hovering;
        private bool pressed;

        public SoftButton()
        {
            FillColor = Color.White;
            HoverColor = Color.FromArgb(244, 241, 235);
            TextColor = Color.FromArgb(48, 51, 48);
            SurfaceColor = Color.FromArgb(209, 220, 229);
            HighlightColor = Color.FromArgb(125, 255, 255, 255);
            ShadowColor = Color.FromArgb(42, 112, 130, 150);
            CornerRadius = 13;
            VisualRole = ButtonVisualRole.Secondary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovering = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovering = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            Color fill = pressed ? ControlPaint.Dark(FillColor, 0.03F) :
                hovering ? HoverColor : FillColor;
            using (GraphicsPath path = Rounded(box, CornerRadius))
            using (SolidBrush brush = new SolidBrush(fill))
            using (Pen border = new Pen(Color.FromArgb(
                VisualRole == ButtonVisualRole.Primary ? 75 : 105, 255, 255, 255)))
            {
                e.Graphics.FillPath(brush, path);
                if (VisualRole != ButtonVisualRole.Ghost)
                    e.Graphics.DrawPath(border, path);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, box, Enabled ? TextColor :
                Color.FromArgb(156, 156, 150),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding |
                TextFormatFlags.PreserveGraphicsClipping);
        }

        private static GraphicsPath Rounded(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class SoftPanel : Panel
    {
        public int CornerRadius { get; set; }
        public Color BorderColor { get; set; }
        public Color FillColor { get; set; }
        public Color HighlightColor { get; set; }
        public Color ShadowColor { get; set; }

        public SoftPanel()
        {
            CornerRadius = 16;
            BorderColor = Color.FromArgb(224, 220, 211);
            FillColor = Color.FromArgb(226, 235, 241);
            HighlightColor = Color.FromArgb(125, 255, 255, 255);
            ShadowColor = Color.FromArgb(42, 112, 130, 150);
            ResizeRedraw = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle box = new Rectangle(3, 3, Math.Max(1, Width - 7), Math.Max(1, Height - 7));
            using (var path = SoftButtonRounded(box, CornerRadius))
            using (var fill = new SolidBrush(FillColor))
            using (var pen = new Pen(Color.FromArgb(118, 255, 255, 255)))
            using (var edge = new Pen(Color.FromArgb(115, BorderColor)))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
                e.Graphics.DrawPath(edge, path);
            }
            base.OnPaint(e);
        }

        private static GraphicsPath SoftButtonRounded(Rectangle rectangle, int radius)
        {
            int diameter = Math.Max(2, radius * 2);
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class LegacyMainForm : ThemedForm
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private readonly Label statusTitle;
        private readonly Label statusDetail;
        private readonly Label statusDot;
        private readonly ProgressBar progress;
        private readonly TextBox log;
        private readonly SoftButton checkButton;
        private readonly SoftButton safeButton;
        private readonly SoftButton repairButton;
        private readonly SoftButton storeButton;
        private readonly SoftButton logButton;
        private readonly SoftButton settingsButton;
        private readonly SoftButton aboutButton;
        private readonly Panel statusCard;
        private readonly Panel logCard;
        private readonly Label privacyNote;
        private readonly string sessionLog;
        private bool logVisible;
        private AppSettings appSettings;

        private static readonly Color Canvas = Color.FromArgb(239, 237, 231);
        private static readonly Color Paper = Color.FromArgb(250, 249, 246);
        private static readonly Color Ink = Color.FromArgb(42, 45, 42);
        private static readonly Color Muted = Color.FromArgb(105, 107, 101);
        private static readonly Color Sage = Color.FromArgb(72, 104, 88);
        private static readonly Color SageHover = Color.FromArgb(61, 91, 76);
        private static readonly Color Amber = Color.FromArgb(177, 119, 62);
        private static readonly Color Red = Color.FromArgb(171, 76, 70);

        public LegacyMainForm()
        {
            appSettings = SettingsStore.Load();
            Text = "Codex 恢复中心";
            ClientSize = new Size(820, 610);
            MinimumSize = new Size(760, 570);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Canvas;
            Font = new Font("Microsoft YaHei UI", 10F);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Icon = CreateAppIcon();

            var eyebrow = new Label
            {
                Text = "WINDOWS · 独立恢复工具",
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = Sage,
                AutoSize = true,
                Location = new Point(42, 30),
                Tag = "accent"
            };
            var title = new Label
            {
                Text = "让 Codex 重新正常打开",
                Font = new Font("Microsoft YaHei UI", 23F, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(38, 52),
                Tag = "title"
            };
            var desc = new Label
            {
                Text = "检测安装状态，必要时自动修复，再用更稳妥的方式启动。",
                Font = new Font("Microsoft YaHei UI", 10.5F),
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(42, 100),
                Tag = "muted"
            };
            var version = new Label
            {
                Text = "v" + ProductInfo.Version,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = Muted,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(566, 33),
                Tag = "muted"
            };
            settingsButton = NewButton("设置", 620, 23, 78, 32, Canvas,
                Color.FromArgb(231, 229, 223), Muted, 9F, false);
            settingsButton.VisualRole = ButtonVisualRole.Ghost;
            settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            aboutButton = NewButton("关于", 706, 23, 74, 32, Canvas,
                Color.FromArgb(231, 229, 223), Muted, 9F, false);
            aboutButton.VisualRole = ButtonVisualRole.Ghost;
            aboutButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            statusCard = new SoftPanel
            {
                BackColor = Paper,
                Location = new Point(40, 140),
                Size = new Size(740, 112),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Tag = "card"
            };
            statusDot = new Label
            {
                Text = "●",
                Font = new Font("Microsoft YaHei UI", 18F),
                ForeColor = Amber,
                AutoSize = true,
                Location = new Point(24, 25)
            };
            statusTitle = new Label
            {
                Text = "正在检查当前状态",
                Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Location = new Point(61, 22),
                Tag = "title"
            };
            statusDetail = new Label
            {
                Text = "通常几秒钟就能完成",
                Font = new Font("Microsoft YaHei UI", 9.5F),
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(63, 54),
                Tag = "muted"
            };
            progress = new ProgressBar
            {
                Location = new Point(64, 82),
                Width = 645,
                Height = 5,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 22
            };
            statusCard.Controls.AddRange(new Control[] { statusDot, statusTitle, statusDetail, progress });

            repairButton = NewButton("一键恢复并启动", 40, 276, 740, 58, Sage, SageHover, Color.White, 12.5F, true);
            repairButton.VisualRole = ButtonVisualRole.Primary;
            repairButton.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            safeButton = NewButton("安全模式启动", 40, 350, 224, 46, Paper,
                Color.FromArgb(244, 242, 236), Ink, 10F, false);
            checkButton = NewButton("重新检查", 278, 350, 224, 46, Paper,
                Color.FromArgb(244, 242, 236), Ink, 10F, false);
            storeButton = NewButton("微软商店修复", 516, 350, 264, 46, Paper,
                Color.FromArgb(244, 242, 236), Ink, 10F, false);
            storeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            privacyNote = new Label
            {
                Text = "不会清空聊天或主动重置应用数据。恢复时会先关闭所有 Codex 窗口。",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(43, 418),
                Tag = "muted"
            };
            var help = new Label
            {
                Text = "打不开时点“一键恢复并启动”；只是多窗口容易崩时，可先用“安全模式启动”。",
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = Color.FromArgb(124, 91, 57),
                AutoSize = true,
                Location = new Point(43, 444),
                Tag = "warning"
            };

            logButton = NewButton("查看处理记录  ▾", 40, 478, 160, 36, Canvas,
                Color.FromArgb(231, 229, 223), Muted, 9F, false);
            logButton.VisualRole = ButtonVisualRole.Ghost;
            logCard = new SoftPanel
            {
                BackColor = Paper,
                Location = new Point(40, 520),
                Size = new Size(740, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Visible = false,
                Tag = "card"
            };
            log = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Paper,
                ForeColor = Color.FromArgb(76, 78, 74),
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 9F),
                Margin = new Padding(16),
                Tag = "editor"
            };
            logCard.Padding = new Padding(16, 12, 16, 12);
            logCard.Controls.Add(log);

            Controls.AddRange(new Control[]
            {
                eyebrow, title, desc, version, settingsButton, aboutButton, statusCard, repairButton,
                safeButton, checkButton, storeButton, privacyNote, help, logButton, logCard
            });

            checkButton.Click += async (s, e) => await CheckAsync();
            safeButton.Click += async (s, e) => await SafeLaunchAsync();
            repairButton.Click += async (s, e) => await RepairAsync();
            storeButton.Click += (s, e) =>
                Process.Start("ms-windows-store://pdp/?ProductId=" + RecoveryEngine.StoreProductId);
            logButton.Click += (s, e) => ToggleLog();
            settingsButton.Click += (s, e) => OpenSettings();
            aboutButton.Click += (s, e) =>
            {
                using (var about = new AboutForm(appSettings.Theme))
                    about.ShowDialog(this);
            };

            sessionLog = Path.Combine(RecoveryEngine.LogRoot,
                "recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            ThemeManager.Apply(this, appSettings.Theme);
            Shown += async (s, e) =>
            {
                await CheckAsync();
                if (appSettings.AutoCheckUpdates)
                    await CheckUpdatesAsync(false);
            };
            Resize += (s, e) => LayoutSecondaryButtons();
        }

        private Icon CreateAppIcon()
        {
            using (var bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (var brush = new SolidBrush(Sage))
                    graphics.FillEllipse(brush, 2, 2, 28, 28);
                using (var font = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(graphics, "C", font, new Rectangle(2, 2, 28, 28),
                        Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                IntPtr handle = bitmap.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }

        private SoftButton NewButton(string text, int x, int y, int width, int height,
            Color fill, Color hover, Color textColor, float fontSize, bool bold)
        {
            return new SoftButton
            {
                Text = text,
                Location = new Point(x, y),
                Width = width,
                Height = height,
                FillColor = fill,
                HoverColor = hover,
                TextColor = textColor,
                VisualRole = bold ? ButtonVisualRole.Primary : ButtonVisualRole.Secondary,
                Font = new Font("Microsoft YaHei UI", fontSize, bold ? FontStyle.Bold : FontStyle.Regular)
            };
        }

        private ThemePalette CurrentPalette
        {
            get { return ThemeManager.Get(appSettings.Theme); }
        }

        private void OpenSettings()
        {
            using (var settings = new SettingsForm(appSettings, ApplySettings,
                () => CheckUpdatesAsync(true)))
                settings.ShowDialog(this);
        }

        private void ApplySettings(AppSettings updated)
        {
            appSettings = updated;
            ThemeManager.Apply(this, appSettings.Theme);
            WriteLog("设置已保存；主题：" +
                (appSettings.Theme == VisualTheme.Neumorphic ? "拟态悬浮" : "克制玻璃") + "。");
        }

        private async Task CheckUpdatesAsync(bool userInitiated)
        {
            bool restartScheduled = false;
            try
            {
                if (userInitiated)
                {
                    SetBusy(true, "正在检查软件更新……");
                    SetStatusDetail("连接 GitHub Releases，通常几秒钟完成");
                }

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
                    if (open == DialogResult.Yes)
                        Process.Start(latest.ReleaseUrl);
                }
                else
                {
                    SetBusy(true, "正在安全下载 v" + latest.Version + "……");
                    SetStatusDetail("下载后会核对 GitHub 提供的 SHA-256，不会直接覆盖");
                    string downloaded = await Task.Run(() => UpdateService.DownloadAndVerify(latest));
                    WriteLog("新版已下载并通过 SHA-256 校验：" + downloaded);
                    DialogResult install = MessageBox.Show(
                        "v" + latest.Version + " 已下载并通过 SHA-256 校验。\n\n" +
                        "安装只会关闭恢复中心，不会关闭 Codex。是否现在更新并重新打开？",
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

        private void LayoutSecondaryButtons()
        {
            int available = ClientSize.Width - 80;
            int gap = 14;
            int first = (available - gap * 2) / 3;
            safeButton.Width = first;
            checkButton.Left = safeButton.Right + gap;
            checkButton.Width = first;
            storeButton.Left = checkButton.Right + gap;
            storeButton.Width = Math.Max(140, 40 + available - storeButton.Left);
        }

        private void ToggleLog()
        {
            logVisible = !logVisible;
            logCard.Visible = logVisible;
            logCard.Height = logVisible ? Math.Max(70, ClientSize.Height - logCard.Top - 22) : 0;
            logButton.Text = logVisible ? "收起处理记录  ▴" : "查看处理记录  ▾";
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
            settingsButton.Enabled = aboutButton.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            progress.Visible = busy;
            statusTitle.Text = text;
            statusDetail.Text = busy ? "请不要重复点击，Windows 可能需要一点时间" : statusDetail.Text;
            statusDot.ForeColor = busy ? CurrentPalette.Warning : statusDot.ForeColor;
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
            SetBusy(true, "正在检查安装与运行状态……");
            PackageState state = await Task.Run(() => RecoveryEngine.GetPackageState());
            bool running = RecoveryEngine.IsCodexRunning();
            WriteLog("安装状态：" + state.Status + "；正在运行：" + (running ? "是" : "否"));
            statusDetail.Text = state.IsOk
                ? (running ? "安装正常，Codex 当前正在运行" : "安装正常，需要时可以直接启动")
                : "Windows 检测到安装注册异常，建议立即恢复";
            statusDot.ForeColor = state.IsOk ? CurrentPalette.Accent : CurrentPalette.Danger;
            SetBusy(false, state.IsOk ? "现在状态正常" : "需要修复");
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
            statusDetail.Text = launched ? "已关闭 GPU 加速，用来降低多窗口崩溃风险" : "安装状态异常，先进行恢复";
            statusDot.ForeColor = launched ? CurrentPalette.Accent : CurrentPalette.Danger;
            SetBusy(false, launched ? "安全模式已启动" : "请执行一键恢复");
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
                    if (register.ExitCode == 0 && !register.TimedOut)
                    {
                        int registrationWait = state.Status.IndexOf(
                            "NeedsRemediation", StringComparison.OrdinalIgnoreCase) >= 0 ? 12 : 30;
                        SetBusy(true, "正在确认注册修复结果……");
                        SetStatusDetail("当前状态最多等待 " + registrationWait + " 秒，避免无效空等");
                        state = RecoveryEngine.WaitForOk(registrationWait, WriteLog);
                    }
                    else
                    {
                        WriteLog("注册修复未正常完成，直接进入微软商店官方恢复。");
                    }
                }

                if (!state.IsOk)
                {
                    WriteLog("注册修复不足，改用微软商店官方源重新暂存。");
                    SetBusy(true, "正在从微软商店恢复……");
                    SetStatusDetail("Windows 正在重新校验程序包，通常需要 2–4 分钟");
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
                    statusDetail.Text = "处理记录已保留，可继续使用微软商店官方修复";
                    statusDot.ForeColor = CurrentPalette.Danger;
                    SetBusy(false, "自动恢复尚未完成");
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
                statusDetail.Text = launched ? "安装状态已恢复，并用更稳妥的模式重新启动" : "安装已恢复，可以再次启动";
                statusDot.ForeColor = CurrentPalette.Accent;
                SetBusy(false, launched ? "恢复完成" : "安装已恢复");
            });
        }
    }
}
