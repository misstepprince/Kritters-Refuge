using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.SpaceCleanup.Commands;

/// <summary>
/// Allows server administrators to manage safeguarded aggressive space-janitor cleanup.
/// </summary>
[AdminCommand(AdminFlags.Server)]
public sealed partial class AggressiveSpaceJanitorCommand : IConsoleCommand
{
    [Dependency] private IEntitySystemManager _systems = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private ISawmill? _sawmill;

    public string Command => "spacejanitor";
    public string Description => "Runs or reports aggressive space janitor cleanup.";
    public string Help => "Usage: spacejanitor cleanup | cleanup-cancel | cleanup-force | cleanup-cull <prototype-id> | status [grid [grid-id]] | grid [grid-id] | grid-cancel [grid-id] | grid-force [grid-id] | grid-cull <prototype-id> [grid-id]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 1 && args[0] == "cleanup")
        {
            var tracked = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>().RunSpaceCleanup();
            LogResult(shell, "started expedited cleanup in open space", tracked);
            return;
        }

        if (args.Length == 1 && args[0] == "cleanup-cancel")
        {
            var cancelled = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>().CancelSpaceCleanup();
            LogResult(shell, "cancelled expedited cleanup in open space", cancelled);
            return;
        }

        if (args.Length == 1 && args[0] == "cleanup-force")
        {
            var queued = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>().ForceSpaceCleanup();
            LogResult(shell, "force-cleaned open space", queued);
            return;
        }

        if (args.Length >= 1 && args[0] == "cleanup-cull")
        {
            if (args.Length != 2)
            {
                shell.WriteError(Help);
                return;
            }

            var prototypeId = args[1];
            if (!_prototypes.HasIndex<EntityPrototype>(prototypeId))
            {
                shell.WriteError($"The specified prototype does not exist: {prototypeId}.");
                return;
            }

            var culled = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>()
                .ForceSpacePrototypeCleanup(prototypeId);
            LogResult(shell, $"culled prototype {prototypeId} in open space", culled);
            return;
        }

        if (args.Length >= 1 && args[0] == "status")
        {
            if (args.Length == 1)
            {
                var count = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>().GetForceEligibleCount();
                shell.WriteLine($"Aggressive space janitor found {count} loose items in open space.");
                return;
            }

            if (args.Length is < 2 or > 3 || args[1] != "grid"
                || !TryGetGrid(shell, args.Length == 3 ? args[2] : null, out var statusGrid))
            {
                shell.WriteError(Help);
                return;
            }

            var gridCount = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>().GetForceEligibleCount(statusGrid);
            shell.WriteLine($"Aggressive space janitor found {gridCount} loose items on grid {statusGrid}.");
            return;
        }

        if (args.Length >= 1 && args[0] == "grid-cull")
        {
            if (args.Length is < 2 or > 3)
            {
                shell.WriteError(Help);
                return;
            }

            var prototypeId = args[1];
            if (!_prototypes.HasIndex<EntityPrototype>(prototypeId))
            {
                shell.WriteError($"The specified prototype does not exist: {prototypeId}.");
                return;
            }

            if (!TryGetGrid(shell, args.Length == 3 ? args[2] : null, out var cullGrid))
                return;

            var culled = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>()
                .ForceGridPrototypeCleanup(cullGrid, prototypeId);
            LogResult(shell, $"culled prototype {prototypeId} on grid {cullGrid}", culled);
            return;
        }

        if (args.Length is < 1 or > 2 || args[0] is not ("grid" or "grid-cancel" or "grid-force"))
        {
            shell.WriteError(Help);
            return;
        }

        if (!TryGetGrid(shell, args.Length == 2 ? args[1] : null, out var grid))
            return;

        var janitor = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>();
        var force = args[0] == "grid-force";
        var cancel = args[0] == "grid-cancel";
        var affected = force
            ? janitor.ForceGridCleanup(grid)
            : cancel
                ? janitor.CancelGridCleanup(grid)
                : janitor.RunGridCleanup(grid);
        var action = force
            ? $"force-cleaned grid {grid}"
            : cancel
                ? $"cancelled expedited cleanup on grid {grid}"
                : $"started expedited cleanup on grid {grid}";
        LogResult(shell, action, affected);
    }

    private bool TryGetGrid(IConsoleShell shell, string? gridArgument, out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        if (gridArgument != null)
        {
            if (!NetEntity.TryParse(gridArgument, out var netGrid)
                || !_entities.TryGetEntity(netGrid, out var gridEntity)
                || !_entities.HasComponent<MapGridComponent>(gridEntity))
            {
                shell.WriteError("The specified entity is not a grid.");
                return false;
            }

            grid = gridEntity.Value;
            return true;
        }

        if (shell.Player?.AttachedEntity is not { Valid: true } player
            || !_entities.TryGetComponent<TransformComponent>(player, out var xform)
            || xform.GridUid is not { } playerGrid)
        {
            shell.WriteError("You must be standing on a grid or specify a grid ID.");
            return false;
        }

        grid = playerGrid;
        return true;
    }

    private void LogResult(IConsoleShell shell, string action, int affected)
    {
        var actor = shell.Player is { } player ? $"{player.Name} ({player.UserId})" : "server console";
        _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme,
            $"{actor} {action}; affected {affected} entities.");
        var report = $"Aggressive space janitor {action}; {affected} entities affected.";
        _sawmill ??= _log.GetSawmill("space_janitor");
        _sawmill.Info(report);
        _chat.SendAdminAnnouncement(report);
        shell.WriteLine(report);
    }
}
