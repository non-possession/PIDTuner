using System.Windows.Threading;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Domain.Configuration;

namespace PIDTuner.Desktop.Services;

public sealed class PlcDiagnosticsController
{
    private readonly PlcDebugViewModel _debug;
    private readonly Action<PlcDiagnosticsOperationResult> _resultHandler;
    private readonly DispatcherTimer _expirationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    public PlcDiagnosticsController(
        PlcDebugViewModel debug,
        Action<PlcDiagnosticsOperationResult> resultHandler)
    {
        _debug = debug;
        _resultHandler = resultHandler;
        _expirationTimer.Tick += async (_, _) =>
            await ApplyAsync(_debug.StopExpiredDiagnosticsAsync(CancellationToken.None));
    }

    public Task StartAsync(
        PlcProjectConfiguration configuration,
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        ApplyAsync(_debug.StartDiagnosticsAsync(configuration, duration, cancellationToken));

    public Task StopAsync(string reason, CancellationToken cancellationToken) =>
        ApplyAsync(_debug.StopDiagnosticsAsync(reason, cancellationToken));

    private async Task ApplyAsync(Task<PlcDiagnosticsOperationResult> operation)
    {
        var result = await operation;
        if (result.ShouldKeepTimerRunning)
        {
            _expirationTimer.Start();
        }
        else
        {
            _expirationTimer.Stop();
        }

        _resultHandler(result);
    }
}
