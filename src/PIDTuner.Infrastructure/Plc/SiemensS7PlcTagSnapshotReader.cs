using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

/// <summary>
/// Siemens S7 snapshot reader. Single refreshes still return one frame, while high-frequency
/// recording opens a read session to reuse the TCP/S7 connection and batch tag reads across frames.
/// </summary>
public sealed class SiemensS7PlcTagSnapshotReader : IPlcTagSnapshotReader, IPlcTagSnapshotSessionReader
{
    public async Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await OpenSessionAsync(configuration, cancellationToken);
            return await session.ReadAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return configuration.Tags
                .Where(tag => tag.IsEnabled && tag.AccessMode != Domain.Models.TagAccessMode.WriteOnly)
                .Select(tag => Failed(tag, $"通信失败：{exception.Message}", "Siemens S7"))
                .ToArray();
        }
    }

    public async Task<IPlcTagSnapshotReadSession> OpenSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // The session captures and parses the enabled tag set once so every recorded frame has the same shape.
        var enabledTags = configuration.Tags
            .Where(tag => tag.IsEnabled && tag.AccessMode != TagAccessMode.WriteOnly)
            .Select(ParseTag)
            .ToArray();
        var client = new SiemensS7Client();

        try
        {
            await client.ConnectAsync(configuration, cancellationToken);
            return new SiemensS7PlcTagSnapshotReadSession(client, enabledTags);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private sealed class SiemensS7PlcTagSnapshotReadSession(
        SiemensS7Client client,
        IReadOnlyList<ParsedTag> enabledTags) : IPlcTagSnapshotReadSession
    {
        public IReadOnlyList<PlcReadOperationDiagnostics> LastReadDiagnostics { get; private set; } =
            Array.Empty<PlcReadOperationDiagnostics>();

        public async Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            LastReadDiagnostics = Array.Empty<PlcReadOperationDiagnostics>();
            var snapshots = new PlcTagSnapshot[enabledTags.Count];
            var readableTags = enabledTags
                .Select((tag, index) => new { Tag = tag, Index = index })
                .Where(item => item.Tag.Address is not null)
                .ToArray();

            foreach (var item in enabledTags.Select((tag, index) => new { Tag = tag, Index = index }))
            {
                if (item.Tag.ParseError is not null)
                {
                    snapshots[item.Index] = SiemensS7PlcTagSnapshotReader.Failed(
                        item.Tag.Definition,
                        $"地址解析失败：{item.Tag.ParseError}",
                        "Siemens S7");
                }
            }

            if (readableTags.Length == 0)
            {
                return snapshots;
            }

            try
            {
                // Read one contiguous byte block per DB so tags from the same DB are decoded from one PLC memory snapshot.
                var batch = await client.ReadNumericDbBlocksWithDiagnosticsAsync(
                    readableTags.Select(item => item.Tag.Address!).ToArray(),
                    cancellationToken);
                LastReadDiagnostics = batch.Operations;

                for (var index = 0; index < readableTags.Length; index++)
                {
                    var item = readableTags[index];
                    var result = batch.Results[index];
                    snapshots[item.Index] = result.Error is null
                        ? Good(item.Tag.Definition, result.Value)
                        : Failed(item.Tag.Definition, $"读取失败：{result.Error}", "Siemens S7");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                foreach (var item in readableTags)
                {
                    snapshots[item.Index] = SiemensS7PlcTagSnapshotReader.Failed(
                        item.Tag.Definition,
                        $"批量读取失败：{exception.Message}",
                        "Siemens S7");
                }
            }

            return snapshots;
        }

        public ValueTask DisposeAsync()
        {
            return client.DisposeAsync();
        }
    }

    private static ParsedTag ParseTag(TagDefinition tag)
    {
        try
        {
            return new ParsedTag(tag, S7AddressParser.Parse(tag.Address, tag.DataType), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ParsedTag(tag, null, exception.Message);
        }
    }

    private static PlcTagSnapshot Good(TagDefinition tag, double? value)
    {
        return new PlcTagSnapshot(
            tag.Id,
            tag.Name,
            tag.Address,
            value.HasValue ? Math.Round(value.Value * tag.Scale, 3) : null,
            tag.Unit,
            DateTimeOffset.Now,
            "Good",
            "Siemens S7");
    }

    private static PlcTagSnapshot Failed(TagDefinition tag, string quality, string source)
    {
        return new PlcTagSnapshot(
            tag.Id,
            tag.Name,
            tag.Address,
            null,
            tag.Unit,
            DateTimeOffset.Now,
            quality,
            source);
    }

    private sealed record ParsedTag(TagDefinition Definition, S7Address? Address, string? ParseError);
}
