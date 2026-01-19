using System.Linq.Expressions;

namespace SteeleTerm.SSH
{
    partial class SteeleTermSSH
    {
        private static string prompt = " 🔒 > ";
        public static int SSH()
        {
        Reset:
            SetPromptDisconnected();
        EnterHost:
            int hostTop = Console.CursorTop;
            var hostAddress = SteeleTerm.ReadToken(prompt, "Enter Hostname or IP address:");
            if (hostAddress == null) { SteeleTerm.ClearLine(hostTop); goto EnterHost; }
            hostAddress = hostAddress.Trim();
            if (hostAddress.Length == 0) { SteeleTerm.ClearLine(hostTop); goto EnterHost; }
            if (string.Equals(hostAddress, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            SetPromptHost(hostAddress);
        EnterPort:
            int portTop = Console.CursorTop;
            int portNum = 22;
            var port = SteeleTerm.ReadToken(prompt, "Enter port (Default 22): ");
            if (string.Equals(port, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            if (port == null || port.Trim().Length == 0) { Console.WriteLine(""); portNum = 22; }
            else
            {
                try { portNum = int.Parse(port.Trim()); }
                catch { SteeleTerm.ClearLine(portTop); goto EnterPort; }
                if (portNum < 1 || portNum > 65535) { SteeleTerm.ClearLine(portTop); goto EnterPort; }
                Console.WriteLine("");
            }
            SetPromptPort(hostAddress, portNum);
        EnterUser:
            int userTop = Console.CursorTop;
            var userID = SteeleTerm.ReadToken(prompt, "Enter user ID: ");
            if (userID == null) { SteeleTerm.ClearLine(userTop); goto EnterUser; }
            userID = userID.Trim();
            if (userID.Length == 0) { SteeleTerm.ClearLine(userTop); goto EnterUser; }
            if (string.Equals(userID, "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
        AuthMethod:

            else
            {
                if (bitNotation.Trim().Length < 3 || bitNotation.Trim().Length > 5) { SteeleTerm.ClearLine(bitsTop); goto EnterBitNotation; }
                bitNotation = bitNotation.Trim().ToUpperInvariant();
                dataBits = bitNotation[0] - '0';
                if (dataBits < 5 || dataBits > 8) { SteeleTerm.ClearLine(bitsTop); goto EnterBitNotation; }
                char pChar = bitNotation[1];
                if (pChar == 'N') parity = Parity.None;
                else if (pChar == 'E') parity = Parity.Even;
                else if (pChar == 'O') parity = Parity.Odd;
                else if (pChar == 'M') parity = Parity.Mark;
                else if (pChar == 'S') parity = Parity.Space;
                else { SteeleTerm.ClearLine(bitsTop); goto EnterBitNotation; }
                string sbText = bitNotation[2..];
                if (sbText == "1") stopBits = StopBits.One;
                else if (sbText == "1.5") stopBits = StopBits.OnePointFive;
                else if (sbText == "2") stopBits = StopBits.Two;
                else { SteeleTerm.ClearLine(bitsTop); goto EnterBitNotation; }
                Console.WriteLine("");
                SetPromptBits(selectedPort.Port, baudRate, dataBits, parity, stopBits);
            }
        Connect:
            int connectTop = Console.CursorTop;
            var Connect = SteeleTerm.ReadToken(prompt, "Are these settings correct? (Y/N): ");
            if (Connect == null) { SteeleTerm.ClearLine(connectTop); goto Connect; }
            if (string.Equals(Connect.Trim(), "Exit", StringComparison.Ordinal)) { Console.WriteLine(""); return 0; }
            Connect = Connect.Trim().ToUpperInvariant();
            if (Connect == "N") { Console.WriteLine(""); goto Reset; }
            if (Connect == "Y")
            {
                return 0;
            }
            else goto Connect;
        }
        private static void SetPromptDisconnected() { prompt = " 🔒 > "; }
        private static void SetPromptHost(string host) { prompt = $" 🔒 {host} > "; }
        private static void SetPromptPort(string host, int port) { prompt = $" 🔒 {host} {port} > "; }
        private static void SetPromptUser(string host, int port, string user) { prompt = $" 🔒 {host} {port} {user} > "; }
    }
}