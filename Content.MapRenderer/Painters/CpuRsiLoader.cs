using System;
using System.Collections.Generic;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.MapRenderer.Painters;

public sealed class CpuRsiLoader : IDisposable
{
    public static readonly ResPath ErrorPath = new("/Textures/error.rsi");

    private readonly IDependencyCollection _dependencies;
    private readonly IResourceCache _resourceCache;
    private readonly Dictionary<ResPath, LoadedRsi> _loaded = new();

    public CpuRsiLoader(IDependencyCollection dependencies)
    {
        _dependencies = dependencies;
        _resourceCache = dependencies.Resolve<IResourceCache>();
    }

    public void Load(ResPath path)
    {
        path = path.ToRootedPath();
        if (_loaded.ContainsKey(path))
            return;

        var resource = new RSIResource();
        LoadedRsi? loaded = null;

        void OnRsiLoaded(RsiLoadedEventArgs args)
        {
            if (!ReferenceEquals(args.Resource, resource) || args.Path != path)
                return;

            var offsets = CloneOffsets(args.AtlasOffsets);
            loaded = new LoadedRsi(
                args.Atlas.CloneAs<Rgba32>(),
                resource.RSI.Size,
                offsets);
        }

        _resourceCache.OnRsiLoaded += OnRsiLoaded;
        try
        {
            resource.Load(_dependencies, path);
        }
        catch
        {
            loaded?.Atlas.Dispose();
            throw;
        }
        finally
        {
            _resourceCache.OnRsiLoaded -= OnRsiLoaded;
            resource.Dispose();
        }

        if (loaded == null)
            throw new InvalidOperationException($"RSI load for {path} did not provide CPU atlas data.");

        _loaded.Add(path, loaded);
    }

    public bool TryGetFrame(ResPath path, RSI.StateId state, int direction, int frame, out Image<Rgba32>? image)
    {
        path = path.ToRootedPath();
        if (!_loaded.TryGetValue(path, out var loaded) ||
            !loaded.Offsets.TryGetValue(state, out var directions) ||
            directions.Length == 0)
        {
            image = null;
            return false;
        }

        direction = Math.Clamp(direction, 0, directions.Length - 1);
        var frames = directions[direction];
        if (frames.Length == 0)
        {
            image = null;
            return false;
        }

        frame = ((frame % frames.Length) + frames.Length) % frames.Length;
        var offset = frames[frame];
        var rect = new Rectangle(offset.X, offset.Y, loaded.FrameSize.X, loaded.FrameSize.Y);
        if (!new Rectangle(Point.Empty, loaded.Atlas.Size).Contains(rect))
        {
            image = null;
            return false;
        }

        image = loaded.Atlas.Clone(o => o.Crop(rect));
        return true;
    }

    public void Dispose()
    {
        foreach (var loaded in _loaded.Values)
        {
            loaded.Atlas.Dispose();
        }

        _loaded.Clear();
    }

    private static Dictionary<RSI.StateId, Vector2i[][]> CloneOffsets(
        Dictionary<RSI.StateId, Vector2i[][]> offsets)
    {
        var clone = new Dictionary<RSI.StateId, Vector2i[][]>(offsets.Count);
        foreach (var (state, directions) in offsets)
        {
            var directionClone = new Vector2i[directions.Length][];
            for (var i = 0; i < directions.Length; i++)
            {
                directionClone[i] = (Vector2i[]) directions[i].Clone();
            }

            clone.Add(state, directionClone);
        }

        return clone;
    }

    private sealed record LoadedRsi(
        Image<Rgba32> Atlas,
        Vector2i FrameSize,
        Dictionary<RSI.StateId, Vector2i[][]> Offsets);
}
