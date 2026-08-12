using System.IO;
using PIDTuner.Domain.Configuration;
using PIDTuner.Application.Interfaces;

namespace PIDTuner.Desktop.ViewModels;

public sealed class FieldProfileWorkflow(IPidSampleFieldProfileStore fieldProfileStore)
{
    public async Task<PidSampleFieldProfile> LoadAsync(
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fileName);
        return await fieldProfileStore.LoadAsync(stream, cancellationToken);
    }

    public async Task SaveAsync(
        PidSampleFieldProfile profile,
        string fileName,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(fileName);
        await fieldProfileStore.SaveAsync(profile, stream, cancellationToken);
    }
}
