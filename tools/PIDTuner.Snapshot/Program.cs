using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PIDTuner.Desktop;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Models;
using PIDTuner.Infrastructure.Persistence;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: PIDTuner.Snapshot <output.png>");
    return 2;
}

var thread = new Thread(() =>
{
    SynchronizationContext.SetSynchronizationContext(
        new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));

    var outputPath = args[0];
    var window = new MainWindow
    {
        Width = 1180,
        Height = 800,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -10000,
        Top = -10000,
        ShowInTaskbar = false
    };

    window.Show();
    window.UpdateLayout();
    if (window.DataContext is MainWindowViewModel viewModel)
    {
        PumpDispatcherUntilComplete(viewModel.LoadExampleAsync());
        PumpDispatcherUntilComplete(viewModel.SaveTestSessionAsync());
        PumpDispatcherUntilComplete(SeedImprovedHistorySessionAsync());
        PumpDispatcherUntilComplete(viewModel.LoadHistoryAsync());
        viewModel.SelectedHistorySession = viewModel.HistorySessions.FirstOrDefault(item =>
            item.Name.Contains("offline", StringComparison.OrdinalIgnoreCase));
        PumpDispatcherUntilComplete(viewModel.SetHistoryBaselineAsync());
        viewModel.SelectedHistorySession = viewModel.HistorySessions.FirstOrDefault(item => item.Name == "improved-step");
        PumpDispatcherUntilComplete(viewModel.CompareHistorySessionAsync());
        viewModel.RecommendationReviewNote = "现场确认先小步验证";
        viewModel.DismissNotificationCommand.Execute(null);
        var tabControl = FindVisualChild<TabControl>(window);
        if (tabControl is not null)
        {
            tabControl.SelectedIndex = 4;
        }

        window.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
    }

    var width = (int)window.ActualWidth;
    var height = (int)window.ActualHeight;
    var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
    bitmap.Render(window);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    using var stream = File.Create(outputPath);
    encoder.Save(stream);

    window.Close();
});

thread.SetApartmentState(ApartmentState.STA);
thread.Start();
thread.Join();

return 0;

static void PumpDispatcherUntilComplete(Task task)
{
    var dispatcher = Dispatcher.CurrentDispatcher;

    while (!task.IsCompleted)
    {
        dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        Thread.Sleep(10);
    }

    task.GetAwaiter().GetResult();
}

static async Task SeedImprovedHistorySessionAsync()
{
    var repositoryRoot = FindRepositoryRoot();
    var directory = Path.Combine(repositoryRoot, "local", "test-sessions");
    var sessionRepository = new JsonTestSessionRepository(directory);
    var sampleRepository = new JsonPidSampleRepository(directory);
    var sessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var start = DateTimeOffset.Parse("2026-07-29T10:20:00.0000000+00:00");
    var session = new TestSession(
        sessionId,
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        "improved-step",
        start,
        start.AddSeconds(6),
        "Device A",
        "Reduced overshoot",
        "After conservative Kp change");
    var samples = new[]
    {
        Sample(start.AddSeconds(0), 100, 0, 0, sessionId),
        Sample(start.AddSeconds(1), 100, 45, 20, sessionId),
        Sample(start.AddSeconds(2), 100, 88, 38, sessionId),
        Sample(start.AddSeconds(3), 100, 104, 45, sessionId),
        Sample(start.AddSeconds(4), 100, 101, 43, sessionId),
        Sample(start.AddSeconds(5), 100, 100.2, 42, sessionId),
        Sample(start.AddSeconds(6), 100, 100.1, 42, sessionId)
    };

    await sessionRepository.SaveAsync(session, CancellationToken.None);
    await sampleRepository.SaveBatchAsync(samples, CancellationToken.None);
}

static PidSample Sample(DateTimeOffset timestamp, double sp, double pv, double mv, Guid sessionId)
{
    return new PidSample(timestamp, sp, pv, mv, 1.2, 0.4, 0.1, true, sessionId, null);
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "PIDTuner.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Directory.GetCurrentDirectory();
}

static T? FindVisualChild<T>(DependencyObject parent)
    where T : DependencyObject
{
    for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
    {
        var child = VisualTreeHelper.GetChild(parent, index);
        if (child is T typedChild)
        {
            return typedChild;
        }

        var descendant = FindVisualChild<T>(child);
        if (descendant is not null)
        {
            return descendant;
        }
    }

    return null;
}
