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

    public string? PickAnalysisResultSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 PID 分析结果",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickHistorySamplesSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出历史采样数据",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickVisiblePlcTrendSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出当前可见 PLC 趋势数据",
            Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
            DefaultExt = ".csv",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPlcProjectConfigurationFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PLC 项目配置",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPlcProjectConfigurationSaveFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 PLC 项目配置",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            DefaultExt = ".json",
            AddExtension = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPlcRecordingFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 PLC 记录文件",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
