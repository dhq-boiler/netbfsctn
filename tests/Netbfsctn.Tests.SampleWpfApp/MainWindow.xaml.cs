using System.Windows;
using System.Windows.Threading;

namespace Netbfsctn.Tests.SampleWpfApp;

public partial class MainWindow : Window
{
	private readonly MainWindowViewModel _viewModel = new();
	private IDisposable? _subscription1;
	private IDisposable? _subscription2;
	private DispatcherTimer? _autoTimer;

	public MainWindow()
	{
		InitializeComponent();
		DataContext = _viewModel;
		SizeChanged += OnSizeChanged;
		DataContextChanged += OnDataContextChanged;
	}

	/// <summary>
	/// Reproduces the pattern from TutorialOverlayControl.OnDataContextChanged:
	/// Lambda capturing outer variable + Dispatcher.InvokeAsync inside Subscribe callback.
	/// This pattern causes max stack calculation errors in netbfsctn's control flow obfuscation.
	/// </summary>
	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		_subscription1?.Dispose();
		_subscription2?.Dispose();
		if (DataContext is MainWindowViewModel vm)
		{
			// Pattern 1: Lambda with Dispatcher.InvokeAsync and captured variable
			_subscription1 = new SimpleSubscription(() =>
			{
				Dispatcher.InvokeAsync(() => UpdateTitle(vm.ResultText));
			});

			// Pattern 2: Nested lambda with condition check and captured variable
			_subscription2 = new SimpleSubscription(() =>
			{
				Dispatcher.InvokeAsync(() =>
				{
					if (vm.InputText.Length >= 0) Focus();
				});
			});
		}
	}

	/// <summary>
	/// Reproduces the pattern from AutoAdvanceAfterDelay:
	/// DispatcherTimer with lambda Tick handler capturing outer variable.
	/// </summary>
	private void OnSizeChanged(object sender, SizeChangedEventArgs e)
	{
		_autoTimer?.Stop();
		if (DataContext is MainWindowViewModel vm)
		{
			_autoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
			_autoTimer.Tick += (_, _) =>
			{
				_autoTimer?.Stop();
				_autoTimer = null;
				if (!string.IsNullOrEmpty(vm.ResultText))
					Title = $"Size: {e.NewSize.Width}x{e.NewSize.Height}";
			};
			_autoTimer.Start();
		}
	}

	private void UpdateTitle(string text)
	{
		if (!string.IsNullOrEmpty(text))
			Title = $"Sample WPF App - {text}";
	}

	private void OnCalculateClick(object sender, RoutedEventArgs e)
	{
		_viewModel.Calculate();
	}

	private void OnTransformClick(object sender, RoutedEventArgs e)
	{
		_viewModel.Transform();
	}
}

/// <summary>
/// Simple IDisposable subscription to simulate R3's Subscribe pattern.
/// </summary>
internal sealed class SimpleSubscription : IDisposable
{
	private Action? _callback;

	public SimpleSubscription(Action callback)
	{
		_callback = callback;
	}

	public void Invoke() => _callback?.Invoke();

	public void Dispose()
	{
		_callback = null;
	}
}
