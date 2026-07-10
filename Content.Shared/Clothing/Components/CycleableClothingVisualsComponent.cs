using Content.Shared.Clothing.EntitySystems;
using Robust.Shared.GameStates;

namespace Content.Shared.Clothing.Components;

/// <summary>
/// Allows clothing to cycle through a fixed set of icon and equipped RSI states from a verb.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(CycleableClothingVisualsSystem))]
public sealed partial class CycleableClothingVisualsComponent : Component
{
    /// <summary>
    /// RSI states to cycle through. These are used for both the item icon and equipped clothing state.
    /// </summary>
    [DataField(required: true)]
    public List<string> States = new();

    /// <summary>
    /// Localization id for the right-click verb.
    /// </summary>
    [DataField]
    public string VerbText = "cycleable-clothing-visuals-verb";

    [DataField, AutoNetworkedField]
    public int CurrentState;
}
