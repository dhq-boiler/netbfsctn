using System.Windows;

namespace Netbfsctn.Tests.SampleWpfApp;

public partial class App : Application
{
	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		// --verify モード: UIを表示せずにロジックを検証して終了
		if (e.Args.Length > 0 && e.Args[0] == "--verify")
		{
			var vm = new MainWindowViewModel();
			var result = vm.RunVerification();
			Console.WriteLine(result);
			Shutdown(result == "OK" ? 0 : 1);
		}
	}
}
