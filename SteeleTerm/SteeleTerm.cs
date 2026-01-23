using Microsoft.Win32;
using SteeleTerm.Serial;
using SteeleTerm.SSH;
using SteeleTerm.ToolBox;
using SteeleTerm.Updater;
using System.Diagnostics;
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
            bool hasFileBrowser = args.Contains("--fileBrowser", StringComparer.Ordinal);
            bool hasHelp = args.Contains("--help", StringComparer.Ordinal);
            bool hasSerial = args.Contains("--serial", StringComparer.Ordinal);
            bool hasSSH = args.Contains("--ssh", StringComparer.Ordinal);
            if (hasHelp)
            {
                Console.WriteLine("Commands:");
                Console.WriteLine("  \'SteeleTerm\'                         Print \'Use --help for arg list\' message.");
                Console.WriteLine("Primary args:");
                Console.WriteLine("  \'--fileBrowser\'                       Open SteeleTerm file browser.");
                Console.WriteLine("  \'--help\'                              Print help list to console.");
                Console.WriteLine("  \'--serial\'                            Open SteeleTerm in serial mode.");
                Console.WriteLine("  \'--ssh\'                               Open SteeleTerm in ssh mode.");
                Console.WriteLine("  \'--update [secondary] [tetiary]\'      Increment patch version.");
                Console.WriteLine("  \'--updateMajor [secondary] [tetiary]\' Increment major version.");
                Console.WriteLine("  \'--updateMinor [secondary] [tetiary]\' Increment minor version.");
                Console.WriteLine("Secondary args:");
                Console.WriteLine("  \'<primary> --forceUpdate [tertiary]\'  Force rebuild/reinstall even if nothing changed. Requires an update primary arg.");
                Console.WriteLine("Tertiary args:");
                Console.WriteLine("  \'<primary> <secondary> --skipVersion\' Do not update version number. Requires --forceUpdate arg.");
                return 0;
            }
            if (!ToolBoxHandshake.VerifyToolBoxHost()) return 1;
            if (Update.TryHandleUpdateCommandTree(args, "SteeleTerm", "SteeleTerm.csproj", out var updateExitCode)) return updateExitCode;
            if ((hasHelp && hasSerial) || (hasHelp && hasSSH) || (hasSerial && hasSSH) || (hasHelp && hasFileBrowser) || (hasSerial && hasFileBrowser) || (hasSSH && hasFileBrowser)) { Console.WriteLine("Only one primary argument allowed."); return 1; }
            if (args.Length == 0) { Console.WriteLine("Use --help for arg list."); return 1; }
            if (hasFileBrowser) { SteeleTermFileBrowser(Directory.GetCurrentDirectory(), true); return 0; }
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
        public static string? SteeleTermFileBrowser(string? startDir, bool allowOpen)
        {
            string promptFileBrowser = " 📂 > ";
            string cwd = startDir ?? "";
            bool inThisPc = false;
            if (cwd.Length == 0 || !Directory.Exists(cwd)) cwd = Directory.GetCurrentDirectory();
            while (true)
            {
                string currentDir = Directory.GetCurrentDirectory();
                string[] dirs = [];
                string[] files = [];
                var items = new List<(bool IsDir, string Name, string FullPath)>();
                if (inThisPc)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    bool redirected = Console.IsOutputRedirected;
                    int scanTop = Console.CursorTop;
                    int waitingScan = 1;
                    ConsoleSpinner? scanSpinner = null;
                    if (!redirected)
                    {
                        scanSpinner = new ConsoleSpinner(consoleLock);
                        if (Console.CursorLeft != 0) Console.WriteLine("");
                        scanSpinner.Start(promptFileBrowser, "Scanning drives ");
                    }
                    else Console.WriteLine($"{promptFileBrowser}Scanning drives...");
                    var drives = DriveInfo.GetDrives();
                    var uncCache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (int d = 0; d < drives.Length; d++)
                    {
                        var di = drives[d];
                        string root = di.Name;
                        string drive = root.TrimEnd('\\');
                        if (drive.Length >= 2 && drive[1] == ':') drive = char.ToUpperInvariant(drive[0]) + ":";
                        string label = "";
                        if (di.DriveType != DriveType.Network)
                        {
                            try { if (di.IsReady) label = (di.VolumeLabel ?? "").Trim(); } catch { }
                            string disp = label.Length == 0 ? $"({drive})" : $"({drive}) {label}";
                            items.Add((true, disp, root));
                            continue;
                        }
                        string disp2 = label.Length == 0 ? $"({drive})" : $"({drive}) {label}";
                        if (!uncCache.TryGetValue(drive, out string? unc))
                        {
                            string? uncResult = null;
                            var t = new Thread(() => { try { uncResult = TryGetUncForDrive(drive); } catch { } }) { IsBackground = true };
                            t.Start();
                            if (t.Join(250)) unc = uncResult;
                            else unc = null;
                            uncCache[drive] = unc;
                        }
                        if (!string.IsNullOrEmpty(unc)) disp2 += $" {unc}";
                        items.Add((true, disp2, root));
                    }
                    if (scanSpinner != null) StopSpinnerIfArmed(ref waitingScan, scanSpinner);
                    if (!redirected)
                    {
                        ClearLine(scanTop);
                        Console.SetCursorPosition(0, scanTop);
                    }
                    try
                    {
                        using var net = Registry.CurrentUser.OpenSubKey(@"Network");
                        if (net != null)
                        {
                            foreach (var letter in net.GetSubKeyNames())
                            {
                                using var dk = net.OpenSubKey(letter);
                                string? unc = dk?.GetValue("RemotePath") as string;
                                if (string.IsNullOrEmpty(unc)) continue;
                                string drive = letter.EndsWith(':') ? letter : letter + ":";
                                drive = letter.Length > 0 ? char.ToUpperInvariant(letter[0]) + ":" : "";
                                if (seen.Contains(drive)) continue;
                                string disp = $"({drive}) {unc}";
                                items.Add((true, disp, unc)); // FullPath is UNC so traversal works
                                seen.Add(drive);
                            }
                        }
                    }
                    catch { }
                }
                else
                {
                    try { dirs = Directory.GetDirectories(cwd); files = Directory.GetFiles(cwd); }
                    catch { Console.WriteLine($"{promptFileBrowser}Cannot access directory."); var parent = Directory.GetParent(cwd); if (parent != null) { cwd = parent.FullName; continue; } inThisPc = true; continue; }
                    for (int d = 0; d < dirs.Length; d++) items.Add((true, Path.GetFileName(dirs[d]), dirs[d]));
                    for (int f = 0; f < files.Length; f++) items.Add((false, Path.GetFileName(files[f]), files[f]));
                }
                int count = items.Count;
                var display = new List<string>(count);
                int maxLen = 0;
                for (int k = 0; k < count; k++)
                {
                    string icon = inThisPc ? "[💽]" : (items[k].IsDir ? "[📁]" : "[📄]");
                    string s = $"{k + 1:00} {icon} {items[k].Name}";
                    display.Add(s);
                    if (s.Length > maxLen) maxLen = s.Length;
                }
                int consoleWidth;
                try { consoleWidth = Console.WindowWidth; } catch { consoleWidth = 120; }
                consoleWidth = Math.Max(60, consoleWidth);
                int prefixWidth = promptFileBrowser.Length;
                int w = Math.Max(40, consoleWidth - prefixWidth);
                int sep = 3;
                int itemWidth = Math.Min(32, Math.Max(18, maxLen));
                int cols = Math.Max(1, (w + sep) / (itemWidth + sep));
                int total = cols * itemWidth + (cols - 1) * sep;
                string bar = new('-', total);
                Console.WriteLine();
                bool redirectedRender = Console.IsOutputRedirected;
                int renderTop = Console.CursorTop;
                int waitingRender = 1;
                ConsoleSpinner? renderSpinner = null;
                if (!redirectedRender)
                {
                    if (Console.CursorLeft != 0) Console.WriteLine("");
                    renderTop = Console.CursorTop;
                    renderSpinner = new ConsoleSpinner(consoleLock);
                    renderSpinner.Start(promptFileBrowser, inThisPc ? "Building drive list " : "Building table ");
                }
                byte[] colour = new byte[count];
                if (!inThisPc)
                {
                    for (int k = 0; k < count; k++)
                    {
                        try
                        {
                            string p = items[k].FullPath;
                            if (p.StartsWith(@"\\", StringComparison.Ordinal)) continue;
                            var attr = File.GetAttributes(p);
                            if ((attr & FileAttributes.Encrypted) != 0) colour[k] = 1;
                            else if ((attr & FileAttributes.Compressed) != 0) colour[k] = 2;
                        }
                        catch { }
                    }
                }
                string header;
                if (inThisPc) header = "This PC\\";
                else
                {
                    try
                    {
                        var root = Path.GetPathRoot(cwd);
                        if (!string.IsNullOrEmpty(root) && root.StartsWith(@"\\", StringComparison.Ordinal)) header = $"This PC\\[UNC] {cwd}\\";
                        else if (!string.IsNullOrEmpty(root))
                        {
                            var di = new DriveInfo(root);
                            string drive = di.Name.TrimEnd('\\'); // "C:"
                            string fmt = di.IsReady ? di.DriveFormat : "NOTREADY";
                            string label = di.IsReady ? (di.VolumeLabel ?? "") : "";
                            label = label.Trim();
                            header = label.Length == 0 ? $"This PC\\[{fmt}] {cwd}" : $"This PC\\{label} [{fmt}] {cwd}\\";
                        }
                        else header = $"This PC\\[PATH] {cwd}\\";
                    }
                    catch { header = $"This PC\\[FS] {cwd}\\"; }
                }
                string pathLine = header.Length > total ? string.Concat(header.AsSpan(0, Math.Max(0, total - 3)), "...") : header;
                int rows = (count + cols - 1) / cols;
                var cellText = new string?[rows, cols];
                var cellIdx = new int[rows, cols];
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = c * rows + r;
                        if (idx >= count) break;
                        string s = display[idx];
                        if (s.Length > itemWidth) s = string.Concat(s.AsSpan(0, Math.Max(0, itemWidth - 3)), "...");
                        cellText[r, c] = s.PadRight(itemWidth);
                        cellIdx[r, c] = idx;
                    }
                }
                if (renderSpinner != null) StopSpinnerIfArmed(ref waitingRender, renderSpinner);
                if (!redirectedRender)
                {
                    ClearLine(renderTop);
                    Console.SetCursorPosition(0, renderTop);
                }
                Console.WriteLine("      " + pathLine.PadRight(total));
                Console.WriteLine("      " + bar);
                for (int r = 0; r < rows; r++)
                {
                    Console.Write("      ");
                    for (int c = 0; c < cols; c++)
                    {
                        string? cell = cellText[r, c];
                        if (cell == null) break;
                        if (c != 0) Console.Write(" | ");
                        int idx = cellIdx[r, c];
                        var old = Console.ForegroundColor;
                        if (colour[idx] == 1) Console.ForegroundColor = ConsoleColor.Green;
                        else if (colour[idx] == 2) Console.ForegroundColor = ConsoleColor.Blue;
                        Console.Write(cell);
                        Console.ForegroundColor = old;
                    }
                    Console.WriteLine();
                }
                Console.WriteLine("      " + bar);
                Console.WriteLine("      Commands: ## = open/select | back = go up a directory | exit = close file browser | Exit = close SteeleTerm");
                Console.WriteLine();
                string? input = ReadToken(promptFileBrowser, "", true, true, true);
                if (input == null) continue;
                input = input.Trim();
                if (input.Length == 0) continue;
                if (string.Equals(input, "Exit", StringComparison.Ordinal)) return "Exit";
                if (string.Equals(input, "exit", StringComparison.Ordinal)) return "exit";
                if (string.Equals(input, "back", StringComparison.Ordinal))
                {
                    if (inThisPc) continue;
                    var parent = Directory.GetParent(cwd);
                    if (parent != null) { cwd = parent.FullName; continue; }
                    inThisPc = true;
                    continue;
                }
                if (input.Length >= 2 && ((input[0] == '"' && input[^1] == '"') || (input[0] == '\'' && input[^1] == '\''))) input = input[1..^1];
                if (File.Exists(input)) return Path.GetFullPath(input);
                if (Directory.Exists(input)) { cwd = Path.GetFullPath(input); continue; }
                if (int.TryParse(input, out int n) && n >= 1 && n <= count)
                {
                    if (allowOpen && !items[n - 1].IsDir)
                    {
                        try { Process.Start(new ProcessStartInfo { FileName = items[n - 1].FullPath, UseShellExecute = true }); }
                        catch (Exception ex) { Console.WriteLine($"{promptFileBrowser}Cannot open file: {ex.Message}"); }
                        continue;
                    }
                    if (items[n - 1].IsDir) { cwd = items[n - 1].FullPath; inThisPc = false; continue; }
                    return items[n - 1].FullPath;
                }
            }
        }
        [System.Runtime.InteropServices.DllImport("mpr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
        static string? TryGetUncForDrive(string driveLetter)
        {
            try
            {
                int len = 1024;
                var sb = new StringBuilder(len);
                int rc = WNetGetConnection(driveLetter, sb, ref len);
                if (rc == 0) return sb.ToString();
            }
            catch { }
            return null;
        }
    }
}