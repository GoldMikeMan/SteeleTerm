using SteeleTerm.AddonModules;
namespace SteeleTerm.ToolBox
{
	class ToolBoxHandshake
	{
		public static bool VerifyToolBoxHost()
		{
			const string sentinel = "🔍 Verifying parent is ToolBox...";
			bool isToolBox = string.Equals(Environment.GetEnvironmentVariable("TOOLBOX_HOST"), "1", StringComparison.Ordinal);
			string prefix = (!Console.IsOutputRedirected && isToolBox) ? (Environment.GetEnvironmentVariable("TOOLBOX_PREFIX") ?? " 🧰 > ") : "";
			Console.WriteLine(prefix + sentinel);
			if (isToolBox)
			{
				Console.WriteLine(prefix + "✅ ToolBox detected.");
				return true;
			}
			using var spinner = new ConsoleSpinner(new Lock(), "");
			if (!Console.IsOutputRedirected) spinner.Start("⏳ Waiting for ToolBox");
			long end = Environment.TickCount64 + 5000;
			var readTask = Task.Run(() => Console.ReadLine());
			while (Environment.TickCount64 < end)
			{
				int remaining = (int)Math.Max(0, end - Environment.TickCount64);
				if (readTask.Wait(remaining))
				{
					var resp = (readTask.Result ?? "").Trim().TrimStart('\uFEFF');
					if (string.Equals(resp, "ToolBox is open", StringComparison.Ordinal))
					{
						spinner.StopAndFlush();
						Console.WriteLine("✅ ToolBox detected.");
						return true;
					}
				}
				Thread.Sleep(10);
			}
			spinner.StopAndFlush();
			Console.WriteLine("❌ ToolBox required to use this tool.");
			return false;
		}
	}
}