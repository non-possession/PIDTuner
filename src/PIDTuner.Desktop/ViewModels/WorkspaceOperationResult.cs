namespace PIDTuner.Desktop.ViewModels;

public interface IWorkspaceOperationResult
{
    string Title { get; }

    string Message { get; }

    string Kind { get; }
}

public sealed record WorkspaceOperationResult(string Title, string Message, string Kind) : IWorkspaceOperationResult
{
    public static WorkspaceOperationResult Success(string title, string message) =>
        new(title, message, "Success");

    public static WorkspaceOperationResult Info(string title, string message) =>
        new(title, message, "Info");

    public static WorkspaceOperationResult Warning(string title, string message) =>
        new(title, message, "Warning");

    public static WorkspaceOperationResult Error(string title, string message) =>
        new(title, message, "Error");
}
