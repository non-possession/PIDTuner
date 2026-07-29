using PIDTuner.Application.Interfaces;
using PIDTuner.Domain.Configuration;
using PIDTuner.Domain.Plc;

namespace PIDTuner.Infrastructure.Plc;

public sealed class SiemensS7PlcTagSnapshotReader : IPlcTagSnapshotReader
{
    public async Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var enabledTags = configuration.Tags
            .Where(tag => tag.IsEnabled && tag.AccessMode != Domain.Models.TagAccessMode.WriteOnly)
            .ToArray();
        var now = DateTimeOffset.Now;

        try
        {
            await using var client = new SiemensS7Client();
            await client.ConnectAsync(configuration, cancellationToken);
            var snapshots = new List<PlcTagSnapshot>();

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
                    snapshots.Add(Failed(tag, $"读取失败：{exception.Message}", "Siemens S7"));
                }
            }

            return snapshots;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return enabledTags
                .Select(tag => Failed(tag, $"通信失败：{exception.Message}", "Siemens S7"))
                .ToArray();
        }

        PlcTagSnapshot Failed(Domain.Models.TagDefinition tag, string quality, string source)
        {
            return new PlcTagSnapshot(
                tag.Id,
                tag.Name,
                tag.Address,
                null,
                tag.Unit,
                now,
                quality,
                source);
        }
    }
}
