namespace PIDTuner.Desktop.Services;

public interface IOpenFileDialogService
{
    string? PickCsvFile();

    string? PickFieldProfileFile();

    string? PickFieldProfileSaveFile();

    string? PickAnalysisResultSaveFile();

    string? PickHistorySamplesSaveFile();

    string? PickVisiblePlcTrendSaveFile();

    string? PickPlcProjectConfigurationFile();

    string? PickPlcProjectConfigurationSaveFile();

    string? PickPlcRecordingFile();
}
