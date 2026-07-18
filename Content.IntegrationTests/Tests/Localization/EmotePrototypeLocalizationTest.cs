using Content.Shared.Chat.Prototypes;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Localization;

[TestFixture]
public sealed class EmotePrototypeLocalizationTest
{
    [Test]
    public async Task LocalizedEmoteIdsExist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var localization = pair.Server.ResolveDependency<ILocalizationManager>();

        Assert.Multiple(() =>
        {
            foreach (var emote in prototypes.EnumeratePrototypes<EmotePrototype>())
            {
                Assert.That(localization.HasString(emote.Name),
                    $"Emote {emote.ID} references missing name localization {emote.Name}.");

                foreach (var message in emote.ChatMessages)
                {
                    if (!message.StartsWith("chat-emote-", StringComparison.Ordinal))
                        continue;

                    Assert.That(localization.HasString(message),
                        $"Emote {emote.ID} references missing chat localization {message}.");
                }
            }
        });

        await pair.CleanReturnAsync();
    }
}
