using Content.Shared.Chat.Prototypes;
using NUnit.Framework;

namespace Content.Tests.Shared.Chat;

[TestFixture]
public sealed class EmoteCategoryTest
{
    [TestCase(EmoteCategory.Vocal)]
    [TestCase(EmoteCategory.Harpy)]
    [TestCase(EmoteCategory.Goblin)]
    [TestCase(EmoteCategory.Vulp)]
    [TestCase(EmoteCategory.Rodentia)]
    [TestCase(EmoteCategory.Diona)]
    [TestCase(EmoteCategory.Sheleg)]
    [TestCase(EmoteCategory.Male)]
    [TestCase(EmoteCategory.Female)]
    [TestCase(EmoteCategory.Avali)]
    [TestCase(EmoteCategory.Vox)]
    [TestCase(EmoteCategory.Moth)]
    [TestCase(EmoteCategory.Felinid)]
    [TestCase(EmoteCategory.Borg)]
    public void VocalCategoriesUseVocalSounds(EmoteCategory category)
    {
        Assert.That(category.UsesVocalSounds(), Is.True);
    }

    [TestCase(EmoteCategory.Invalid)]
    [TestCase(EmoteCategory.General)]
    [TestCase(EmoteCategory.Hands)]
    [TestCase(EmoteCategory.Lizard)]
    [TestCase(EmoteCategory.Sex)]
    public void NonVocalCategoriesDoNotUseVocalSounds(EmoteCategory category)
    {
        Assert.That(category.UsesVocalSounds(), Is.False);
    }
}
