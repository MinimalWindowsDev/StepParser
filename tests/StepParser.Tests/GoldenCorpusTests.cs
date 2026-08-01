using System.Text.Json;
using System.Text.Json.Nodes;
using StepParser.Output;

namespace StepParser.Tests;

/// <summary>
/// Checked-in fixtures with checked-in expected output. Any behaviour change must be re-blessed
/// deliberately: a missing or stale expectation FAILS the run rather than quietly rewriting itself,
/// because a snapshot test that regenerates its own baseline asserts nothing at all.
/// </summary>
public sealed class GoldenCorpusTests
{
    private static readonly string[] FixtureNames =
    [
        "minimal_ap214.stp",
        "ap242_pmi.stp",
        "nonconformant_complex.stp",
    ];

    public static TheoryData<string> Fixtures
    {
        get
        {
            TheoryData<string> data = new();
            foreach (string name in FixtureNames)
            {
                data.Add(name);
            }

            return data;
        }
    }

    private static string FixtureDirectory => Path.Combine(Sut.RepoRoot(), "tests", "StepParser.Tests", "Fixtures");

    /// <summary>
    /// Strip fields that legitimately differ between machines and checkouts: the absolute source
    /// path, and the byte length (git may normalise line endings on checkout).
    /// </summary>
    private static string Snapshot(string fixturePath)
    {
        string json = new JsonFormatter().Format(Sut.ParseToResult(File.ReadAllText(fixturePath)));
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root.Remove("source");
        root.Remove("fileSize");
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void FixtureOutputMatchesTheBlessedSnapshot(string fixtureName)
    {
        string fixturePath = Path.Combine(FixtureDirectory, fixtureName);
        string expectedPath = Path.ChangeExtension(fixturePath, ".expected.json");
        Assert.True(File.Exists(fixturePath), $"missing fixture {fixturePath}");

        string actual = Snapshot(fixturePath);

        if (!File.Exists(expectedPath))
        {
            File.WriteAllText(expectedPath, actual);
            Assert.Fail(
                $"No blessed snapshot for {fixtureName}. One has been written to {expectedPath} — " +
                "review it by hand and commit it if the output is correct.");
        }

        string expected = File.ReadAllText(expectedPath);
        if (Normalise(expected) != Normalise(actual))
        {
            string rejectedPath = Path.ChangeExtension(fixturePath, ".actual.json");
            File.WriteAllText(rejectedPath, actual);
            Assert.Fail(
                $"{fixtureName} output changed. Actual written to {rejectedPath}. " +
                "If the change is intended, replace the .expected.json and commit it.");
        }
    }

    private static string Normalise(string json) => json.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    // A snapshot only protects what it actually contains — these assert the fixtures cover the
    // behaviour they were chosen for, so a snapshot cannot pass while being empty of signal.
    [Fact]
    public void Ap242FixtureCarriesPmiDatumAndGdtSignal()
    {
        string json = File.ReadAllText(Path.Combine(FixtureDirectory, "ap242_pmi.expected.json"));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement semantics = document.RootElement.GetProperty("semantics");

        Assert.True(semantics.GetProperty("isAp242").GetBoolean());
        Assert.True(semantics.GetProperty("hasModelBasedDefinition").GetBoolean());
        Assert.True(semantics.GetProperty("dimensions").GetArrayLength() > 0);
        Assert.True(semantics.GetProperty("datums").GetArrayLength() > 0);
    }

    [Fact]
    public void NonconformantFixtureRecordsRecoveryWarnings()
    {
        string json = File.ReadAllText(Path.Combine(FixtureDirectory, "nonconformant_complex.expected.json"));
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement[] diagnostics = document.RootElement.GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("warning", diagnostic.GetProperty("severity").GetString());
            Assert.Contains("11.3.3", diagnostic.GetProperty("message").GetString()!);
        });
    }

    [Fact]
    public void MinimalFixtureParsesCleanly()
    {
        string json = File.ReadAllText(Path.Combine(FixtureDirectory, "minimal_ap214.expected.json"));
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(0, document.RootElement.GetProperty("diagnostics").GetArrayLength());
        Assert.Equal(11, document.RootElement.GetProperty("stats").GetProperty("entityCount").GetInt32());
    }

    [Fact]
    public void EveryFixtureOnDiskIsRegisteredInThisSuite()
    {
        // Prevents a fixture being added and silently never exercised.
        string[] onDisk = Directory.GetFiles(FixtureDirectory, "*.stp").Select(Path.GetFileName).ToArray()!;

        Assert.Equal(
            onDisk.OrderBy(name => name, StringComparer.Ordinal),
            FixtureNames.OrderBy(name => name, StringComparer.Ordinal));
    }
}
