using PIDTuner.Domain.Plc;

namespace PIDTuner.Desktop.Services;

public sealed class PlcSampleBuffer
{
    private readonly object _gate = new();
    private readonly Queue<PlcAcquisitionFrame> _frames = new();
    private readonly int _capacity;

    public PlcSampleBuffer(int capacity = 10_000)
    {
        _capacity = Math.Max(1, capacity);
    }

    public void Add(PlcAcquisitionFrame frame)
    {
        lock (_gate)
        {
            _frames.Enqueue(frame);
            while (_frames.Count > _capacity)
            {
                _frames.Dequeue();
            }
        }
    }

    public IReadOnlyList<PlcAcquisitionFrame> Drain()
    {
        lock (_gate)
        {
            if (_frames.Count == 0)
            {
                return Array.Empty<PlcAcquisitionFrame>();
            }

            var drained = _frames.ToArray();
            _frames.Clear();
            return drained;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _frames.Clear();
        }
    }
}
