using SteeleTerm.Serial;
using SteeleTerm.SSH;
using SteeleTerm.ToolBox;
using SteeleTerm.Updater;
using System.Runtime.Versioning;
using System.Text;
[assembly: SupportedOSPlatform("windows")]
namespace SteeleTerm
{
    class SteeleTerm
    {
        internal static readonly object consoleLock = new();
        static int Main(string[] args)
        {
            Console.InputEncoding = Encoding.UTF8;
            Console.OutputEncoding = Encoding.UTF8;
            if (!ToolBoxHandshake.VerifyToolBoxHost()) return 1;
            if (Update.TryHandleUpdateCommandTree(args, "SteeleTerm", "SteeleTerm.csproj", out var updateExitCode)) return updateExitCode;
            bool hasHelp = args.Contains("--help", StringComparer.Ordinal);
            bool hasSerial = args.Contains("--serial", StringComparer.Ordinal);
            bool hasSSH = args.Contains("--ssh", StringComparer.Ordinal);
            if ((hasHelp && hasSerial) || (hasHelp && hasSSH) || (hasSerial && hasSSH)) { Console.WriteLine("Only one primary argument allowed."); return 1; }
            if (args.Length == 0) { Console.WriteLine("Use --help for arg list."); return 1; }
            if (hasHelp)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("  \'SteeleTerm\'                         Print \'Use --help for arg list\' message.");
                Console.WriteLine("Primary args:");
                Console.WriteLine("  \'--help\'                              Print help list to console.");
                Console.WriteLine("  \'--serial\'                            Open SteeleTerm in serial mode.");
                Console.WriteLine("  \'--ssh\'                               Open SteeleTerm in ssh mode.");
                Console.WriteLine("  \'--updateMajor [secondary] [tetiary]\' Increment major version.");
                Console.WriteLine("  \'--updateMinor [secondary] [tetiary]\' Increment minor version.");
                Console.WriteLine("  \'--update [secondary] [tetiary]\'      Increment patch version.");
                Console.WriteLine("Secondary args:");
                Console.WriteLine("  \'<primary> --forceUpdate [tertiary]\'  Force rebuild/reinstall even if nothing changed. Requires an update primary arg.");
                Console.WriteLine("Tertiary args:");
                Console.WriteLine("  \'<primary> <secondary> --skipVersion\' Do not update version number. Requires --forceUpdate arg.");
                return 0;
            }
            if (hasSerial) { SteeleTermSerial.Serial(); return 0; }
            if (hasSSH) { SteeleTermSSH.SSH(); return 0; }
            else return 0;
        }
        public static void Say(string prompt, string message) { Console.WriteLine($"{prompt}{message}"); }
        public static void ClearLine(int top)
        {
            int height;
            try { height = Console.BufferHeight; }
            catch { return; }
            if (height <= 0) return;
            if (top < 0) top = 0;
            if (top >= height) top = height - 1;
            int width;
            try { width = Console.BufferWidth; }
            catch { return; }
            width = Math.Max(1, width);
            try { Console.SetCursorPosition(0, top); Console.Write(new string(' ', Math.Max(0, width - 1))); Console.SetCursorPosition(0, top); }
            catch (ArgumentOutOfRangeException) { }
        }
        public static string? ReadToken(string prompt, string promptText, bool echo = true, bool printPrompt = true, bool commitNewlineOnEnter = false, Func<ConsoleKeyInfo, bool>? immediateKey = null, Action<ConsoleKeyInfo>? onImmediateKey = null, Func<bool>? echoEnabled = null)
        {
            int startTop;
            int startLeft;
            lock (consoleLock)
            {
                if (printPrompt) Console.Write($"{prompt}{promptText}");
                startTop = Console.CursorTop;
                startLeft = Console.CursorLeft;
            }
            var buf = new StringBuilder();
            int echoedCount = 0;
            bool lastEcho = (echoEnabled?.Invoke() ?? echo);
            while (true)
            {
                var k = Console.ReadKey(true);
                bool echoNow = (echoEnabled?.Invoke() ?? echo);
                if (lastEcho && !echoNow && echoedCount != 0)
                {
                    lock (consoleLock)
                    {
                        for (int i = 0; i < echoedCount; i++)
                        {
                            int top = Console.CursorTop;
                            int left = Console.CursorLeft;
                            if (top < startTop || (top == startTop && left <= startLeft)) break;
                            Console.Write("\b \b");
                        }
                    }
                    echoedCount = 0;
                }
                lastEcho = echoNow;
                if (buf.Length == 0 && immediateKey != null && immediateKey(k)) { onImmediateKey?.Invoke(k); continue; }
                if (k.Key == ConsoleKey.Enter)
                {
                    if (commitNewlineOnEnter) { lock (consoleLock) { Console.WriteLine(""); } }
                    if (buf.Length == 0) return null;
                    return buf.ToString();
                }
                if (k.Key == ConsoleKey.Backspace)
                {
                    if (buf.Length == 0) continue;
                    buf.Length--;
                    if (echoNow && echoedCount != 0)
                    {
                        lock (consoleLock)
                        {
                            int top = Console.CursorTop;
                            int left = Console.CursorLeft;
                            if (top > startTop || (top == startTop && left > startLeft)) Console.Write("\b \b");
                        }
                        echoedCount--;
                    }
                    continue;
                }
                if (k.KeyChar == '\0') continue;
                if (char.IsControl(k.KeyChar)) continue;
                buf.Append(k.KeyChar);
                if (echoNow) { lock (consoleLock) { Console.Write(k.KeyChar); } echoedCount++; }
            }
        }
        public sealed class ConsoleSpinner(object outputLock, int intervalMs = 100) : IDisposable
        {
            readonly string[] frames = ["|", "/", "-", "\\"];
            readonly object outputLock = outputLock;
            readonly int intervalMs = intervalMs;
            Thread? t;
            volatile bool running;
            string text = "";
            bool oldCursorVisible = true;
            public void Start(string prompt, string text)
            {
                if (Console.IsOutputRedirected) return;
                if (running) return;
                this.text = text;
                running = true;
                try { oldCursorVisible = Console.CursorVisible; Console.CursorVisible = false; } catch { }
                t = new Thread(() =>
                {
                    int i = 0;
                    while (running)
                    {
                        lock (outputLock) { try { Console.Write("\r" + prompt + this.text + " " + frames[i++ % frames.Length]); } catch { } }
                        Thread.Sleep(intervalMs);
                    }
                })
                { IsBackground = true };
                t.Start();
            }
            public void Stop()
            {
                if (!running) return;
                running = false;
                try { t?.Join(); } catch { }
                lock (outputLock)
                {
                    try
                    {
                        int w = Math.Max(1, Console.BufferWidth);
                        Console.Write("\r" + new string(' ', Math.Max(0, w - 1)) + "\r");
                    }
                    catch { try { Console.Write("\r"); } catch { } }
                    try { Console.CursorVisible = oldCursorVisible; } catch { }
                }
            }
            public void Dispose() { Stop(); }
        }
        public static void StopSpinnerIfArmed(ref int waitingRx, ConsoleSpinner rxSpinner)
        {
            if (Interlocked.Exchange(ref waitingRx, 0) == 1) rxSpinner.Stop();
        }
        public static string? SteeleTermFileBrowser(string prompt, string title, string? startDir)
        {
            string cwd = startDir ?? "";
            if (cwd.Length == 0 || !Directory.Exists(cwd)) cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            while (true)
            {
                try { Console.Clear(); } catch { }
                Console.WriteLine($"{prompt}{title}");
                Console.WriteLine($"{prompt}{cwd}");
                string[] dirs;
                string[] files;
                try { dirs = Directory.GetDirectories(cwd); files = Directory.GetFiles(cwd); }
                catch { Console.WriteLine($"{prompt}Cannot access directory."); var parent = Directory.GetParent(cwd); if (parent != null) cwd = parent.FullName; ReadToken(prompt, "Press Enter to continue: ", true, true, true); continue; }
                var items = new List<(bool IsDir, string Name, string FullPath)>();
                for (int d = 0; d < dirs.Length; d++) items.Add((true, Path.GetFileName(dirs[d]), dirs[d]));
                for (int f = 0; f < files.Length; f++) items.Add((false, Path.GetFileName(files[f]), files[f]));
                int count = items.Count;
                var display = new List<string>(count);
                int maxLen = 0;
                for (int k = 0; k < count; k++)
                {
                    string s = $"{k + 1:00} {(items[k].IsDir ? "[📁]" : "[📜]")} {items[k].Name}";
                    display.Add(s);
                    if (s.Length > maxLen) maxLen = s.Length;
                }
                int consoleWidth;
                try { consoleWidth = Console.WindowWidth; } catch { consoleWidth = 120; }
                consoleWidth = Math.Max(60, consoleWidth);
                int prefixWidth = prompt.Length;
                int w = Math.Max(40, consoleWidth - prefixWidth);
                int sep = 3;
                int itemWidth = Math.Min(70, Math.Max(18, maxLen));
                int cols = Math.Max(1, (w + sep) / (itemWidth + sep));
                int total = cols * itemWidth + (cols - 1) * sep;
                string bar = new('-', total);
                Console.WriteLine(prompt + bar);
                string head = title.Length > total ? string.Concat(title.AsSpan(0, Math.Max(0, total - 3)), "...") : title;
                Console.WriteLine(prompt + head.PadRight(total));
                string pathLine = cwd.Length > total ? string.Concat(cwd.AsSpan(0, Math.Max(0, total - 3)), "...") : cwd;
                Console.WriteLine(prompt + pathLine.PadRight(total));
                Console.WriteLine(prompt + bar);
                int rows = (count + cols - 1) / cols;
                for (int r = 0; r < rows; r++)
                {
                    var line = new System.Text.StringBuilder(total + 16);
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        if (idx >= count) break;
                        string s = display[idx];
                        if (s.Length > itemWidth) s = string.Concat(s.AsSpan(0, Math.Max(0, itemWidth - 3)), "...");
                        if (c != 0) line.Append(" | ");
                        line.Append(s.PadRight(itemWidth));
                    }
                    Console.WriteLine(prompt + line.ToString());
                }
                Console.WriteLine(prompt + bar);
                Console.WriteLine(prompt + "Commands: <n>=open/select  open <n>  select <n>  back/..  exit(browser)  Exit(SteeleTerm)");
                string? input = ReadToken(prompt, "> ", true, true, true);
                if (input == null) continue;
                input = input.Trim();
                if (input.Length == 0) continue;
                if (string.Equals(input, "Exit", StringComparison.Ordinal)) return "Exit";
                if (string.Equals(input, "exit", StringComparison.Ordinal)) return null;
                if (string.Equals(input, "back", StringComparison.Ordinal) || string.Equals(input, "..", StringComparison.Ordinal))
                {
                    var parent = Directory.GetParent(cwd);
                    if (parent != null) cwd = parent.FullName;
                    continue;
                }
                if (input.Length >= 2 && ((input[0] == '"' && input[^1] == '"') || (input[0] == '\'' && input[^1] == '\''))) input = input[1..^1];
                if (File.Exists(input)) return Path.GetFullPath(input);
                if (Directory.Exists(input)) { cwd = Path.GetFullPath(input); continue; }
                int sp = input.IndexOf(' ');
                string cmd = sp > 0 ? input[..sp] : input;
                string arg = sp > 0 ? input[(sp + 1)..].Trim() : "";
                int n;
                if (string.Equals(cmd, "open", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(arg, out n) || n < 1 || n > count) continue;
                    if (!items[n - 1].IsDir) continue;
                    cwd = items[n - 1].FullPath;
                    continue;
                }
                if (string.Equals(cmd, "select", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(arg, out n) || n < 1 || n > count) continue;
                    if (items[n - 1].IsDir) continue;
                    return items[n - 1].FullPath;
                }
                if (int.TryParse(input, out n) && n >= 1 && n <= count)
                {
                    if (items[n - 1].IsDir) { cwd = items[n - 1].FullPath; continue; }
                    return items[n - 1].FullPath;
                }
            }
        }
    }
}