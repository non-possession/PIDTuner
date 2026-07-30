using System.Globalization;
using System.Text;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Csv;

/// <summary>
/// CSV adapter driven by a field profile. The profile lets each PID project rename or add
/// columns without changing the domain model or the analysis use case.
/// </summary>
public sealed class ConfigurablePidSampleCsvExchange(PidSampleFieldProfile fieldProfile) : ICsvSampleExchange
{
    public async Task<IReadOnlyList<PidSample>> ImportAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        // Accept UTF-8 with or without BOM; exports intentionally include BOM for spreadsheet compatibility.
        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            throw new FormatException("CSV header is required.");
        }

        var headers = SplitCsvLine(headerLine);
        // Header lookup is case-insensitive so plant CSV files can vary casing without a new parser.
        var headerIndexes = headers
            .Select((header, index) => new { Header = header, Index = index })
            .ToDictionary(item => item.Header, item => item.Index, StringComparer.OrdinalIgnoreCase);

        ValidateRequiredFields(headerIndexes);

        var samples = new List<PidSample>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            samples.Add(ParseSample(SplitCsvLine(line), headerIndexes));
        }

        return samples;
    }

    public async Task ExportAsync(
        IReadOnlyList<PidSample> samples,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync(string.Join(',', fieldProfile.Fields.Select(field => field.Key)).AsMemory(), cancellationToken);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var values = fieldProfile.Fields.Select(field => FormatField(sample, field));
            await writer.WriteLineAsync(string.Join(',', values).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private void ValidateRequiredFields(IReadOnlyDictionary<string, int> headerIndexes)
    {
        var missing = fieldProfile.Fields
            .Where(field => field.Required)
            .Where(field => !headerIndexes.ContainsKey(field.Key))
            .Select(field => field.Key)
            .ToArray();

        if (missing.Length > 0)
        {
            throw new FormatException($"CSV is missing required PID sample fields: {string.Join(", ", missing)}.");
        }
    }

    private PidSample ParseSample(IReadOnlyList<string> fields, IReadOnlyDictionary<string, int> headerIndexes)
    {
        var extraFields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        // Metadata and PID parameter columns are preserved even when they are not core SP/PV/MV fields.
        foreach (var field in fieldProfile.Fields.Where(field => field.Role is PidSampleFieldRole.Metadata or PidSampleFieldRole.PidParameter))
        {
            extraFields[field.Key] = GetField(fields, headerIndexes, field.Key);
        }

        return new PidSample(
            ParseDateTimeOffset(GetField(fields, headerIndexes, PidSampleFieldRole.SampleTime)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.SetPoint)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.ProcessValue)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.ManipulatedValue)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.Kp)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.KiOrTi)),
            ParseNullableDouble(GetField(fields, headerIndexes, PidSampleFieldRole.KdOrTd)),
            ParseNullableBoolean(GetField(fields, headerIndexes, PidSampleFieldRole.ConnectionState)) ?? false,
            ParseNullableGuid(GetField(fields, headerIndexes, PidSampleFieldRole.TestSession)) ?? Guid.Empty,
            ParseNullableGuid(GetField(fields, headerIndexes, PidSampleFieldRole.ParameterSet)),
            extraFields);
    }

    private string? GetField(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> headerIndexes,
        PidSampleFieldRole role)
    {
        var definition = fieldProfile.Fields.FirstOrDefault(field => field.Role == role);
        return definition is null ? null : GetField(fields, headerIndexes, definition.Key);
    }

    private static string? GetField(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> headerIndexes,
        string key)
    {
        return headerIndexes.TryGetValue(key, out var index) && index < fields.Count
            ? EmptyToNull(fields[index])
            : null;
    }

    private static IReadOnlyList<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (current == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    // CSV escapes a literal quote as two adjacent quote characters.
                    field.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (current == ',' && !inQuotes)
            {
                fields.Add(field.ToString());
                field.Clear();
                continue;
            }

            field.Append(current);
        }

        if (inQuotes)
        {
            throw new FormatException("CSV line contains an unterminated quoted field.");
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value)
    {
        return DateTimeOffset.Parse(Require(value, "timestamp"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static double? ParseNullableDouble(string? value)
    {
        return value is null ? null : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static bool? ParseNullableBoolean(string? value)
    {
        return value is null ? null : bool.Parse(value);
    }

    private static Guid? ParseNullableGuid(string? value)
    {
        return value is null ? null : Guid.Parse(value);
    }

    private static string FormatField(PidSample sample, PidSampleFieldDefinition field)
    {
        return field.Role switch
        {
            PidSampleFieldRole.SampleTime => sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            PidSampleFieldRole.SetPoint => FormatNullableDouble(sample.SetPoint),
            PidSampleFieldRole.ProcessValue => FormatNullableDouble(sample.ProcessValue),
            PidSampleFieldRole.ManipulatedValue => FormatNullableDouble(sample.ManipulatedValue),
            PidSampleFieldRole.Kp => FormatNullableDouble(sample.Kp),
            PidSampleFieldRole.KiOrTi => FormatNullableDouble(sample.KiOrTi),
            PidSampleFieldRole.KdOrTd => FormatNullableDouble(sample.KdOrTd),
            PidSampleFieldRole.ConnectionState => sample.IsPlcConnected.ToString(CultureInfo.InvariantCulture),
            PidSampleFieldRole.TestSession => sample.TestSessionId == Guid.Empty ? string.Empty : sample.TestSessionId.ToString("D"),
            PidSampleFieldRole.ParameterSet => sample.ParameterSetId?.ToString("D") ?? string.Empty,
            _ => sample.ExtraFields?.GetValueOrDefault(field.Key) ?? string.Empty
        };
    }

    private static string FormatNullableDouble(double? value)
    {
        return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string Require(string? value, string fieldName)
    {
        return value ?? throw new FormatException($"CSV field '{fieldName}' is required.");
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
