using System.Numerics;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.CCVar;
using Content.Shared.Light.Components;
using Content.Shared.Weather;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using AudioComponent = Robust.Shared.Audio.Components.AudioComponent;

namespace Content.Client.Weather;

public sealed partial class WeatherSystem : SharedWeatherSystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private MapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IConfigurationManager _cfg = default!; // _CS

    private float _weatherVolume = 0f; // _CS

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, CCVars.WeatherEffectsVolume, SetWeatherGain, true); // _CS
        SubscribeLocalEvent<WeatherComponent, ComponentHandleState>(OnWeatherHandleState);
        SubscribeLocalEvent<WeatherComponent, ComponentShutdown>(OnWeatherShutdown);
    }

    protected override void Run(EntityUid uid, WeatherData weather, WeatherPrototype weatherProto, float frameTime)
    {
        base.Run(uid, weather, weatherProto, frameTime);

        var ent = _playerManager.LocalEntity;

        if (ent == null)
        {
            weather.Stream = _audio.Stop(weather.Stream);
            return;
        }

        var entXform = Transform(ent.Value);
        if (!ShouldPlayWeatherForEntity(uid, entXform))
        {
            weather.Stream = _audio.Stop(weather.Stream);
            return;
        }

        if (!Timing.IsFirstTimePredicted || weatherProto.Sound == null)
            return;

        weather.Stream ??= _audio.PlayGlobal(weatherProto.Sound, Filter.Local(), true)?.Entity;

        if (!TryComp(weather.Stream, out AudioComponent? comp))
            return;

        var occlusion = 0f;

        // Work out tiles nearby to determine volume.
        if (TryComp<MapGridComponent>(entXform.GridUid, out var grid))
        {
            TryComp(entXform.GridUid, out RoofComponent? roofComp);
            var gridId = entXform.GridUid.Value;
            // FloodFill to the nearest tile and use that for audio.
            var seed = _mapSystem.GetTileRef(gridId, grid, entXform.Coordinates);
            var frontier = new Queue<TileRef>();
            frontier.Enqueue(seed);
            // If we don't have a nearest node don't play any sound.
            EntityCoordinates? nearestNode = null;
            var visited = new HashSet<Vector2i>();

            while (frontier.TryDequeue(out var node))
            {
                if (!visited.Add(node.GridIndices))
                    continue;

                if (!CanWeatherAffect(entXform.GridUid.Value, grid, node, roofComp))
                {
                    // Add neighbors
                    // TODO: Ideally we pick some deterministically random direction and use that
                    // We can't just do that naively here because it will flicker between nearby tiles.
                    for (var x = -1; x <= 1; x++)
                    {
                        for (var y = -1; y <= 1; y++)
                        {
                            if (Math.Abs(x) == 1 && Math.Abs(y) == 1 ||
                                x == 0 && y == 0 ||
                                (new Vector2(x, y) + node.GridIndices - seed.GridIndices).Length() > 3)
                            {
                                continue;
                            }

                            frontier.Enqueue(_mapSystem.GetTileRef(gridId, grid, new Vector2i(x, y) + node.GridIndices));
                        }
                    }

                    continue;
                }

                nearestNode = new EntityCoordinates(entXform.GridUid.Value,
                    node.GridIndices + grid.TileSizeHalfVector);
                break;
            }

            // Get occlusion to the targeted node if it exists, otherwise set a default occlusion.
            if (nearestNode != null)
            {
                var entPos = _transform.GetMapCoordinates(entXform);
                var nodePosition = _transform.ToMapCoordinates(nearestNode.Value).Position;
                var delta = nodePosition - entPos.Position;
                var distance = delta.Length();
                occlusion = _audio.GetOcclusion(entPos, delta, distance);
            }
            else
            {
                occlusion = 3f;
            }
        }

        var alpha = GetPercent(weather, uid);
        alpha *= SharedAudioSystem.VolumeToGain(weatherProto.Sound.Params.Volume);
        alpha *= GetWeatherVolumeScale(weatherProto.ID); // _CS
        alpha *= _weatherVolume; // _CS
        _audio.SetGain(weather.Stream, alpha, comp);
        comp.Occlusion = occlusion;
    }

    // _CS Start: weather effects volume control
    private void SetWeatherGain(float value)
    {
        _weatherVolume = SharedAudioSystem.GainToVolume(value);
    }

    private static float GetWeatherVolumeScale(string weatherId)
    {
        if (weatherId.EndsWith("Light", StringComparison.OrdinalIgnoreCase))
            return 0.5f;

        if (weatherId.EndsWith("Medium", StringComparison.OrdinalIgnoreCase))
            return 0.75f;

        if (weatherId.EndsWith("Heavy", StringComparison.OrdinalIgnoreCase))
            return 1.0f;

        // Single-level weather effects are reduced by 50%.
        return 0.5f;
    }
    // _CS End: weather effects volume control

    protected override bool SetState(EntityUid uid, WeatherState state, WeatherComponent comp, WeatherData weather, WeatherPrototype weatherProto)
    {
        if (!base.SetState(uid, state, comp, weather, weatherProto))
            return false;

        if (!Timing.IsFirstTimePredicted)
            return true;

        if (_playerManager.LocalEntity is not { } ent)
        {
            weather.Stream = _audio.Stop(weather.Stream);
            return true;
        }

        var entXform = Transform(ent);
        if (!ShouldPlayWeatherForEntity(uid, entXform))
        {
            weather.Stream = _audio.Stop(weather.Stream);
            return true;
        }

        // TODO: Fades (properly)
        weather.Stream = _audio.Stop(weather.Stream);
        weather.Stream = _audio.PlayGlobal(weatherProto.Sound, Filter.Local(), true)?.Entity;
        return true;
    }

    private bool ShouldPlayWeatherForEntity(EntityUid weatherUid, TransformComponent entXform)
    {
        var mapUid = Transform(weatherUid).MapUid;

        if (mapUid == null || entXform.MapUid != mapUid)
            return false;

        if (entXform.GridUid is { } gridUid &&
            TryComp<FTLComponent>(gridUid, out var ftl) &&
            (ftl.State == FTLState.Starting || ftl.State == FTLState.Travelling))
            return false;

        return true;
    }

    private void OnWeatherShutdown(EntityUid uid, WeatherComponent component, ComponentShutdown args)
    {
        foreach (var weather in component.Weather.Values)
        {
            weather.Stream = _audio.Stop(weather.Stream);
        }
    }

    private void OnWeatherHandleState(EntityUid uid, WeatherComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not WeatherComponentState state)
            return;

        foreach (var (proto, weather) in component.Weather)
        {
            // End existing one
            if (!state.Weather.TryGetValue(proto, out var stateData))
            {
                EndWeather(uid, component, proto);
                continue;
            }

            // Data update?
            weather.StartTime = stateData.StartTime;
            weather.EndTime = stateData.EndTime;
            weather.State = stateData.State;
        }

        foreach (var (proto, weather) in state.Weather)
        {
            if (component.Weather.ContainsKey(proto))
                continue;

            // New weather
            StartWeather(uid, component, ProtoMan.Index<WeatherPrototype>(proto), weather.EndTime);
        }
    }
}
