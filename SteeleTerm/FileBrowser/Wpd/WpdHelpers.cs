using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Devices.PortableDevices;
namespace SteeleTerm.FileBrowser.Wpd
{
	internal class WpdHelpers
	{
		internal static string[] GetDeviceNamesList(IPortableDeviceManager deviceManager, string[] deviceIDs)
		{
			List<string> deviceNames = [];
			foreach (string deviceID in deviceIDs)
			{
				uint requiredChars = 0;
                Span<char> nameProbe = default;
                try { deviceManager.GetDeviceFriendlyName(deviceID, ref nameProbe, ref requiredChars); } catch (COMException ex) { Console.WriteLine($"WPD: GetDeviceFriendlyName probe failed for {deviceID} (0x{ex.HResult:X8})"); }
                if (requiredChars == 0) { deviceNames.Add("Unknown Device"); continue; }
                char[] nameBuffer = new char[requiredChars];
                nameBuffer[^1] = '\0';
                Span<char> nameSpan = nameBuffer;
                uint capacityChars = requiredChars;
                try
                {
                    deviceManager.GetDeviceFriendlyName(deviceID, ref nameSpan, ref capacityChars);
                    string deviceName = new(nameSpan);
                    deviceNames.Add(deviceName);
                }
                catch { deviceNames.Add("Unknown Device"); }
			}
			return [.. deviceNames];
		}
		internal static string[] PointerArrayToList(nint basePtr, uint count)
		{
			List<string> results = new((int)count);
			int limit = (int)count;
			for (int i = 0; i < limit; i++)
			{
				int offsetBytes = i * IntPtr.Size;
				nint currentPtr = Marshal.ReadIntPtr(basePtr, offsetBytes);
				if (currentPtr == 0) continue;
				try
				{
					string? s = Marshal.PtrToStringUni(currentPtr);
					if (s == null || s.Length == 0) continue;
					results.Add(s);
				}
				finally { Marshal.FreeCoTaskMem(currentPtr); }
			}
			return [.. results];
		}
	}
}