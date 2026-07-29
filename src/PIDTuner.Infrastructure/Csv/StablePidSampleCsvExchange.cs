using System.Globalization;
using System.Text;
using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Models;

namespace PIDTuner.Infrastructure.Csv;

public sealed class StablePidSampleCsvExchange : ICsvSampleExchange
{
    private const string Header =
        "timestamp,sp,pv,mv,kp,ki_or_ti,kd_or_td,is_plc_connected,test_session_id,parameter_set_id";

    public async Task<IReadOnlyList<PidSample>> ImportAsync(Stream csvStream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csvStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var header = await reader.ReadLineAsync(cancellationToken);

        if (!string.Equals(header, Header, StringComparison.Ordinal))
        {
            throw new FormatException($"Unexpected PID sample CSV header. Expected '{Header}'.");
        }

        var samples = new List<PidSample>();

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            samples.Add(ParseSample(line));
        }

        return samples;
    }

    public async Task ExportAsync(
        IReadOnlyList<PidSample> samples,
        Stream destination,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
        await writer.WriteLineAsync(Header.AsMemory(), cancellationToken);

        foreach (var sample in samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(FormatSample(sample).AsMemory(), cancellationToken);
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static PidSample ParseSample(string line)
    {
        var fields = line.Split(',');

        if (fields.Length != 10)
        {
            throw new FormatException($"Expected 10 CSV fields, got {fields.Length}.");
        }

        return new PidSample(
            DateTimeOffset.Parse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ParseNullableDouble(fields[1]),
            ParseNullableDouble(fields[2]),
            ParseNullableDouble(fields[3]),
            ParseNullableDouble(fields[4]),
            ParseNullableDouble(fields[5]),
            ParseNullableDouble(fields[6]),
            bool.Parse(fields[7]),
            Guid.Parse(fields[8]),
            ParseNullableGuid(fields[9]));
    }

    private static string FormatSample(PidSample sample)
    {
        return string.Join(
            ',',
            sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            FormatNullableDouble(sample.SetPoint),
            FormatNullableDouble(sample.ProcessValue),
            FormatNullableDouble(sample.ManipulatedValue),
            FormatNullableDouble(sample.Kp),
            FormatNullableDouble(sample.KiOrTi),
            FormatNullableDouble(sample.KdOrTd),
            sample.IsPlcConnected.ToString(CultureInfo.InvariantCulture),
            sample.TestSessionId.ToString("D"),
            sample.ParameterSetId?.ToString("D") ?? string.Empty);
    }

    private static double? ParseNullableDouble(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : double.Parse(value, CultureInfo.InvariantCulture);
    }

    private static Guid? ParseNullableGuid(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Guid.Parse(value);
    }

    private static string FormatNullableDouble(double? value)
    {
        return value?.ToString("G", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
