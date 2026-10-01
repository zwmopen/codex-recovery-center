using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace CodexRecoveryCenter
{
    internal static class OpenClawController
    {
        public const string ManagerPath =
            @"D:\AICode\AI\infrastructure\launchers\manage-openclaw.ps1";
        public const string DashboardUrl = "http://127.0.0.1:18789/";

        public static bool IsAvailable
        {
            get { return File.Exists(ManagerPath); }
        }

        public static CommandResult Run(string action, string model, int timeoutMs)
        {
            if (!IsAvailable)
                return new CommandResult { ExitCode = 2, Output = "未找到小龙虾控制脚本：" + ManagerPath };

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (String.IsNullOrWhiteSpace(systemRoot)) systemRoot = @"C:\Windows";
            string powershell = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            string args = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " +
                Quote(ManagerPath) + " -Action " + Quote(action);
            if (!String.IsNullOrWhiteSpace(model)) args += " -Model " + Quote(model);
            // manage-openclaw.ps1 is Windows PowerShell 5.1 code and emits the
            // local Windows code page. Read it with the normal native encoding
            // so Chinese status text remains parseable in the UI.
            return RecoveryEngine.RunProcess(powershell, args, timeoutMs);
        }

        public static OpenClawStatus GetStatus()
        {
            CommandResult result = Run("Status", null, 30000);
            string output = result.Output ?? "";
            bool running = output.IndexOf("运行中", StringComparison.OrdinalIgnoreCase) >= 0;
            string model = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("当前模型：", StringComparison.Ordinal))
                .Select(line => line.Substring("当前模型：".Length).Trim())
                .FirstOrDefault() ?? "未知";
            return new OpenClawStatus
            {
                Available = IsAvailable,
                Running = running,
                Model = model,
                Result = result
            };
        }

        public static bool EnsureDashboardAvailable()
        {
            if (ProbeDashboard()) return true;
            CommandResult result = Run("Start", null, 240000);
            return result != null && result.ExitCode == 0 && ProbeDashboard();
        }

        private static bool ProbeDashboard()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(DashboardUrl);
                request.Method = "GET";
                request.Timeout = 1200;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    return (int)response.StatusCode >= 200 && (int)response.StatusCode < 300;
            }
            catch
            {
                return false;
            }
        }

        public static List<string> GetModels(out CommandResult result)
        {
            result = Run("Models", null, 30000);
            if (result.TimedOut || result.ExitCode != 0) return new List<string>();
            return (result.Output ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(line => line, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string Describe(CommandResult result)
        {
            if (result == null) return "没有收到控制结果。";
            if (result.TimedOut) return "操作超过规定时间，已停止等待。";
            if (result.ExitCode != 0) return FirstUsefulLine(result.Output) + "（退出码 " + result.ExitCode + "）";
            return FirstUsefulLine(result.Output);
        }

        private static string FirstUsefulLine(string output)
        {
            string[] lines = (output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "操作完成。";
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }

    internal sealed class OpenClawStatus
    {
        public bool Available;
        public bool Running;
        public string Model = "未知";
        public CommandResult Result;
    }
}
