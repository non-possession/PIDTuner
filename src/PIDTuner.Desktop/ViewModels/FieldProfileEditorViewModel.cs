using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.ViewModels;

public sealed class FieldProfileEditorViewModel : INotifyPropertyChanged
{
    private PidSampleFieldProfile _profile = PidSampleFieldProfile.CreateDefault();
    private string _currentProfile = "default-pid-sample-fields (10 字段)";
    private ObservableCollection<PidSampleFieldDefinitionViewModel> _fieldDefinitions = [];
    private PidSampleFieldDefinitionViewModel? _selectedFieldDefinition;

    public FieldProfileEditorViewModel()
    {
        RefreshFieldDefinitions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<string> AvailableDataTypes { get; } =
        Enum.GetNames<PidSampleFieldDataType>();

    public IReadOnlyList<string> AvailableRoles { get; } =
        Enum.GetNames<PidSampleFieldRole>();

    public PidSampleFieldProfile Profile
    {
        get => _profile;
        private set
        {
            if (SetProperty(ref _profile, value))
            {
                CurrentProfile = FormatProfile(value);
                RefreshFieldDefinitions();
            }
        }
    }

    public string CurrentProfile
    {
        get => _currentProfile;
        private set => SetProperty(ref _currentProfile, value);
    }

    public ObservableCollection<PidSampleFieldDefinitionViewModel> FieldDefinitions
    {
        get => _fieldDefinitions;
        private set => SetProperty(ref _fieldDefinitions, value);
    }

    public PidSampleFieldDefinitionViewModel? SelectedFieldDefinition
    {
        get => _selectedFieldDefinition;
        set => SetProperty(ref _selectedFieldDefinition, value);
    }

    public void LoadProfile(PidSampleFieldProfile profile)
    {
        Profile = profile;
    }

    public void AddField()
    {
        var index = FieldDefinitions.Count + 1;
        while (FieldDefinitions.Any(field => string.Equals(field.Key, $"metadata_{index}", StringComparison.OrdinalIgnoreCase)))
        {
            index++;
        }

        var field = PidSampleFieldDefinitionViewModel.CreateNew(index);
        FieldDefinitions.Add(field);
        SelectedFieldDefinition = field;
    }

    public bool RemoveSelectedField()
    {
        if (SelectedFieldDefinition is null)
        {
            return false;
        }

        FieldDefinitions.Remove(SelectedFieldDefinition);
        SelectedFieldDefinition = null;
        return true;
    }

    public PidSampleFieldProfile BuildProfileFromGrid()
    {
        var fields = FieldDefinitions.Select(field => field.ToDefinition()).ToArray();
        var duplicateKey = fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;

        if (!string.IsNullOrWhiteSpace(duplicateKey))
        {
            throw new InvalidOperationException($"字段 Key 重复：{duplicateKey}");
        }

        Profile = Profile with { Fields = fields };
        return Profile;
    }

    private void RefreshFieldDefinitions()
    {
        FieldDefinitions = new ObservableCollection<PidSampleFieldDefinitionViewModel>(
            Profile.Fields.Select(field => new PidSampleFieldDefinitionViewModel(field)));
    }

    private static string FormatProfile(PidSampleFieldProfile profile)
    {
        return $"{profile.ProfileName} ({profile.Fields.Count} 字段)";
    }

    private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
