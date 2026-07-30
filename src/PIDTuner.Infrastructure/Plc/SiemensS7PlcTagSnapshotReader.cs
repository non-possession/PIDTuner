using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

/// <summary>
/// Siemens S7 snapshot reader. Single refreshes still return one frame, while high-frequency
/// recording opens a read session to reuse the TCP/S7 connection across frames.
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
        // The session captures the enabled tag set once so every recorded frame has the same shape.
        var enabledTags = configuration.Tags
            .Where(tag => tag.IsEnabled && tag.AccessMode != Domain.Models.TagAccessMode.WriteOnly)
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
        IReadOnlyList<Domain.Models.TagDefinition> enabledTags) : IPlcTagSnapshotReadSession
    {
        public async Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            var snapshots = new List<PlcTagSnapshot>();

            // The connection is reused; only tag read PDUs are sent for each frame.
            foreach (var tag in enabledTags)
            {
                try
                {
                    var address = S7AddressParser.Parse(tag.Address, tag.DataType);
                    var value = await client.ReadNumericAsync(address, cancellationToken);
                    snapshots.Add(new PlcTagSnapshot(
                        tag.Id,
                        tag.Name,
                        tag.Address,
                        value.HasValue ? Math.Round(value.Value * tag.Scale, 3) : null,
                        tag.Unit,
                        DateTimeOffset.Now,
                        "Good",
                        "Siemens S7"));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    snapshots.Add(SiemensS7PlcTagSnapshotReader.Failed(tag, $"读取失败：{exception.Message}", "Siemens S7"));
                }
            }

            return snapshots;
        }

        public ValueTask DisposeAsync()
        {
            return client.DisposeAsync();
        }
    }

    private static PlcTagSnapshot Failed(Domain.Models.TagDefinition tag, string quality, string source)
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
}
