using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
namespace SteeleTerm.Serial
{
    internal static partial class SteeleTermSerial
    {
        private sealed record PortInfo(string Port, string FriendlyName, string PnpDeviceId, string VidPid);
        private const int DefaultBaud = 115200;
        private const int DefaultDataBits = 8;
        private const Parity DefaultParity = Parity.None;
        private const StopBits DefaultStopBits = StopBits.One;
        private static string prompt = " 🔌 > ";
        public static int Serial()
        {
        Reset:
            SetPromptDisconnected();
            var ports = GetPorts();
            if (ports.Count == 0) { SteeleTerm.Say(prompt, "❌ No COM ports found."); return 1; }
            var validPortNumbers = ports.Select(p => GetPortNumber(p.Port)).Where(n => n > 0).Distinct().Select(n => n.ToString()).ToHashSet(StringComparer.Ordinal);
            SteeleTerm.Say(prompt, "Available COM ports:");
            PrintTable(ports);
        SelectPort:
            int lineTop = Console.CursorTop;
            var selected = SteeleTerm.ReadToken(prompt, "Select port#: ");
            if (selected == null) { SteeleTerm.ClearLine(lineTop); goto SelectPort; }
            selected = selected.Trim();
            if (selected.Length == 0) { SteeleTerm.ClearLine(lineTop); goto SelectPort; }
            if (string.Equals(selected, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            if (!validPortNumbers.Contains(selected)) { SteeleTerm.ClearLine(lineTop); goto SelectPort; }
            Console.WriteLine("");
            var portNum = int.Parse(selected);
            var selectedPort = ports.First(p => GetPortNumber(p.Port) == portNum);
            SetPromptCOM(selectedPort.Port);
            SteeleTerm.Say(prompt, $"✅ Selected: {selectedPort.Port}  {selectedPort.FriendlyName}");
        EnterBaud:
            var baud = SteeleTerm.ReadToken(prompt, "Enter baud rate (Default 115200): ");
            int baudRate;
            if (string.Equals(baud, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            if (baud == null || baud.Trim().Length == 0) { Console.WriteLine(""); baudRate = DefaultBaud; }
            else { try { baudRate = int.Parse(baud.Trim()); Console.WriteLine(""); } catch { SteeleTerm.ClearLine(lineTop); goto EnterBaud; } }
            SetPromptBaud(selectedPort.Port, baudRate);
        EnterBitNotation:
            var bitNotation = SteeleTerm.ReadToken(prompt, "Enter bit notation (Default 8N1): ");
            int dataBits = DefaultDataBits;
            Parity parity = DefaultParity;
            StopBits stopBits = DefaultStopBits;
            if (string.Equals(bitNotation, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            if (bitNotation == null || bitNotation.Trim().Length == 0) { Console.WriteLine(""); SetPromptBits(selectedPort.Port, baudRate, DefaultDataBits, DefaultParity, DefaultStopBits); }
            else
            {
                if (bitNotation.Trim().Length < 3 || bitNotation.Trim().Length > 5) { SteeleTerm.ClearLine(lineTop); goto EnterBitNotation; }
                bitNotation = bitNotation.Trim().ToUpperInvariant();
                dataBits = bitNotation[0] - '0';
                if (dataBits < 5 || dataBits > 8) { SteeleTerm.ClearLine(lineTop); goto EnterBitNotation; }
                char pChar = bitNotation[1];
                if (pChar == 'N') parity = Parity.None;
                else if (pChar == 'E') parity = Parity.Even;
                else if (pChar == 'O') parity = Parity.Odd;
                else if (pChar == 'M') parity = Parity.Mark;
                else if (pChar == 'S') parity = Parity.Space;
                else { SteeleTerm.ClearLine(lineTop); goto EnterBitNotation; }
                string sbText = bitNotation[2..];
                if (sbText == "1") stopBits = StopBits.One;
                else if (sbText == "1.5") stopBits = StopBits.OnePointFive;
                else if (sbText == "2") stopBits = StopBits.Two;
                else { SteeleTerm.ClearLine(lineTop); goto EnterBitNotation; }
                Console.WriteLine("");
                SetPromptBits(selectedPort.Port, baudRate, dataBits, parity, stopBits);
            }
        Connect:
            var Connect = SteeleTerm.ReadToken(prompt, "Are these settings correct? (Y/N): ");
            if (Connect == null) { SteeleTerm.ClearLine(lineTop); goto Connect; }
            if (string.Equals(Connect.Trim(), "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            Connect = Connect.Trim().ToUpperInvariant();
            if (Connect == "N") { Console.WriteLine(""); goto Reset; }
            if (Connect == "Y")
            {
                Console.WriteLine("");
                try
                {
                    using var serialPort = new SerialPort(selectedPort.Port, baudRate, parity, dataBits, stopBits);
                    serialPort.NewLine = "\n";
                    serialPort.ReadTimeout = 250;
                    serialPort.WriteTimeout = 250;
                    serialPort.Open();
                    SteeleTerm.Say(prompt, $"✅ Connected to {selectedPort.Port} at {baudRate} {dataBits}{GetParityChar(parity)}{GetStopBitsText(stopBits)}.");
                    var stop = false;
                    var rxLock = new object();
                    var rxThread = new Thread(() => {
                        var buf = new char[4096];
                        while (!Volatile.Read(ref stop))
                        {
                            try
                            {
                                int n = serialPort.Read(buf, 0, buf.Length);
                                if (n <= 0) continue;
                                lock (rxLock)
                                {
                                    Console.Write(new string(buf, 0, n));
                                }
                            }
                            catch (TimeoutException) { }
                            catch { break; }
                        }
                    }) { IsBackground = true };
                    rxThread.Start();
                    while (true)
                    {
                        var line = SteeleTerm.ReadToken(prompt, "");
                        if (line == null) { SteeleTerm.ClearLine(lineTop); continue; }
                        if (string.Equals(line.Trim(), "Exit", StringComparison.Ordinal))
                        {
                            Console.WriteLine("");
                            Volatile.Write(ref stop, true);
                            try { serialPort.Close(); } catch { }
                            try { rxThread.Join(250); } catch { }
                            SteeleTerm.Say(prompt, $"✅ Disconnected from {selectedPort.Port}.");
                            break;
                        }
                        serialPort.WriteLine(line);
                    }
                }
                catch (Exception ex)
                {
                    SteeleTerm.Say(prompt, $"❌ Error: {ex.Message}");
                    return 1;
                }
            }
            return 0;
        }
        private static void SetPromptDisconnected() { prompt = " 🔌 > "; }
        private static void SetPromptCOM(string port) { prompt = $" 🔌 {port} > "; }
        private static string SetPromptBaud(string port, int baud)
        {
            prompt = $" 🔌 {port} {baud} > ";
            return prompt;
        }
        private static void SetPromptBits(string port, int baud, int dataBits, Parity parity, StopBits stopBits)
        {
            var fmt = $"{dataBits}{GetParityChar(parity)}{GetStopBitsText(stopBits)}";
            prompt = $" 🔌 {port} {baud} {fmt} > ";
        }
        private static string GetStopBitsText(StopBits stopBits)
        {
            if (stopBits == StopBits.One) return "1";
            if (stopBits == StopBits.OnePointFive) return "1.5";
            if (stopBits == StopBits.Two) return "2";
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
            int numW = Math.Max(5, ports.Max(p => GetPortNumber(p.Port).ToString().Length));
            int portW = Math.Max(4, ports.Max(p => p.Port.Length));
            int nameW = Math.Max(12, ports.Max(p => p.FriendlyName.Length));
            int vidW = Math.Max(7, ports.Max(p => p.VidPid.Length));
            static string H(string s, int w) => s.PadRight(w);
            Console.WriteLine("");
            Console.WriteLine($"      {H("Port", portW)}  {H("Friendly Name", nameW)}  {H("VID:PID", vidW)}");
            Console.WriteLine($"      {new string('-', portW)}  {new string('-', nameW)}  {new string('-', vidW)}");
            for (int i = 0; i < ports.Count; i++)
            {
                var p = ports[i];
                var n = GetPortNumber(p.Port).ToString();
                Console.WriteLine($"      {H(p.Port, portW)}  {H(p.FriendlyName, nameW)}  {H(p.VidPid, vidW)}");
            }
            Console.WriteLine("");
        }
        private static int GetPortNumber(string port)
        {
            if (string.IsNullOrWhiteSpace(port)) return 0;
            int i = 0;
            while (i < port.Length && !char.IsDigit(port[i])) i++;
            if (i >= port.Length) return 0;
            int n = 0;
            while (i < port.Length && char.IsDigit(port[i])) { n = (n * 10) + (port[i] - '0'); i++; }
            return n;
        }
        [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase, "en-GB")]
        private static partial Regex COM();
        [GeneratedRegex(@"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase, "en-GB")]
        private static partial Regex VIDPID();
    }
}