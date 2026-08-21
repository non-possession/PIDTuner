using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.ViewModels;

public sealed class PlcConfigurationEditorViewModel : INotifyPropertyChanged
{
    private readonly PlcConfigurationWorkflow? _workflow;
    private PlcProjectConfiguration _configuration;
    private string _configurationName = string.Empty;
    private string _protocol = string.Empty;
    private string _ipAddress = string.Empty;
    private int _rack;
    private int _slot;
    private int _timeoutMilliseconds;
    private int _defaultSamplingMilliseconds;
    private int _minimumSamplingMilliseconds;
    private string _status = "PLC 配置尚未保存。";
    private string _communicationStatus = "尚未检查 PLC 通信。";
    private ObservableCollection<TagDefinitionViewModel> _tagDefinitions = [];
    private TagDefinitionViewModel? _selectedTagDefinition;

    public PlcConfigurationEditorViewModel(
        PlcProjectConfiguration configuration,
        PlcConfigurationWorkflow? workflow = null)
    {
        _workflow = workflow;
        _configuration = configuration;
        ApplyConfiguration(configuration, resetStatus: false);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ConfigurationName
    {
        get => _configurationName;
        set => SetProperty(ref _configurationName, value);
    }

    public string Protocol
    {
        get => _protocol;
        set => SetProperty(ref _protocol, value);
    }

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public int Rack
    {
        get => _rack;
        set => SetProperty(ref _rack, value);
    }

    public int Slot
    {
        get => _slot;
        set => SetProperty(ref _slot, value);
    }

    public int TimeoutMilliseconds
    {
        get => _timeoutMilliseconds;
        set => SetProperty(ref _timeoutMilliseconds, value);
    }

    public int DefaultSamplingMilliseconds
    {
        get => _defaultSamplingMilliseconds;
        set => SetProperty(ref _defaultSamplingMilliseconds, value);
    }

    public int MinimumSamplingMilliseconds
    {
        get => _minimumSamplingMilliseconds;
        set => SetProperty(ref _minimumSamplingMilliseconds, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string CommunicationStatus
    {
        get => _communicationStatus;
        set => SetProperty(ref _communicationStatus, value);
    }

    public ObservableCollection<TagDefinitionViewModel> TagDefinitions
    {
        get => _tagDefinitions;
        private set => SetProperty(ref _tagDefinitions, value);
    }

    public TagDefinitionViewModel? SelectedTagDefinition
    {
        get => _selectedTagDefinition;
        set => SetProperty(ref _selectedTagDefinition, value);
    }

    public PlcProjectConfiguration CurrentConfiguration => _configuration;

    public int TagCount => TagDefinitions.Count;

    public async Task<string> LoadFromFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var workflow = RequireWorkflow();
        ApplyConfiguration(await workflow.LoadAsync(fileName, cancellationToken));
        return Path.GetFileName(fileName);
    }

    public async Task<string> SaveToFileAsync(string fileName, CancellationToken cancellationToken)
    {
        var workflow = RequireWorkflow();
        var savedPath = await workflow.SaveAsync(BuildConfiguration(), fileName, cancellationToken);
        MarkSaved();
        return savedPath;
    }

    public async Task<PlcCommunicationCheckResult> CheckCommunicationAsync(CancellationToken cancellationToken)
    {
        var result = await RequireWorkflow().CheckCommunicationAsync(BuildConfiguration(), cancellationToken);
        CommunicationStatus = result.PendingStatus;
        CommunicationStatus = result.Status;
        return result;
    }

    public void ApplyConfiguration(PlcProjectConfiguration configuration, bool resetStatus = true)
    {
        _configuration = configuration;
        ConfigurationName = configuration.Name;
        Protocol = configuration.Protocol;
        IpAddress = configuration.IpAddress;
        Rack = configuration.Rack;
        Slot = configuration.Slot;
        TimeoutMilliseconds = configuration.TimeoutMilliseconds;
        DefaultSamplingMilliseconds = configuration.DefaultSamplingMilliseconds;
        MinimumSamplingMilliseconds = ResolveMinimumSamplingMilliseconds(configuration);
        TagDefinitions = new ObservableCollection<TagDefinitionViewModel>(
            configuration.Tags.Select(tag => new TagDefinitionViewModel(tag)));
        SelectedTagDefinition = null;

        if (resetStatus)
        {
            Status = $"已加载 {TagDefinitions.Count} 个点位。";
        }
    }

    public PlcProjectConfiguration BuildConfiguration()
    {
        var tags = TagDefinitions.Select(tag => tag.ToDefinition()).ToArray();
        var duplicateName = tags
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"点位名称重复：{duplicateName.Key}");
        }

        _configuration = new PlcProjectConfiguration(
            1,
            ConfigurationName.Trim(),
            Protocol.Trim(),
            IpAddress.Trim(),
            Rack,
            Slot,
            TimeoutMilliseconds,
            DefaultSamplingMilliseconds,
            MinimumSamplingMilliseconds,
            tags);
        return _configuration;
    }

    public TagDefinitionViewModel AddTag()
    {
        var tag = TagDefinitionViewModel.CreateNew(TagDefinitions.Count + 1, DefaultSamplingMilliseconds);
        TagDefinitions.Add(tag);
        SelectedTagDefinition = tag;
        Status = "已新增点位，请保存 PLC 配置。";
        OnPropertyChanged(nameof(TagCount));
        return tag;
    }

    public bool RemoveSelectedTag()
    {
        if (SelectedTagDefinition is null)
        {
            return false;
        }

        TagDefinitions.Remove(SelectedTagDefinition);
        SelectedTagDefinition = null;
        Status = "已删除点位，请保存 PLC 配置。";
        OnPropertyChanged(nameof(TagCount));
        return true;
    }

    public void MarkSaved()
    {
        Status = $"已保存 {TagDefinitions.Count} 个点位。";
    }

    private static int ResolveMinimumSamplingMilliseconds(PlcProjectConfiguration configuration)
    {
        return configuration.MinimumSamplingMilliseconds > 0
            ? configuration.MinimumSamplingMilliseconds
            : PlcProjectConfiguration.DefaultMinimumSamplingMilliseconds;
    }

    private PlcConfigurationWorkflow RequireWorkflow() =>
        _workflow ?? throw new InvalidOperationException("PLC configuration workflow is not configured.");

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
