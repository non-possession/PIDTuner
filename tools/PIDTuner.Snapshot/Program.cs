using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PIDTuner.Desktop;
using PIDTuner.Desktop.ViewModels;

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
        viewModel.SelectedHistorySession = viewModel.HistorySessions.FirstOrDefault();
        viewModel.SelectedTuningRecommendation = viewModel.TuningRecommendations.FirstOrDefault();
        viewModel.RecommendationReviewNote = "现场确认先小步验证";
        PumpDispatcherUntilComplete(viewModel.AcceptRecommendationAsync());
        viewModel.DismissNotificationCommand.Execute(null);
        var tabControl = FindVisualChild<TabControl>(window);
        if (tabControl is not null)
        {
            tabControl.SelectedIndex = 0;
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
