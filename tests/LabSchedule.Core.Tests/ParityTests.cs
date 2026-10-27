using System.Text.Json.Nodes;
using LabSchedule.Core;

namespace LabSchedule.Core.Tests;

/// <summary>
/// Compares the C# port against output recorded from the Electron build's core.js
/// (see Fixtures/README.md).
/// </summary>
public class ParityTests
{
    public static TheoryData<string> Cases => new() { "sample", "variant", "crossyear", "leapyear" };

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)).Replace("\r\n", "\n");

    private static JsonNode? Input(string name) => ScheduleJson.Parse(Fixture($"{name}.input.json"));

    private static string StringList(IEnumerable<string> values) =>
        ScheduleJson.Stringify(new JsonArray(values.Select(value => (JsonNode)value).ToArray()), indent: 2) + "\n";

    [Theory]
    [MemberData(nameof(Cases))]
    public void ValidationMatchesElectron(string name)
    {
        Assert.Equal(Fixture($"{name}.issues.json"), StringList(ScheduleRules.Validate(Input(name))));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void SavedJsonMatchesElectron(string name)
    {
        var prepared = ScheduleRules.PrepareSchedule(Input(name));
        Assert.Equal(Fixture($"{name}.prepared.json"), ScheduleJson.ToFileText(prepared));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void HtmlMatchesElectron(string name)
    {
        var result = OutputHtml.BuildResult(ScheduleRules.NormalizeJson(Input(name)));
        Assert.Equal(Fixture($"{name}.expected.html"), result.Html);
        Assert.Equal(Fixture($"{name}.warnings.json"), StringList(result.Warnings));
    }

    [Fact]
    public void InvalidFilesReportTheSameIssues()
    {
        var cases = ScheduleJson.Parse(Fixture("invalid.cases.json"))!.AsArray();
        foreach (var testCase in cases)
        {
            var expected = testCase!["issues"]!.AsArray().Select(issue => issue!.GetValue<string>()).ToList();
            Assert.Equal(expected, ScheduleRules.Validate(testCase["input"]));
        }
    }

    [Fact]
    public void BuiltInSampleMatchesElectronSample()
    {
        Assert.Equal(
            ScheduleJson.Stringify(ScheduleJson.Parse(Fixture("sample.input.json")), indent: 2),
            ScheduleJson.Stringify(SampleSchedule.CreateRaw(), indent: 2));
    }
}
