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
    ("historical time picker values stay ordered and inside data range", HistoricalTimePickerValuesStayOrdered),
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
    ("s7 communication failures use stable categories and codes", S7CommunicationFailuresUseStableCategoriesAndCodes),
    ("s7 db read planner honors pdu and sparse gaps", S7DbReadPlannerHonorsPduAndSparseGaps),
    ("s7 setup response exposes negotiated pdu length", S7SetupResponseExposesNegotiatedPduLength),
    ("s7 response rejects mismatched pdu reference", S7ResponseRejectsMismatchedPduReference),
    ("plc acquisition diagnostics summarizes frame timing", PlcAcquisitionDiagnosticsSummarizesFrameTiming),
    ("plc read diagnostics summarize latency and payload efficiency", PlcReadDiagnosticsSummarizeLatencyAndPayloadEfficiency),
    ("plc acquisition engine rejects invalid interval", PlcAcquisitionEngineRejectsInvalidInterval),
    ("plc acquisition engine skips overdue schedule slots", PlcAcquisitionEngineSkipsOverdueScheduleSlots),
    ("plc trend chart calculates live retention from time windows", PlcTrendChartCalculatesLiveRetentionFromTimeWindows),
    ("sqlite plc live diagnostics store writes queued frames", SqlitePlcLiveDiagnosticsStoreWritesQueuedFrames),
    ("sqlite plc historical store queries planned-time frames", SqlitePlcHistoricalStoreQueriesPlannedTimeFrames),
    ("sqlite plc historical store sustains thirty simulated minutes", SqlitePlcHistoricalStoreSustainsThirtySimulatedMinutes),
    ("historical trend coordinator merges buffered frames", HistoricalTrendCoordinatorMergesBufferedFrames),
    ("main view model respects infrastructure seam", MainViewModelRespectsInfrastructureSeam),
    ("plc project configuration store round trips editable connection and tags", PlcProjectConfigurationStoreRoundTripsEditableConnectionAndTags),
    ("main view model saves plc configuration with absolute path notification", MainViewModelSavesPlcConfigurationWithAbsolutePathNotification),
    ("main view model checks plc communication after loading configuration", MainViewModelChecksPlcCommunicationAfterLoadingConfiguration),
    ("main view model checks plc communication through injected probe", MainViewModelChecksPlcCommunicationThroughInjectedProbe),
    ("main view model refreshes plc monitor snapshots and trends", MainViewModelRefreshesPlcMonitorSnapshotsAndTrends),
    ("main view model shows live snapshots as historical trend", MainViewModelShowsLiveSnapshotsAsHistoricalTrend),
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

static Task HistoricalTimePickerValuesStayOrdered()
{
    var start = new DateTimeOffset(2026, 8, 19, 10, 0, 0, TimeSpan.Zero);
    var end = start.AddMinutes(1);
    var tagId = Guid.Parse("20000000-0000-0000-0000-000000000003");
    IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames = new[]
    {
        new[] { new PlcTagSnapshot(tagId, "SP", "DB1.DBD6", 10, null, start, "Good", "PLC") },
        new[] { new PlcTagSnapshot(tagId, "SP", "DB1.DBD6", 11, null, end, "Good", "PLC") },
    };
    var workbench = new HistoricalTrendWorkbenchViewModel();

    workbench.LoadFrames(frames);
    workbench.RangeStartValue = start.AddSeconds(40);
    workbench.RangeEndValue = start.AddSeconds(20);

    AssertEqual(start.AddSeconds(40), workbench.RangeEndValue, "end cannot precede start");

    workbench.RangeStartValue = start.AddMinutes(-1);
    workbench.RangeEndValue = end.AddMinutes(1);

    AssertEqual(start, workbench.RangeStartValue, "start clamps to dataset minimum");
    AssertEqual(end, workbench.RangeEndValue, "end clamps to dataset maximum");
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

static Task S7CommunicationFailuresUseStableCategoriesAndCodes()
{
    var failureType = typeof(SiemensS7Client).Assembly.GetType(
        "PIDTuner.Infrastructure.Plc.SiemensS7CommunicationFailure",
        throwOnError: true)!;
    var classifier = failureType.GetMethod(
        "FromException",
        BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("S7 failure classifier was not found.");
    var failure = classifier.Invoke(null, new object[] { new IOException("socket closed") })
        ?? throw new InvalidOperationException("S7 failure classifier returned null.");

    AssertEqual(
        PlcCommunicationErrorCategory.Connection,
        failureType.GetProperty("Category")?.GetValue(failure),
        "S7 IO failure category");
    AssertEqual(
        "S7.CONNECTION_IO",
        failureType.GetProperty("Code")?.GetValue(failure),
        "S7 IO failure code");
    AssertEqual(
        true,
        failureType.GetProperty("IsTransient")?.GetValue(failure),
        "S7 IO failure transient flag");
    return Task.CompletedTask;
}

static Task S7DbReadPlannerHonorsPduAndSparseGaps()
{
    var plannerType = typeof(SiemensS7Client).Assembly.GetType(
        "PIDTuner.Infrastructure.Plc.S7DbReadPlanner",
        throwOnError: true)!;
    var planMethod = plannerType.GetMethod("Plan", BindingFlags.Public | BindingFlags.Static)
        ?? throw new InvalidOperationException("S7 DB read planner was not found.");
    var addresses = new[]
    {
        S7AddressParser.Parse("DB8.DBD6", PlcDataType.Float),
        S7AddressParser.Parse("DB8.DBD10", PlcDataType.Float),
        S7AddressParser.Parse("DB8.DBD48", PlcDataType.Float),
        S7AddressParser.Parse("DB8.DBD200", PlcDataType.Float),
        S7AddressParser.Parse("DB9.DBD0", PlcDataType.Float)
    };
    var blocks = ((System.Collections.IEnumerable)(planMethod.Invoke(null, new object[] { addresses, 100 })
        ?? throw new InvalidOperationException("S7 DB read planner returned null.")))
        .Cast<object>()
        .ToArray();

    AssertEqual(3, blocks.Length, "S7 DB read planned block count");
    AssertEqual(6, GetIntProperty(blocks[0], "StartByte"), "S7 first block start");
    AssertEqual(46, GetIntProperty(blocks[0], "ByteCount"), "S7 existing sparse points remain coherent");
    AssertEqual(200, GetIntProperty(blocks[1], "StartByte"), "S7 large sparse gap starts another block");
    AssertEqual(9, GetIntProperty(blocks[2], "DataBlock"), "S7 different DB starts another block");

    var pduBlocks = ((System.Collections.IEnumerable)(planMethod.Invoke(null, new object[] { addresses[..3], 40 })
        ?? throw new InvalidOperationException("S7 DB read planner returned null.")))
        .Cast<object>()
        .ToArray();
    AssertEqual(2, pduBlocks.Length, "S7 PDU payload splits oversized coverage");
    return Task.CompletedTask;
}

static Task S7SetupResponseExposesNegotiatedPduLength()
{
    var response = BuildS7SetupCommunicationResponse(480);
    var method = typeof(SiemensS7Client).GetMethod(
        "ParseSetupCommunicationResponse",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("S7 setup response parser was not found.");

    AssertEqual(480, (int)method.Invoke(null, new object[] { response })!, "negotiated S7 PDU length");
    return Task.CompletedTask;
}

static Task S7ResponseRejectsMismatchedPduReference()
{
    var response = BuildS7ReadResponseWithDbBlock(0, 4, new Dictionary<int, float> { [0] = 1f });
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(11, 2), 7);
    var method = typeof(SiemensS7Client).GetMethod(
        "ValidateResponsePduReference",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("S7 PDU reference validator was not found.");

    try
    {
        method.Invoke(null, new object[] { response, (ushort)6 });
    }
    catch (TargetInvocationException exception) when (exception.InnerException is not null)
    {
        AssertContains("does not match request", exception.InnerException.Message);
        return Task.CompletedTask;
    }

    throw new InvalidOperationException("S7 mismatched PDU reference was accepted.");
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

static Task PlcReadDiagnosticsSummarizeLatencyAndPayloadEfficiency()
{
    var start = DateTimeOffset.UtcNow;
    var operations = new[]
    {
        new PlcReadOperationDiagnostics(
            0, "S7ReadDbBlock", "DB8.DBB6-DBB51", 3,
            start, start.AddMilliseconds(8), 1, 5, 2, 3, 0, null,
            RequestedPayloadBytes: 46, UsefulPayloadBytes: 12, NegotiatedPduLength: 480),
        new PlcReadOperationDiagnostics(
            1, "S7ReadDbBlock", "DB8.DBB200-DBB203", 1,
            start, start.AddMilliseconds(20), 1, 15, 4, 0, 1, "timeout",
            PlcCommunicationErrorCategory.Timeout, "S7.TIMEOUT", "ReadVar", true,
            RequestedPayloadBytes: 4, UsefulPayloadBytes: 4, NegotiatedPduLength: 480)
    };

    var summary = PlcReadOperationsDiagnostics.Summarize(operations);

    AssertEqual(2, summary.OperationCount, "PLC read diagnostic operation count");
    AssertEqual(4, summary.AddressCount, "PLC read diagnostic address count");
    AssertEqual(50, summary.RequestedPayloadBytes, "PLC read diagnostic requested bytes");
    AssertEqual(16, summary.UsefulPayloadBytes, "PLC read diagnostic useful bytes");
    AssertClose(0.32, summary.PayloadEfficiency, 0.001, "PLC read diagnostic payload efficiency");
    AssertClose(14, summary.AverageDurationMilliseconds, 0.001, "PLC read diagnostic average duration");
    AssertClose(20, summary.P95DurationMilliseconds, 0.001, "PLC read diagnostic p95 duration");
    AssertClose(20, summary.P99DurationMilliseconds, 0.001, "PLC read diagnostic p99 duration");
    AssertEqual(1, summary.FailedOperationCount, "PLC read diagnostic failed operations");
    AssertEqual(1, summary.TransientFailureCount, "PLC read diagnostic transient failures");
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
                2,
                1,
                "socket closed",
                PlcCommunicationErrorCategory.Connection,
                "S7.CONNECTION_IO",
                "TCP stream",
                true,
                23,
                23,
                46,
                12,
                480)
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
            failure_count,
            error_category,
            error_code,
            error_context,
            is_transient,
            request_pdu_reference,
            response_pdu_reference,
            requested_payload_bytes,
            useful_payload_bytes,
            negotiated_pdu_length
        FROM plc_read_operations
        GROUP BY operation_kind, target, address_count, duration_ms, send_duration_ms, receive_header_duration_ms, receive_payload_duration_ms, success_count, failure_count, error_category, error_code, error_context, is_transient, request_pdu_reference, response_pdu_reference, requested_payload_bytes, useful_payload_bytes, negotiated_pdu_length;
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
    AssertEqual(2, reader.GetInt32(8), "sqlite diagnostics read operation success count");
    AssertEqual(1, reader.GetInt32(9), "sqlite diagnostics read operation failure count");
    AssertEqual("Connection", reader.GetString(10), "sqlite diagnostics read operation error category");
    AssertEqual("S7.CONNECTION_IO", reader.GetString(11), "sqlite diagnostics read operation error code");
    AssertEqual("TCP stream", reader.GetString(12), "sqlite diagnostics read operation error context");
    AssertEqual(1, reader.GetInt32(13), "sqlite diagnostics read operation transient flag");
    AssertEqual(23, reader.GetInt32(14), "sqlite diagnostics request PDU reference");
    AssertEqual(23, reader.GetInt32(15), "sqlite diagnostics response PDU reference");
    AssertEqual(46, reader.GetInt32(16), "sqlite diagnostics requested payload bytes");
    AssertEqual(12, reader.GetInt32(17), "sqlite diagnostics useful payload bytes");
    AssertEqual(480, reader.GetInt32(18), "sqlite diagnostics negotiated PDU length");

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

    viewModel.PlcConfigurationEditor.ConfigurationName = "line-a-temperature-loop";
    viewModel.PlcConfigurationEditor.IpAddress = "10.10.0.5";
    viewModel.PlcConfigurationEditor.DefaultSamplingMilliseconds = 1000;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 200;
    viewModel.PlcConfigurationEditor.TagDefinitions[0].SamplingMilliseconds = 200;

    await viewModel.SavePlcConfigurationAsync();

    AssertEqual(true, File.Exists(plcConfigurationPath), "plc configuration file exists");
    AssertEqual(true, viewModel.Notification.IsVisible, "plc save notification visibility");
    AssertContains(Path.GetFullPath(plcConfigurationPath), viewModel.Notification.Message);

    await using var input = File.OpenRead(plcConfigurationPath);
    var saved = await new JsonPlcProjectConfigurationStore().LoadAsync(input, CancellationToken.None);
    AssertEqual("line-a-temperature-loop", saved.Name, "saved plc configuration name");
    AssertEqual("10.10.0.5", saved.IpAddress, "saved plc ip address");
    AssertEqual(1000, saved.DefaultSamplingMilliseconds, "saved default sampling milliseconds");
    AssertEqual(200, saved.MinimumSamplingMilliseconds, "saved minimum sampling milliseconds");
    AssertEqual(TimeSpan.FromMilliseconds(200), saved.Tags[0].SamplingInterval, "saved tag sampling interval");
    AssertEqual(viewModel.PlcConfigurationEditor.TagDefinitions.Count, saved.Tags.Count, "saved plc tag count");
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
    AssertEqual("PLC 通信检查通过", viewModel.Notification.Title, "plc load configuration communication notification");
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

    viewModel.PlcConfigurationEditor.IpAddress = "127.0.0.1";

    await viewModel.CheckPlcCommunicationAsync();

    AssertEqual("PLC 通信检查通过", viewModel.Notification.Title, "plc communication notification title");
    AssertContains("127.0.0.1", viewModel.PlcConfigurationEditor.CommunicationStatus);
    AssertContains("Ping 成功", viewModel.PlcConfigurationEditor.CommunicationStatus);
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

    AssertEqual(true, viewModel.LiveMonitor.Tags.Count > 0, "plc monitor tag count");
    AssertEqual("2", viewModel.LiveMonitor.Tags[0].ValueText, "second monitor value");
    AssertEqual(true, viewModel.LiveMonitor.Tags[0].TrendPoints.Count >= 2, "monitor trend point count");
    var editedAddress = "DB1.DBD120";
    viewModel.PlcConfigurationEditor.TagDefinitions[0].Address = editedAddress;
    await viewModel.RefreshPlcMonitorAsync();
    AssertEqual(editedAddress, viewModel.LiveMonitor.Tags[0].Address, "monitor address after configuration edit");
    AssertContains("已刷新", viewModel.LiveMonitor.MonitorStatus);
}

static async Task SqlitePlcHistoricalStoreQueriesPlannedTimeFrames()
{
    var directory = CreateTestStorageDirectory();
    var databasePath = Path.Combine(directory, "plc-history.sqlite");
    var store = new SqlitePlcHistoricalTrendStore(databasePath);
    var configuration = PlcProjectConfiguration.CreateDefault();
    var origin = DateTimeOffset.Parse("2026-08-20T10:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    var tag = configuration.Tags[0];
    var session = await store.StartSessionAsync(configuration, CancellationToken.None);

    for (var index = 0; index < 3; index++)
    {
        var planned = origin.AddMilliseconds(index * 100);
        session.Enqueue(new PlcAcquisitionFrame(
            new[]
            {
                new PlcTagSnapshot(
                    tag.Id,
                    tag.Name,
                    tag.Address,
                    10d + index,
                    tag.Unit,
                    planned.AddMilliseconds(37),
                    "Good",
                    "Test")
            },
            DiagnosticFrame(
                index,
                origin,
                plannedMs: index * 100,
                requestMs: index * 100 + 12,
                responseMs: index * 100 + 25,
                bufferedMs: index * 100 + 27,
                uiMs: index * 100 + 40,
                snapshots: 1,
                PlcAcquisitionFrameState.Normal)));
    }

    var summary = await session.StopAsync(CancellationToken.None);
    var availableRange = await store.GetAvailableRangeAsync(CancellationToken.None);
    var frames = await store.QueryFramesAsync(
        origin.AddMilliseconds(50),
        origin.AddMilliseconds(250),
        maximumPointsPerTag: 100,
        CancellationToken.None);

    AssertEqual(3, summary.FrameCount, "historical sqlite frame count");
    AssertEqual(3, summary.SnapshotCount, "historical sqlite snapshot count");
    AssertEqual(Path.GetFullPath(databasePath), summary.DatabasePath, "historical sqlite absolute path");
    AssertEqual(origin, availableRange!.Value.Start, "historical sqlite available start");
    AssertEqual(origin.AddMilliseconds(200), availableRange.Value.End, "historical sqlite available end");
    AssertEqual(2, frames.Count, "historical sqlite range frame count");
    AssertEqual(origin.AddMilliseconds(100), frames[0][0].Timestamp, "historical sqlite uses planned timestamp");
    AssertClose(11d, frames[0][0].Value!.Value, 0.001, "historical sqlite queried value");
}

static async Task SqlitePlcHistoricalStoreSustainsThirtySimulatedMinutes()
{
    const int frameCount = 18_000;
    var directory = CreateTestStorageDirectory();
    var store = new SqlitePlcHistoricalTrendStore(Path.Combine(directory, "plc-history-30min.sqlite"));
    var configuration = PlcProjectConfiguration.CreateDefault();
    var tag = configuration.Tags[0];
    var origin = DateTimeOffset.Parse("2026-08-20T11:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    var session = await store.StartSessionAsync(configuration, CancellationToken.None);

    for (var index = 0; index < frameCount; index++)
    {
        var timestamp = origin.AddMilliseconds(index * 100L);
        session.Enqueue(new PlcAcquisitionFrame(
            new[]
            {
                new PlcTagSnapshot(
                    tag.Id,
                    tag.Name,
                    tag.Address,
                    index,
                    tag.Unit,
                    timestamp,
                    "Good",
                    "StabilityTest")
            },
            DiagnosticFrame(
                index,
                origin,
                plannedMs: index * 100,
                requestMs: index * 100 + 5,
                responseMs: index * 100 + 15,
                bufferedMs: index * 100 + 16,
                uiMs: index * 100 + 30,
                snapshots: 1,
                PlcAcquisitionFrameState.Normal)));
    }

    var summary = await session.StopAsync(CancellationToken.None);
    var range = await store.GetAvailableRangeAsync(CancellationToken.None);
    var displayFrames = await store.QueryFramesAsync(
        origin,
        origin.AddMilliseconds((frameCount - 1) * 100L),
        maximumPointsPerTag: 600,
        CancellationToken.None);

    AssertEqual(frameCount, summary.FrameCount, "thirty minute historical frame count");
    AssertEqual(frameCount, summary.SnapshotCount, "thirty minute historical snapshot count");
    AssertEqual(origin, range!.Value.Start, "thirty minute historical start");
    AssertEqual(origin.AddMilliseconds((frameCount - 1) * 100L), range.Value.End, "thirty minute historical end");
    AssertEqual(true, displayFrames.Count <= 602, "thirty minute query is bounded for plotting");
    AssertEqual(
        origin.ToUnixTimeMilliseconds(),
        displayFrames[0][0].Timestamp.ToUnixTimeMilliseconds(),
        "thirty minute query preserves first point");
    AssertEqual(
        range.Value.End.ToUnixTimeMilliseconds(),
        displayFrames[^1][0].Timestamp.ToUnixTimeMilliseconds(),
        "thirty minute query preserves last point");
}

static async Task HistoricalTrendCoordinatorMergesBufferedFrames()
{
    var store = new FakePlcHistoricalTrendStore();
    var coordinator = new PlcHistoricalTrendCoordinator(store);
    var configuration = PlcProjectConfiguration.CreateDefault();
    var tag = configuration.Tags[0];
    var timestamp = DateTimeOffset.Parse("2026-08-20T12:00:00.0000000+00:00", CultureInfo.InvariantCulture);
    var persistedFrame = new PlcAcquisitionFrame(
        new[]
        {
            new PlcTagSnapshot(tag.Id, tag.Name, tag.Address, 10d, tag.Unit, timestamp, "Good", "SQLite")
        },
        DiagnosticFrame(0, timestamp, 0, 1, 2, 3, 4, 1, PlcAcquisitionFrameState.Normal));
    var session = await store.StartSessionAsync(configuration, CancellationToken.None);
    session.Enqueue(persistedFrame);

    coordinator.ObserveLiveFrame(
        persistedFrame with
        {
            Snapshots = new[]
            {
                new PlcTagSnapshot(tag.Id, tag.Name, tag.Address, 20d, tag.Unit, timestamp, "Good", "Buffer")
            }
        },
        samplingIntervalMilliseconds: 100);
    var frames = await coordinator.LoadRangeAsync(
        timestamp.AddSeconds(-1),
        timestamp.AddSeconds(1),
        CancellationToken.None);

    AssertEqual(1, frames.Count, "historical coordinator merged frame count");
    AssertEqual(1, frames[0].Count, "historical coordinator merged tag count");
    AssertClose(20d, frames[0][0].Value!.Value, 0.001, "historical coordinator buffered value wins");
}

static Task MainViewModelRespectsInfrastructureSeam()
{
    var root = FindRepositoryRootForTests();
    var source = File.ReadAllText(Path.Combine(
        root,
        "src",
        "PIDTuner.Desktop",
        "ViewModels",
        "MainWindowViewModel.cs"));

    AssertEqual(false, source.Contains("PIDTuner.Infrastructure", StringComparison.Ordinal), "main view model infrastructure namespace");
    AssertEqual(false, source.Contains("QueryFramesAsync(", StringComparison.Ordinal), "main view model historical query implementation");
    AssertEqual(false, source.Contains("SqlitePlc", StringComparison.Ordinal), "main view model sqlite implementation");
    AssertEqual(false, source.Contains("_plcHistoricalWriter", StringComparison.Ordinal), "main view model historical writer ownership");
    AssertEqual(false, source.Contains("IPlcHistoricalTrendWriteSession", StringComparison.Ordinal), "main view model historical session ownership");
    AssertEqual(false, source.Contains("StartPlcHistoricalRecording", StringComparison.Ordinal), "main view model historical start workflow");
    AssertEqual(false, source.Contains("StopPlcHistoricalRecording", StringComparison.Ordinal), "main view model historical stop workflow");
    AssertEqual(false, source.Contains("StreamWriter", StringComparison.Ordinal), "main view model csv writer implementation");
    AssertEqual(false, source.Contains("EscapeCsv", StringComparison.Ordinal), "main view model csv escaping implementation");
    AssertEqual(false, source.Contains("File.OpenRead", StringComparison.Ordinal), "main view model recording file access");
    AssertEqual(false, source.Contains("DeserializeAsync<PlcOneSecondRecording>", StringComparison.Ordinal), "main view model recording deserialization");
    AssertEqual(false, source.Contains("_plcReplayTimer", StringComparison.Ordinal), "main view model replay timer ownership");
    AssertEqual(false, source.Contains("ApplyNextPlcReplayFrame", StringComparison.Ordinal), "main view model replay tick implementation");
    AssertEqual(false, source.Contains("_plcLiveDiagnosticsTimer", StringComparison.Ordinal), "main view model diagnostics timer ownership");
    AssertEqual(false, source.Contains("StopExpiredPlcLiveDiagnosticsAsync", StringComparison.Ordinal), "main view model diagnostics expiration polling");
    AssertEqual(false, source.Contains("_experimentSessionCoordinator", StringComparison.Ordinal), "main view model experiment repository orchestration");
    AssertEqual(false, source.Contains("AnalyzeHistorySessionAsync", StringComparison.Ordinal), "main view model history comparison analysis");
    AssertEqual(false, source.Contains("FindRepositoryRoot", StringComparison.Ordinal), "main view model repository discovery");
    AssertEqual(false, source.Contains("DirectoryInfo", StringComparison.Ordinal), "main view model filesystem traversal");
    AssertEqual(false, source.Contains("_monitorTimer", StringComparison.Ordinal), "main view model live refresh timer ownership");
    AssertEqual(false, source.Contains("DrainPresentedFrames", StringComparison.Ordinal), "main view model sample buffer draining");
    AssertEqual(false, source.Contains("IPlcTagSnapshotSessionReader", StringComparison.Ordinal), "main view model plc session capability detection");
    AssertEqual(false, source.Contains("SingleReadSnapshotSession", StringComparison.Ordinal), "main view model plc session fallback");
    AssertEqual(false, source.Contains("public int PlcDiagnosticsDurationMinutes", StringComparison.Ordinal), "main view model diagnostics duration proxy");
    AssertEqual(false, source.Contains("public string PlcReplayStatus", StringComparison.Ordinal), "main view model replay status proxy");
    AssertEqual(false, source.Contains("Debug_PropertyChanged", StringComparison.Ordinal), "main view model debug property forwarding");
    AssertEqual(false, source.Contains("public bool IsPlcMonitoring", StringComparison.Ordinal), "main view model live monitoring proxy");
    AssertEqual(false, source.Contains("public bool IsPlcHistoricalTrendMode", StringComparison.Ordinal), "main view model trend mode proxy");
    AssertEqual(false, source.Contains("public string NotificationTitle", StringComparison.Ordinal), "main view model notification proxy");
    AssertEqual(false, source.Contains("Notification_PropertyChanged", StringComparison.Ordinal), "main view model notification property forwarding");
    AssertEqual(false, source.Contains("PlcConfigurationEditor_PropertyChanged", StringComparison.Ordinal), "main view model configuration property forwarding");
    AssertEqual(false, source.Contains("OfflineAnalysis_PropertyChanged", StringComparison.Ordinal), "main view model analysis property forwarding");
    AssertEqual(false, source.Contains("ExperimentHistory_PropertyChanged", StringComparison.Ordinal), "main view model experiment history property forwarding");
    AssertEqual(false, source.Contains("_plcCommunicationStatus", StringComparison.Ordinal), "main view model communication status ownership");
    AssertEqual(false, source.Contains("_plcMonitorStatus", StringComparison.Ordinal), "main view model monitor status ownership");
    AssertEqual(false, source.Contains("_plcAcquisitionDiagnosticsStatus", StringComparison.Ordinal), "main view model acquisition status ownership");
    AssertEqual(false, source.Contains("public string PlcCommunicationStatus", StringComparison.Ordinal), "main view model communication status proxy");
    AssertEqual(false, source.Contains("public string PlcMonitorStatus", StringComparison.Ordinal), "main view model monitor status proxy");
    AssertEqual(false, source.Contains("public string PlcAcquisitionDiagnosticsStatus", StringComparison.Ordinal), "main view model acquisition status proxy");
    AssertEqual(false, source.Contains("_fieldProfileWorkflow", StringComparison.Ordinal), "main view model field profile workflow ownership");
    AssertEqual(false, source.Contains("BuildProfileFromGrid()", StringComparison.Ordinal), "main view model field profile validation");
    AssertEqual(false, source.Contains("_plcConfigurationWorkflow", StringComparison.Ordinal), "main view model plc configuration workflow ownership");
    AssertEqual(false, source.Contains("BuildPlcConfigurationFromForm", StringComparison.Ordinal), "main view model plc configuration construction wrapper");
    AssertEqual(false, source.Contains("_analysisResultExportWorkflow", StringComparison.Ordinal), "main view model analysis export workflow ownership");
    AssertEqual(false, source.Contains("OfflineAnalysis.LastMetrics", StringComparison.Ordinal), "main view model analysis export state inspection");
    AssertEqual(false, source.Contains("_plcTrendVisibleExportWorkflow", StringComparison.Ordinal), "main view model trend export workflow ownership");
    AssertEqual(false, source.Contains("_lastPlcRecordingFrames", StringComparison.Ordinal), "main view model historical frame ownership");
    AssertEqual(false, source.Contains("LastPlcRecordingFrames", StringComparison.Ordinal), "main view model historical frame proxy");
    AssertEqual(false, source.Contains("_plcLiveMonitoringController", StringComparison.Ordinal), "main view model live monitoring controller ownership");
    AssertEqual(false, source.Contains("_plcSnapshotSessionFactory", StringComparison.Ordinal), "main view model snapshot session ownership");
    AssertEqual(false, source.Contains("_plcMonitorSnapshotPresenter", StringComparison.Ordinal), "main view model snapshot presenter ownership");
    AssertEqual(false, source.Contains("ApplyBufferedLiveMonitorFrames", StringComparison.Ordinal), "main view model live frame distribution");
    AssertEqual(false, source.Contains("ApplyPlcMonitorSnapshots", StringComparison.Ordinal), "main view model snapshot presentation");
    AssertEqual(false, source.Contains("PlcTrendMode_PropertyChanged", StringComparison.Ordinal), "main view model trend mode synchronization");
    AssertEqual(false, source.Contains("ApplyHistoricalTrendAction", StringComparison.Ordinal), "main view model historical action interpretation");
    AssertEqual(false, source.Contains("ShowLoadedPlcHistoricalTrend", StringComparison.Ordinal), "main view model historical frame presentation");
    AssertEqual(false, source.Contains("ShowPlcHistoricalTrendFromStore", StringComparison.Ordinal), "main view model historical query coordination");
    AssertEqual(false, source.Contains("_plcOneSecondRecorder", StringComparison.Ordinal), "main view model recorder ownership");
    AssertEqual(false, source.Contains("_plcReplayController", StringComparison.Ordinal), "main view model replay controller ownership");
    AssertEqual(false, source.Contains("ApplyPlcReplayOperation", StringComparison.Ordinal), "main view model replay result interpretation");
    AssertEqual(false, source.Contains("EnsurePlcReplayLoaded", StringComparison.Ordinal), "main view model replay validation");
    AssertEqual(false, source.Contains("OfflineAnalysis.LastSamples", StringComparison.Ordinal), "main view model parameter set sample assembly");
    AssertEqual(false, source.Contains("ParameterSetLibrary.SaveAsync", StringComparison.Ordinal), "main view model parameter set persistence");
    return Task.CompletedTask;
}

static async Task MainViewModelShowsLiveSnapshotsAsHistoricalTrend()
{
    var directory = CreateTestStorageDirectory();
    var reader = new SequencePlcTagSnapshotReader();
    var diagnosticsStore = new FakePlcLiveDiagnosticsStore();
    var historicalStore = new FakePlcHistoricalTrendStore();
    var viewModel = new MainWindowViewModel(
        new NoFileDialogService(),
        new JsonPidSampleFieldProfileStore(),
        new JsonPlcProjectConfigurationStore(),
        plcTagSnapshotReader: reader,
        plcLiveDiagnosticsStore: diagnosticsStore,
        plcHistoricalTrendStore: historicalStore,
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: Path.Combine(directory, "plc-recordings"));

    var viewportRequestCount = 0;
    DateTimeOffset? requestedViewportStart = null;
    DateTimeOffset? requestedViewportEnd = null;
    var batchAppliedCount = 0;
    viewModel.PlcHistoricalViewportRequested += (start, end) =>
    {
        viewportRequestCount++;
        requestedViewportStart = start;
        requestedViewportEnd = end;
    };
    viewModel.PlcSnapshotFramesApplied += _ => batchAppliedCount++;

    viewModel.PlcConfigurationEditor.DefaultSamplingMilliseconds = 50;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 50;
    await viewModel.TogglePlcMonitoringAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 3);
    AssertEqual(
        true,
        historicalStore.LastSession!.EnqueueCount >= 3,
        "historical sqlite receives acquisition frames before ui drain");
    await viewModel.RefreshPlcMonitorAsync();
    await viewModel.ShowPlcHistoricalTrendAsync(TimeSpan.FromSeconds(10));

    AssertEqual(true, viewModel.PlcTrendMode.IsHistoricalMode, "live snapshots switch to historical trend mode");
    AssertEqual(true, viewModel.LiveMonitor.IsMonitoring, "historical trend keeps acquisition running");
    AssertEqual(true, viewModel.HistoricalTrendWorkbench.HasDataset, "live snapshots create historical dataset");
    AssertEqual(true, viewModel.HistoricalTrendWorkbench.IsViewportEnabled, "live snapshots enable x viewport slider");
    AssertEqual(true, viewModel.HistoricalTrendWorkbench.IsYSliderEnabled, "live snapshots enable y slider");
    AssertEqual(1, batchAppliedCount, "live snapshots publish one historical frame batch");
    AssertEqual(true, viewModel.HistoricalTrend.CurrentFrames.Count >= 3, "live snapshots are retained as historical frames");
    AssertEqual(0, diagnosticsStore.StartCount, "live acquisition does not start diagnostics");
    AssertEqual(1, historicalStore.StartCount, "live acquisition starts historical sqlite session");
    AssertEqual(true, historicalStore.LastSession!.EnqueueCount > 0, "live acquisition frames enqueue to historical sqlite session");
    AssertEqual(
        true,
        DateTimeOffset.Now - requestedViewportEnd!.Value < TimeSpan.FromSeconds(2),
        "historical window ends at mode switch time");
    AssertEqual(
        true,
        requestedViewportEnd.Value - requestedViewportStart!.Value <= TimeSpan.FromSeconds(10),
        "historical window starts at selected duration before switch time");

    viewModel.HistoricalTrendWorkbench.ViewportStart =
        viewModel.HistoricalTrendWorkbench.ViewportMinimum +
        (viewModel.HistoricalTrendWorkbench.ViewportMaximum - viewModel.HistoricalTrendWorkbench.ViewportMinimum) / 2d;
    AssertEqual(2, viewportRequestCount, "live historical x slider requests viewport update");
    AssertEqual(true, requestedViewportStart.HasValue, "live historical x slider start timestamp");
    AssertEqual(true, requestedViewportEnd.HasValue, "live historical x slider end timestamp");
    viewModel.HistoricalTrendWorkbench.ViewportStart = 900d;
    viewModel.HistoricalTrendWorkbench.ViewportEnd = 100d;
    AssertEqual(
        true,
        viewModel.HistoricalTrendWorkbench.ViewportStart <= viewModel.HistoricalTrendWorkbench.ViewportEnd,
        "historical x slider lower cannot exceed upper");
    viewModel.HistoricalTrendWorkbench.YLower = 900d;
    viewModel.HistoricalTrendWorkbench.YUpper = 100d;
    AssertEqual(
        true,
        viewModel.HistoricalTrendWorkbench.YLower <= viewModel.HistoricalTrendWorkbench.YUpper,
        "historical y1 slider lower cannot exceed upper");
    viewModel.HistoricalTrendWorkbench.RightYLower = 900d;
    viewModel.HistoricalTrendWorkbench.RightYUpper = 100d;
    AssertEqual(
        true,
        viewModel.HistoricalTrendWorkbench.RightYLower <= viewModel.HistoricalTrendWorkbench.RightYUpper,
        "historical y2 slider lower cannot exceed upper");

    await viewModel.SetPlcHistoricalTrendWindowAsync(TimeSpan.FromSeconds(10));
    AssertEqual(true, viewportRequestCount >= 3, "live historical preset requests viewport update");
    AssertEqual(true, viewModel.PlcTrendMode.IsHistoricalMode, "live historical preset keeps trend mode");
    AssertContains("历史趋势窗口", viewModel.LiveMonitor.MonitorStatus);
    await viewModel.ShowPlcLiveTrendAsync();
    AssertEqual(true, viewModel.LiveMonitor.IsMonitoring, "live trend resumes without stopping acquisition");
    await viewModel.TogglePlcMonitoringAsync();
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
    viewModel.PlcConfigurationEditor.DefaultSamplingMilliseconds = 50;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 50;
    viewModel.PlcSnapshotsApplied += (_, timestamp) => lastTrendTimestamp = timestamp;

    await viewModel.TogglePlcMonitoringAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 3);
    await viewModel.RefreshPlcMonitorAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, reader.OpenSessionCount, "live monitor session open count");
    AssertEqual(true, reader.SessionReadCount >= 3, "live monitor session read count");
    AssertEqual(0, reader.ReadCount, "live monitor single read count");
    AssertEqual(true, lastTrendTimestamp.HasValue, "live monitor planned trend timestamp");
    AssertContains("SQLite 写入已关闭", viewModel.LiveMonitor.AcquisitionDiagnosticsStatus);
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
    viewModel.PlcConfigurationEditor.DefaultSamplingMilliseconds = 50;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 50;
    viewModel.Debug.DiagnosticsDurationMinutes = 40;

    await viewModel.TogglePlcLiveDiagnosticsAsync();
    AssertEqual(0, diagnosticsStore.StartCount, "diagnostics should not start without monitoring");
    AssertEqual(30, viewModel.Debug.DiagnosticsDurationMinutes, "diagnostics duration clamps to thirty minutes");

    await viewModel.TogglePlcMonitoringAsync();
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 2);
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, diagnosticsStore.StartCount, "diagnostics starts only when explicitly requested");
    AssertEqual(true, diagnosticsStore.LastSession is not null, "diagnostics session created");
    AssertEqual(true, diagnosticsStore.LastSession!.StopCount >= 1, "diagnostics session stopped");
    AssertContains("帧", viewModel.Debug.DiagnosticsStatus);
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
    viewModel.PlcConfigurationEditor.DefaultSamplingMilliseconds = 50;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 50;

    await viewModel.TogglePlcMonitoringAsync();
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await WaitUntilAsync(() => reader.SessionReadCount >= 2);
    await viewModel.TogglePlcLiveDiagnosticsAsync();
    await viewModel.TogglePlcMonitoringAsync();

    AssertEqual(1, diagnosticsStore.StartCount, "future diagnostics starts only when explicitly requested");
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
        plcHistoricalTrendStore: new FakePlcHistoricalTrendStore(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);

    AssertEqual(false, viewModel.PlcTrendMode.IsLiveScrollingPaused, "initial live trend pause state");
    AssertEqual("暂停滚动", viewModel.PlcTrendMode.PauseButtonText, "initial live trend pause button text");

    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(true, viewModel.PlcTrendMode.IsLiveScrollingPaused, "paused live trend state");
    AssertEqual("恢复滚动", viewModel.PlcTrendMode.PauseButtonText, "paused live trend pause button text");
    AssertContains("暂停", viewModel.PlcTrendMode.Status);

    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(false, viewModel.PlcTrendMode.IsLiveScrollingPaused, "resumed live trend state");
    AssertEqual("暂停滚动", viewModel.PlcTrendMode.PauseButtonText, "resumed live trend pause button text");

    await viewModel.ShowPlcHistoricalTrendAsync();
    AssertEqual(true, viewModel.PlcTrendMode.IsLiveScrollingPaused, "historical trend pauses live scrolling");
    await viewModel.TogglePlcLiveTrendPauseAsync();
    AssertEqual(true, viewModel.PlcTrendMode.IsLiveScrollingPaused, "historical trend ignores manual pause toggle");
    viewModel.UsePlcLiveTrendMode();
    AssertEqual(false, viewModel.PlcTrendMode.IsLiveScrollingPaused, "live trend mode resumes scrolling");
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

    foreach (var tag in viewModel.PlcConfigurationEditor.TagDefinitions)
    {
        tag.IsEnabled = false;
    }

    viewModel.PlcConfigurationEditor.TagDefinitions[0].IsEnabled = true;
    viewModel.PlcConfigurationEditor.TagDefinitions[0].SamplingMilliseconds = 50;
    viewModel.PlcConfigurationEditor.TagDefinitions[1].IsEnabled = true;
    viewModel.PlcConfigurationEditor.TagDefinitions[1].SamplingMilliseconds = 500;
    viewModel.PlcConfigurationEditor.MinimumSamplingMilliseconds = 50;

    await viewModel.RecordPlcOneSecondAsync();

    AssertEqual(true, viewModel.HistoricalTrend.CurrentFrames.Count >= 18, "recorded frame count");
    AssertEqual(true, viewModel.HistoricalTrend.CurrentFrames.All(frame => frame.Count == 2), "recorded frame tag count");
    AssertEqual(1, reader.OpenSessionCount, "plc reader session open count");
    AssertEqual(true, reader.SessionReadCount >= 18, "plc session read count");
    AssertEqual("PLC 1s 记录完成", viewModel.Notification.Title, "plc recording notification title");
    AssertContains("周期 50 ms", viewModel.LiveMonitor.MonitorStatus);
    AssertContains("2 个点位", viewModel.LiveMonitor.MonitorStatus);
    AssertContains("诊断：调度延迟", viewModel.LiveMonitor.AcquisitionDiagnosticsStatus);
    var recordingPath = Directory.GetFiles(Path.Combine(directory, "plc-recordings"), "plc-recording-*.json").Single();
    AssertContains(Path.GetFullPath(recordingPath), viewModel.Notification.Message);
    AssertContains("诊断：调度延迟", viewModel.Notification.Message);
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
        plcHistoricalTrendStore: new FakePlcHistoricalTrendStore(),
        testSessionStorageDirectory: directory,
        plcRecordingStorageDirectory: recordingDirectory);

    var resetCount = 0;
    var appliedCount = 0;
    var batchAppliedCount = 0;
    var viewportRequestCount = 0;
    DateTimeOffset? requestedViewportStart = null;
    DateTimeOffset? requestedViewportEnd = null;
    var yRangeRequestCount = 0;
    var rightYRangeRequestCount = 0;
    double? requestedYMin = null;
    double? requestedYMax = null;
    double? requestedRightYMin = null;
    double? requestedRightYMax = null;
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
    loader.PlcTrendRightYRangeRequested += (min, max) =>
    {
        rightYRangeRequestCount++;
        requestedRightYMin = min;
        requestedRightYMax = max;
    };

    await loader.LoadPlcRecordingAsync();

    AssertEqual(true, loader.HistoricalTrend.CurrentFrames.Count > 0, "loaded plc recording frame count");
    AssertEqual(true, loader.LiveMonitor.Tags.Count > 0, "loaded plc monitor tag count");
    AssertEqual(1, resetCount, "loaded plc trend reset count");
    AssertEqual(true, appliedCount > 0, "loaded plc trend applied count");
    AssertContains(Path.GetFullPath(recordingPath), loader.Notification.Message);
    AssertContains("第 1/", loader.Debug.ReplayStatus);

    await loader.SetPlcReplaySpeedAsync(2d);
    AssertContains("速度 2x", loader.Debug.ReplayStatus);

    await loader.StepPlcReplayForwardAsync();
    AssertContains("第 2/", loader.Debug.ReplayStatus);

    await loader.StepPlcReplayBackwardAsync();
    AssertContains("第 1/", loader.Debug.ReplayStatus);
    AssertEqual(true, resetCount >= 2, "backward replay trend reset count");

    var appliedCountBeforeHistory = appliedCount;
    await loader.ShowPlcHistoricalTrendAsync();
    AssertEqual(true, loader.PlcTrendMode.IsHistoricalMode, "historical plc trend mode");
    AssertContains("历史", loader.PlcTrendMode.Status);
    AssertContains(loader.HistoricalTrend.CurrentFrames.Count.ToString(CultureInfo.InvariantCulture), loader.LiveMonitor.MonitorStatus);
    AssertEqual(appliedCountBeforeHistory, appliedCount, "historical trend avoids per-frame plot events");
    AssertEqual(1, batchAppliedCount, "historical trend raises one batch plot event");
    AssertEqual(true, loader.HistoricalTrendWorkbench.IsViewportEnabled, "historical viewport slider enabled");
    AssertEqual(true, loader.HistoricalTrendWorkbench.IsYSliderEnabled, "historical y slider enabled");
    AssertEqual(true, loader.HistoricalTrendWorkbench.IsDualAxisLayout, "historical workbench defaults dual axis");
    AssertEqual("Y1", loader.LiveMonitor.Tags[0].AxisGroup, "historical tag defaults to y1 axis");
    loader.LiveMonitor.Tags[0].AxisGroup = "Y2";
    AssertEqual("Y2", loader.LiveMonitor.Tags[0].AxisGroup, "historical tag can move to y2 axis");
    await loader.SetPlcSingleAxisLayoutAsync();
    AssertEqual(true, loader.HistoricalTrendWorkbench.IsSingleAxisLayout, "historical workbench switches single axis");
    await loader.SetPlcDualAxisLayoutAsync();
    AssertEqual(true, loader.HistoricalTrendWorkbench.IsDualAxisLayout, "historical workbench switches dual axis");
    if (loader.LiveMonitor.Tags.Count > 1)
    {
        foreach (var tag in loader.LiveMonitor.Tags)
        {
            tag.AxisGroup = "Y2";
        }

        loader.LiveMonitor.EnsureVisibleAxisGroups();
        AssertEqual(true, loader.LiveMonitor.Tags.Any(tag => tag.AxisGroup == "Y1"), "dual axis keeps a y1 series");
        AssertEqual(true, loader.LiveMonitor.Tags.Any(tag => tag.AxisGroup == "Y2"), "dual axis keeps a y2 series");
        AssertEqual(
            true,
            loader.LiveMonitor.LeftAxisTags.All(tag => tag.AxisGroup == "Y1"),
            "left axis candidates only include y1 tags");
        AssertEqual(
            true,
            loader.LiveMonitor.RightAxisTags.All(tag => tag.AxisGroup == "Y2"),
            "right axis candidates only include y2 tags");
        loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId = loader.LiveMonitor.RightAxisTags.First().TagId;
        loader.HistoricalTrendWorkbench.EnsureSelectedAxisSeries(
            loader.LiveMonitor.LeftAxisTags.Select(tag => tag.TagId).ToArray(),
            loader.LiveMonitor.RightAxisTags.Select(tag => tag.TagId).ToArray());
        AssertEqual(
            true,
            loader.LiveMonitor.LeftAxisTags.Any(tag => tag.TagId == loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId),
            "left selected series is repaired to y1 candidate");
    }

    AssertEqual(true, loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId.HasValue, "historical y1 selected series");
    AssertEqual(true, loader.HistoricalTrendWorkbench.SelectedRightAxisSeriesId.HasValue, "historical y2 selected series");
    var preservedSeriesId = loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId!.Value;
    var alternateSeriesId = loader.HistoricalTrendWorkbench.State.Dataset.Series
        .First(series => series.SeriesId != preservedSeriesId)
        .SeriesId;
    loader.HistoricalTrendWorkbench.YLower = loader.HistoricalTrendWorkbench.YSliderMinimum +
        (loader.HistoricalTrendWorkbench.YSliderMaximum - loader.HistoricalTrendWorkbench.YSliderMinimum) * 0.3d;
    var preservedLower = loader.HistoricalTrendWorkbench.YLower;
    loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId = alternateSeriesId;
    loader.HistoricalTrendWorkbench.SelectedLeftAxisSeriesId = preservedSeriesId;
    AssertClose(preservedLower, loader.HistoricalTrendWorkbench.YLower, 0.0001d, "historical selected series keeps y brush");

    viewportRequestCount = 0;
    var selectedHistoricalFrame = loader.HistoricalTrend.CurrentFrames.First(frame => frame.Count > 0);
    var selectedHistoricalTimestamp = selectedHistoricalFrame[0].Timestamp;
    loader.HistoricalTrendWorkbench.RangeStartText = selectedHistoricalTimestamp.ToString("O", CultureInfo.InvariantCulture);
    loader.HistoricalTrendWorkbench.RangeEndText = selectedHistoricalTimestamp.ToString("O", CultureInfo.InvariantCulture);
    await loader.ApplyPlcHistoricalRangeAsync();
    AssertEqual(1, viewportRequestCount, "historical range requests viewport update");
    AssertEqual(selectedHistoricalTimestamp, requestedViewportStart, "historical viewport start");
    AssertEqual(selectedHistoricalTimestamp, requestedViewportEnd, "historical viewport end");
    AssertEqual(true, loader.PlcTrendMode.IsHistoricalMode, "historical range keeps trend mode");

    await loader.ResetPlcHistoricalRangeAsync();
    AssertEqual(2, viewportRequestCount, "historical reset requests viewport update");
    AssertContains(loader.HistoricalTrend.CurrentFrames.Count.ToString(CultureInfo.InvariantCulture), loader.LiveMonitor.MonitorStatus);
    await loader.SetPlcHistoricalTrendWindowAsync(TimeSpan.FromSeconds(10));
    AssertEqual(3, viewportRequestCount, "historical preset requests viewport update");
    AssertEqual(true, loader.PlcTrendMode.IsHistoricalMode, "historical preset keeps trend mode");
    AssertEqual(
        true,
        requestedViewportEnd - requestedViewportStart <= TimeSpan.FromSeconds(10),
        "historical preset clamps visible duration");

    var historicalTimestamps = loader.HistoricalTrend.CurrentFrames
        .Where(frame => frame.Count > 0)
        .Select(frame => frame.Min(snapshot => snapshot.Timestamp))
        .Order()
        .ToArray();
    AssertEqual(0d, loader.HistoricalTrendWorkbench.ViewportMinimum, "historical slider normalized minimum");
    AssertEqual(1000d, loader.HistoricalTrendWorkbench.ViewportMaximum, "historical slider normalized maximum");
    var sliderStart = loader.HistoricalTrendWorkbench.ViewportMinimum +
        (loader.HistoricalTrendWorkbench.ViewportMaximum - loader.HistoricalTrendWorkbench.ViewportMinimum) / 2d;
    loader.HistoricalTrendWorkbench.ViewportStart = sliderStart;
    AssertEqual(4, viewportRequestCount, "historical start slider requests viewport update");
    var expectedMiddleTimestamp = new DateTimeOffset(
        historicalTimestamps[0].Ticks + (long)Math.Round((historicalTimestamps[^1].Ticks - historicalTimestamps[0].Ticks) / 2d),
        historicalTimestamps[0].Offset);
    AssertEqual(
        expectedMiddleTimestamp,
        requestedViewportStart,
        "historical start slider timestamp");

    var historicalValues = loader.HistoricalTrend.CurrentFrames
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
    AssertEqual(0d, loader.HistoricalTrendWorkbench.YSliderMinimum, "historical y slider normalized minimum");
    AssertEqual(1000d, loader.HistoricalTrendWorkbench.YSliderMaximum, "historical y slider normalized maximum");
    var sliderYLower = loader.HistoricalTrendWorkbench.YSliderMinimum +
        (loader.HistoricalTrendWorkbench.YSliderMaximum - loader.HistoricalTrendWorkbench.YSliderMinimum) / 4d;
    var yRangeRequestsBeforeLowerSlider = yRangeRequestCount;
    loader.HistoricalTrendWorkbench.YLower = sliderYLower;
    AssertEqual(yRangeRequestsBeforeLowerSlider + 1, yRangeRequestCount, "historical y lower slider requests y range update");
    AssertClose(yMinimum + ((yMaximum - yMinimum) * 0.25d), requestedYMin, 0.0001d, "historical y lower slider value");
    AssertClose(yMaximum, requestedYMax, 0.0001d, "historical y upper slider value");
    var rightYRangeRequestsBeforeUpperSlider = rightYRangeRequestCount;
    loader.HistoricalTrendWorkbench.RightYUpper = loader.HistoricalTrendWorkbench.YSliderMinimum +
        (loader.HistoricalTrendWorkbench.YSliderMaximum - loader.HistoricalTrendWorkbench.YSliderMinimum) / 2d;
    AssertEqual(rightYRangeRequestsBeforeUpperSlider + 1, rightYRangeRequestCount, "historical y2 upper slider requests right y range update");
    AssertClose(yMinimum, requestedRightYMin, 0.0001d, "historical y2 lower slider value");
    AssertClose(yMinimum + ((yMaximum - yMinimum) * 0.5d), requestedRightYMax, 0.0001d, "historical y2 upper slider value");

    loader.UsePlcLiveTrendMode();
    AssertEqual(false, loader.PlcTrendMode.IsHistoricalMode, "live plc trend mode");
    AssertEqual(false, loader.HistoricalTrendWorkbench.IsViewportEnabled, "live plc trend disables historical slider");
    AssertContains("实时", loader.PlcTrendMode.Status);
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

    AssertEqual(true, viewModel.Notification.IsVisible, "analysis notification visibility");
    AssertEqual("离线分析已完成", viewModel.Notification.Title, "analysis notification title");
    AssertEqual("Success", viewModel.Notification.Kind, "analysis notification kind");
    AssertEqual(true, viewModel.OfflineAnalysis.TuningRecommendations.Any(item => item.Parameter == "Kp"), "view model Kp recommendation");
    AssertContains("保守调整建议", viewModel.OfflineAnalysis.RecommendationSummary);
    await viewModel.SaveParameterSetAsync();
    AssertEqual("参数方案已保存", viewModel.Notification.Title, "parameter set notification title");
    AssertEqual(1, viewModel.ParameterSetLibrary.ParameterSets.Count, "parameter set count after save");
    AssertEqual("1.2", viewModel.ParameterSetLibrary.ParameterSets[0].Kp, "parameter set Kp display");
    viewModel.OfflineAnalysis.SelectedTuningRecommendation = viewModel.OfflineAnalysis.TuningRecommendations.First(item => item.Parameter == "Kp");
    viewModel.ExperimentHistory.RecommendationReviewNote = "现场确认先小步调整";
    await viewModel.AcceptRecommendationAsync();
    AssertEqual("建议审查已记录", viewModel.Notification.Title, "review notification title");
    AssertEqual(1, viewModel.ExperimentHistory.RecommendationReviews.Count, "recommendation review count");
    AssertContains("现场确认", viewModel.ExperimentHistory.RecommendationReviews[0].EngineerNote);

    await viewModel.SaveTestSessionAsync();

    AssertEqual(true, viewModel.Notification.IsVisible, "save session notification visibility");
    AssertEqual("试验记录已保存", viewModel.Notification.Title, "save session notification title");
    AssertContains(Path.GetFullPath(directory), viewModel.Notification.Message);
    AssertContains(Path.Combine(Path.GetFullPath(directory), "test-sessions.json"), viewModel.Notification.Message);
    AssertContains(".samples.json", viewModel.Notification.Message);
    AssertEqual(1, viewModel.ExperimentHistory.HistorySessions.Count, "history count after save");
    AssertEqual("7", viewModel.ExperimentHistory.HistorySessions[0].SampleCount, "history sample count after save");
    AssertEqual("00:00:06", viewModel.ExperimentHistory.HistorySessions[0].Duration, "history duration after save");

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
    AssertEqual(2, viewModel.ExperimentHistory.HistorySessions.Count, "history count after adding improved session");

    viewModel.ExperimentHistory.HistorySearchText = "offline";
    AssertEqual(1, viewModel.ExperimentHistory.HistorySessions.Count, "filtered history count");
    viewModel.ExperimentHistory.HistorySearchText = "not-found";
    AssertEqual(0, viewModel.ExperimentHistory.HistorySessions.Count, "filtered empty history count");
    viewModel.ExperimentHistory.HistorySearchText = string.Empty;

    viewModel.ExperimentHistory.SelectedHistorySession = viewModel.ExperimentHistory.HistorySessions.First(item =>
        item.Name.Contains("offline", StringComparison.OrdinalIgnoreCase));
    AssertContains("样本：7", viewModel.ExperimentHistory.SelectedHistoryDetails);
    await viewModel.SetHistoryBaselineAsync();
    viewModel.ExperimentHistory.SelectedHistorySession = viewModel.ExperimentHistory.HistorySessions.First(item => item.Name == "improved-step");
    await viewModel.CompareHistorySessionAsync();
    AssertEqual(true, viewModel.ExperimentHistory.HistoryComparisonMetrics.Count >= 4, "history comparison metric count");
    AssertEqual(true, viewModel.ExperimentHistory.HistoryComparisonMetrics.Any(item =>
        item.Metric == "超调量" && item.Delta.StartsWith("-", StringComparison.Ordinal)), "history comparison overshoot improvement");
    AssertContains("improved-step", viewModel.ExperimentHistory.HistoryComparisonStatus);

    await viewModel.OpenHistorySessionAsync();

    AssertEqual("历史记录已打开", viewModel.Notification.Title, "open history notification title");
    AssertEqual("7", viewModel.OfflineAnalysis.SampleCount, "history sample count");

    await viewModel.ExportHistorySamplesAsync();

    AssertEqual("历史采样已导出", viewModel.Notification.Title, "export history notification title");
    AssertContains(Path.GetFullPath(exportedHistorySamplesPath), viewModel.Notification.Message);
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

    AssertEqual("可见趋势已导出", viewModel.Notification.Title, "visible trend export notification title");
    AssertContains(Path.GetFullPath(exportedVisibleTrendPath), viewModel.Notification.Message);
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

static string FindRepositoryRootForTests()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "PIDTuner.sln")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    throw new DirectoryNotFoundException("PIDTuner repository root was not found.");
}

static byte[] BuildS7SetupCommunicationResponse(int pduLength)
{
    var response = new byte[27];
    response[0] = 0x03;
    response[1] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(2, 2), (ushort)response.Length);
    response[4] = 0x02;
    response[5] = 0xF0;
    response[6] = 0x80;
    response[7] = 0x32;
    response[8] = 0x03;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(11, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(13, 2), 8);
    response[19] = 0xF0;
    response[20] = 0x00;
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(21, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(23, 2), 1);
    BinaryPrimitives.WriteUInt16BigEndian(response.AsSpan(25, 2), (ushort)pduLength);
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

static int GetIntProperty(object instance, string propertyName) =>
    (int)(instance.GetType().GetProperty(propertyName)?.GetValue(instance)
        ?? throw new InvalidOperationException($"Property {propertyName} was not found."));

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

file sealed class FakePlcHistoricalTrendStore : IPlcHistoricalTrendStore
{
    private readonly List<PlcAcquisitionFrame> _frames = [];

    public int StartCount { get; private set; }

    public FakePlcHistoricalTrendWriteSession? LastSession { get; private set; }

    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), "fake-plc-history.sqlite");

    public Task<IPlcHistoricalTrendWriteSession> StartSessionAsync(
        PlcProjectConfiguration configuration,
        CancellationToken cancellationToken)
    {
        StartCount++;
        LastSession = new FakePlcHistoricalTrendWriteSession(DatabasePath, _frames);
        return Task.FromResult<IPlcHistoricalTrendWriteSession>(LastSession);
    }

    public Task<(DateTimeOffset Start, DateTimeOffset End)?> GetAvailableRangeAsync(
        CancellationToken cancellationToken)
    {
        var timestamps = _frames.Select(frame => frame.Diagnostics.PlannedTimestampUtc).ToArray();
        (DateTimeOffset Start, DateTimeOffset End)? range = timestamps.Length == 0
            ? null
            : (timestamps.Min(), timestamps.Max());
        return Task.FromResult(range);
    }

    public Task<IReadOnlyList<IReadOnlyList<PlcTagSnapshot>>> QueryFramesAsync(
        DateTimeOffset start,
        DateTimeOffset end,
        int maximumPointsPerTag,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<IReadOnlyList<PlcTagSnapshot>> frames = _frames
            .Where(frame => frame.Diagnostics.PlannedTimestampUtc >= start && frame.Diagnostics.PlannedTimestampUtc <= end)
            .Select(frame => (IReadOnlyList<PlcTagSnapshot>)frame.Snapshots
                .Select(snapshot => snapshot with { Timestamp = frame.Diagnostics.PlannedTimestampUtc })
                .ToArray())
            .ToArray();
        return Task.FromResult(frames);
    }
}

file sealed class FakePlcHistoricalTrendWriteSession(
    string databasePath,
    List<PlcAcquisitionFrame> frames) : IPlcHistoricalTrendWriteSession
{
    public int EnqueueCount { get; private set; }

    public string DatabasePath { get; } = databasePath;

    public void Enqueue(PlcAcquisitionFrame frame)
    {
        EnqueueCount++;
        frames.Add(frame);
    }

    public Task<PlcHistoricalTrendWriteSummary> StopAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PlcHistoricalTrendWriteSummary(
            DatabasePath,
            frames.Count,
            frames.Sum(frame => frame.Snapshots.Count)));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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


