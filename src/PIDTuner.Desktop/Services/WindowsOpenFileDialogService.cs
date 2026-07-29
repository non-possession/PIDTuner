using Microsoft.Win32;

namespace PIDTuner.Desktop.Services;

public sealed class WindowsOpenFileDialogService : IOpenFileDialogService
{
    public string? PickCsvFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PID 采样 CSV",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFieldProfileFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PID 采样字段配置",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickFieldProfileSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 PID 采样字段配置",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
