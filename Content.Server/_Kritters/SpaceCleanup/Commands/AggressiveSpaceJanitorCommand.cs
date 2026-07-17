using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Kritters.SpaceCleanup.Commands;

/// <summary>
/// Allows server administrators to manage Bluespace Janitorial Services cleanup.
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
    [Dependency] private IComponentFactory _components = default!;

    private ISawmill? _sawmill;

    private static readonly string[] Subcommands =
    [
        "list",
        "exempt",
        "sweep",
        "cleanup",
        "cleanup-cancel",
        "cleanup-force",
        "cleanup-cull",
        "status",
        "grid",
        "grid-cancel",
        "grid-force",
        "grid-cull",
    ];

    public string Command => "spacejanny";
    public string Description => "Manages Bluespace Janitorial Services cleanup.";
    public string Help => "Usage: spacejanny list [prototype-filter ... | has:<component> ...] | exempt <prototype-id> [<prototype-id> ...] | sweep <prototype-id> [<prototype-id> ...] | cleanup | cleanup-cancel | cleanup-force | cleanup-cull <prototype-id> [<prototype-id> ...] | status [grid [grid-id]] | grid [grid-id] | grid-cancel [grid-id] | grid-force [grid-id] | grid-cull <prototype-id> [<prototype-id> ...] [grid-id]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }

        var janitor = _systems.GetEntitySystem<AggressiveSpaceJanitorSystem>();
        switch (args[0])
        {
            case "list":
                ListEntities(shell, janitor, args[1..]);
                return;
            case "exempt":
                SetExemption(shell, janitor, args[1..], exempt: true);
                return;
            case "sweep":
                SetExemption(shell, janitor, args[1..], exempt: false);
                return;
            case "cleanup" when args.Length == 1:
                LogResult(shell, "started expedited cleanup in open space", janitor.RunSpaceCleanup());
                return;
            case "cleanup-cancel" when args.Length == 1:
                LogResult(shell, "cancelled expedited cleanup in open space", janitor.CancelSpaceCleanup());
                return;
            case "cleanup-force" when args.Length == 1:
                LogResult(shell, "force-cleaned open space", janitor.ForceSpaceCleanup());
                return;
            case "cleanup-cull":
                CullSpace(shell, janitor, args[1..]);
                return;
            case "status":
                ShowStatus(shell, janitor, args);
                return;
            case "grid-cull":
                CullGrid(shell, janitor, args[1..]);
                return;
            case "grid" or "grid-cancel" or "grid-force":
                RunGridCommand(shell, janitor, args);
                return;
            default:
                shell.WriteError(Help);
                return;
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHintOptions(Subcommands, "<subcommand>");

        return args[0] switch
        {
            "list" => GetListCompletion(args[^1]),
            "exempt" or "sweep" or "cleanup-cull" => GetPrototypeCompletion(args[^1]),
            "status" => GetStatusCompletion(args),
            "grid" or "grid-cancel" or "grid-force" => GetGridCommandCompletion(args),
            "grid-cull" => GetGridCullCompletion(args[^1]),
            _ => CompletionResult.Empty,
        };
    }

    private CompletionResult GetListCompletion(string argument)
    {
        if (argument.StartsWith("has:", StringComparison.OrdinalIgnoreCase))
        {
            var componentName = argument[4..];
            var components = _components.GetAllRegistrations()
                .Where(registration => registration.Name.StartsWith(componentName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(registration => registration.Name, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .Select(registration => new CompletionOption($"has:{registration.Name}"));
            return CompletionResult.FromHintOptions(components, "<component>");
        }

        var options = CompletionHelper.PrototypeIdsLimited<EntityPrototype>(argument, _prototypes)
            .Append(new CompletionOption("has:", Flags: CompletionOptionFlags.PartialCompletion));
        return CompletionResult.FromHintOptions(options, "<prototype filter | has:component>");
    }

    private CompletionResult GetPrototypeCompletion(string argument)
    {
        return CompletionResult.FromHintOptions(
            CompletionHelper.PrototypeIdsLimited<EntityPrototype>(argument, _prototypes),
            "<prototype id>");
    }

    private CompletionResult GetStatusCompletion(string[] args)
    {
        if (args.Length == 2)
            return CompletionResult.FromHintOptions(new[] { "grid" }, "[grid]");

        return args.Length == 3 && args[1] == "grid"
            ? GetGridCompletion(args[2])
            : CompletionResult.Empty;
    }

    private CompletionResult GetGridCommandCompletion(string[] args)
    {
        return args.Length == 2
            ? GetGridCompletion(args[1])
            : CompletionResult.Empty;
    }

    private CompletionResult GetGridCullCompletion(string argument)
    {
        var options = CompletionHelper.PrototypeIdsLimited<EntityPrototype>(argument, _prototypes)
            .Concat(CompletionHelper.Components<MapGridComponent>(argument, _entities));
        return CompletionResult.FromHintOptions(options, "<prototype id | grid id>");
    }

    private CompletionResult GetGridCompletion(string argument)
    {
        return CompletionResult.FromHintOptions(
            CompletionHelper.Components<MapGridComponent>(argument, _entities),
            "[grid id]");
    }

    private void ListEntities(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] filters)
    {
        var prototypeFilters = new List<string>();
        var componentFilters = new List<Type>();
        foreach (var filter in filters)
        {
            if (!filter.StartsWith("has:", StringComparison.OrdinalIgnoreCase))
            {
                prototypeFilters.Add(filter);
                continue;
            }

            var componentName = filter[4..];
            if (componentName.Length == 0
                || !_components.TryGetRegistration(componentName, out var registration, ignoreCase: true))
            {
                shell.WriteError($"The specified component does not exist: {componentName}.");
                return;
            }

            componentFilters.Add(registration.Type);
        }

        var entries = janitor.GetInspectionEntries(prototypeFilters, componentFilters);
        if (entries.Count == 0)
        {
            shell.WriteLine("Bluespace Janitorial Services found no matching entities.");
            return;
        }

        foreach (var entry in entries.OrderBy(entry => entry.PrototypeId, StringComparer.Ordinal)
                     .ThenBy(entry => entry.Grid?.Id ?? int.MinValue)
                     .ThenBy(entry => entry.Position.X)
                     .ThenBy(entry => entry.Position.Y))
        {
            var location = entry.Grid is { } grid ? $"Grid {_entities.GetNetEntity(grid)}" : "space";
            var timer = entry.Remaining is { } remaining
                ? $"{FormatRemaining(remaining)} remaining"
                : $"not deletable ({entry.IneligibilityReason ?? "unknown"})";
            var scope = entry.Grid == null ? "in space" : "on grid";
            var noun = entry.ScopePrototypeCount == 1 ? "instance" : "instances";
            shell.WriteLine($"{entry.PrototypeId} | {location} | ({entry.Position.X:F1}, {entry.Position.Y:F1}) | {timer} | {entry.ScopePrototypeCount} {entry.PrototypeId} {noun} {scope}");
        }
    }

    private void SetExemption(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] prototypeArguments, bool exempt)
    {
        if (!TryGetPrototypeIds(shell, prototypeArguments, out var prototypeIds)
            || !TryGetGrid(shell, null, out var grid))
        {
            return;
        }

        var affected = janitor.SetGridPrototypeExemption(grid, prototypeIds, exempt);
        var action = exempt ? "exempted entities on grid" : "returned entities to normal cleanup on grid";
        LogResult(shell, $"{action} {grid}", affected);
    }

    private void CullSpace(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] prototypeArguments)
    {
        if (!TryGetPrototypeIds(shell, prototypeArguments, out var prototypeIds))
            return;

        var culled = janitor.ForceSpacePrototypeCleanup(prototypeIds);
        LogResult(shell, $"culled {prototypeIds.Count} prototypes in open space", culled);
    }

    private void CullGrid(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] arguments)
    {
        if (arguments.Length == 0)
        {
            shell.WriteError(Help);
            return;
        }

        EntityUid grid;
        var prototypeArguments = arguments;
        if (arguments.Length > 1 && TryParseGrid(arguments[^1], out var specifiedGrid))
        {
            grid = specifiedGrid;
            prototypeArguments = arguments[..^1];
        }
        else if (!TryGetGrid(shell, null, out grid))
        {
            return;
        }

        if (!TryGetPrototypeIds(shell, prototypeArguments, out var prototypeIds))
            return;

        var culled = janitor.ForceGridPrototypeCleanup(grid, prototypeIds);
        LogResult(shell, $"culled {prototypeIds.Count} prototypes on grid {grid}", culled);
    }

    private void ShowStatus(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] args)
    {
        if (args.Length == 1)
        {
            var count = janitor.GetForceEligibleCount();
            shell.WriteLine($"Bluespace Janitorial Services found {count} loose items in open space.");
            return;
        }

        if (args.Length is < 2 or > 3 || args[1] != "grid"
            || !TryGetGrid(shell, args.Length == 3 ? args[2] : null, out var grid))
        {
            shell.WriteError(Help);
            return;
        }

        var gridCount = janitor.GetForceEligibleCount(grid);
        shell.WriteLine($"Bluespace Janitorial Services found {gridCount} loose items on grid {grid}.");
    }

    private void RunGridCommand(IConsoleShell shell, AggressiveSpaceJanitorSystem janitor, string[] args)
    {
        if (args.Length is < 1 or > 2 || !TryGetGrid(shell, args.Length == 2 ? args[1] : null, out var grid))
        {
            shell.WriteError(Help);
            return;
        }

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

    private bool TryGetPrototypeIds(IConsoleShell shell, string[] arguments, out HashSet<string> prototypeIds)
    {
        prototypeIds = new HashSet<string>(StringComparer.Ordinal);
        if (arguments.Length == 0)
        {
            shell.WriteError(Help);
            return false;
        }

        foreach (var prototypeId in arguments)
        {
            if (!_prototypes.HasIndex<EntityPrototype>(prototypeId))
            {
                shell.WriteError($"The specified prototype does not exist: {prototypeId}.");
                return false;
            }

            prototypeIds.Add(prototypeId);
        }

        return true;
    }

    private bool TryGetGrid(IConsoleShell shell, string? gridArgument, out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        if (gridArgument != null && !TryParseGrid(gridArgument, out grid))
        {
            shell.WriteError("The specified entity is not a grid.");
            return false;
        }

        if (gridArgument != null)
            return true;

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

    private bool TryParseGrid(string gridArgument, out EntityUid grid)
    {
        grid = EntityUid.Invalid;
        return NetEntity.TryParse(gridArgument, out var netGrid)
               && _entities.TryGetEntity(netGrid, out var gridEntity)
               && _entities.HasComponent<MapGridComponent>(gridEntity)
               && (grid = gridEntity.Value) != EntityUid.Invalid;
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var totalHours = (int) Math.Floor(Math.Max(0, remaining.TotalHours));
        return totalHours > 0
            ? $"{totalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}"
            : $"{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }

    private void LogResult(IConsoleShell shell, string action, int affected)
    {
        var actor = shell.Player is { } player ? $"{player.Name} ({player.UserId})" : "server console";
        _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme,
            $"{actor} {action}; affected {affected} entities.");
        var report = $"Bluespace Janitorial Services {action}; {affected} entities affected.";
        _sawmill ??= _log.GetSawmill("space_janitor");
        _sawmill.Info(report);
        _chat.SendAdminAnnouncement(report);
        shell.WriteLine(report);
    }
}
