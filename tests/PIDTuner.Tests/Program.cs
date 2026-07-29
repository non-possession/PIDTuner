using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using PIDTuner.Domain.Analysis;
using PIDTuner.Application.Services;
using PIDTuner.Domain.Trends;
using PIDTuner.Domain.Models;
using PIDTuner.Application.UseCases;
using PIDTuner.Domain.Configuration;
using PIDTuner.Desktop.Services;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Persistence;

var tests = new (string Name, Func<Task> Run)[]
{
    ("analysis calculates core response metrics from an offline step response", AnalysisCalculatesCoreMetrics),
    ("csv exchange imports and exports stable pid sample fields", CsvExchangeRoundTripsSamples),
    ("offline csv use case imports samples and analyzes the requested window", OfflineCsvUseCaseAnalyzesRequestedWindow),
    ("field profile store loads project metadata from json", FieldProfileStoreLoadsProjectMetadata),
    ("configurable csv exchange maps renamed fields and preserves extra metadata", ConfigurableCsvExchangeMapsRenamedFields),
    ("configurable csv exchange imports quoted metadata with commas", ConfigurableCsvExchangeImportsQuotedMetadataWithCommas),
    ("trend series builder normalizes SP PV and MV for plotting", TrendSeriesBuilderNormalizesPidSamples),
    ("field profile editor adds updates and removes fields", FieldProfileEditorAddsUpdatesAndRemovesFields),
    ("analysis window parser returns optional validated windows", AnalysisWindowParserReturnsOptionalValidatedWindows),
    ("response assessment flags high overshoot and steady-state error", ResponseAssessmentFlagsHighOvershootAndSteadyStateError),
    ("analysis result csv exporter writes stable fields", AnalysisResultCsvExporterWritesStableFields),
    ("json test session repository saves and lists sessions", JsonTestSessionRepositorySavesAndListsSessions),
    ("json pid sample repository saves and loads samples by session", JsonPidSampleRepositorySavesAndLoadsSamplesBySession),
    ("main view model shows notifications and refreshes history", MainViewModelShowsNotificationsAndRefreshesHistory)
};

var failures = new List<string>();

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(exception);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures)
    {
        Console.WriteLine(failure);
    }

    return 1;
}

return 0;

static Task AnalysisCalculatesCoreMetrics()
{
    var sessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    var start = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    var samples = new[]
    {
        Sample(start.AddSeconds(0), 100, 0, 0, sessionId),
        Sample(start.AddSeconds(1), 100, 40, 20, sessionId),
        Sample(start.AddSeconds(2), 100, 90, 40, sessionId),
        Sample(start.AddSeconds(3), 100, 112, 55, sessionId),
        Sample(start.AddSeconds(4), 100, 103, 48, sessionId),
        Sample(start.AddSeconds(5), 100, 101, 45, sessionId),
        Sample(start.AddSeconds(6), 100, 100.5, 44, sessionId)
    };

    var service = new BasicPidAnalysisService();
    var metrics = service.Analyze(samples, new AnalysisWindow(start, start.AddSeconds(6)));

    AssertClose(12, metrics.OvershootPercent, 0.001, "overshoot percent");
    AssertEqual(TimeSpan.FromSeconds(2), metrics.RiseTime, "rise time");
    AssertEqual(TimeSpan.FromSeconds(5), metrics.SettlingTime, "settling time");
    AssertClose(0.5, metrics.SteadyStateError, 0.001, "steady-state error");

    return Task.CompletedTask;
}

static async Task CsvExchangeRoundTripsSamples()
{
    const string csv = """
timestamp,sp,pv,mv,kp,ki_or_ti,kd_or_td,is_plc_connected,test_session_id,parameter_set_id
2026-07-29T10:00:00.0000000+00:00,100,98.5,41,2.5,0.8,0.05,false,bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb,
2026-07-29T10:00:01.0000000+00:00,100,99.5,40,2.5,0.8,0.05,true,bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb,cccccccc-cccc-cccc-cccc-cccccccccccc
""";

    var exchange = new StablePidSampleCsvExchange();
    await using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var imported = await exchange.ImportAsync(input, CancellationToken.None);

    AssertEqual(2, imported.Count, "imported count");
    AssertClose(98.5, imported[0].ProcessValue, 0.001, "first PV");
    AssertEqual(false, imported[0].IsPlcConnected, "first PLC state");
    AssertEqual(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), imported[1].ParameterSetId, "second parameter set");

    await using var output = new MemoryStream();
    await exchange.ExportAsync(imported, output, CancellationToken.None);

    var exportedBytes = output.ToArray();
    AssertUtf8Bom(exportedBytes, "sample CSV export encoding");
    var exported = Encoding.UTF8.GetString(exportedBytes);
    AssertContains("timestamp,sp,pv,mv,kp,ki_or_ti,kd_or_td,is_plc_connected,test_session_id,parameter_set_id", exported);
    AssertContains("2026-07-29T10:00:00.0000000+00:00,100,98.5,41,2.5,0.8,0.05,False,bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb,", exported);
}

static async Task OfflineCsvUseCaseAnalyzesRequestedWindow()
{
    const string csv = """
timestamp,sp,pv,mv,kp,ki_or_ti,kd_or_td,is_plc_connected,test_session_id,parameter_set_id
2026-07-29T10:00:00.0000000+00:00,100,0,0,1,0.1,0.01,false,dddddddd-dddd-dddd-dddd-dddddddddddd,
2026-07-29T10:00:01.0000000+00:00,100,40,20,1,0.1,0.01,false,dddddddd-dddd-dddd-dddd-dddddddddddd,
2026-07-29T10:00:02.0000000+00:00,100,90,40,1,0.1,0.01,false,dddddddd-dddd-dddd-dddd-dddddddddddd,
2026-07-29T10:00:03.0000000+00:00,100,112,55,1,0.1,0.01,false,dddddddd-dddd-dddd-dddd-dddddddddddd,
2026-07-29T10:00:04.0000000+00:00,100,101,45,1,0.1,0.01,false,dddddddd-dddd-dddd-dddd-dddddddddddd,
""";

    var useCase = new AnalyzeOfflineCsvUseCase(new StablePidSampleCsvExchange(), new BasicPidAnalysisService());
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var window = new AnalysisWindow(
        DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-29T10:00:04.0000000+00:00", CultureInfo.InvariantCulture));

    var result = await useCase.AnalyzeAsync(stream, window, CancellationToken.None);

    AssertEqual(5, result.Samples.Count, "offline sample count");
    AssertEqual(window, result.Window, "requested analysis window");
    AssertClose(12, result.Metrics.OvershootPercent, 0.001, "offline overshoot percent");
}

static async Task FieldProfileStoreLoadsProjectMetadata()
{
    const string json = """
{
  "schemaVersion": 1,
  "profileName": "temperature-loop",
  "description": "Customer site temperature PID fields",
  "fields": [
    { "key": "time", "displayName": "Time", "dataType": "DateTimeOffset", "required": true, "unit": null, "role": "SampleTime" },
    { "key": "setpoint_c", "displayName": "Setpoint C", "dataType": "Double", "required": true, "unit": "degC", "role": "SetPoint" },
    { "key": "actual_c", "displayName": "Actual C", "dataType": "Double", "required": true, "unit": "degC", "role": "ProcessValue" }
  ]
}
""";

    var store = new JsonPidSampleFieldProfileStore();
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

    var profile = await store.LoadAsync(stream, CancellationToken.None);

    AssertEqual("temperature-loop", profile.ProfileName, "profile name");
    AssertEqual(3, profile.Fields.Count, "field count");
    AssertEqual(PidSampleFieldRole.ProcessValue, profile.Fields[2].Role, "process value role");
    AssertEqual("degC", profile.Fields[2].Unit, "process value unit");
}

static async Task ConfigurableCsvExchangeMapsRenamedFields()
{
    var profile = new PidSampleFieldProfile(
        1,
        "renamed-field-profile",
        "Renamed CSV columns for a project",
        new[]
        {
            Field("time", PidSampleFieldRole.SampleTime, PidSampleFieldDataType.DateTimeOffset, true),
            Field("setpoint_c", PidSampleFieldRole.SetPoint, PidSampleFieldDataType.Double, true),
            Field("actual_c", PidSampleFieldRole.ProcessValue, PidSampleFieldDataType.Double, true),
            Field("output_pct", PidSampleFieldRole.ManipulatedValue, PidSampleFieldDataType.Double, true),
            Field("operator_note", PidSampleFieldRole.Metadata, PidSampleFieldDataType.String, false)
        });

    const string csv = """
time,setpoint_c,actual_c,output_pct,operator_note
2026-07-29T10:00:00.0000000+00:00,80,75.2,41.5,start of trial
""";

    var exchange = new ConfigurablePidSampleCsvExchange(profile);
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var imported = await exchange.ImportAsync(stream, CancellationToken.None);

    AssertEqual(1, imported.Count, "renamed imported count");
    AssertClose(80, imported[0].SetPoint, 0.001, "renamed SP");
    AssertClose(75.2, imported[0].ProcessValue, 0.001, "renamed PV");
    AssertClose(41.5, imported[0].ManipulatedValue, 0.001, "renamed MV");
    AssertEqual("start of trial", imported[0].ExtraFields?["operator_note"], "extra metadata");
}

static async Task ConfigurableCsvExchangeImportsQuotedMetadataWithCommas()
{
    var profile = new PidSampleFieldProfile(
        1,
        "quoted-metadata-profile",
        "CSV columns with quoted metadata",
        new[]
        {
            Field("time", PidSampleFieldRole.SampleTime, PidSampleFieldDataType.DateTimeOffset, true),
            Field("sp", PidSampleFieldRole.SetPoint, PidSampleFieldDataType.Double, true),
            Field("pv", PidSampleFieldRole.ProcessValue, PidSampleFieldDataType.Double, true),
            Field("mv", PidSampleFieldRole.ManipulatedValue, PidSampleFieldDataType.Double, true),
            Field("operator_note", PidSampleFieldRole.Metadata, PidSampleFieldDataType.String, false)
        });

    const string csv = """
time,sp,pv,mv,operator_note
2026-07-29T10:00:00.0000000+00:00,80,75.2,41.5,"opened valve, watched response"
""";

    var exchange = new ConfigurablePidSampleCsvExchange(profile);
    await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

    var imported = await exchange.ImportAsync(stream, CancellationToken.None);

    AssertEqual("opened valve, watched response", imported[0].ExtraFields?["operator_note"], "quoted metadata");
}

static Task TrendSeriesBuilderNormalizesPidSamples()
{
    var sessionId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
    var start = new DateTimeOffset(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);
    var samples = new[]
    {
        Sample(start, 100, 10, 0, sessionId),
        Sample(start.AddSeconds(1), 100, 55, 50, sessionId),
        Sample(start.AddSeconds(2), 100, 100, 100, sessionId)
    };

    var builder = new PidTrendSeriesBuilder();
    var trend = builder.Build(samples);

    AssertEqual(3, trend.SetPoint.Points.Count, "SP point count");
    AssertEqual(3, trend.ProcessValue.Points.Count, "PV point count");
    AssertEqual(3, trend.ManipulatedValue.Points.Count, "MV point count");
    AssertClose(0, trend.ProcessValue.Points[0].NormalizedX, 0.001, "first normalized X");
    AssertClose(0.5, trend.ProcessValue.Points[1].NormalizedX, 0.001, "middle normalized X");
    AssertClose(1, trend.ProcessValue.Points[2].NormalizedX, 0.001, "last normalized X");
    AssertClose(0.1, trend.ProcessValue.Points[0].NormalizedY, 0.001, "first normalized Y");
    AssertClose(1, trend.ProcessValue.Points[2].NormalizedY, 0.001, "last normalized Y");

    return Task.CompletedTask;
}

static Task FieldProfileEditorAddsUpdatesAndRemovesFields()
{
    var profile = new PidSampleFieldProfile(
        1,
        "editable-profile",
        "Editable field profile",
        new[]
        {
            Field("timestamp", PidSampleFieldRole.SampleTime, PidSampleFieldDataType.DateTimeOffset, true),
            Field("sp", PidSampleFieldRole.SetPoint, PidSampleFieldDataType.Double, true)
        });

    var editor = new PidSampleFieldProfileEditor(profile)
        .Add(Field("operator_note", PidSampleFieldRole.Metadata, PidSampleFieldDataType.String, false))
        .Update(new PidSampleFieldDefinition(
            "sp",
            "Setpoint C",
            PidSampleFieldDataType.Double,
            true,
            "degC",
            PidSampleFieldRole.SetPoint))
        .Remove("operator_note");

    var edited = editor.ToProfile();

    AssertEqual(2, edited.Fields.Count, "edited field count");
    AssertEqual("Setpoint C", edited.Fields.Single(field => field.Key == "sp").DisplayName, "updated display name");
    AssertEqual("degC", edited.Fields.Single(field => field.Key == "sp").Unit, "updated unit");
    AssertEqual(false, edited.Fields.Any(field => field.Key == "operator_note"), "removed metadata field");

    AssertThrows<InvalidOperationException>(
        () => new PidSampleFieldProfileEditor(edited).Add(Field("sp", PidSampleFieldRole.Metadata, PidSampleFieldDataType.String, false)),
        "duplicate field key is rejected");

    return Task.CompletedTask;
}

static Task AnalysisWindowParserReturnsOptionalValidatedWindows()
{
    var parser = new AnalysisWindowParser();

    AssertEqual(null, parser.Parse(null, " "), "empty window");

    var window = parser.Parse(
        "2026-07-29T10:00:01.0000000+00:00",
        "2026-07-29T10:00:06.0000000+00:00");

    AssertEqual(
        DateTimeOffset.Parse("2026-07-29T10:00:01.0000000+00:00", CultureInfo.InvariantCulture),
        window?.Start,
        "window start");
    AssertEqual(
        DateTimeOffset.Parse("2026-07-29T10:00:06.0000000+00:00", CultureInfo.InvariantCulture),
        window?.End,
        "window end");

    AssertThrows<FormatException>(
        () => parser.Parse("2026-07-29T10:00:06.0000000+00:00", "2026-07-29T10:00:01.0000000+00:00"),
        "reversed analysis window is rejected");

    return Task.CompletedTask;
}

static Task ResponseAssessmentFlagsHighOvershootAndSteadyStateError()
{
    var service = new PidResponseAssessmentService();
    var assessment = service.Assess(new PidResponseMetrics(
        OvershootPercent: 18,
        RiseTime: TimeSpan.FromSeconds(2),
        SettlingTime: TimeSpan.FromSeconds(12),
        SteadyStateError: 3.2));

    AssertEqual(PidResponseSeverity.Warning, assessment.Severity, "assessment severity");
    AssertContains("超调偏高", assessment.Summary);
    AssertContains("稳态误差偏大", assessment.Summary);
    AssertEqual(2, assessment.Findings.Count, "assessment finding count");

    var normal = service.Assess(new PidResponseMetrics(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), 0.1));
    AssertEqual(PidResponseSeverity.Normal, normal.Severity, "normal severity");
    AssertContains("未发现明显异常", normal.Summary);

    return Task.CompletedTask;
}

static async Task AnalysisResultCsvExporterWritesStableFields()
{
    var window = new AnalysisWindow(
        DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-29T10:00:06.0000000+00:00", CultureInfo.InvariantCulture));
    var metrics = new PidResponseMetrics(12, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 0.5);
    var assessment = new PidResponseAssessment(
        PidResponseSeverity.Warning,
        "超调偏高，可能需要降低比例增益或增强阻尼。",
        new[] { "超调偏高，可能需要降低比例增益或增强阻尼。" });

    var exporter = new PidAnalysisResultCsvExporter();
    await using var output = new MemoryStream();

    await exporter.ExportAsync(window, metrics, assessment, output, CancellationToken.None);

    var exportedBytes = output.ToArray();
    AssertUtf8Bom(exportedBytes, "analysis result CSV export encoding");
    var csv = Encoding.UTF8.GetString(exportedBytes);
    AssertContains("window_start,window_end,overshoot_percent,rise_time_seconds,settling_time_seconds,steady_state_error,severity,summary", csv);
    AssertContains("2026-07-29T10:00:00.0000000+00:00,2026-07-29T10:00:06.0000000+00:00,12,2,5,0.5,Warning", csv);
    AssertContains("\"超调偏高，可能需要降低比例增益或增强阻尼。\"", csv);
}

static async Task JsonTestSessionRepositorySavesAndListsSessions()
{
    var directory = CreateTestStorageDirectory();
    var repository = new JsonTestSessionRepository(directory);
    var sessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    var projectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    var session = new TestSession(
        sessionId,
        projectId,
        "cold-start-step",
        DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-29T10:00:06.0000000+00:00", CultureInfo.InvariantCulture),
        "Pump A",
        "Cold start",
        "Baseline PID");

    await repository.SaveAsync(session, CancellationToken.None);
    await repository.SaveAsync(session with { Notes = "Updated notes" }, CancellationToken.None);

    var sessions = await repository.ListAsync(CancellationToken.None);

    AssertEqual(1, sessions.Count, "stored session count");
    AssertEqual("cold-start-step", sessions[0].Name, "stored session name");
    AssertEqual("Updated notes", sessions[0].Notes, "stored session update");
}

static async Task JsonPidSampleRepositorySavesAndLoadsSamplesBySession()
{
    var directory = CreateTestStorageDirectory();
    var repository = new JsonPidSampleRepository(directory);
    var sessionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    var start = DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);

    var samples = new[]
    {
        Sample(start, 100, 80, 20, sessionId),
        Sample(start.AddSeconds(1), 100, 95, 35, sessionId)
    };

    await repository.SaveBatchAsync(samples, CancellationToken.None);

    var loaded = await repository.GetBySessionAsync(sessionId, CancellationToken.None);

    AssertEqual(2, loaded.Count, "stored sample count");
    AssertClose(95, loaded[1].ProcessValue, 0.001, "stored sample PV");
    AssertEqual(sessionId, loaded[0].TestSessionId, "stored sample session id");
}

static async Task MainViewModelShowsNotificationsAndRefreshesHistory()
{
    var directory = CreateTestStorageDirectory();
    var sessionRepository = new JsonTestSessionRepository(directory);
    var sampleRepository = new JsonPidSampleRepository(directory);
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        sessionRepository,
        sampleRepository,
        directory);

    await viewModel.LoadExampleAsync();

    AssertEqual(true, viewModel.IsNotificationVisible, "analysis notification visibility");
    AssertEqual("离线分析已完成", viewModel.NotificationTitle, "analysis notification title");
    AssertEqual("Success", viewModel.NotificationKind, "analysis notification kind");

    await viewModel.SaveTestSessionAsync();

    AssertEqual(true, viewModel.IsNotificationVisible, "save session notification visibility");
    AssertEqual("试验记录已保存", viewModel.NotificationTitle, "save session notification title");
    AssertContains(Path.GetFullPath(directory), viewModel.NotificationMessage);
    AssertContains(Path.Combine(Path.GetFullPath(directory), "test-sessions.json"), viewModel.NotificationMessage);
    AssertContains(".samples.json", viewModel.NotificationMessage);
    AssertEqual(1, viewModel.HistorySessions.Count, "history count after save");

    viewModel.SelectedHistorySession = viewModel.HistorySessions[0];
    await viewModel.OpenHistorySessionAsync();

    AssertEqual("历史记录已打开", viewModel.NotificationTitle, "open history notification title");
    AssertEqual("7", viewModel.SampleCount, "history sample count");
}

static PidSample Sample(DateTimeOffset timestamp, double sp, double pv, double mv, Guid sessionId)
{
    return new PidSample(timestamp, sp, pv, mv, 1.2, 0.4, 0.1, true, sessionId, null);
}

static string CreateTestStorageDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "pidtuner-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
    }
}

static void AssertClose(double expected, double? actual, double tolerance, string name)
{
    if (actual is null || Math.Abs(expected - actual.Value) > tolerance)
    {
        throw new InvalidOperationException(
            string.Create(CultureInfo.InvariantCulture, $"{name}: expected {expected}, got {actual}"));
    }
}

static void AssertContains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected exported CSV to contain: {expected}");
    }
}

static void AssertUtf8Bom(byte[] actual, string name)
{
    if (actual.Length < 3 || actual[0] != 0xEF || actual[1] != 0xBB || actual[2] != 0xBF)
    {
        throw new InvalidOperationException($"{name}: expected UTF-8 BOM for Excel-compatible CSV.");
    }
}

static void AssertThrows<TException>(Action action, string name)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
}

static PidSampleFieldDefinition Field(
    string key,
    PidSampleFieldRole role,
    PidSampleFieldDataType dataType,
    bool required)
{
    return new PidSampleFieldDefinition(key, key, dataType, required, null, role);
}

file sealed class NoFileDialogService : IOpenFileDialogService
{
    public string? PickCsvFile()
    {
        return null;
    }

    public string? PickFieldProfileFile()
    {
        return null;
    }

    public string? PickFieldProfileSaveFile()
    {
        return null;
    }

    public string? PickAnalysisResultSaveFile()
    {
        return null;
    }
}
