using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PIDTuner.Desktop;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: PIDTuner.Snapshot <output.png>");
    return 2;
}

var thread = new Thread(() =>
{
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
