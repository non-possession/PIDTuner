using System.IO;
using PIDTuner.Application.Interfaces;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Persistence;
using PIDTuner.Infrastructure.Plc;

namespace PIDTuner.Desktop.Services;

internal static class MainWindowComposition
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string ResolvePath(params string[] segments) =>
        Path.Combine(new[] { RepositoryRoot }.Concat(segments).ToArray());

    public static IPidSampleFieldProfileStore CreateFieldProfileStore() =>
        new JsonPidSampleFieldProfileStore();

    public static IPlcProjectConfigurationStore CreatePlcConfigurationStore() =>
        new JsonPlcProjectConfigurationStore();

    public static IPlcConnectivityProbe CreatePlcConnectivityProbe() =>
        new ConfiguredPlcConnectivityProbe(
            new SiemensS7ConnectivityProbe(),
            new PingPlcConnectivityProbe());

    public static IPlcTagSnapshotReader CreatePlcTagSnapshotReader() =>
        new ConfiguredPlcTagSnapshotReader(
            new SiemensS7PlcTagSnapshotReader(),
            new PreviewPlcTagSnapshotReader());

    public static ITestSessionRepository CreateTestSessionRepository(string directory) =>
        new JsonTestSessionRepository(directory);

    public static IPidSampleRepository CreatePidSampleRepository(string directory) =>
        new JsonPidSampleRepository(directory);

    public static IPidRecommendationReviewRepository CreateRecommendationReviewRepository(string directory) =>
        new JsonPidRecommendationReviewRepository(directory);

    public static IPidParameterSetRepository CreateParameterSetRepository(string directory) =>
        new JsonPidParameterSetRepository(directory);

    public static IPlcLiveDiagnosticsStore CreateLiveDiagnosticsStore(string databasePath) =>
        new SqlitePlcLiveDiagnosticsStore(databasePath);

    public static IPlcHistoricalTrendStore CreateHistoricalTrendStore(string databasePath) =>
        new SqlitePlcHistoricalTrendStore(databasePath);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var configPath = Path.Combine(directory.FullName, "config", "pid-sample-fields.example.json");
            var samplePath = Path.Combine(directory.FullName, "samples", "offline-step-response.csv");
            if (File.Exists(configPath) && File.Exists(samplePath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }
}
