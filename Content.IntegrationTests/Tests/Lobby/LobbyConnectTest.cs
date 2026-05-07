using Content.Client.Lobby;
using Content.Client.Gameplay;
using Robust.Client.State;
using Robust.Shared.Exceptions;

namespace Content.IntegrationTests.Tests.Lobby;

/// <summary>
/// Verifies that a client successfully transitions from Direct Connect to LobbyState after connecting.
/// </summary>
[TestFixture]
public sealed class LobbyConnectTest
{
    [Test]
    public async Task ClientReachesLobbyAfterConnect()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });

        var client = pair.Client;

        // Run enough ticks for TickerJoinLobbyEvent to arrive and LobbyState to initialize.
        await pair.RunTicksSync(30);

        await client.WaitAssertion(() =>
        {
            var stateManager = client.ResolveDependency<IStateManager>();
            Assert.That(stateManager.CurrentState is LobbyState or GameplayState, Is.True,
                "Client should leave main menu after connecting (LobbyState or GameplayState).");

            var runtimeLog = client.ResolveDependency<IRuntimeLog>();
            Assert.That(runtimeLog.ExceptionCount, Is.EqualTo(0),
                "No exceptions should occur during lobby transition.");
        });

        await pair.CleanReturnAsync();
    }
}
