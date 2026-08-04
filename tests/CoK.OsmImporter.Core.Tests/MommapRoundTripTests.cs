using System.Text.Json;
using CoK.OsmImporter.Core.Mommap;

namespace CoK.OsmImporter.Core.Tests;

public class MommapRoundTripTests
{
    // template.mommap is an optional, gitignored, bring-your-own file (see README.md) — any real
    // .mommap works here, so these tests check structure/fidelity generically rather than
    // hardcoding counts tied to one specific downloaded map, and no-op if nobody's dropped one in.

    [Fact]
    public void Load_RealTemplate_ParsesWithSaneCounts()
    {
        if (TestPaths.TemplateMommap is not { } path)
            return;

        var doc = MommapSerializer.Load(path);

        Assert.True(doc.Objects.Count > 0);
        Assert.True(doc.Paths.Count > 0);
        Assert.True(doc.Info.LastUid >= 0);
    }

    [Fact]
    public void RoundTrip_RealTemplate_IsStructurallyLossless()
    {
        if (TestPaths.TemplateMommap is not { } path)
            return;

        var originalJson = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        var doc = MommapSerializer.Load(path);
        var roundTripped = MommapSerializer.SaveToString(doc);
        var roundTrippedJson = JsonDocument.Parse(roundTripped).RootElement;

        AssertJsonEquivalent(originalJson, roundTrippedJson, "$");
    }

    /// <summary>Deep structural comparison ignoring property order (JSON objects are unordered).</summary>
    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual, string path)
    {
        Assert.True(expected.ValueKind == actual.ValueKind,
            $"{path}: kind mismatch, expected {expected.ValueKind}, actual {actual.ValueKind}");

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProps = expected.EnumerateObject().ToList();
                var actualProps = actual.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
                Assert.True(expectedProps.Count == actualProps.Count,
                    $"{path}: property count mismatch, expected {expectedProps.Count} ({string.Join(",", expectedProps.Select(p => p.Name))}), actual {actualProps.Count} ({string.Join(",", actualProps.Keys)})");
                foreach (var prop in expectedProps)
                {
                    Assert.True(actualProps.TryGetValue(prop.Name, out var actualValue),
                        $"{path}: missing property '{prop.Name}' after round-trip");
                    AssertJsonEquivalent(prop.Value, actualValue, $"{path}.{prop.Name}");
                }
                break;

            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                Assert.True(expectedItems.Count == actualItems.Count,
                    $"{path}: array length mismatch, expected {expectedItems.Count}, actual {actualItems.Count}");
                for (var i = 0; i < expectedItems.Count; i++)
                    AssertJsonEquivalent(expectedItems[i], actualItems[i], $"{path}[{i}]");
                break;

            case JsonValueKind.Number:
                Assert.True(expected.GetRawText() == actual.GetRawText()
                    || expected.GetDouble() == actual.GetDouble(),
                    $"{path}: number mismatch, expected {expected.GetRawText()}, actual {actual.GetRawText()}");
                break;

            case JsonValueKind.String:
                Assert.True(expected.GetString() == actual.GetString(),
                    $"{path}: string mismatch, expected '{expected.GetString()}', actual '{actual.GetString()}'");
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break; // ValueKind check above already covers these.

            default:
                throw new NotSupportedException($"Unexpected JsonValueKind {expected.ValueKind}");
        }
    }
}
