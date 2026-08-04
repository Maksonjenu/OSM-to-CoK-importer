using System.Text.Json;
using CoK.OsmImporter.Core.Mommap;

namespace CoK.OsmImporter.Core.Tests;

public class MommapDocumentTests
{
    /// <summary>
    /// Regression test for a real bug: CreateBlank() used to default environment/map_settings to
    /// `{}`, which CoK renders as a gray screen with fully transparent lighting/fog/water instead
    /// of falling back to sane defaults. A blank canvas must ship real values (see DefaultCanvas).
    /// </summary>
    [Fact]
    public void CreateBlank_HasNonEmptyEnvironmentAndMapSettings()
    {
        var doc = MommapDocument.CreateBlank();

        Assert.Equal(JsonValueKind.Object, doc.Environment.ValueKind);
        Assert.True(doc.Environment.EnumerateObject().Any(), "environment must not be empty — CoK needs real lighting/fog/water values.");
        Assert.True(doc.Environment.TryGetProperty("light_color", out _));
        Assert.True(doc.Environment.TryGetProperty("water_color", out _));
        Assert.True(doc.Environment.TryGetProperty("fog_color", out _));

        Assert.Equal(JsonValueKind.Object, doc.MapSettings.ValueKind);
        Assert.True(doc.MapSettings.EnumerateObject().Any(), "map_settings must not be empty.");
    }

    [Fact]
    public void CreateBlank_RoundTripsToValidJson()
    {
        var doc = MommapDocument.CreateBlank();

        var json = MommapSerializer.SaveToString(doc);
        var reparsed = JsonDocument.Parse(json).RootElement;

        Assert.True(reparsed.GetProperty("environment").EnumerateObject().Any());
        Assert.True(reparsed.GetProperty("map_settings").EnumerateObject().Any());
    }
}
