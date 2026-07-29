using System.Globalization;
using System.Text;
using PIDTuner.Domain.Analysis;
using PIDTuner.Domain.Models;
using PIDTuner.Application.UseCases;
using PIDTuner.Domain.Configuration;
using PIDTuner.Infrastructure.Analysis;
using PIDTuner.Infrastructure.Csv;
using PIDTuner.Infrastructure.Configuration;

var tests = new (string Name, Func<Task> Run)[]
{
    ("analysis calculates core response metrics from an offline step response", AnalysisCalculatesCoreMetrics),
    ("csv exchange imports and exports stable pid sample fields", CsvExchangeRoundTripsSamples),
    ("offline csv use case imports samples and analyzes the requested window", OfflineCsvUseCaseAnalyzesRequestedWindow),
    ("field profile store loads project metadata from json", FieldProfileStoreLoadsProjectMetadata),
    ("configurable csv exchange maps renamed fields and preserves extra metadata", ConfigurableCsvExchangeMapsRenamedFields)
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

    var exported = Encoding.UTF8.GetString(output.ToArray());
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

static PidSample Sample(DateTimeOffset timestamp, double sp, double pv, double mv, Guid sessionId)
{
    return new PidSample(timestamp, sp, pv, mv, 1.2, 0.4, 0.1, true, sessionId, null);
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

static PidSampleFieldDefinition Field(
    string key,
    PidSampleFieldRole role,
    PidSampleFieldDataType dataType,
    bool required)
{
    return new PidSampleFieldDefinition(key, key, dataType, required, null, role);
}
