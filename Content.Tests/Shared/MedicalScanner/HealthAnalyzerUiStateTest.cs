using System.IO;
using Content.Shared.MedicalScanner;
using Content.Shared.Mobs;
using NetSerializer;
using NUnit.Framework;

namespace Content.Tests.Shared.MedicalScanner;

[TestFixture]
[TestOf(typeof(HealthAnalyzerUiState))]
public sealed class HealthAnalyzerUiStateTest
{
    [Test]
    public void ConstructorStoresScanModeAndMobState()
    {
        var state = new HealthAnalyzerUiState(
            null,
            310.15f,
            0.75f,
            true,
            MobState.Critical,
            true,
            false,
            true);

        Assert.Multiple(() =>
        {
            Assert.That(state.ScanMode, Is.True);
            Assert.That(state.MobState, Is.EqualTo(MobState.Critical));
            Assert.That(state.Bleeding, Is.True);
            Assert.That(state.Unrevivable, Is.False);
            Assert.That(state.Unclonable, Is.True);
        });
    }

    [Test]
    public void ConstructorAllowsUnknownMobState()
    {
        var state = new HealthAnalyzerUiState(
            null,
            float.NaN,
            float.NaN,
            false,
            null,
            null,
            null,
            null);

        Assert.Multiple(() =>
        {
            Assert.That(state.ScanMode, Is.False);
            Assert.That(state.MobState, Is.Null);
        });
    }

    [Test]
    public void MobStateSurvivesSerializationRoundTrip()
    {
        var state = new HealthAnalyzerUiState(
            null,
            310.15f,
            0.75f,
            true,
            MobState.Dead,
            false,
            true,
            false);
        var serializer = new Serializer([typeof(HealthAnalyzerUiState)]);
        using var stream = new MemoryStream();

        serializer.Serialize(stream, state);
        stream.Position = 0;
        var deserialized = (HealthAnalyzerUiState) serializer.Deserialize(stream);

        Assert.Multiple(() =>
        {
            Assert.That(deserialized.ScanMode, Is.True);
            Assert.That(deserialized.MobState, Is.EqualTo(MobState.Dead));
            Assert.That(deserialized.Unrevivable, Is.True);
        });
    }
}
