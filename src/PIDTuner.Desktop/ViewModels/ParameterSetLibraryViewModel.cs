using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Domain.Models;

namespace PIDTuner.Desktop.ViewModels;

public sealed class ParameterSetLibraryViewModel(
    IPidParameterSetRepository parameterSetRepository,
    PidParameterSetExtractor parameterSetExtractor) : INotifyPropertyChanged
{
    private ObservableCollection<PidParameterSetViewModel> _parameterSets = [];
    private string _status = "尚未保存参数方案。";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<PidParameterSetViewModel> ParameterSets
    {
        get => _parameterSets;
        private set => SetProperty(ref _parameterSets, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public async Task<ParameterSetSaveResult> SaveAsync(
        IReadOnlyList<PidSample> samples,
        Guid? testSessionId,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return ParameterSetSaveResult.Warning("无法保存参数方案", "请先导入 CSV、载入示例或打开历史记录。");
        }

        var sourceName = string.IsNullOrWhiteSpace(sourceFileName)
            ? "current-analysis"
            : Path.GetFileNameWithoutExtension(sourceFileName);
        var parameterSet = parameterSetExtractor.Extract(
            samples,
            testSessionId,
            sourceName,
            $"Captured from {sourceName}");

        if (parameterSet is null)
        {
            return ParameterSetSaveResult.Warning("无法保存参数方案", "当前样本没有 Kp、Ki/Ti 或 Kd/Td 参数值。");
        }

        await parameterSetRepository.SaveAsync(parameterSet, cancellationToken);
        await LoadAsync(cancellationToken);

        return ParameterSetSaveResult.Success(
            "参数方案已保存",
            $"{parameterSet.Name}: Kp={FormatParameterValue(parameterSet.Kp)}, Ki/Ti={FormatParameterValue(parameterSet.KiOrTi)}, Kd/Td={FormatParameterValue(parameterSet.KdOrTd)}");
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var parameterSets = await parameterSetRepository.ListAsync(cancellationToken);
        ParameterSets = new ObservableCollection<PidParameterSetViewModel>(
            parameterSets
                .OrderByDescending(item => item.CapturedAt)
                .Select(item => new PidParameterSetViewModel(item)));
        Status = ParameterSets.Count == 0
            ? "尚无参数方案记录。"
            : $"已加载 {ParameterSets.Count} 条参数方案。";
    }

    public void MarkLoadFailed()
    {
        Status = "参数方案加载失败。";
    }

    private static string FormatParameterValue(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record ParameterSetSaveResult(string Title, string Message, string Kind)
{
    public static ParameterSetSaveResult Success(string title, string message)
    {
        return new ParameterSetSaveResult(title, message, "Success");
    }

    public static ParameterSetSaveResult Warning(string title, string message)
    {
        return new ParameterSetSaveResult(title, message, "Warning");
    }
}
