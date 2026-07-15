using Content.Client.Computer;
using Content.Client.UserInterface.Controls;
using Content.Shared.Shuttles.BUIStates;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using System;

namespace Content.Client.Shuttles.UI;

public sealed partial class RadarConsoleWindow : FancyWindow,
    IComputerWindow<NavInterfaceState>
{
    [Dependency] private IEntityManager _entManager = default!;

    protected override void EnteredTree()
    {
        base.EnteredTree();

        IoCManager.InjectDependencies(this);

        // Wire up IFF search
        IffSearchCriteria.OnTextChanged += OnIffSearchChanged;
    }

    private void OnIffSearchChanged(LineEdit.LineEditEventArgs args)
    {
        var text = args.Text.Trim();

        RadarScreen.IFFFilter = text.Length == 0
            ? null // If empty, do not filter
            : (entity, grid, iff) => // Otherwise use simple search criteria
            {
                _entManager.TryGetComponent<MetaDataComponent>(entity, out var metadata);
                return metadata != null && metadata.EntityName.Contains(text, StringComparison.OrdinalIgnoreCase);
            };
    }

    public void SetConsole(EntityUid consoleEntity)
    {
        RadarScreen.SetConsole(consoleEntity);
    }
}
