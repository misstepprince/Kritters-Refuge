using System.Collections.Generic;
using Content.IntegrationTests.Pair;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._NF.Shuttle;

[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class ServiceFlagsSuffixTests
{
    TestPair _pair;
    SharedShuttleSystem _shuttle;

    [SetUp]
    public async Task Setup()
    {
        _pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true });
        var server = _pair.Server;

        var entManager = server.ResolveDependency<IEntityManager>();
        _shuttle = entManager.System<SharedShuttleSystem>();
    }

    [TearDown]
    public async Task TearDownInternal()
    {
        if (_pair != null)
            await _pair.CleanReturnAsync();
    }

    [Test]
    public void GetServiceFlagsSuffix_None_ReturnsEmptyString()
    {
        var result = _shuttle.GetServiceFlagsSuffix(ServiceFlags.None);
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void GetServiceFlagsSuffix_SingleFlag_ReturnsSingleCharacter()
    {
        var result = _shuttle.GetServiceFlagsSuffix(ServiceFlags.Services);
        Assert.That(result.Length, Is.Positive);
    }

    [Test]
    public void GetServiceFlagsSuffix_MultipleFlagsUniqueChars_ReturnsFirstCharacters()
    {
        // Flags intentionally excluded from the suffix display
        var excludedFlags = new HashSet<ServiceFlags>
        {
            ServiceFlags.None,
            ServiceFlags.InterdictionsEnabled,
            ServiceFlags.InterdictionsDisabled,
        };

        // Assemble all displayed enum values into one
        var valueCount = 0;
        var allFlags = ServiceFlags.None;
        foreach (var flag in Enum.GetValues<ServiceFlags>())
        {
            if (excludedFlags.Contains(flag))
                continue;
            allFlags |= flag;
            valueCount++;
        }

        // Extract the characters between brackets
        var characters = _shuttle.GetServiceFlagsSuffix(allFlags).Trim('[', ']');

        // Check that we have unique character combinations for each displayed flag.
        Assert.Multiple(() =>
        {
            Assert.That(characters, Is.Unique);
            Assert.That(characters.Length, Is.EqualTo(valueCount));

            foreach (var flag in Enum.GetValues<ServiceFlags>())
            {
                if (excludedFlags.Contains(flag))
                    continue;

                var oneFlagResult = _shuttle.GetServiceFlagsSuffix(flag);
                // Extract the characters between brackets
                var oneFlagCharacters = oneFlagResult.Trim('[', ']');
                // Check that we have a single character for each displayed flag.
                Assert.That(oneFlagCharacters.Length, Is.EqualTo(1));
                Assert.That(characters.Contains(oneFlagCharacters[0]), Is.True);
            }
        });
    }
}
