using SteeleTerm.ToolBox;
using SteeleTerm.Updater;
using System;
using System.IO.Ports;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
[assembly: SupportedOSPlatform("windows")]
namespace SteeleTerm
{
    internal static partial class SteeleTerm
    {
        private sealed record PortInfo(string Port, string FriendlyName, string PnpDeviceId, string VidPid);
        private const int DefaultBaud = 115200;
        private const int DefaultDataBits = 8;
        private const Parity DefaultParity = Parity.None;
        private const StopBits DefaultStopBits = StopBits.One;
        private static string prompt = " 🔌 > ";
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
            if (hasSerial)
            {
                SetPromptDisconnected();
                var ports = GetPorts();
                if (ports.Count == 0) { Say("❌ No COM ports found."); return 1; }
                Say("Available COM ports:");
                PrintTable(ports);
            SelectPort:
                int lineTop = Console.CursorTop;
                var selected = PromptSelectPort(ports);
                if (selected == null) { ClearLine(lineTop); goto SelectPort; }
                if (string.Equals(selected.Port, "Exit", StringComparison.Ordinal)) return 0;
                SetPromptConnected(selected.Port, DefaultBaud, DefaultDataBits, DefaultParity, DefaultStopBits);
                Console.WriteLine("");
                Say($"✅ Selected: {selected.Port}  {selected.FriendlyName}");
                return 0;
            }
            else return 0;
        }
        private static void SetPromptDisconnected() { prompt = "🔌 > "; }
        private static void SetPromptConnected(string port, int baud, int dataBits, Parity parity, StopBits stopBits)
        {
            var fmt = $"{dataBits}{GetParityChar(parity)}{GetStopBitsText(stopBits)}";
            prompt = $"🔌 {port} {baud} {fmt} > ";
        }
        private static string GetStopBitsText(StopBits stopBits)
        {
            if (stopBits == StopBits.One) return "1";
            if (stopBits == StopBits.Two) return "2";
            if (stopBits == StopBits.OnePointFive) return "1.5";
            return "1";
        }
        private static char GetParityChar(Parity parity)
        {
            if (parity == Parity.None) return 'N';
            if (parity == Parity.Even) return 'E';
            if (parity == Parity.Odd) return 'O';
            if (parity == Parity.Mark) return 'M';
            if (parity == Parity.Space) return 'S';
            return 'N';
        }
        private static void Say(string message) { Console.WriteLine($"{prompt}{message}"); }
        private static string? Ask(string message)
        {
            Console.Write($"{prompt}{message}");
            return Console.ReadLine();
        }
        private static List<PortInfo> GetPorts()
        {
            var basePorts = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
            var list = new List<PortInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
                foreach (ManagementObject o in searcher.Get().Cast<ManagementObject>())
                {
                    var name = (o["Name"] as string) ?? "";
                    var pnp = (o["PNPDeviceID"] as string) ?? "";
                    var m = COM().Match(name);
                    if (!m.Success) continue;
                    var com = m.Groups[1].Value.ToUpperInvariant();
                    if (!basePorts.Contains(com)) continue;
                    var vidpid = TryExtractVidPid(pnp);
                    list.Add(new PortInfo(com, name, pnp, vidpid));
                }
            }
            catch { }
            if (list.Count == 0)
            {
                foreach (var p in basePorts.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(new PortInfo(p.ToUpperInvariant(), p.ToUpperInvariant(), "", ""));
                }
            }
            return [.. list.OrderBy(x => x.Port, StringComparer.OrdinalIgnoreCase)];
        }
        private static string TryExtractVidPid(string pnpDeviceId)
        {
            if (string.IsNullOrWhiteSpace(pnpDeviceId)) return "";
            var m = VIDPID().Match(pnpDeviceId);
            if (!m.Success) return "";
            return $"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}";
        }
        private static void PrintTable(List<PortInfo> ports)
        {
            const string indent = "       ";
            int idxW = Math.Max(2, ports.Count.ToString().Length);
            int portW = Math.Max(4, ports.Max(p => p.Port.Length));
            int nameW = Math.Max(12, ports.Max(p => p.FriendlyName.Length));
            int vidW = Math.Max(9, ports.Max(p => p.VidPid.Length));
            static string H(string s, int w) => s.PadRight(w);
            Console.WriteLine("");
            Console.WriteLine($"{indent}{H("##", idxW)}  {H("Port", portW)}  {H("Friendly Name", nameW)}  {H("VID : PID", vidW)}");
            Console.WriteLine($"{indent}{new string('-', idxW)}  {new string('-', portW)}  {new string('-', nameW)}  {new string('-', vidW)}");
            for (int i = 0; i < ports.Count; i++)
            {
                var p = ports[i];
                var n = (i + 1).ToString($"D{idxW}");
                Console.WriteLine($"{indent}{H(n, idxW)}  {H(p.Port, portW)}  {H(p.FriendlyName, nameW)}  {H(p.VidPid, vidW)}");
            }
            Console.WriteLine("");
        }
        private static PortInfo? PromptSelectPort(List<PortInfo> ports)
        {
            while (true)
            {
                var input = Ask($"Select port [1-{ports.Count}] or COMx (blank to exit): ");
                if (input == null) continue;
                input = input.Trim();
                if (input.Length == 0) return null;
                if (int.TryParse(input, out int n))
                {
                    if (n >= 1 && n <= ports.Count) return ports[n - 1];
                    Say("Invalid number.");
                    continue;
                }
                var com = input.ToUpperInvariant();
                if (!com.StartsWith("COM", StringComparison.OrdinalIgnoreCase)) com = "COM" + com;
                var match = ports.FirstOrDefault(p => string.Equals(p.Port, com, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
                Say("Invalid port.");
            }
        }
        static void ClearLine(int top)
        {
            int width = Math.Max(1, Console.BufferWidth);
            Console.SetCursorPosition(0, top);
            Console.Write(new string(' ', Math.Max(0, width - 1)));
            Console.SetCursorPosition(0, top);
        }
        [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase, "en-GB")]
        private static partial Regex COM();
        [GeneratedRegex(@"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase, "en-GB")]
        private static partial Regex VIDPID();
    }
}