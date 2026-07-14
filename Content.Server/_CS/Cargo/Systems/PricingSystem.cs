using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Linq;

namespace Content.Server.Cargo.Systems;

public sealed partial class PricingSystem
{
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedMapSystem _mapManager = default!;
    /// <summary>
    /// Minimum price added to a ship for being exped capable.
    /// </summary>
    private int _expedCapableMinPrice = 50000;

    /// <summary>
    /// Minimum price added to a ship for using donk appliances.
    /// </summary>
    private int _donkCapableMinPrice = 30000;

    private void CSInitialize()
    {
        _consoleHost.RegisterCommand("coyoteappraisegrid",
            "Calculates the total value of a grid including tile count, markup, price per tile, and expedition/donk bonuses.",
            "coyoteappraisegrid <gridId> [markup=value] [pricePerTile=value] [expedCapable=bool] [donkCapable=bool]",
            CoyoteAppraiseGridCommand, CoyoteAppraiseGridCommandCompletions);
    }

    [AdminCommand(AdminFlags.Debug)]
    private void CoyoteAppraiseGridCommand(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length == 0)
        {
            shell.WriteError("Not enough arguments. Usage: coyoteappraisegrid <gridId> [markup=value] [pricePerTile=value] [expedCapable=bool] [donkCapable=bool]");
            return;
        }

        // Parse overrides from arguments (format: key=value)
        float? markupOverride = null;
        int? pricePerTileOverride = null;
        bool? expedCapableOverride = null;
        bool? donkCapableOverride = null;

        List<string> gridArgs = new();
        foreach (var arg in args)
        {
            var parts = arg.Split('=', 2);
            if (parts.Length == 2)
            {
                var key = parts[0].ToLowerInvariant();
                var val = parts[1];
                switch (key)
                {
                    case "markup":
                        if (float.TryParse(val, out var m))
                            markupOverride = m;
                        else
                            shell.WriteError($"Invalid markup value: {val}");
                        break;
                    case "pricepertile":
                        if (int.TryParse(val, out var p))
                            pricePerTileOverride = p;
                        else
                            shell.WriteError($"Invalid pricePerTile value: {val}");
                        break;
                    case "expedcapable":
                        if (bool.TryParse(val, out var e))
                            expedCapableOverride = e;
                        else
                            shell.WriteError($"Invalid expedCapable value: {val}");
                        break;
                    case "donkcapable":
                        if (bool.TryParse(val, out var d))
                            donkCapableOverride = d;
                        else
                            shell.WriteError($"Invalid donkCapable value: {val}");
                        break;
                    default:
                        shell.WriteError($"Unknown parameter: {key}");
                        break;
                }
            }
            else
            {
                gridArgs.Add(arg);
            }
        }

        // Process each grid ID
        foreach (var gid in gridArgs)
        {
            if (!EntityManager.TryParseNetEntity(gid, out var gridId) || !gridId.Value.IsValid())
            {
                shell.WriteError($"Invalid grid ID \"{gid}\".");
                continue;
            }

            if (!TryComp(gridId, out MapGridComponent? mapGrid))
            {
                shell.WriteError($"Grid \"{gridId}\" doesn't exist.");
                continue;
            }

            // 1. Raw appraisal
            var rawAppraisal = AppraiseGrid(gridId.Value, null);

            // 2. Get tile count
            var tileCount = 0;
            if (TryComp(gridId, out MapGridComponent? gridComp))
            {
                tileCount = _map.GetAllTiles(gridId.Value, gridComp).Count();
            }

            // 3. Determine modifiers (deed or overrides)
            float markup = 1f;
            int pricePerTile = 100;
            bool expedCapable = false;
            bool donkCapable = false;

            // Apply overrides
            if (markupOverride.HasValue) markup = markupOverride.Value;
            if (pricePerTileOverride.HasValue) pricePerTile = pricePerTileOverride.Value;
            if (expedCapableOverride.HasValue) expedCapable = expedCapableOverride.Value;
            if (donkCapableOverride.HasValue) donkCapable = donkCapableOverride.Value;

            // 4. Compute modified value
            var modified = rawAppraisal * markup;

            if (pricePerTile > 0)
                modified += tileCount * pricePerTile;

            if (expedCapable)
            {
                var expedBonus = modified * 0.5;
                modified += expedBonus <= _expedCapableMinPrice ? _expedCapableMinPrice : expedBonus;
            }
            if (donkCapable)
            {
                var donkBonus = modified * 0.3;
                modified += donkBonus <= _donkCapableMinPrice ? _donkCapableMinPrice : donkBonus;
            }

            int modifiedInt = (int)Math.Round(modified);

            // 5. Output
            shell.WriteLine($"Grid {gid}:");
            shell.WriteLine($"  Raw appraisal: {rawAppraisal:F2} credits");
            shell.WriteLine($"  Tile count: {tileCount}");
            shell.WriteLine($"  Markup: {markup:F2}x");
            shell.WriteLine($"  Price per tile: {pricePerTile} credits/tile");
            shell.WriteLine($"  Exped capable: {expedCapable}");
            shell.WriteLine($"  Donk capable: {donkCapable}");
            shell.WriteLine($"  Modified appraisal: {modifiedInt:F2} credits");
            shell.WriteLine("");
        }
    }
    private CompletionResult CoyoteAppraiseGridCommandCompletions(IConsoleShell shell, string[] args)
    {
        MapId? playerMap = null;
        if (shell.Player is { AttachedEntity: { } playerEnt })
            playerMap = Transform(playerEnt).MapID;

        var options = new List<CompletionOption>();

        if (playerMap == null)
            return CompletionResult.FromOptions(options);

        foreach (var grid in _mapManager.GetAllGrids(playerMap.Value).OrderBy(o => o.Owner))
        {
            var uid = grid.Owner;
            if (!TryComp(uid, out TransformComponent? gridXform))
                continue;

            options.Add(new CompletionOption(uid.ToString(), $"{MetaData(uid).EntityName} - Map {gridXform.MapID}"));
        }

        return CompletionResult.FromOptions(options);
    }
}
