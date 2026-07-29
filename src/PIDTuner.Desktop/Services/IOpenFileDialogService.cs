namespace PIDTuner.Desktop.Services;

public interface IOpenFileDialogService
{
    string? PickCsvFile();

    string? PickFieldProfileFile();

    string? PickFieldProfileSaveFile();
}
