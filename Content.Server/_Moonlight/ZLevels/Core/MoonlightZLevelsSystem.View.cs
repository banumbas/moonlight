/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared._Moonlight.ZLevels.Core.Components;
using Content.Shared._Moonlight.ZLevels.Core.EntitySystems;
using Content.Shared.Actions;
using Content.Shared.CCVar;
using Content.Shared.Ghost;
using Content.Shared.IdentityManagement;
using Content.Shared.Popups;
using Content.Shared.Tag;
using Content.Server.Parallax;
using System.Numerics;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Moonlight.ZLevels.Core;

public sealed partial class MoonlightZLevelsSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private ViewSubscriberSystem _viewSubscriber = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private TagSystem _viewerTags = default!;
    [Dependency] private EntityQuery<GhostComponent> _viewerGhostQuery = default!;

    private readonly EntProtoId _zEyeProto = "MoonlightZLevelEye";
    private static readonly ProtoId<TagPrototype> AllowBiomeLoadingTag = "AllowBiomeLoading";
    private readonly List<EntityUid> _visibleUpperMaps = new(MaxZLevelsAboveRendering);
    private readonly List<EntityUid> _staleUpperEyes = new(MaxZLevelsAboveRendering);

    private readonly TimeSpan _zLevelViewerUpdateRate = TimeSpan.FromSeconds(1f);
    private TimeSpan _nextZLevelViewerUpdate = TimeSpan.Zero;

    private void InitView()
    {
        _configuration.OnValueChanged(
            CCVars.MoonlightZLevelsRenderingMaxZLevelsBelowRendering,
            OnMaxLevelsBelowChanged);

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        SubscribeLocalEvent<GhostComponent, ComponentInit>(OnGhostStateChanged);

        SubscribeLocalEvent<MoonlightZLevelViewerComponent, MapInitEvent>(OnViewerInit);
        SubscribeLocalEvent<MoonlightZLevelViewerComponent, ComponentRemove>(OnCompRemove);

        SubscribeLocalEvent<MoonlightZLevelViewerComponent, MoonlightZLevelBeforeMapMoveEvent>(OnViewerBeforeMapMove);
        SubscribeLocalEvent<MoonlightZLevelViewerComponent, MapUidChangedEvent>(OnViewerMapUidChanged);
        SubscribeLocalEvent<MoonlightZLevelViewerComponent, MoveEvent>(OnViewerMove);
        SubscribeLocalEvent<MoonlightZLevelViewerComponent, MetaFlagRemoveAttemptEvent>(OnViewerMetaFlagRemoveAttempt);
    }

    private void ShutdownView()
    {
        _configuration.UnsubValueChanged(
            CCVars.MoonlightZLevelsRenderingMaxZLevelsBelowRendering,
            OnMaxLevelsBelowChanged);
    }

    private void OnMaxLevelsBelowChanged(int value)
    {
        var query = EntityQueryEnumerator<MoonlightZLevelViewerComponent>();
        while (query.MoveNext(out var uid, out var viewer))
            UpdateViewer((uid, viewer));
    }

    private void UpdateView(float frameTime)
    {
        var refreshVisibility = _timing.CurTime >= _nextZLevelViewerUpdate;
        if (refreshVisibility)
            _nextZLevelViewerUpdate = _timing.CurTime + _zLevelViewerUpdateRate;

        var query = EntityQueryEnumerator<MoonlightZLevelViewerComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var viewer, out var xform))
        {
            var view = GetViewedPosition(uid, xform);
            if (viewer.ViewMap != view.Map || viewer.CanLoadBiomes != CanLoadViewerBiomes(uid))
            {
                UpdateViewer((uid, viewer));
                continue;
            }

            if (!viewer.CanLoadBiomes)
                continue;

            // Camera targets and offsets can move without a MoveEvent on the viewer.
            if (viewer.ViewPosition != view.Position)
            {
                foreach (var eye in viewer.Eyes)
                {
                    if (!viewer.UpperEyes.Contains(eye))
                        _transform.SetWorldPosition(eye, view.Position);
                }
                viewer.ViewPosition = view.Position;
            }

            if (viewer.LookUp)
                UpdateUpperEyes((uid, viewer), view, allowSameTileFastPath: !refreshVisibility);
        }
    }

    private void OnViewerInit(Entity<MoonlightZLevelViewerComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.ActionId);
        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    /// <summary>Whether an entity is an auxiliary Z-eye belonging to this viewer.</summary>
    public bool IsViewerEye(EntityUid? viewer, EntityUid eye)
    {
        return TryComp<MoonlightZLevelViewerComponent>(viewer, out var component) && component.Eyes.Contains(eye);
    }

    private void OnCompRemove(Entity<MoonlightZLevelViewerComponent> ent, ref ComponentRemove args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);

        TryComp<ActorComponent>(ent, out var actor);
        foreach (var eye in ent.Comp.Eyes)
        {
            CacheEyeBiome(eye);
            if (actor != null)
                _viewSubscriber.RemoveViewSubscriber(eye, actor.PlayerSession);
            QueueDel(eye);
        }
        ent.Comp.Eyes.Clear();
        ent.Comp.UpperEyes.Clear();
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        var viewer = EnsureComp<MoonlightZLevelViewerComponent>(ev.Entity);
        UpdateViewer((ev.Entity, viewer));
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        RemComp<MoonlightZLevelViewerComponent>(ev.Entity);
    }

    private void OnGhostStateChanged(Entity<GhostComponent> ent, ref ComponentInit args)
    {
        if (TryComp<MoonlightZLevelViewerComponent>(ent, out var viewer))
            UpdateViewer((ent.Owner, viewer));
    }

    private void OnViewerMapUidChanged(Entity<MoonlightZLevelViewerComponent> ent, ref MapUidChangedEvent args)
    {
        UpdateViewer(ent);
    }

    private void OnViewerMove(Entity<MoonlightZLevelViewerComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.LookUp && !args.OnlyRotation)
            UpdateUpperEyes(ent);
    }

    private void OnViewerMetaFlagRemoveAttempt(
        Entity<MoonlightZLevelViewerComponent> ent,
        ref MetaFlagRemoveAttemptEvent args)
    {
        if ((args.ToRemove & MetaDataFlags.ExtraTransformEvents) != 0 &&
            ent.Comp.LifeStage <= ComponentLifeStage.Running)
        {
            args.ToRemove &= ~MetaDataFlags.ExtraTransformEvents;
        }
    }

    private void OnViewerBeforeMapMove(
        Entity<MoonlightZLevelViewerComponent> ent,
        ref MoonlightZLevelBeforeMapMoveEvent args)
    {
        CacheEyeBiome(ent);
    }

    protected override void LookUpChanged(Entity<MoonlightZLevelViewerComponent> entity)
    {
        UpdateUpperEyes(entity);
    }

    private void UpdateViewer(Entity<MoonlightZLevelViewerComponent> ent)
    {
        var view = GetViewedPosition(ent);
        ent.Comp.ViewMap = view.Map;
        ent.Comp.ViewPosition = view.Position;
        ent.Comp.CanLoadBiomes = CanLoadViewerBiomes(ent);

        var eyes = ent.Comp.Eyes;
        TryComp<ActorComponent>(ent, out var actor);
        foreach (var eye in ent.Comp.Eyes)
        {
            CacheEyeBiome(eye);
            if (actor != null)
                _viewSubscriber.RemoveViewSubscriber(eye, actor.PlayerSession);
            QueueDel(eye);
        }
        eyes.Clear();
        ent.Comp.UpperEyes.Clear();
        SetVisibleZLevelsAbove(ent, null, default, 0);

        if (!ent.Comp.CanLoadBiomes)
            return;

        if (actor == null)
            return;

        var map = view.Map;

        if (map is null)
            return;

        var globalPos = view.Position;

        var maxLevelsBelow = Math.Clamp(
            Math.Max(1, _configuration.GetCVar(CCVars.MoonlightZLevelsRenderingMaxZLevelsBelowRendering)),
            1,
            MaxZLevelsBelowRendering);

        for (var i = 1; i <= maxLevelsBelow; i++)
        {
            if (!TryMapOffset(map.Value, -i, out var mapUidBelow))
                break;

            var newEye = SpawnAtPosition(_zEyeProto, new EntityCoordinates(mapUidBelow, globalPos));

            Transform(newEye).GridTraversal = false;
            _viewSubscriber.AddViewSubscriber(newEye, actor.PlayerSession);
            eyes.Add(newEye);
        }

        UpdateUpperEyes(ent, view);
    }

    private void UpdateUpperEyes(
        Entity<MoonlightZLevelViewerComponent> ent,
        (EntityUid? Map, Vector2 Position)? view = null,
        bool allowSameTileFastPath = false)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
        {
            foreach (var eye in ent.Comp.Eyes)
            {
                CacheEyeBiome(eye);
                QueueDel(eye);
            }
            ent.Comp.Eyes.Clear();
            ent.Comp.UpperEyes.Clear();
            SetVisibleZLevelsAbove(ent, null, default, 0);
            return;
        }

        var (viewMap, globalPos) = view ?? GetViewedPosition(ent);
        EntityUid? currentMap = null;
        var currentTile = default(Vector2i);

        if (viewMap is { } mapUid)
        {
            currentMap = mapUid;
            currentTile = new Vector2i(
                (int) MathF.Floor(globalPos.X),
                (int) MathF.Floor(globalPos.Y));
        }

        if (allowSameTileFastPath &&
            currentMap != null &&
            ent.Comp.VisibleZLevelsAboveMap == currentMap &&
            ent.Comp.VisibleZLevelsAboveTile == currentTile)
        {
            foreach (var eye in ent.Comp.UpperEyes)
                _transform.SetWorldPosition(eye, globalPos);
            return;
        }

        _visibleUpperMaps.Clear();
        _staleUpperEyes.Clear();

        if (ent.Comp.LookUp &&
            CanLoadViewerBiomes(ent) &&
            viewMap is { } map)
        {
            var biomes = EntityManager.System<BiomeSystem>();
            for (var i = 1; i <= MaxZLevelsAboveRendering; i++)
            {
                if (!TryMapOffset(map, i, out var mapAbove) || biomes.MoonlightIsTileOpaque(mapAbove, globalPos))
                    break;

                _visibleUpperMaps.Add(mapAbove);
            }
        }

        SetVisibleZLevelsAbove(ent, currentMap, currentTile, _visibleUpperMaps.Count);

        foreach (var eye in ent.Comp.UpperEyes)
        {
            if (TryComp<TransformComponent>(eye, out var eyeXform) &&
                eyeXform.MapUid is { } eyeMap &&
                _visibleUpperMaps.Contains(eyeMap))
                continue;

            _staleUpperEyes.Add(eye);
        }

        foreach (var eye in _staleUpperEyes)
        {
            CacheEyeBiome(eye);
            _viewSubscriber.RemoveViewSubscriber(eye, actor.PlayerSession);
            ent.Comp.Eyes.Remove(eye);
            ent.Comp.UpperEyes.Remove(eye);
            QueueDel(eye);
        }

        foreach (var eye in ent.Comp.UpperEyes)
            _transform.SetWorldPosition(eye, globalPos);

        foreach (var mapAbove in _visibleUpperMaps)
        {
            var exists = false;
            foreach (var eye in ent.Comp.UpperEyes)
            {
                if (TryComp<TransformComponent>(eye, out var eyeXform) && eyeXform.MapUid == mapAbove)
                {
                    exists = true;
                    break;
                }
            }

            if (exists)
                continue;

            var newEye = SpawnAtPosition(_zEyeProto, new EntityCoordinates(mapAbove, globalPos));
            Transform(newEye).GridTraversal = false;
            _viewSubscriber.AddViewSubscriber(newEye, actor.PlayerSession);
            ent.Comp.Eyes.Add(newEye);
            ent.Comp.UpperEyes.Add(newEye);
        }
    }

    private void SetVisibleZLevelsAbove(
        Entity<MoonlightZLevelViewerComponent> ent,
        EntityUid? map,
        Vector2i tile,
        int count)
    {
        var value = (byte) Math.Clamp(count, 0, MaxZLevelsAboveRendering);
        if (ent.Comp.VisibleZLevelsAbove == value &&
            ent.Comp.VisibleZLevelsAboveMap == map &&
            ent.Comp.VisibleZLevelsAboveTile == tile)
            return;

        ent.Comp.VisibleZLevelsAbove = value;
        ent.Comp.VisibleZLevelsAboveMap = map;
        ent.Comp.VisibleZLevelsAboveTile = tile;
        Dirty(ent, ent.Comp);
    }

    private void CacheEyeBiome(EntityUid eye)
    {
        EntityManager.System<BiomeSystem>().CacheMoonlightZLevel(eye);
    }

    private bool CanLoadViewerBiomes(EntityUid viewer)
    {
        return !_viewerGhostQuery.HasComp(viewer) || _viewerTags.HasTag(viewer, AllowBiomeLoadingTag);
    }
}
