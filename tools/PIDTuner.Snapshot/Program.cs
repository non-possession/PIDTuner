using System.IO;
using System.Windows;
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
