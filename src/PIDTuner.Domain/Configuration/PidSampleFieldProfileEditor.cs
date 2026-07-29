namespace PIDTuner.Domain.Configuration;

public sealed class PidSampleFieldProfileEditor
{
    private readonly PidSampleFieldProfile _source;
    private readonly List<PidSampleFieldDefinition> _fields;

    public PidSampleFieldProfileEditor(PidSampleFieldProfile source)
    {
        _source = source;
        _fields = source.Fields.ToList();
    }

    public PidSampleFieldProfileEditor Add(PidSampleFieldDefinition field)
    {
        EnsureUniqueKey(field.Key);
        _fields.Add(field);
        return this;
    }

    public PidSampleFieldProfileEditor Update(PidSampleFieldDefinition field)
    {
        var index = _fields.FindIndex(existing => SameKey(existing.Key, field.Key));
        if (index < 0)
        {
            throw new InvalidOperationException($"PID sample field '{field.Key}' does not exist.");
        }

        _fields[index] = field;
        return this;
    }

    public PidSampleFieldProfileEditor Remove(string key)
    {
        var removed = _fields.RemoveAll(field => SameKey(field.Key, key));
        if (removed == 0)
        {
            throw new InvalidOperationException($"PID sample field '{key}' does not exist.");
        }

        return this;
    }

    public PidSampleFieldProfile ToProfile()
    {
        return _source with { Fields = _fields.ToArray() };
    }

    private void EnsureUniqueKey(string key)
    {
        if (_fields.Any(field => SameKey(field.Key, key)))
        {
            throw new InvalidOperationException($"PID sample field '{key}' already exists.");
        }
    }

    private static bool SameKey(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
