namespace Content.Shared._Kritters.Novakin.Components;

/// <summary>
/// Marks one unit of a stack as fuel that a Novakin can consume directly.
/// </summary>
[RegisterComponent]
public sealed partial class NovakinFuelMaterialComponent : Component
{
    [DataField]
    public float Fuel = 25f;
}
