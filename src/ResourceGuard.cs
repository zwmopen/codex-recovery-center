using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CodexRecoveryCenter
{
    internal sealed class GuardResult
    {
        public bool Success = true;
        public bool PressureDetected;
        public int Inspected;
        public int Stopped;
        public int Trimmed;
        public readonly List<string> Actions = new List<string>();

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "success={0}\r\npressure={1}\r\ninspected={2}\r\nstopped={3}\r\ntrimmed={4}\r\nactions={5}",
                Success, PressureDetected, Inspected, Stopped, Trimmed,
                Actions.Count == 0 ? "none" : string.Join(" | ", Actions.ToArray()));
        }
    }

    internal static class ResourceGuard
    {
        private const string TaskName = "CodexRecoveryCenter-ResourceGuard";
        private const long HardPrivateBytes = 6L * 1024 * 1024 * 1024;
        private const int OrphanHandleLimit = 3000;
        private const int OrphanConsoleThreadLimit = 256;
        private static readonly TimeSpan OrphanDevServerGrace = TimeSpan.FromHours(4);
        private static readonly string[] DevServerMarkers =
        {
            "vite.js", "webpack", "next dev", "react-scripts", "npm-cli.js run dev"
        };
        private static readonly string[] HeavyProcessNames =
        {
            "ChatGPT", "codex", "codex-code-mode-host", "node", "node_repl",
            "msedge", "chrome", "electron", "pythonw"
        };

        [DllImport("psapi.dll")]
        private static extern bool EmptyWorkingSet(IntPtr process);

        public static string GuardRoot
        {
            get
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CodexRecoveryCenter", "resource-guard");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static GuardResult RunOnce(bool enforce)
        {
            bool created;
            using (var mutex = new Mutex(true,
                @"Local\CodexRecoveryCenter.ResourceGuard", out created))
            {
                if (!created)
                    return new GuardResult { Success = true };

                try
                {
                    GuardResult result = InspectAndAct(enforce);
                    WriteState(result, enforce);
                    if (result.Actions.Count > 0 || result.PressureDetected || !result.Success)
                        AppendLog(result, enforce);
                    DeleteOldLogs();
                    return result;
                }
                catch (Exception ex)
                {
                    var failed = new GuardResult { Success = false };
                    failed.Actions.Add("error=" + ex.GetType().Name + ":" + ex.Message);
                    AppendLog(failed, enforce);
                    return failed;
                }
                finally
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
            }
        }

        private static GuardResult InspectAndAct(bool enforce)
        {
            var result = new GuardResult();
            MemoryState memory = RecoveryEngine.GetMemoryState();
            result.PressureDetected = memory.AvailablePhysicalGb < 1.5 ||
                memory.AvailableVirtualGb < 6.0 || memory.CommitPressurePercent >= 85.0;

            Dictionary<uint, ManagementObject> processes = LoadProcesses();
            result.Inspected = processes.Count;
            foreach (ManagementObject item in processes.Values)
            {
                uint pid = UIntValue(item["ProcessId"]);
                if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) continue;
                uint parentPid = UIntValue(item["ParentProcessId"]);
                string name = TextValue(item["Name"]);
                string command = TextValue(item["CommandLine"]);
                bool orphan = IsOrphan(item, parentPid, processes);

                Process process;
                try { process = Process.GetProcessById((int)pid); }
                catch { continue; }

                using (process)
                {
                    long privateBytes;
                    int handles;
                    int threads;
                    TimeSpan age;
                    try
                    {
                        privateBytes = process.PrivateMemorySize64;
                        handles = process.HandleCount;
                        threads = process.Threads.Count;
                        age = DateTime.Now - process.StartTime;
                    }
                    catch { continue; }

                    bool runaway = (name.Equals("node.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) &&
                        privateBytes >= HardPrivateBytes;
                    bool orphanDevServer = name.Equals("node.exe", StringComparison.OrdinalIgnoreCase) &&
                        orphan && age >= OrphanDevServerGrace &&
                        handles >= OrphanHandleLimit && IsKnownDevServer(command);
                    bool orphanConsole = name.Equals("conhost.exe", StringComparison.OrdinalIgnoreCase) &&
                        orphan && threads >= OrphanConsoleThreadLimit;

                    if (runaway || orphanDevServer || orphanConsole)
                    {
                        string reason = runaway ? "hard-memory-limit" :
                            (orphanDevServer ? "orphan-dev-server" : "orphan-console");
                        string action = string.Format(CultureInfo.InvariantCulture,
                            "{0}:{1} {2} privateMB={3} handles={4} threads={5}",
                            name, pid, reason, privateBytes / 1024 / 1024, handles, threads);
                        if (enforce)
                        {
                            try
                            {
                                process.Kill();
                                result.Stopped++;
                                result.Actions.Add("stopped " + action);
                            }
                            catch (Exception ex)
                            {
                                result.Success = false;
                                result.Actions.Add("stop-failed " + action + " " + ex.GetType().Name);
                            }
                        }
                        else result.Actions.Add("would-stop " + action);
                    }
                }
            }

            if (result.PressureDetected)
                RelievePressure(result, enforce);
            return result;
        }

        private static Dictionary<uint, ManagementObject> LoadProcesses()
        {
            var result = new Dictionary<uint, ManagementObject>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId,ParentProcessId,Name,CommandLine,CreationDate FROM Win32_Process"))
            using (ManagementObjectCollection values = searcher.Get())
            {
                foreach (ManagementObject item in values)
                {
                    uint pid = UIntValue(item["ProcessId"]);
                    if (pid != 0) result[pid] = item;
                }
            }
            return result;
        }

        private static bool IsOrphan(ManagementObject child, uint parentPid,
            Dictionary<uint, ManagementObject> processes)
        {
            if (parentPid == 0) return false;
            ManagementObject parent;
            if (!processes.TryGetValue(parentPid, out parent)) return true;
            DateTime childCreated = CimDate(child["CreationDate"]);
            DateTime parentCreated = CimDate(parent["CreationDate"]);
            return childCreated != DateTime.MinValue && parentCreated != DateTime.MinValue &&
                parentCreated > childCreated;
        }

        private static bool IsKnownDevServer(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            return DevServerMarkers.Any(marker =>
                command.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void RelievePressure(GuardResult result, bool enforce)
        {
            foreach (string name in HeavyProcessNames)
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (!enforce) continue;
                        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                        try
                        {
                            if (EmptyWorkingSet(process.Handle)) result.Trimmed++;
                        }
                        catch { }
                    }
                }
            }
            result.Actions.Add(enforce ? "pressure-relief-applied" : "would-apply-pressure-relief");
        }

        public static bool IsScheduled()
        {
            CommandResult result = RunTaskCommand("/Query /TN \"" + TaskName + "\"");
            return result.ExitCode == 0;
        }

        public static CommandResult SetScheduledGuardEnabled(bool enabled)
        {
            if (!enabled)
                return RunTaskCommand("/Delete /TN \"" + TaskName + "\" /F");

            string executable = ApplicationPath();
            string taskRun = "\\\"" + executable + "\\\" --resource-guard-once";
            string arguments = "/Create /TN \"" + TaskName + "\" /TR \"" + taskRun +
                "\" /SC MINUTE /MO 2 /F /RL LIMITED";
            return RunTaskCommand(arguments);
        }

        private static CommandResult RunTaskCommand(string arguments)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "schtasks.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit(15000);
                return new CommandResult
                {
                    ExitCode = process.HasExited ? process.ExitCode : -1,
                    Output = output + error
                };
            }
        }

        private static string ApplicationPath()
        {
            return Process.GetCurrentProcess().MainModule.FileName;
        }

        private static void WriteState(GuardResult result, bool enforce)
        {
            string path = Path.Combine(GuardRoot, "latest.txt");
            File.WriteAllText(path,
                "checked=" + DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + "\r\n" +
                "mode=" + (enforce ? "enforce" : "audit") + "\r\n" + result,
                Encoding.UTF8);
        }

        private static void AppendLog(GuardResult result, bool enforce)
        {
            string path = Path.Combine(GuardRoot,
                "guard-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
            File.AppendAllText(path,
                DateTime.Now.ToString("o", CultureInfo.InvariantCulture) + " mode=" +
                (enforce ? "enforce" : "audit") + " " + result.ToString().Replace("\r\n", "; ") +
                Environment.NewLine, Encoding.UTF8);
        }

        private static void DeleteOldLogs()
        {
            foreach (string path in Directory.GetFiles(GuardRoot, "guard-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(path) < DateTime.Now.AddDays(-14)) File.Delete(path);
                }
                catch { }
            }
        }

        private static uint UIntValue(object value)
        {
            return value == null ? 0 : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
        }

        private static string TextValue(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        private static DateTime CimDate(object value)
        {
            string text = TextValue(value);
            if (text.Length == 0) return DateTime.MinValue;
            try { return ManagementDateTimeConverter.ToDateTime(text); }
            catch { return DateTime.MinValue; }
        }
    }
}
