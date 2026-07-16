using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.RCD;
using Content.Shared.RCD.Components;
using Content.Shared.RCD.Systems;
using Robust.Client.Placement;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.RCD;

public sealed partial class RCDConstructionGhostSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IPlacementManager _placementManager = default!;
    [Dependency] private IPrototypeManager _protoManager = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    private string _placementMode = typeof(AlignRCDConstruction).Name;
    private Direction _placementDirection = default;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Get current placer data
        var placerEntity = _placementManager.CurrentPermission?.MobUid;
        var placerProto = _placementManager.CurrentPermission?.EntityType;
        var placerIsRCD = HasComp<RCDComponent>(placerEntity);

        // Exit if erasing or the current placer is not an RCD (build mode is active)
        if (_placementManager.Eraser || (placerEntity != null && !placerIsRCD))
            return;

        // Determine if player is carrying an RCD in their active hand
        var player = _playerManager.LocalSession?.AttachedEntity;

        if (!TryComp<HandsComponent>(player, out var hands))
            return;

        var heldEntity = hands.ActiveHand?.HeldEntity;

        if (!TryComp<RCDComponent>(heldEntity, out var rcd))
        {
            // If the player was holding an RCD, but is no longer, cancel placement
            if (placerIsRCD)
                _placementManager.Clear();

            return;
        }
        var prototype = _protoManager.Index(rcd.ProtoId);

        // Update the direction the RCD prototype based on the placer direction
        if (_placementDirection != _placementManager.Direction)
        {
            _placementDirection = _placementManager.Direction;
            RaiseNetworkEvent(new RCDConstructionGhostRotationEvent(GetNetEntity(heldEntity.Value), _placementDirection));
        }

        var placementId = prototype.Prototype ?? string.Empty;
        var tileType = 0;
        if (prototype.Mode == RcdMode.ConstructTile && _tileDefs.TryGetDefinition(placementId, out var tile))
            tileType = tile.TileId;

        // Tile recipes may select a different preview as their direction changes.
        if (heldEntity == placerEntity && placementId == placerProto
            && _placementManager.CurrentPermission?.TileType == tileType)
            return;

        // Create a new placer
        var newObjInfo = new PlacementInformation
        {
            MobUid = heldEntity.Value,
            PlacementOption = _placementMode,
            EntityType = placementId,
            TileType = tileType,
            Range = (int) Math.Ceiling(SharedInteractionSystem.InteractionRange),
            IsTile = (prototype.Mode == RcdMode.ConstructTile),
            UseEditorContext = false,
        };

        _placementManager.Clear();
        _placementManager.BeginPlacing(newObjInfo);
    }
}
