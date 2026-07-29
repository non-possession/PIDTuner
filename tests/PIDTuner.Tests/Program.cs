using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using PIDTuner.Domain.Analysis;
using PIDTuner.Application.Interfaces;
using PIDTuner.Application.Services;
using PIDTuner.Domain.Trends;
using PIDTuner.Domain.Models;
using PIDTuner.Domain.Plc;
using PIDTuner.Application.UseCases;
using PIDTuner.Domain.Configuration;
using PIDTuner.Desktop.Services;
using PIDTuner.Desktop.ViewModels;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Configuration;
using PIDTuner.Infrastructure.Persistence;
using PIDTuner.Infrastructure.Plc;

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
    ("response assessment flags oscillation and output saturation", ResponseAssessmentFlagsOscillationAndOutputSaturation),
    ("tuning recommendation service suggests conservative adjustments", TuningRecommendationServiceSuggestsConservativeAdjustments),
    ("analysis result csv exporter writes stable fields", AnalysisResultCsvExporterWritesStableFields),
    ("json test session repository saves and lists sessions", JsonTestSessionRepositorySavesAndListsSessions),
    ("json pid sample repository saves and loads samples by session", JsonPidSampleRepositorySavesAndLoadsSamplesBySession),
    ("json recommendation review repository saves and lists reviews", JsonRecommendationReviewRepositorySavesAndListsReviews),
    ("pid parameter set extractor captures latest PID values", PidParameterSetExtractorCapturesLatestPidValues),
    ("json pid parameter set repository saves and lists sets", JsonPidParameterSetRepositorySavesAndListsSets),
    ("s7 address parser maps DB offsets and bits", S7AddressParserMapsDbOffsetsAndBits),
    ("plc project configuration store round trips editable connection and tags", PlcProjectConfigurationStoreRoundTripsEditableConnectionAndTags),
    ("main view model saves plc configuration with absolute path notification", MainViewModelSavesPlcConfigurationWithAbsolutePathNotification),
    ("main view model checks plc communication through injected probe", MainViewModelChecksPlcCommunicationThroughInjectedProbe),
    ("main view model refreshes plc monitor snapshots and trends", MainViewModelRefreshesPlcMonitorSnapshotsAndTrends),
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
    AssertClose(112, metrics.PeakProcessValue, 0.001, "peak process value");
    AssertEqual(TimeSpan.FromSeconds(3), metrics.PeakTime, "peak time");
    AssertClose(0, metrics.MinimumProcessValue, 0.001, "minimum process value");
    AssertClose(26.642, metrics.MeanAbsoluteError, 0.001, "mean absolute error");
    AssertClose(1979.178, metrics.MeanSquaredError, 0.001, "mean squared error");
    AssertClose(136.25, metrics.IntegralAbsoluteError, 0.001, "integral absolute error");
    AssertClose(17.800, metrics.OutputStandardDeviation, 0.001, "output standard deviation");
    AssertEqual(false, metrics.HasSustainedOscillation, "sustained oscillation");
    AssertEqual(false, metrics.HasOutputSaturation, "output saturation");

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

static Task ResponseAssessmentFlagsOscillationAndOutputSaturation()
{
    var service = new PidResponseAssessmentService();
    var assessment = service.Assess(new PidResponseMetrics(
        OvershootPercent: 4,
        RiseTime: TimeSpan.FromSeconds(3),
        SettlingTime: TimeSpan.FromSeconds(8),
        SteadyStateError: 0.2,
        HasSustainedOscillation: true,
        HasOutputSaturation: true));

    AssertEqual(PidResponseSeverity.Warning, assessment.Severity, "oscillation assessment severity");
    AssertContains("持续振荡", assessment.Summary);
    AssertContains("饱和", assessment.Summary);

    return Task.CompletedTask;
}

static Task TuningRecommendationServiceSuggestsConservativeAdjustments()
{
    var service = new PidTuningRecommendationService();
    var recommendations = service.Recommend(new PidResponseMetrics(
        OvershootPercent: 18,
        RiseTime: TimeSpan.FromSeconds(2),
        SettlingTime: TimeSpan.FromSeconds(12),
        SteadyStateError: 3.2));

    AssertEqual(true, recommendations.Any(recommendation =>
        recommendation.Parameter == "Kp"
        && recommendation.Direction == PidTuningAdjustmentDirection.Decrease), "Kp decrease recommendation");
    AssertEqual(true, recommendations.Any(recommendation =>
        recommendation.Parameter == "Ki/Ti"
        && recommendation.Direction == PidTuningAdjustmentDirection.Increase), "Ki/Ti increase recommendation");

    var normal = service.Recommend(new PidResponseMetrics(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), 0.1));
    AssertEqual(1, normal.Count, "normal recommendation count");
    AssertEqual(PidTuningAdjustmentDirection.Hold, normal[0].Direction, "normal hold recommendation");

    return Task.CompletedTask;
}

static async Task AnalysisResultCsvExporterWritesStableFields()
{
    var window = new AnalysisWindow(
        DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse("2026-07-29T10:00:06.0000000+00:00", CultureInfo.InvariantCulture));
    var metrics = new PidResponseMetrics(
        12,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        0.5,
        PeakProcessValue: 112,
        PeakTime: TimeSpan.FromSeconds(3),
        MinimumProcessValue: 0,
        MeanAbsoluteError: 22.928,
        MeanSquaredError: 2062.035,
        IntegralAbsoluteError: 138.25,
        OutputStandardDeviation: 17.292,
        HasSustainedOscillation: false,
        HasOutputSaturation: false);
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
    AssertContains("window_start,window_end,overshoot_percent,rise_time_seconds,settling_time_seconds,steady_state_error,peak_process_value,peak_time_seconds,minimum_process_value,mean_absolute_error,mean_squared_error,integral_absolute_error,output_standard_deviation,has_sustained_oscillation,has_output_saturation,severity,summary", csv);
    AssertContains("2026-07-29T10:00:00.0000000+00:00,2026-07-29T10:00:06.0000000+00:00,12,2,5,0.5,112,3,0,22.928,2062.035,138.25,17.292,False,False,Warning", csv);
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

static async Task JsonRecommendationReviewRepositorySavesAndListsReviews()
{
    var directory = CreateTestStorageDirectory();
    var repository = new JsonPidRecommendationReviewRepository(directory);
    var review = new PidRecommendationReview(
        Guid.Parse("44444444-4444-4444-4444-444444444444"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "offline-step-response",
        "Kp",
        PidTuningAdjustmentDirection.Decrease,
        "建议降低 5% - 10%",
        PidRecommendationReviewDecision.Accepted,
        "现场同意小步验证",
        DateTimeOffset.Parse("2026-07-29T10:10:00.0000000+00:00", CultureInfo.InvariantCulture));

    await repository.SaveAsync(review, CancellationToken.None);

    var reviews = await repository.ListAsync(CancellationToken.None);

    AssertEqual(1, reviews.Count, "stored review count");
    AssertEqual(PidRecommendationReviewDecision.Accepted, reviews[0].Decision, "stored review decision");
    AssertEqual("现场同意小步验证", reviews[0].EngineerNote, "stored review note");
}

static Task PidParameterSetExtractorCapturesLatestPidValues()
{
    var extractor = new PidParameterSetExtractor();
    var sessionId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    var start = DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    var parameterSetId = Guid.Parse("88888888-8888-8888-8888-888888888888");
    var samples = new[]
    {
        new PidSample(start, 100, 90, 30, 1.2, 0.4, 0.1, true, sessionId, null),
        new PidSample(start.AddSeconds(1), 100, 95, 35, 1.4, 0.5, 0.2, true, sessionId, parameterSetId)
    };

    var parameterSet = extractor.Extract(samples, sessionId, "offline-step-response", "baseline");

    AssertEqual(parameterSetId, parameterSet?.Id, "extracted parameter set id");
    AssertClose(1.4, parameterSet?.Kp, 0.001, "extracted Kp");
    AssertClose(0.5, parameterSet?.KiOrTi, 0.001, "extracted Ki/Ti");
    AssertClose(0.2, parameterSet?.KdOrTd, 0.001, "extracted Kd/Td");
    AssertEqual("baseline", parameterSet?.Notes, "extracted notes");

    return Task.CompletedTask;
}

static async Task JsonPidParameterSetRepositorySavesAndListsSets()
{
    var directory = CreateTestStorageDirectory();
    var repository = new JsonPidParameterSetRepository(directory);
    var parameterSet = new PidParameterSet(
        Guid.Parse("99999999-9999-9999-9999-999999999999"),
        Guid.Parse("77777777-7777-7777-7777-777777777777"),
        "baseline",
        1.2,
        0.4,
        0.1,
        DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+00:00", CultureInfo.InvariantCulture),
        "offline-step-response",
        "before tuning");

    await repository.SaveAsync(parameterSet, CancellationToken.None);
    await repository.SaveAsync(parameterSet with { Kp = 1.1 }, CancellationToken.None);

    var parameterSets = await repository.ListAsync(CancellationToken.None);

    AssertEqual(1, parameterSets.Count, "stored parameter set count");
    AssertClose(1.1, parameterSets[0].Kp, 0.001, "updated parameter set Kp");
    AssertEqual("before tuning", parameterSets[0].Notes, "stored parameter set notes");
}

static Task S7AddressParserMapsDbOffsetsAndBits()
{
    var realAddress = S7AddressParser.Parse("DB1.DBD24", PlcDataType.Double);
    AssertEqual(1, realAddress.DataBlock, "s7 DB number");
    AssertEqual(24, realAddress.ByteOffset, "s7 byte offset");
    AssertEqual(null, realAddress.BitOffset, "s7 non-bit offset");
    AssertEqual(192, realAddress.BitAddress, "s7 bit address");
    AssertEqual(4, realAddress.ReadByteCount, "s7 DBD numeric read byte count");

    var bitAddress = S7AddressParser.Parse("DB2.DBX3.5", PlcDataType.Boolean);
    AssertEqual(2, bitAddress.DataBlock, "s7 bit DB number");
    AssertEqual(3, bitAddress.ByteOffset, "s7 bit byte offset");
    AssertEqual(5, bitAddress.BitOffset, "s7 bit offset");
    AssertEqual(29, bitAddress.BitAddress, "s7 absolute bit address");

    AssertThrows<FormatException>(
        () => S7AddressParser.Parse("M0.0", PlcDataType.Boolean),
        "unsupported non-DB S7 address");

    return Task.CompletedTask;
}

static async Task PlcProjectConfigurationStoreRoundTripsEditableConnectionAndTags()
{
    var store = new JsonPlcProjectConfigurationStore();
    var configuration = new PlcProjectConfiguration(
        1,
        "line-a-temperature-loop",
        "Siemens S7",
        "10.10.0.5",
        0,
        2,
        2500,
        250,
        new[]
        {
            new TagDefinition(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "PV",
                "DB12.DBD8",
                PlcDataType.Double,
                TagAccessMode.ReadOnly,
                0.1,
                "degC",
                "reactor temperature",
                TimeSpan.FromMilliseconds(250),
                true)
        });

    await using var output = new MemoryStream();
    await store.SaveAsync(configuration, output, CancellationToken.None);

    var json = Encoding.UTF8.GetString(output.ToArray());
    AssertContains("\"dataType\": \"Double\"", json);
    AssertContains("\"accessMode\": \"ReadOnly\"", json);

    output.Position = 0;
    var roundTripped = await store.LoadAsync(output, CancellationToken.None);

    AssertEqual("line-a-temperature-loop", roundTripped.Name, "plc configuration name");
    AssertEqual("10.10.0.5", roundTripped.IpAddress, "plc ip address");
    AssertEqual(1, roundTripped.Tags.Count, "plc tag count");
    AssertEqual(PlcDataType.Double, roundTripped.Tags[0].DataType, "plc tag data type");
    AssertEqual(TagAccessMode.ReadOnly, roundTripped.Tags[0].AccessMode, "plc tag access mode");
}

static async Task MainViewModelSavesPlcConfigurationWithAbsolutePathNotification()
{
    var directory = CreateTestStorageDirectory();
    var plcConfigurationPath = Path.Combine(directory, "plc-project.json");
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(plcProjectConfigurationSaveFile: plcConfigurationPath),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        testSessionStorageDirectory: directory);

    viewModel.PlcConfigurationName = "line-a-temperature-loop";
    viewModel.PlcIpAddress = "10.10.0.5";

    await viewModel.SavePlcConfigurationAsync();

    AssertEqual(true, File.Exists(plcConfigurationPath), "plc configuration file exists");
    AssertEqual(true, viewModel.IsNotificationVisible, "plc save notification visibility");
    AssertContains(Path.GetFullPath(plcConfigurationPath), viewModel.NotificationMessage);

    await using var input = File.OpenRead(plcConfigurationPath);
    var saved = await new JsonPlcProjectConfigurationStore().LoadAsync(input, CancellationToken.None);
    AssertEqual("line-a-temperature-loop", saved.Name, "saved plc configuration name");
    AssertEqual("10.10.0.5", saved.IpAddress, "saved plc ip address");
    AssertEqual(viewModel.TagDefinitions.Count, saved.Tags.Count, "saved plc tag count");
}

static async Task MainViewModelChecksPlcCommunicationThroughInjectedProbe()
{
    var directory = CreateTestStorageDirectory();
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        new FixedPlcConnectivityProbe(true),
        testSessionStorageDirectory: directory);

    viewModel.PlcIpAddress = "127.0.0.1";

    await viewModel.CheckPlcCommunicationAsync();

    AssertEqual("PLC 通信检查通过", viewModel.NotificationTitle, "plc communication notification title");
    AssertContains("127.0.0.1", viewModel.PlcCommunicationStatus);
    AssertContains("Ping 成功", viewModel.PlcCommunicationStatus);
}

static async Task MainViewModelRefreshesPlcMonitorSnapshotsAndTrends()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        testSessionStorageDirectory: directory);

    await viewModel.RefreshPlcMonitorAsync();
    await viewModel.RefreshPlcMonitorAsync();

    AssertEqual(true, viewModel.PlcMonitorTags.Count > 0, "plc monitor tag count");
    AssertEqual("2", viewModel.PlcMonitorTags[0].ValueText, "second monitor value");
    AssertEqual(true, viewModel.PlcMonitorTags[0].TrendPoints.Count >= 2, "monitor trend point count");
    AssertContains("已刷新", viewModel.PlcMonitorStatus);
}

static async Task MainViewModelShowsNotificationsAndRefreshesHistory()
{
    var directory = CreateTestStorageDirectory();
    var sessionRepository = new JsonTestSessionRepository(directory);
    var sampleRepository = new JsonPidSampleRepository(directory);
    var reviewRepository = new JsonPidRecommendationReviewRepository(directory);
    var parameterSetRepository = new JsonPidParameterSetRepository(directory);
    var exportedHistorySamplesPath = Path.Combine(directory, "history-samples.csv");
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(historySamplesSaveFile: exportedHistorySamplesPath),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        testSessionRepository: sessionRepository,
        pidSampleRepository: sampleRepository,
        recommendationReviewRepository: reviewRepository,
        parameterSetRepository: parameterSetRepository,
        testSessionStorageDirectory: directory);

    await viewModel.LoadExampleAsync();

    AssertEqual(true, viewModel.IsNotificationVisible, "analysis notification visibility");
    AssertEqual("离线分析已完成", viewModel.NotificationTitle, "analysis notification title");
    AssertEqual("Success", viewModel.NotificationKind, "analysis notification kind");
    AssertEqual(true, viewModel.TuningRecommendations.Any(item => item.Parameter == "Kp"), "view model Kp recommendation");
    AssertContains("保守调整建议", viewModel.RecommendationSummary);
    await viewModel.SaveParameterSetAsync();
    AssertEqual("参数方案已保存", viewModel.NotificationTitle, "parameter set notification title");
    AssertEqual(1, viewModel.ParameterSets.Count, "parameter set count after save");
    AssertEqual("1.2", viewModel.ParameterSets[0].Kp, "parameter set Kp display");
    viewModel.SelectedTuningRecommendation = viewModel.TuningRecommendations.First(item => item.Parameter == "Kp");
    viewModel.RecommendationReviewNote = "现场确认先小步调整";
    await viewModel.AcceptRecommendationAsync();
    AssertEqual("建议审查已记录", viewModel.NotificationTitle, "review notification title");
    AssertEqual(1, viewModel.RecommendationReviews.Count, "recommendation review count");
    AssertContains("现场确认", viewModel.RecommendationReviews[0].EngineerNote);

    await viewModel.SaveTestSessionAsync();

    AssertEqual(true, viewModel.IsNotificationVisible, "save session notification visibility");
    AssertEqual("试验记录已保存", viewModel.NotificationTitle, "save session notification title");
    AssertContains(Path.GetFullPath(directory), viewModel.NotificationMessage);
    AssertContains(Path.Combine(Path.GetFullPath(directory), "test-sessions.json"), viewModel.NotificationMessage);
    AssertContains(".samples.json", viewModel.NotificationMessage);
    AssertEqual(1, viewModel.HistorySessions.Count, "history count after save");
    AssertEqual("7", viewModel.HistorySessions[0].SampleCount, "history sample count after save");
    AssertEqual("00:00:06", viewModel.HistorySessions[0].Duration, "history duration after save");

    var improvedSessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    var improvedStart = DateTimeOffset.Parse("2026-07-29T10:20:00.0000000+00:00", CultureInfo.InvariantCulture);
    await sessionRepository.SaveAsync(new TestSession(
        improvedSessionId,
        Guid.Parse("66666666-6666-6666-6666-666666666666"),
        "improved-step",
        improvedStart,
        improvedStart.AddSeconds(6),
        "Device A",
        "Reduced overshoot",
        "After conservative Kp change"), CancellationToken.None);
    await sampleRepository.SaveBatchAsync(new[]
    {
        Sample(improvedStart.AddSeconds(0), 100, 0, 0, improvedSessionId),
        Sample(improvedStart.AddSeconds(1), 100, 45, 20, improvedSessionId),
        Sample(improvedStart.AddSeconds(2), 100, 88, 38, improvedSessionId),
        Sample(improvedStart.AddSeconds(3), 100, 104, 45, improvedSessionId),
        Sample(improvedStart.AddSeconds(4), 100, 101, 43, improvedSessionId),
        Sample(improvedStart.AddSeconds(5), 100, 100.2, 42, improvedSessionId),
        Sample(improvedStart.AddSeconds(6), 100, 100.1, 42, improvedSessionId)
    }, CancellationToken.None);
    await viewModel.LoadHistoryAsync();
    AssertEqual(2, viewModel.HistorySessions.Count, "history count after adding improved session");

    viewModel.HistorySearchText = "offline";
    AssertEqual(1, viewModel.HistorySessions.Count, "filtered history count");
    viewModel.HistorySearchText = "not-found";
    AssertEqual(0, viewModel.HistorySessions.Count, "filtered empty history count");
    viewModel.HistorySearchText = string.Empty;

    viewModel.SelectedHistorySession = viewModel.HistorySessions.First(item =>
        item.Name.Contains("offline", StringComparison.OrdinalIgnoreCase));
    AssertContains("样本：7", viewModel.SelectedHistoryDetails);
    await viewModel.SetHistoryBaselineAsync();
    viewModel.SelectedHistorySession = viewModel.HistorySessions.First(item => item.Name == "improved-step");
    await viewModel.CompareHistorySessionAsync();
    AssertEqual(true, viewModel.HistoryComparisonMetrics.Count >= 4, "history comparison metric count");
    AssertEqual(true, viewModel.HistoryComparisonMetrics.Any(item =>
        item.Metric == "超调量" && item.Delta.StartsWith("-", StringComparison.Ordinal)), "history comparison overshoot improvement");
    AssertContains("improved-step", viewModel.HistoryComparisonStatus);

    await viewModel.OpenHistorySessionAsync();

    AssertEqual("历史记录已打开", viewModel.NotificationTitle, "open history notification title");
    AssertEqual("7", viewModel.SampleCount, "history sample count");

    await viewModel.ExportHistorySamplesAsync();

    AssertEqual("历史采样已导出", viewModel.NotificationTitle, "export history notification title");
    AssertContains(Path.GetFullPath(exportedHistorySamplesPath), viewModel.NotificationMessage);
    AssertEqual(true, File.Exists(exportedHistorySamplesPath), "exported history samples file exists");
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

file sealed class NoFileDialogService(
    string? historySamplesSaveFile = null,
    string? plcProjectConfigurationFile = null,
    string? plcProjectConfigurationSaveFile = null) : IOpenFileDialogService
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

    public string? PickPlcProjectConfigurationFile()
    {
        return plcProjectConfigurationFile;
    }

    public string? PickPlcProjectConfigurationSaveFile()
    {
        return plcProjectConfigurationSaveFile;
    }

    public string? PickAnalysisResultSaveFile()
    {
        return null;
    }

    public string? PickHistorySamplesSaveFile()
    {
        return historySamplesSaveFile;
    }
}

file sealed class FixedPlcConnectivityProbe(bool reachable) : IPlcConnectivityProbe
{
    public Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new PlcCommunicationCheck(
            reachable,
            configuration.IpAddress,
            TimeSpan.FromMilliseconds(1),
            reachable ? "Ping 成功，往返 1 ms。" : "Ping 未成功。",
            DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+08:00", CultureInfo.InvariantCulture)));
    }
}

file sealed class SequencePlcTagSnapshotReader : IPlcTagSnapshotReader
{
    private int _value;

    public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        _value++;
        var tag = configuration.Tags.First(item => item.IsEnabled);
        IReadOnlyList<PlcTagSnapshot> snapshots = new[]
        {
            new PlcTagSnapshot(
                tag.Id,
                tag.Name,
                tag.Address,
                _value,
                tag.Unit,
                DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+08:00", CultureInfo.InvariantCulture).AddSeconds(_value),
                "Good",
                "Test")
        };

        return Task.FromResult(snapshots);
    }
}
