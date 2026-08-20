using System.Windows.Threading;
using PIDTuner.Desktop.ViewModels;

namespace PIDTuner.Desktop.Services;

public sealed class PlcReplayController
{
    private readonly PlcDebugViewModel _debug;
    private readonly Action<PlcReplayOperationResult> _resultHandler;
    private readonly DispatcherTimer _timer = new();

    public PlcReplayController(
        PlcDebugViewModel debug,
        Action<PlcReplayOperationResult> resultHandler)
    {
        _debug = debug;
        _resultHandler = resultHandler;
        _timer.Tick += (_, _) => ApplyNextFrame();
    }

    public void Toggle()
    {
        if (_debug.IsReplayRunning)
        {
            _timer.Stop();
            _resultHandler(_debug.PauseReplay());
            return;
        }

        _resultHandler(_debug.StartReplay());
        SynchronizeTimer();
    }

    public void StepBackward()
    {
        _timer.Stop();
        _resultHandler(_debug.StepBackward());
    }

    public void StepForward()
    {
        _timer.Stop();
        _resultHandler(_debug.StepForward());
    }

    public void SetSpeed(double speedMultiplier)
    {
        _debug.SetReplaySpeed(speedMultiplier);
        if (_debug.IsReplayRunning)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_debug.EffectiveReplayIntervalMilliseconds);
        }
    }

    public void Stop()
    {
        _timer.Stop();
        _debug.StopReplay();
    }

    private void ApplyNextFrame()
    {
        _resultHandler(_debug.ApplyNextReplayFrame());
        if (!_debug.IsReplayRunning)
        {
            _timer.Stop();
        }
    }

    private void SynchronizeTimer()
    {
        if (!_debug.IsReplayRunning)
        {
            _timer.Stop();
            return;
        }

        _timer.Interval = TimeSpan.FromMilliseconds(_debug.EffectiveReplayIntervalMilliseconds);
        _timer.Start();
    }
}
