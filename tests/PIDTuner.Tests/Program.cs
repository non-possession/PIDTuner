using System.Buffers.Binary;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
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
    ("plc trend dataset bridge groups frames by tag", PlcTrendDatasetBridgeGroupsFramesByTag),
    ("historical trend workbench clamps ranges and filters series", HistoricalTrendWorkbenchClampsRangesAndFiltersSeries),
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
    ("s7 read response parser handles adjacent multi-real items", S7ReadResponseParserHandlesAdjacentMultiRealItems),
    ("s7 db block parser decodes sparse real offsets", S7DbBlockParserDecodesSparseRealOffsets),
    ("plc acquisition diagnostics summarizes frame timing", PlcAcquisitionDiagnosticsSummarizesFrameTiming),
    ("plc acquisition engine rejects invalid interval", PlcAcquisitionEngineRejectsInvalidInterval),
    ("plc acquisition engine skips overdue schedule slots", PlcAcquisitionEngineSkipsOverdueScheduleSlots),
    ("plc trend chart calculates live retention from time windows", PlcTrendChartCalculatesLiveRetentionFromTimeWindows),
    ("sqlite plc live diagnostics store writes queued frames", SqlitePlcLiveDiagnosticsStoreWritesQueuedFrames),
    ("plc project configuration store round trips editable connection and tags", PlcProjectConfigurationStoreRoundTripsEditableConnectionAndTags),
    ("main view model saves plc configuration with absolute path notification", MainViewModelSavesPlcConfigurationWithAbsolutePathNotification),
    ("main view model checks plc communication after loading configuration", MainViewModelChecksPlcCommunicationAfterLoadingConfiguration),
    ("main view model checks plc communication through injected probe", MainViewModelChecksPlcCommunicationThroughInjectedProbe),
    ("main view model refreshes plc monitor snapshots and trends", MainViewModelRefreshesPlcMonitorSnapshotsAndTrends),
    ("main view model reuses a plc session while live monitoring", MainViewModelReusesPlcSessionWhileLiveMonitoring),
    ("main view model toggles live diagnostics while monitoring", MainViewModelTogglesLiveDiagnosticsWhileMonitoring),
    ("main view model filters diagnostics frames before session start", MainViewModelFiltersDiagnosticsFramesBeforeSessionStart),
    ("main view model toggles live trend scrolling pause", MainViewModelTogglesLiveTrendScrollingPause),
    ("plc monitor row displays milliseconds", PlcMonitorRowDisplaysMilliseconds),
    ("main view model records one second plc monitor frames at fastest tag interval", MainViewModelRecordsOneSecondPlcMonitorFramesAtFastestTagInterval),
    ("main view model loads saved plc recording for replay", MainViewModelLoadsSavedPlcRecordingForReplay),
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

static Task PlcTrendDatasetBridgeGroupsFramesByTag()
{
    var start = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    var spId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var pvId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    var frames = new[]
    {
        new[]
        {
            new PlcTagSnapshot(spId, "SP", "DB1.DBD6", 10, "degC", start, "Good", "PLC"),
            new PlcTagSnapshot(pvId, "PV", "DB1.DBD10", 8, "degC", start, "Good", "PLC")
        },
        new[]
        {
            new PlcTagSnapshot(spId, "SP", "DB1.DBD6", 11, "degC", start.AddMilliseconds(100), "Good", "PLC"),
            new PlcTagSnapshot(pvId, "PV", "DB1.DBD10", double.NaN, "degC", start.AddMilliseconds(100), "Bad", "PLC")
        }
    };

    var dataset = new PlcTrendDatasetBridge().BuildDataset(frames);

    AssertEqual(2, dataset.Series.Count, "historical dataset series count");
    AssertEqual(start, dataset.Start, "historical dataset start");
    AssertEqual(start.AddMilliseconds(100), dataset.End, "historical dataset end");
    AssertEqual(2, dataset.Series.Single(series => series.SeriesId == spId).Points.Count, "SP points count");
    AssertEqual(1, dataset.Series.Single(series => series.SeriesId == pvId).Points.Count, "PV ignores non-finite values");

    return Task.CompletedTask;
}

static Task HistoricalTrendWorkbenchClampsRangesAndFiltersSeries()
{
    var start = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    var spId = Guid.Parse("20000000-0000-0000-0000-000000000001");
    var pvId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    var dataset = new HistoricalTrendDataset(new[]
    {
        new HistoricalTrendSeries(
            spId,
            "SP",
            "DB1.DBD6",
            null,
            new[]
            {
                new HistoricalTrendPoint(start, 10, "Good", "Test"),
                new HistoricalTrendPoint(start.AddSeconds(1), 11, "Good", "Test"),
                new HistoricalTrendPoint(start.AddSeconds(2), 12, "Good", "Test")
            }),
        new HistoricalTrendSeries(
            pvId,
            "PV",
            "DB1.DBD10",
            null,
            new[]
            {
                new HistoricalTrendPoint(start, 8, "Good", "Test"),
                new HistoricalTrendPoint(start.AddSeconds(1), 9, "Good", "Test"),
                new HistoricalTrendPoint(start.AddSeconds(2), 10, "Good", "Test")
            })
    });
    var coordinator = new HistoricalTrendWorkbenchCoordinator();

    var state = coordinator.LoadDataset(dataset);
    state = coordinator.SetVisibleTimeRange(state, start.AddMilliseconds(500), start.AddSeconds(5));
    state = coordinator.SetVisibleYRange(state, 20, 0);
    state = coordinator.SetSeriesVisibility(state, pvId, isVisible: false);
    var visibleSeries = coordinator.GetVisibleSeries(state);

    AssertEqual(start.AddMilliseconds(500), state.VisibleTimeRange?.Start, "historical visible range start");
    AssertEqual(start.AddSeconds(2), state.VisibleTimeRange?.End, "historical visible range end clamps to dataset");
    AssertEqual(0d, state.VisibleYRange?.Minimum, "historical y range swaps minimum");
    AssertEqual(20d, state.VisibleYRange?.Maximum, "historical y range swaps maximum");
    AssertEqual(1, visibleSeries.Count, "hidden series removed");
    AssertEqual(spId, visibleSeries[0].SeriesId, "remaining visible series");
    AssertEqual(2, visibleSeries[0].Points.Count, "visible points filtered by time");

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

static Task S7ReadResponseParserHandlesAdjacentMultiRealItems()
{
    var addresses = new[]
    {
        S7AddressParser.Parse("DB1.DBD0", PlcDataType.Float),
        S7AddressParser.Parse("DB1.DBD4", PlcDataType.Float)
    };
    var response = BuildS7ReadResponseWithTwoAdjacentRealItems(1.25f, 2.5f);
    var method = typeof(SiemensS7Client).GetMethod(
        "ExtractReadResults",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractReadResults method was not found.");

    var results = ((System.Collections.IEnumerable)(method.Invoke(null, new object[] { response, addresses })
        ?? throw new InvalidOperationException("ExtractReadResults returned null.")))
        .Cast<object>()
        .ToArray();

    AssertEqual(2, results.Length, "s7 parsed result count");
    AssertClose(1.25, GetS7ReadResultValue(results[0]), 0.001, "first batched REAL");
    AssertClose(2.5, GetS7ReadResultValue(results[1]), 0.001, "second batched REAL");
    AssertEqual(null, GetS7ReadResultError(results[0]), "first batched REAL error");
    AssertEqual(null, GetS7ReadResultError(results[1]), "second batched REAL error");
    return Task.CompletedTask;
}

static Task S7DbBlockParserDecodesSparseRealOffsets()
{
    var addresses = new[]
    {
        S7AddressParser.Parse("DB8.DBD6", PlcDataType.Double),
        S7AddressParser.Parse("DB8.DBD10", PlcDataType.Double),
        S7AddressParser.Parse("DB8.DBD48", PlcDataType.Double)
    };
    var response = BuildS7ReadResponseWithDbBlock(
        startByte: 6,
        byteCount: 46,
        new Dictionary<int, float>
        {
            [6] = 80f,
            [10] = 30f,
            [48] = 50f
        });
    var method = typeof(SiemensS7Client).GetMethod(
        "ExtractBlockReadResults",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractBlockReadResults method was not found.");

    var results = ((System.Collections.IEnumerable)(method.Invoke(null, new object[] { response, addresses, 6, 46 })
        ?? throw new InvalidOperationException("ExtractBlockReadResults returned null.")))
        .Cast<object>()
        .ToArray();

    AssertEqual(3, results.Length, "s7 db block parsed result count");
    AssertClose(80, GetS7ReadResultValue(results[0]), 0.001, "db block SP");
    AssertClose(30, GetS7ReadResultValue(results[1]), 0.001, "db block other");
    AssertClose(50, GetS7ReadResultValue(results[2]), 0.001, "db block PV");
    AssertEqual(null, GetS7ReadResultError(results[0]), "db block SP error");
    AssertEqual(null, GetS7ReadResultError(results[1]), "db block other error");
    AssertEqual(null, GetS7ReadResultError(results[2]), "db block PV error");
    return Task.CompletedTask;
}

static Task PlcAcquisitionDiagnosticsSummarizesFrameTiming()
{
    var start = new DateTimeOffset(2026, 8, 5, 9, 0, 0, TimeSpan.Zero);
    var frames = new[]
    {
        DiagnosticFrame(0, start, plannedMs: 0, requestMs: 1, responseMs: 8, bufferedMs: 9, uiMs: 10, snapshots: 2, PlcAcquisitionFrameState.Normal),
        DiagnosticFrame(1, start, plannedMs: 100, requestMs: 106, responseMs: 121, bufferedMs: 123, uiMs: 128, snapshots: 2, PlcAcquisitionFrameState.Late),
        DiagnosticFrame(2, start, plannedMs: 200, requestMs: 204, responseMs: 214, bufferedMs: 215, uiMs: 219, snapshots: 2, PlcAcquisitionFrameState.Normal)
    };

    var summary = PlcAcquisitionDiagnostics.Summarize(frames);

    AssertEqual(3, summary.FrameCount, "diagnostic frame count");
    AssertEqual(6, summary.SnapshotCount, "diagnostic snapshot count");
    AssertClose(3.667, summary.AverageScheduleDelayMilliseconds, 0.001, "average schedule delay");
    AssertClose(6, summary.P95ScheduleDelayMilliseconds, 0.001, "p95 schedule delay");
    AssertClose(6, summary.MaxScheduleDelayMilliseconds, 0.001, "max schedule delay");
    AssertClose(10.667, summary.AverageReadDurationMilliseconds, 0.001, "average read duration");
    AssertClose(15, summary.P95ReadDurationMilliseconds, 0.001, "p95 read duration");
    AssertEqual(1, summary.LateFrameCount, "late frame count");
    AssertEqual(0, summary.TimeoutFrameCount, "timeout frame count");
    AssertEqual(0, summary.DroppedFrameCount, "dropped frame count");

    return Task.CompletedTask;
}

static async Task PlcAcquisitionEngineRejectsInvalidInterval()
{
    var reader = new SequencePlcTagSnapshotReader();
    var engine = new PlcAcquisitionEngine(reader.OpenSessionAsync);
    var buffer = new PlcSampleBuffer();

    await AssertThrowsAsync<ArgumentOutOfRangeException>(
        () => engine.StartAsync(PlcProjectConfiguration.CreateDefault(), TimeSpan.Zero, buffer, CancellationToken.None),
        "plc acquisition engine zero interval");

    AssertEqual(0, reader.OpenSessionCount, "invalid interval should not open plc session");
}

static Task PlcAcquisitionEngineSkipsOverdueScheduleSlots()
{
    var interval = TimeSpan.FromMilliseconds(100);

    AssertEqual(
        TimeSpan.FromMilliseconds(100),
        PlcAcquisitionEngine.AdvanceNextDue(TimeSpan.Zero, TimeSpan.FromMilliseconds(20), interval),
        "on-time frame advances to the next regular slot");
    AssertEqual(
        TimeSpan.FromMilliseconds(200),
        PlcAcquisitionEngine.AdvanceNextDue(TimeSpan.Zero, TimeSpan.FromMilliseconds(125), interval),
        "slow first frame skips the already overdue 100 ms slot");
    AssertEqual(
        TimeSpan.FromMilliseconds(500),
        PlcAcquisitionEngine.AdvanceNextDue(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(405), interval),
        "late frame advances to the first future schedule slot");
    AssertEqual(
        3,
        PlcAcquisitionEngine.CalculateScheduleAdvance(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(405),
            interval).SkippedScheduleSlots,
        "late frame reports skipped schedule slots");

    return Task.CompletedTask;
}

static Task PlcTrendChartCalculatesLiveRetentionFromTimeWindows()
{
    var standardRetention = PlcTrendChartAdapter.CalculateLiveRetentionWindow(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(100));
    var slowSamplingRetention = PlcTrendChartAdapter.CalculateLiveRetentionWindow(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromSeconds(5));

    AssertEqual(TimeSpan.FromSeconds(310), standardRetention, "standard live trend retention window");
    AssertEqual(TimeSpan.FromSeconds(325), slowSamplingRetention, "slow sampling live trend retention window");
    return Task.CompletedTask;
}

static async Task SqlitePlcLiveDiagnosticsStoreWritesQueuedFrames()
{
    var directory = CreateTestStorageDirectory();
    var databasePath = Path.Combine(directory, "plc-diagnostics.sqlite");
    var store = new SqlitePlcLiveDiagnosticsStore(databasePath);
    var configuration = PlcProjectConfiguration.CreateDefault();
    var started = DateTimeOffset.Parse("2026-08-07T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    var tag = configuration.Tags[0];
    var session = await store.StartSessionAsync(configuration, TimeSpan.FromMinutes(1), CancellationToken.None);

    session.Enqueue(new PlcAcquisitionFrame(
        new[]
        {
            new PlcTagSnapshot(
                tag.Id,
                tag.Name,
                tag.Address,
                12.5,
                tag.Unit,
                started,
                "Good",
                "Test")
        },
        DiagnosticFrame(
            0,
            started,
            plannedMs: 0,
            requestMs: 2,
            responseMs: 12,
            bufferedMs: 13,
            uiMs: 20,
            snapshots: 1,
            PlcAcquisitionFrameState.Normal),
        new[]
        {
            new PlcReadOperationDiagnostics(
                0,
                "S7ReadVar",
                "DB8.DBB6-DBB48",
                3,
                started.AddMilliseconds(2),
                started.AddMilliseconds(12),
                1.5,
                7.5,
                1.0,
                3,
                0,
                null)
        }));
    session.Enqueue(new PlcAcquisitionFrame(
        new[]
        {
            new PlcTagSnapshot(
                tag.Id,
                tag.Name,
                tag.Address,
                13.5,
                tag.Unit,
                started.AddMilliseconds(100),
                "Good",
                "Test")
        },
        DiagnosticFrame(
            1,
            started,
            plannedMs: 100,
            requestMs: 160,
            responseMs: 210,
            bufferedMs: 211,
            uiMs: 220,
            snapshots: 1,
            PlcAcquisitionFrameState.Late) with
        {
            ActualIntervalMilliseconds = 158,
            ResponseIntervalMilliseconds = 198,
            PhaseErrorMilliseconds = 60,
            CatchUpFrame = true,
            PlannedElapsedMilliseconds = 100,
            RequestElapsedMilliseconds = 160,
            ScheduleSlotIndex = 1,
            SkippedScheduleSlots = 2,
            PlannedPhase1000Milliseconds = 100,
            PlannedPhase5000Milliseconds = 100,
            PlannedPhase10000Milliseconds = 100,
            PlannedPhase11000Milliseconds = 100,
            RequestPhase1000Milliseconds = 160,
            RequestPhase5000Milliseconds = 160,
            RequestPhase10000Milliseconds = 160,
            RequestPhase11000Milliseconds = 160
        }));

    var summary = await session.StopAsync(CancellationToken.None);

    AssertEqual(true, File.Exists(databasePath), "sqlite diagnostics database exists");
    AssertEqual(2, summary.FrameCount, "sqlite diagnostics frame count");
    AssertEqual(2, summary.SnapshotCount, "sqlite diagnostics snapshot count");
    AssertEqual(1, summary.LateFrameCount, "sqlite diagnostics late frame count");
    AssertClose(31, summary.AverageScheduleDelayMilliseconds, 0.001, "sqlite diagnostics schedule avg");
    AssertClose(50, summary.MaxReadDurationMilliseconds, 0.001, "sqlite diagnostics max read");

    await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly");
    await connection.OpenAsync();
    await using var command = connection.CreateCommand();
    command.CommandText = """
        SELECT
            COUNT(*),
            operation_kind,
            target,
            address_count,
            duration_ms,
            send_duration_ms,
            receive_header_duration_ms,
            receive_payload_duration_ms,
            success_count,
            failure_count
        FROM plc_read_operations
        GROUP BY operation_kind, target, address_count, duration_ms, send_duration_ms, receive_header_duration_ms, receive_payload_duration_ms, success_count, failure_count;
        """;
    await using var reader = await command.ExecuteReaderAsync();
    AssertEqual(true, await reader.ReadAsync(), "sqlite diagnostics read operation exists");
    AssertEqual(1, (int)reader.GetInt64(0), "sqlite diagnostics read operation count");
    AssertEqual("S7ReadVar", reader.GetString(1), "sqlite diagnostics read operation kind");
    AssertEqual("DB8.DBB6-DBB48", reader.GetString(2), "sqlite diagnostics read operation target");
    AssertEqual(3, reader.GetInt32(3), "sqlite diagnostics read operation address count");
    AssertClose(10, reader.GetDouble(4), 0.001, "sqlite diagnostics read operation duration");
    AssertClose(1.5, reader.GetDouble(5), 0.001, "sqlite diagnostics read operation send duration");
    AssertClose(7.5, reader.GetDouble(6), 0.001, "sqlite diagnostics read operation header duration");
    AssertClose(1, reader.GetDouble(7), 0.001, "sqlite diagnostics read operation payload duration");
    AssertEqual(3, reader.GetInt32(8), "sqlite diagnostics read operation success count");
    AssertEqual(0, reader.GetInt32(9), "sqlite diagnostics read operation failure count");

    await using var frameCommand = connection.CreateCommand();
    frameCommand.CommandText = """
        SELECT
            actual_interval_ms,
            response_interval_ms,
            phase_error_ms,
            catch_up_frame,
            planned_elapsed_ms,
            request_elapsed_ms,
            schedule_slot_index,
            skipped_schedule_slots,
            planned_phase_11000_ms,
            request_phase_11000_ms
        FROM plc_sample_frames
        WHERE frame_index = 1;
        """;
    await using var frameReader = await frameCommand.ExecuteReaderAsync();
    AssertEqual(true, await frameReader.ReadAsync(), "sqlite diagnostics frame timing exists");
    AssertClose(158, frameReader.GetDouble(0), 0.001, "sqlite diagnostics actual interval");
    AssertClose(198, frameReader.GetDouble(1), 0.001, "sqlite diagnostics response interval");
    AssertClose(60, frameReader.GetDouble(2), 0.001, "sqlite diagnostics phase error");
    AssertEqual(1, frameReader.GetInt32(3), "sqlite diagnostics catch-up frame");
    AssertClose(100, frameReader.GetDouble(4), 0.001, "sqlite diagnostics planned elapsed");
    AssertClose(160, frameReader.GetDouble(5), 0.001, "sqlite diagnostics request elapsed");
    AssertEqual(1L, frameReader.GetInt64(6), "sqlite diagnostics schedule slot index");
    AssertEqual(2, frameReader.GetInt32(7), "sqlite diagnostics skipped schedule slots");
    AssertClose(100, frameReader.GetDouble(8), 0.001, "sqlite diagnostics planned 11s phase");
    AssertClose(160, frameReader.GetDouble(9), 0.001, "sqlite diagnostics request 11s phase");
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
        200,
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
    AssertContains("\"minimumSamplingMilliseconds\": 200", json);

    output.Position = 0;
    var roundTripped = await store.LoadAsync(output, CancellationToken.None);

    AssertEqual("line-a-temperature-loop", roundTripped.Name, "plc configuration name");
    AssertEqual("10.10.0.5", roundTripped.IpAddress, "plc ip address");
    AssertEqual(200, roundTripped.MinimumSamplingMilliseconds, "plc minimum sampling milliseconds");
    AssertEqual(1, roundTripped.Tags.Count, "plc tag count");
    AssertEqual(PlcDataType.Double, roundTripped.Tags[0].DataType, "plc tag data type");
    AssertEqual(TagAccessMode.ReadOnly, roundTripped.Tags[0].AccessMode, "plc tag access mode");

    var legacyJson = """
        {
          "schemaVersion": 1,
          "name": "legacy",
          "protocol": "Preview",
          "ipAddress": "127.0.0.1",
          "rack": 0,
          "slot": 1,
          "timeoutMilliseconds": 3000,
          "defaultSamplingMilliseconds": 500,
          "tags": []
        }
        """;
    await using var legacyInput = new MemoryStream(Encoding.UTF8.GetBytes(legacyJson));
    var legacy = await store.LoadAsync(legacyInput, CancellationToken.None);
    AssertEqual(0, legacy.MinimumSamplingMilliseconds, "legacy minimum sampling default remains unset");
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
    viewModel.PlcDefaultSamplingMilliseconds = 1000;
    viewModel.PlcMinimumSamplingMilliseconds = 200;
    viewModel.TagDefinitions[0].SamplingMilliseconds = 200;

    await viewModel.SavePlcConfigurationAsync();

    AssertEqual(true, File.Exists(plcConfigurationPath), "plc configuration file exists");
    AssertEqual(true, viewModel.IsNotificationVisible, "plc save notification visibility");
    AssertContains(Path.GetFullPath(plcConfigurationPath), viewModel.NotificationMessage);

    await using var input = File.OpenRead(plcConfigurationPath);
    var saved = await new JsonPlcProjectConfigurationStore().LoadAsync(input, CancellationToken.None);
    AssertEqual("line-a-temperature-loop", saved.Name, "saved plc configuration name");
    AssertEqual("10.10.0.5", saved.IpAddress, "saved plc ip address");
    AssertEqual(1000, saved.DefaultSamplingMilliseconds, "saved default sampling milliseconds");
    AssertEqual(200, saved.MinimumSamplingMilliseconds, "saved minimum sampling milliseconds");
    AssertEqual(TimeSpan.FromMilliseconds(200), saved.Tags[0].SamplingInterval, "saved tag sampling interval");
    AssertEqual(viewModel.TagDefinitions.Count, saved.Tags.Count, "saved plc tag count");
}

static async Task MainViewModelChecksPlcCommunicationAfterLoadingConfiguration()
{
    var directory = CreateTestStorageDirectory();
    var plcConfigurationPath = Path.Combine(directory, "plc-project.json");
    var configuration = new PlcProjectConfiguration(
        1,
        "loaded-loop",
        "Preview",
        "10.10.0.8",
        0,
        1,
        3000,
        500,
        100,
        PlcProjectConfiguration.CreateDefault().Tags);
    await using (var stream = File.Create(plcConfigurationPath))
    {
        await new JsonPlcProjectConfigurationStore().SaveAsync(configuration, stream, CancellationToken.None);
    }

    var probe = new FixedPlcConnectivityProbe(true);
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(plcProjectConfigurationFile: plcConfigurationPath),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        probe,
        testSessionStorageDirectory: directory);

    await viewModel.LoadPlcConfigurationAsync();

    AssertEqual(1, probe.CheckCount, "plc load configuration communication check count");
    AssertEqual("10.10.0.8", probe.LastHost, "plc load configuration communication check host");
    AssertEqual("PLC 通信检查通过", viewModel.NotificationTitle, "plc load configuration communication notification");
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
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));

    await viewModel.RefreshPlcMonitorAsync();
    await viewModel.RefreshPlcMonitorAsync();

    AssertEqual(true, viewModel.PlcMonitorTags.Count > 0, "plc monitor tag count");
    AssertEqual("2", viewModel.PlcMonitorTags[0].ValueText, "second monitor value");
    AssertEqual(true, viewModel.PlcMonitorTags[0].TrendPoints.Count >= 2, "monitor trend point count");
    var editedAddress = "DB1.DBD120";
    viewModel.TagDefinitions[0].Address = editedAddress;
    await viewModel.RefreshPlcMonitorAsync();
    AssertEqual(editedAddress, viewModel.PlcMonitorTags[0].Address, "monitor address after configuration edit");
    AssertContains("已刷新", viewModel.PlcMonitorStatus);
}

static async Task MainViewModelReusesPlcSessionWhileLiveMonitoring()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    DateTimeOffset? lastTrendTimestamp = null;
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));
    viewModel.PlcDefaultSamplingMilliseconds = 50;
    viewModel.PlcMinimumSamplingMilliseconds = 50;
    viewModel.PlcSnapshotsApplied += (_, timestamp) => lastTrendTimestamp = timestamp;

    await viewModel.TogglePlcMonitoringAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 3);
    await viewModel.RefreshPlcMonitorAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, reader.OpenSessionCount, "live monitor session open count");
    AssertEqual(true, reader.SessionReadCount >= 3, "live monitor session read count");
    AssertEqual(0, reader.ReadCount, "live monitor single read count");
    AssertEqual(true, lastTrendTimestamp.HasValue, "live monitor planned trend timestamp");
    AssertContains("诊断：调度延迟", viewModel.PlcAcquisitionDiagnosticsStatus);
}

static async Task MainViewModelTogglesLiveDiagnosticsWhileMonitoring()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    var diagnosticsStore = new FakePlcLiveDiagnosticsStore();
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        plcLiveDiagnosticsStore: diagnosticsStore,
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));
    viewModel.PlcDefaultSamplingMilliseconds = 50;
    viewModel.PlcMinimumSamplingMilliseconds = 50;
    viewModel.PlcDiagnosticsDurationMinutes = 40;

    await viewModel.TogglePlcLiveDiagnosticsAsync();
    AssertEqual(0, diagnosticsStore.StartCount, "diagnostics should not start without monitoring");
    AssertEqual(30, viewModel.PlcDiagnosticsDurationMinutes, "diagnostics duration clamps to thirty minutes");

    await viewModel.TogglePlcMonitoringAsync();
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 2);
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, diagnosticsStore.StartCount, "diagnostics start count");
    AssertEqual(true, diagnosticsStore.LastSession is not null, "diagnostics session created");
    AssertEqual(true, diagnosticsStore.LastSession!.StopCount >= 1, "diagnostics session stopped");
    AssertContains("帧", viewModel.PlcLiveDiagnosticsStatus);
}

static async Task MainViewModelFiltersDiagnosticsFramesBeforeSessionStart()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    var diagnosticsStore = new FakePlcLiveDiagnosticsStore(DateTimeOffset.UtcNow.AddMinutes(1));
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        plcLiveDiagnosticsStore: diagnosticsStore,
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));
    viewModel.PlcDefaultSamplingMilliseconds = 50;
    viewModel.PlcMinimumSamplingMilliseconds = 50;

    await viewModel.TogglePlcMonitoringAsync();
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 2);
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, diagnosticsStore.StartCount, "future diagnostics start count");
    AssertEqual(0, diagnosticsStore.LastSession!.EnqueueCount, "future diagnostics should ignore frames before session start");
}

static async Task MainViewModelTogglesLiveTrendScrollingPause()
{
    var directory = CreateTestStorageDirectory();
    var recordingDirectory = Path.Combine(directory, "plc-recordings");
    var recorder = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: new SequencePlcTagSnapshotReader(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);
    await recorder.RecordPlcOneSecondAsync();
    var recordingPath = Directory.GetFiles(recordingDirectory, "plc-recording-*.json").Single();

    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(plcRecordingFile: recordingPath),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);

    AssertEqual(false, viewModel.IsPlcLiveTrendPaused, "initial live trend pause state");
    AssertEqual("暂停滚动", viewModel.PlcLiveTrendPauseButtonText, "initial live trend pause button text");

    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(true, viewModel.IsPlcLiveTrendPaused, "paused live trend state");
    AssertEqual("恢复滚动", viewModel.PlcLiveTrendPauseButtonText, "paused live trend pause button text");
    AssertContains("暂停", viewModel.PlcTrendModeStatus);

    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(false, viewModel.IsPlcLiveTrendPaused, "resumed live trend state");
    AssertEqual("暂停滚动", viewModel.PlcLiveTrendPauseButtonText, "resumed live trend pause button text");

    await viewModel.ShowPlcHistoricalTrendAsync();
    AssertEqual(true, viewModel.IsPlcLiveTrendPaused, "historical trend pauses live scrolling");
    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(true, viewModel.IsPlcLiveTrendPaused, "historical trend ignores manual pause toggle");
    viewModel.UsePlcLiveTrendMode();
    AssertEqual(false, viewModel.IsPlcLiveTrendPaused, "live trend mode resumes scrolling");
}

static Task PlcMonitorRowDisplaysMilliseconds()
{
    var snapshot = new PlcTagSnapshot(
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
        "PV",
        "DB1.DBD8",
        42.5,
        "degC",
        new DateTimeOffset(2026, 8, 6, 10, 11, 12, 345, TimeSpan.Zero),
        "Good",
        "Test");
    var row = new PlcTagMonitorViewModel(snapshot);

    AssertEqual("10:11:12.345", row.TimestampText, "plc monitor timestamp milliseconds");
    return Task.CompletedTask;
}

static async Task MainViewModelRecordsOneSecondPlcMonitorFramesAtFastestTagInterval()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));

    foreach (var tag in viewModel.TagDefinitions)
    {
        tag.IsEnabled = false;
    }

    viewModel.TagDefinitions[0].IsEnabled = true;
    viewModel.TagDefinitions[0].SamplingMilliseconds = 50;
    viewModel.TagDefinitions[1].IsEnabled = true;
    viewModel.TagDefinitions[1].SamplingMilliseconds = 500;
    viewModel.PlcMinimumSamplingMilliseconds = 50;

    await viewModel.RecordPlcOneSecondAsync();

    AssertEqual(true, viewModel.LastPlcRecordingFrames.Count >= 18, "recorded frame count");
    AssertEqual(true, viewModel.LastPlcRecordingFrames.All(frame => frame.Count == 2), "recorded frame tag count");
    AssertEqual(1, reader.OpenSessionCount, "plc reader session open count");
    AssertEqual(true, reader.SessionReadCount >= 18, "plc session read count");
    AssertEqual("PLC 1s 记录完成", viewModel.NotificationTitle, "plc recording notification title");
    AssertContains("周期 50 ms", viewModel.PlcMonitorStatus);
    AssertContains("2 个点位", viewModel.PlcMonitorStatus);
    AssertContains("诊断：调度延迟", viewModel.PlcAcquisitionDiagnosticsStatus);
    var recordingPath = Directory.GetFiles(Path.Combine(directory, "plc-recordings"), "plc-recording-*.json").Single();
    AssertContains(Path.GetFullPath(recordingPath), viewModel.NotificationMessage);
    AssertContains("诊断：调度延迟", viewModel.NotificationMessage);
    AssertContains("\"frameCount\"", File.ReadAllText(recordingPath));
    AssertContains("\"diagnostics\"", File.ReadAllText(recordingPath));
}

static async Task MainViewModelLoadsSavedPlcRecordingForReplay()
{
    var directory = CreateTestStorageDirectory();
    var recordingDirectory = Path.Combine(directory, "plc-recordings");
    var recorder = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: new SequencePlcTagSnapshotReader(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);

    await recorder.RecordPlcOneSecondAsync();
    var recordingPath = Directory.GetFiles(recordingDirectory, "plc-recording-*.json").Single();

    var loader = new MainWindowViewModel(
        new NoFileDialogService(plcRecordingFile: recordingPath),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);

    var resetCount = 0;
    var appliedCount = 0;
    var batchAppliedCount = 0;
    var viewportRequestCount = 0;
    DateTimeOffset? requestedViewportStart = null;
    DateTimeOffset? requestedViewportEnd = null;
    var yRangeRequestCount = 0;
    double? requestedYMin = null;
    double? requestedYMax = null;
    loader.PlcTrendResetRequested += () => resetCount++;
    loader.PlcSnapshotsApplied += (_, _) => appliedCount++;
    loader.PlcSnapshotFramesApplied += _ => batchAppliedCount++;
    loader.PlcHistoricalViewportRequested += (start, end) =>
    {
        viewportRequestCount++;
        requestedViewportStart = start;
        requestedViewportEnd = end;
    };
    loader.PlcTrendYRangeRequested += (min, max) =>
    {
        yRangeRequestCount++;
        requestedYMin = min;
        requestedYMax = max;
    };

    await loader.LoadPlcRecordingAsync();

    AssertEqual(true, loader.LastPlcRecordingFrames.Count > 0, "loaded plc recording frame count");
    AssertEqual(true, loader.PlcMonitorTags.Count > 0, "loaded plc monitor tag count");
    AssertEqual(1, resetCount, "loaded plc trend reset count");
    AssertEqual(true, appliedCount > 0, "loaded plc trend applied count");
    AssertContains(Path.GetFullPath(recordingPath), loader.NotificationMessage);
    AssertContains("第 1/", loader.PlcReplayStatus);

    await loader.SetPlcReplaySpeedAsync(2d);
    AssertContains("速度 2x", loader.PlcReplayStatus);

    await loader.StepPlcReplayForwardAsync();
    AssertContains("第 2/", loader.PlcReplayStatus);

    await loader.StepPlcReplayBackwardAsync();
    AssertContains("第 1/", loader.PlcReplayStatus);
    AssertEqual(true, resetCount >= 2, "backward replay trend reset count");

    var appliedCountBeforeHistory = appliedCount;
    await loader.ShowPlcHistoricalTrendAsync();
    AssertEqual(true, loader.IsPlcHistoricalTrendMode, "historical plc trend mode");
    AssertContains("历史", loader.PlcTrendModeStatus);
    AssertContains(loader.LastPlcRecordingFrames.Count.ToString(CultureInfo.InvariantCulture), loader.PlcMonitorStatus);
    AssertEqual(appliedCountBeforeHistory, appliedCount, "historical trend avoids per-frame plot events");
    AssertEqual(1, batchAppliedCount, "historical trend raises one batch plot event");
    AssertEqual(true, loader.IsPlcHistoricalViewportEnabled, "historical viewport slider enabled");
    AssertEqual(true, loader.IsPlcTrendYSliderEnabled, "historical y slider enabled");

    var selectedHistoricalFrame = loader.LastPlcRecordingFrames.First(frame => frame.Count > 0);
    var selectedHistoricalTimestamp = selectedHistoricalFrame[0].Timestamp;
    loader.PlcHistoricalRangeStartText = selectedHistoricalTimestamp.ToString("O", CultureInfo.InvariantCulture);
    loader.PlcHistoricalRangeEndText = selectedHistoricalTimestamp.ToString("O", CultureInfo.InvariantCulture);
    await loader.ApplyPlcHistoricalRangeAsync();
    AssertEqual(1, viewportRequestCount, "historical range requests viewport update");
    AssertEqual(selectedHistoricalTimestamp, requestedViewportStart, "historical viewport start");
    AssertEqual(selectedHistoricalTimestamp, requestedViewportEnd, "historical viewport end");
    AssertEqual(true, loader.IsPlcHistoricalTrendMode, "historical range keeps trend mode");

    await loader.ResetPlcHistoricalRangeAsync();
    AssertEqual(2, viewportRequestCount, "historical reset requests viewport update");
    AssertContains(loader.LastPlcRecordingFrames.Count.ToString(CultureInfo.InvariantCulture), loader.PlcMonitorStatus);

    var historicalTimestamps = loader.LastPlcRecordingFrames
        .Where(frame => frame.Count > 0)
        .Select(frame => frame.Min(snapshot => snapshot.Timestamp))
        .Order()
        .ToArray();
    AssertEqual(0d, loader.PlcHistoricalViewportMinimum, "historical slider normalized minimum");
    AssertEqual(1000d, loader.PlcHistoricalViewportMaximum, "historical slider normalized maximum");
    var sliderStart = loader.PlcHistoricalViewportMinimum +
        (loader.PlcHistoricalViewportMaximum - loader.PlcHistoricalViewportMinimum) / 2d;
    loader.PlcHistoricalViewportStart = sliderStart;
    AssertEqual(3, viewportRequestCount, "historical start slider requests viewport update");
    var expectedMiddleTimestamp = new DateTimeOffset(
        historicalTimestamps[0].Ticks + (long)Math.Round((historicalTimestamps[^1].Ticks - historicalTimestamps[0].Ticks) / 2d),
        historicalTimestamps[0].Offset);
    AssertEqual(
        expectedMiddleTimestamp,
        requestedViewportStart,
        "historical start slider timestamp");

    var historicalValues = loader.LastPlcRecordingFrames
        .SelectMany(frame => frame)
        .Select(snapshot => snapshot.Value)
        .OfType<double>()
        .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
        .ToArray();
    var yMinimum = historicalValues.Min();
    var yMaximum = historicalValues.Max();
    var yPadding = Math.Max((yMaximum - yMinimum) * 0.05d, 1d);
    yMinimum -= yPadding;
    yMaximum += yPadding;
    AssertEqual(0d, loader.PlcTrendYSliderMinimum, "historical y slider normalized minimum");
    AssertEqual(1000d, loader.PlcTrendYSliderMaximum, "historical y slider normalized maximum");
    var sliderYLower = loader.PlcTrendYSliderMinimum +
        (loader.PlcTrendYSliderMaximum - loader.PlcTrendYSliderMinimum) / 4d;
    loader.PlcTrendYLower = sliderYLower;
    AssertEqual(1, yRangeRequestCount, "historical y lower slider requests y range update");
    AssertClose(yMinimum + ((yMaximum - yMinimum) * 0.25d), requestedYMin, 0.0001d, "historical y lower slider value");
    AssertClose(yMaximum, requestedYMax, 0.0001d, "historical y upper slider value");

    loader.UsePlcLiveTrendMode();
    AssertEqual(false, loader.IsPlcHistoricalTrendMode, "live plc trend mode");
    AssertEqual(false, loader.IsPlcHistoricalViewportEnabled, "live plc trend disables historical slider");
    AssertContains("实时", loader.PlcTrendModeStatus);
}

static async Task MainViewModelShowsNotificationsAndRefreshesHistory()
{
    var directory = CreateTestStorageDirectory();
    var sessionRepository = new JsonTestSessionRepository(directory);
    var sampleRepository = new JsonPidSampleRepository(directory);
    var reviewRepository = new JsonPidRecommendationReviewRepository(directory);
    var parameterSetRepository = new JsonPidParameterSetRepository(directory);
    var exportedHistorySamplesPath = Path.Combine(directory, "history-samples.csv");
    var exportedVisibleTrendPath = Path.Combine(directory, "visible-trend.csv");
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(
            historySamplesSaveFile: exportedHistorySamplesPath,
            visiblePlcTrendSaveFile: exportedVisibleTrendPath),
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

    var visibleStart = DateTimeOffset.Parse("2026-07-29T10:00:01.0000000+00:00", CultureInfo.InvariantCulture);
    var visibleEnd = visibleStart.AddSeconds(2);
    var export = new PlcTrendVisibleExport(
        visibleStart,
        visibleEnd,
        true,
        new[]
        {
            new PlcTrendVisibleExportPoint(
                visibleStart.AddMilliseconds(100),
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                "SP,visible",
                "DB8.DBD6",
                42.5,
                "%",
                "Good",
                "Replay")
        });
    await viewModel.ExportVisiblePlcTrendAsync(export);

    AssertEqual("可见趋势已导出", viewModel.NotificationTitle, "visible trend export notification title");
    AssertContains(Path.GetFullPath(exportedVisibleTrendPath), viewModel.NotificationMessage);
    AssertEqual(true, File.Exists(exportedVisibleTrendPath), "exported visible trend file exists");
    var visibleBytes = await File.ReadAllBytesAsync(exportedVisibleTrendPath);
    AssertEqual(true, visibleBytes.Length > 3, "visible trend export has content");
    AssertEqual(0xEF, visibleBytes[0], "visible trend csv utf8 bom byte 1");
    AssertEqual(0xBB, visibleBytes[1], "visible trend csv utf8 bom byte 2");
    AssertEqual(0xBF, visibleBytes[2], "visible trend csv utf8 bom byte 3");
    var visibleCsv = await File.ReadAllTextAsync(exportedVisibleTrendPath, Encoding.UTF8);
    AssertContains("visibleStartUtc,visibleEndUtc,trendMode", visibleCsv);
    AssertContains("\"SP,visible\"", visibleCsv);
    AssertContains("Historical", visibleCsv);
}

static PidSample Sample(DateTimeOffset timestamp, double sp, double pv, double mv, Guid sessionId)
{
    return new PidSample(timestamp, sp, pv, mv, 1.2, 0.4, 0.1, true, sessionId, null);
}

static PlcAcquisitionFrameDiagnostics DiagnosticFrame(
    int frameIndex,
    DateTimeOffset start,
    int plannedMs,
    int requestMs,
    int responseMs,
    int bufferedMs,
    int uiMs,
    int snapshots,
    PlcAcquisitionFrameState state)
{
    return new PlcAcquisitionFrameDiagnostics(
        frameIndex,
        start.AddMilliseconds(plannedMs),
        start.AddMilliseconds(requestMs),
        start.AddMilliseconds(responseMs),
        start.AddMilliseconds(bufferedMs),
        start.AddMilliseconds(uiMs),
        snapshots,
        state);
}

static string CreateTestStorageDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "pidtuner-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static async Task WaitUntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
    while (!condition())
    {
        timeout.Token.ThrowIfCancellationRequested();
        await Task.Delay(10, timeout.Token);
    }
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

static async Task AssertThrowsAsync<TException>(Func<Task> action, string name)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{name}: expected {typeof(TException).Name}");
}

static byte[] BuildS7ReadResponseWithTwoAdjacentRealItems(float first, float second)
{
    var response = new byte[37];
    response[0] = 0x03;
    response[1] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), (ushort)response.Length);
    response[4] = 0x02;
    response[5] = 0xF0;
    response[6] = 0x80;
    response[7] = 0x32;
    response[8] = 0x03;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(13, 2), 2);
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(15, 2), 16);
    response[19] = 0x04;
    response[20] = 0x02;
    WriteS7RealReadItem(response.AsSpan(21, 8), first);
    WriteS7RealReadItem(response.AsSpan(29, 8), second);
    return response;
}

static byte[] BuildS7ReadResponseWithDbBlock(
    int startByte,
    int byteCount,
    IReadOnlyDictionary<int, float> valuesByAbsoluteOffset)
{
    var response = new byte[25 + byteCount];
    response[0] = 0x03;
    response[1] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), (ushort)response.Length);
    response[4] = 0x02;
    response[5] = 0xF0;
    response[6] = 0x80;
    response[7] = 0x32;
    response[8] = 0x03;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(13, 2), 2);
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(15, 2), (ushort)(4 + byteCount));
    response[19] = 0x04;
    response[20] = 0x01;
    response[21] = 0xFF;
    response[22] = 0x04;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(23, 2), (ushort)(byteCount * 8));

    foreach (var (absoluteOffset, value) in valuesByAbsoluteOffset)
    {
        BinaryPrimitives.WriteSingleBigEndian(
            response.AsSpan(25 + absoluteOffset - startByte, 4),
            value);
    }

    return response;
}

static void WriteS7RealReadItem(Span<byte> item, float value)
{
    item[0] = 0xFF;
    item[1] = 0x04;
    BinaryPrimitives.WriteUInt16BigEndian(item[2..4], 32);
    BinaryPrimitives.WriteSingleBigEndian(item[4..8], value);
}

static double? GetS7ReadResultValue(object result)
{
    return (double?)result.GetType().GetProperty("Value")?.GetValue(result);
}

static string? GetS7ReadResultError(object result)
{
    return (string?)result.GetType().GetProperty("Error")?.GetValue(result);
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
    string? visiblePlcTrendSaveFile = null,
    string? plcProjectConfigurationFile = null,
    string? plcProjectConfigurationSaveFile = null,
    string? plcRecordingFile = null) : IOpenFileDialogService
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

    public string? PickVisiblePlcTrendSaveFile()
    {
        return visiblePlcTrendSaveFile;
    }

    public string? PickPlcRecordingFile()
    {
        return plcRecordingFile;
    }
}

file sealed class FixedPlcConnectivityProbe(bool reachable) : IPlcConnectivityProbe
{
    public int CheckCount { get; private set; }

    public string? LastHost { get; private set; }

    public Task<PlcCommunicationCheck> CheckAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        CheckCount++;
        LastHost = configuration.IpAddress;
        return Task.FromResult(new PlcCommunicationCheck(
            reachable,
            configuration.IpAddress,
            TimeSpan.FromMilliseconds(1),
            reachable ? "Ping 成功，往返 1 ms。" : "Ping 未成功。",
            DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+08:00", CultureInfo.InvariantCulture)));
    }
}

file sealed class SequencePlcTagSnapshotReader : IPlcTagSnapshotReader, IPlcTagSnapshotSessionReader
{
    private int _value;

    public int ReadCount { get; private set; }

    public int OpenSessionCount { get; private set; }

    public int SessionReadCount { get; private set; }

    public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ReadCount++;
        return CaptureAsync(configuration);
    }

    public Task<IPlcTagSnapshotReadSession> OpenSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        OpenSessionCount++;
        return Task.FromResult<IPlcTagSnapshotReadSession>(new Session(this, configuration));
    }

    private Task<IReadOnlyList<PlcTagSnapshot>> CaptureAsync(PlcProjectConfiguration configuration)
    {
        _value++;
        var timestamp = DateTimeOffset.Parse("2026-07-29T10:00:00.0000000+08:00", CultureInfo.InvariantCulture)
            .AddMilliseconds(_value * 200);
        IReadOnlyList<PlcTagSnapshot> snapshots = configuration.Tags
            .Where(item => item.IsEnabled && item.AccessMode != TagAccessMode.WriteOnly)
            .Select(tag => new PlcTagSnapshot(
                tag.Id,
                tag.Name,
                tag.Address,
                _value,
                tag.Unit,
                timestamp,
                "Good",
                "Test"))
            .ToArray();

        return Task.FromResult(snapshots);
    }

    private sealed class Session(
        SequencePlcTagSnapshotReader reader,
        PlcProjectConfiguration configuration) : IPlcTagSnapshotReadSession
    {
        public Task<IReadOnlyList<PlcTagSnapshot>> ReadAsync(CancellationToken cancellationToken)
        {
            reader.SessionReadCount++;
            return reader.CaptureAsync(configuration);
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}

file sealed class FakePlcLiveDiagnosticsStore(DateTimeOffset? startedAtUtc = null) : IPlcLiveDiagnosticsStore
{
    public int StartCount { get; private set; }

    public FakePlcLiveDiagnosticsSession? LastSession { get; private set; }

    public Task<IPlcLiveDiagnosticsSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        StartCount++;
        LastSession = new FakePlcLiveDiagnosticsSession(duration, startedAtUtc);
        return Task.FromResult<IPlcLiveDiagnosticsSession>(LastSession);
    }
}

file sealed class FakePlcLiveDiagnosticsSession(TimeSpan duration, DateTimeOffset? startedAtUtc) : IPlcLiveDiagnosticsSession
{
    public int EnqueueCount { get; private set; }

    public int StopCount { get; private set; }

    public Guid SessionId { get; } = Guid.NewGuid();

    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), "fake-plc-live-diagnostics.sqlite");

    public DateTimeOffset StartedAtUtc { get; } = startedAtUtc ?? DateTimeOffset.UtcNow;

    public DateTimeOffset EndsAtUtc { get; } = (startedAtUtc ?? DateTimeOffset.UtcNow).Add(duration);

    public void Enqueue(PlcAcquisitionFrame frame)
    {
        EnqueueCount++;
    }

    public Task<PlcLiveDiagnosticsSummary> StopAsync(CancellationToken cancellationToken)
    {
        StopCount++;
        return Task.FromResult(new PlcLiveDiagnosticsSummary(
            SessionId,
            DatabasePath,
            EnqueueCount,
            EnqueueCount,
            1,
            2,
            3,
            4,
            0,
            EnqueueCount,
            3,
            4,
            0));
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
