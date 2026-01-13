using SteeleTerm.Serial;
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
            else return 0;
        }
        public static void Say(string prompt, string message) { Console.WriteLine($"{prompt}{message}"); }
        public static void ClearLine(int top)
        {
            int width = Math.Max(1, Console.BufferWidth);
            Console.SetCursorPosition(0, top);
            Console.Write(new string(' ', Math.Max(0, width - 1)));
            Console.SetCursorPosition(0, top);
        }
        public static string? ReadToken(string prompt, string promptText, bool echo = true, bool printPrompt = true, bool commitNewlineOnEnter = false, Func<ConsoleKeyInfo, bool>? immediateKey = null, Action<ConsoleKeyInfo>? onImmediateKey = null)
        {
            if (printPrompt) { lock (consoleLock) { Console.Write($"{prompt}{promptText}"); } }
            var buf = new StringBuilder();
            while (true)
            {
                var k = Console.ReadKey(true);
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
                    if (echo) { lock (consoleLock) { Console.Write("\b \b"); } }
                    continue;
                }
                if (k.KeyChar == '\0') continue;
                if (char.IsControl(k.KeyChar)) continue;
                buf.Append(k.KeyChar);
                if (echo) { lock (consoleLock) { Console.Write(k.KeyChar); } }
            }
        }
    }
}