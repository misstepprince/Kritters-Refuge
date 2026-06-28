namespace Content.Client._Kritters.BloodTypes.Components;

[RegisterComponent]
public sealed partial class KrittersBloodColoredDamageVisualsComponent : Component
{
    /// <summary>
    /// Damage overlay groups whose wound layers should be colored from the entity's blood reagent.
    /// </summary>
    [DataField]
    public HashSet<string> Groups = new() { "Brute" };
}
