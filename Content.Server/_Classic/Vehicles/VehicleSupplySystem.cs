using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Shared._Classic.Vehicles;
using Content.Shared._Classic.Vehicles.Supply;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Classic.Vehicles;

public sealed class VehicleSupplySystem : EntitySystem
{
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IComponentFactory _compFactory = default!;
    [Dependency] private readonly VehicleHardpointVisualsSystem _hardpointVisuals = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly PhysicsSystem _physics = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ClassicVehicleSystem _rmcVehicles = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly record struct PreviewOffset(
        Vector2 Base,
        bool UseDirectional,
        Vector2 North,
        Vector2 East,
        Vector2 South,
        Vector2 West);

    public override void Initialize()
    {
        SubscribeLocalEvent<VehicleSupplyConsoleComponent, BeforeActivatableUIOpenEvent>(OnConsoleBeforeUiOpen);
        SubscribeLocalEvent<VehicleSupplyLiftComponent, MapInitEvent>(OnLiftMapInit);

        Subs.BuiEvents<VehicleSupplyConsoleComponent>(VehicleSupplyUIKey.Key, subs =>
        {
            subs.Event<VehicleSupplySelectMsg>(OnVehicleSelected);
            subs.Event<VehicleSupplyLiftMsg>(OnLiftToggleRequested);
        });
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static int GetStoredCount(VehicleSupplyLiftComponent lift, string key)
    {
        return lift.Stored.TryGetValue(key, out var count) ? count : 0;
    }

    private static void AddStored(VehicleSupplyLiftComponent lift, string key, int amount = 1)
    {
        if (amount <= 0)
            return;

        lift.Stored[key] = GetStoredCount(lift, key) + amount;
    }

    private static bool TryRemoveStored(VehicleSupplyLiftComponent lift, string key, int amount = 1)
    {
        if (amount <= 0)
            return true;

        if (!lift.Stored.TryGetValue(key, out var count) || count < amount)
            return false;

        var next = count - amount;
        if (next <= 0)
            lift.Stored.Remove(key);
        else
            lift.Stored[key] = next;

        return true;
    }

    private static void AddStoredEntity(VehicleSupplyLiftComponent lift, string key, EntityUid vehicle)
    {
        if (!lift.StoredEntities.TryGetValue(key, out var list))
        {
            list = new List<EntityUid>();
            lift.StoredEntities[key] = list;
        }

        list.Add(vehicle);
    }

    private bool TryPopStoredEntity(VehicleSupplyLiftComponent lift, string key, out EntityUid vehicle)
    {
        vehicle = default;
        if (!lift.StoredEntities.TryGetValue(key, out var list))
            return false;

        for (var i = list.Count - 1; i >= 0; i--)
        {
            var candidate = list[i];
            list.RemoveAt(i);
            if (Deleted(candidate))
                continue;

            if (list.Count == 0)
                lift.StoredEntities.Remove(key);

            vehicle = candidate;
            return true;
        }

        if (list.Count == 0)
            lift.StoredEntities.Remove(key);

        return false;
    }

    private bool TryTakeStoredEntity(VehicleSupplyLiftComponent lift, string key, int index, out EntityUid vehicle)
    {
        vehicle = default;
        if (!lift.StoredEntities.TryGetValue(key, out var list) || list.Count == 0)
            return false;

        if (index < 0 || index >= list.Count)
            index = list.Count - 1;

        for (var attempts = 0; attempts < list.Count; attempts++)
        {
            var takeIndex = index;
            var candidate = list[takeIndex];
            list.RemoveAt(takeIndex);

            if (Deleted(candidate))
            {
                if (list.Count == 0)
                    break;

                index = Math.Min(index, list.Count - 1);
                continue;
            }

            if (list.Count == 0)
                lift.StoredEntities.Remove(key);

            vehicle = candidate;
            return true;
        }

        if (list.Count == 0)
            lift.StoredEntities.Remove(key);

        return false;
    }

    private bool TryGetStoredEntity(VehicleSupplyLiftComponent lift, string key, int index, out EntityUid vehicle)
    {
        vehicle = default;
        if (!lift.StoredEntities.TryGetValue(key, out var list) || list.Count == 0)
            return false;

        if (index < 0 || index >= list.Count)
            return false;

        var candidate = list[index];
        if (!Deleted(candidate))
        {
            vehicle = candidate;
            return true;
        }

        list.RemoveAt(index);

        if (list.Count == 0)
            lift.StoredEntities.Remove(key);

        return false;
    }

    private void OnConsoleBeforeUiOpen(Entity<VehicleSupplyConsoleComponent> ent, ref BeforeActivatableUIOpenEvent args)
    {
        SendConsoleState(ent.Owner, ent.Comp);
    }

    private void OnLiftMapInit(Entity<VehicleSupplyLiftComponent> ent, ref MapInitEvent args)
    {
        SeedStoredFromConsoles(ent);

        Dirty(ent);
    }

    private void SeedStoredFromConsoles(Entity<VehicleSupplyLiftComponent> lift)
    {
        var mapId = _transform.GetMapId(lift.Owner);

        var query = EntityQueryEnumerator<VehicleSupplyConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            foreach (var entry in console.Vehicles)
            {
                var key = Normalize(entry.Vehicle.Id);
                if (lift.Comp.Deployed.Contains(key))
                    continue;

                if (GetStoredCount(lift.Comp, key) > 0)
                    continue;

                AddStored(lift.Comp, key);
            }
        }
    }

    private void OnVehicleSelected(Entity<VehicleSupplyConsoleComponent> ent, ref VehicleSupplySelectMsg args)
    {
        if (string.IsNullOrWhiteSpace(args.VehicleId))
            return;

        if (!TryGetLift(ent.Owner, ent.Comp, out var lift))
            return;

        if (!TryGetEntry(ent.Comp, args.VehicleId, out var entry))
            return;

        var id = entry.Vehicle.Id;
        var idKey = Normalize(id);
        if (Normalize(lift.Comp.PendingVehicle) == idKey)
            return;

        if (GetStoredCount(lift.Comp, idKey) <= 0)
            return;

        ent.Comp.SelectedVehicle = id;
        ent.Comp.SelectedVehicleCopyIndex = Math.Max(0, args.CopyIndex);
        SendConsoleStateAll();
    }

    private void OnLiftToggleRequested(Entity<VehicleSupplyConsoleComponent> ent, ref VehicleSupplyLiftMsg args)
    {
        if (!TryGetLift(ent.Owner, ent.Comp, out var lift))
            return;

        TryToggleLift(ent, lift, args.Raise);
    }

    private void TryToggleLift(Entity<VehicleSupplyConsoleComponent> console, Entity<VehicleSupplyLiftComponent> lift, bool raise)
    {
        var comp = lift.Comp;
        if (comp.NextMode != null || comp.Busy)
            return;

        if (comp.Mode == VehicleSupplyLiftMode.Lowering || comp.Mode == VehicleSupplyLiftMode.Raising)
            return;

        if (raise)
        {
            if (comp.Mode == VehicleSupplyLiftMode.Raised)
                return;
            var selected = console.Comp.SelectedVehicle;
            var canQueueVehicle = false;
            string? nextVehicle = null;

            if (!string.IsNullOrWhiteSpace(selected))
            {
                if (TryGetEntry(console.Comp, selected, out var entry))
                {
                    var key = Normalize(selected);
                    if (GetStoredCount(comp, key) > 0 && _prototypes.TryIndex<EntityPrototype>(selected, out _))
                    {
                        if (TryRemoveStored(comp, key))
                        {
                            canQueueVehicle = true;
                            nextVehicle = selected;
                            comp.PendingVehicleEntity = null;
                            if (TryTakeStoredEntity(comp, key, console.Comp.SelectedVehicleCopyIndex, out var pendingEntity))
                                comp.PendingVehicleEntity = pendingEntity;

                            console.Comp.SelectedVehicle = string.Empty;
                            console.Comp.SelectedVehicleCopyIndex = 0;
                        }
                    }
                }
            }

            if (canQueueVehicle && nextVehicle != null)
            {
                comp.PendingVehicle = nextVehicle;
            }
            else
            {
                comp.PendingVehicle = string.Empty;
                comp.PendingVehicleEntity = null;
            }
        }
        else
        {
            if (comp.Mode == VehicleSupplyLiftMode.Lowered)
                return;

            if (comp.ActiveVehicle == null || !IsOnLift(lift, comp.ActiveVehicle.Value))
            {
                comp.ActiveVehicle = null;
                comp.ActiveVehicleId = string.Empty;
                TryAdoptVehicleOnLift(lift);
            }

            if (IsLoweringBlocked(lift))
                return;
        }

        comp.ToggledAt = _timing.CurTime;
        comp.Busy = true;
        SetMode(lift, VehicleSupplyLiftMode.Preparing, raise ? VehicleSupplyLiftMode.Raising : VehicleSupplyLiftMode.Lowering);
    }

    private void TryAdoptVehicleOnLift(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        var coords = _transform.GetMapCoordinates(lift);
        foreach (var candidate in _lookup.GetEntitiesInRange<ClassicVehicleComponent>(coords, comp.Radius))
        {
            if (Deleted(candidate.Owner) || candidate.Owner == comp.ActiveVehicle)
                continue;

            if (!TryComp(candidate.Owner, out MetaDataComponent? meta) || meta.EntityPrototype is not { } prototype)
                continue;

            comp.ActiveVehicle = candidate.Owner;
            comp.ActiveVehicleId = prototype.ID;
            return;
        }
    }

    private bool IsLoweringBlocked(Entity<VehicleSupplyLiftComponent> lift)
    {
        if (lift.Comp.ActiveVehicle is { } active &&
            IsOnLift(lift, active) &&
            _rmcVehicles.TryGetInteriorMapId(active, out var interiorMap))
        {
            var actorQuery = EntityQueryEnumerator<ActorComponent, TransformComponent>();
            while (actorQuery.MoveNext(out _, out _, out var xform))
            {
                if (xform.MapID == interiorMap)
                    return true;
            }
        }

        var mask = (int) (CollisionGroup.MobLayer | CollisionGroup.MobMask);
        foreach (var entity in _physics.GetEntitiesIntersectingBody(lift, mask, false))
        {
            if (HasComp<MobStateComponent>(entity))
                return true;
        }

        return false;
    }

    private void SetMode(Entity<VehicleSupplyLiftComponent> lift, VehicleSupplyLiftMode mode, VehicleSupplyLiftMode? nextMode)
    {
        lift.Comp.Mode = mode;
        lift.Comp.NextMode = nextMode;
        Dirty(lift);
        SendConsoleStateAll();
    }

    private void TryPlayAudio(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        if (comp.Audio != null || comp.ToggledAt == null)
            return;

        var time = _timing.CurTime;
        if (comp.NextMode == VehicleSupplyLiftMode.Lowering || comp.Mode == VehicleSupplyLiftMode.Lowering)
        {
            if (time < comp.ToggledAt + comp.LowerSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.LoweringSound, lift)?.Entity;
            return;
        }

        if (comp.NextMode == VehicleSupplyLiftMode.Raising || comp.Mode == VehicleSupplyLiftMode.Raising)
        {
            if (time < comp.ToggledAt + comp.RaiseSoundDelay)
                return;

            comp.Audio = _audio.PlayPvs(comp.RaisingSound, lift)?.Entity;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var updateUi = false;
        var liftQuery = EntityQueryEnumerator<VehicleSupplyLiftComponent>();
        while (liftQuery.MoveNext(out var uid, out var lift))
        {
            if (CleanupDestroyedActive((uid, lift)))
                updateUi = true;

            if (ProcessLift((uid, lift)))
                updateUi = true;
        }

        if (updateUi)
            SendConsoleStateAll();
    }

    private bool CleanupDestroyedActive(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        if (comp.ActiveVehicle == null)
            return false;

        var active = comp.ActiveVehicle.Value;
        if (Deleted(active))
        {
            if (!string.IsNullOrWhiteSpace(comp.ActiveVehicleId))
                comp.Deployed.Remove(Normalize(comp.ActiveVehicleId));

            comp.ActiveVehicle = null;
            comp.ActiveVehicleId = string.Empty;
            return true;
        }

        return false;
    }

    private bool ProcessLift(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        if (comp.ToggledAt == null)
            return false;

        var time = _timing.CurTime;
        if (time > comp.ToggledAt + comp.ToggleDelay)
        {
            comp.ToggledAt = null;
            comp.Busy = false;
            Dirty(lift);
            return true;
        }

        TryPlayAudio(lift);

        var delay = comp.NextMode == VehicleSupplyLiftMode.Raising ? comp.RaiseDelay : comp.LowerDelay;
        if (comp.Mode == VehicleSupplyLiftMode.Preparing &&
            comp.NextMode != null &&
            time > comp.ToggledAt + delay)
        {
            SetMode(lift, comp.NextMode.Value, null);
            return true;
        }

        if (comp.Mode != VehicleSupplyLiftMode.Lowering && comp.Mode != VehicleSupplyLiftMode.Raising)
            return false;

        var moveDelay = delay + (comp.Mode == VehicleSupplyLiftMode.Raising ? comp.RaiseDelay : comp.LowerDelay);
        if (time > comp.ToggledAt + moveDelay)
        {
            comp.Audio = null;

            var mode = comp.Mode == VehicleSupplyLiftMode.Raising
                ? VehicleSupplyLiftMode.Raised
                : VehicleSupplyLiftMode.Lowered;

            SetMode(lift, mode, comp.NextMode);
            if (mode == VehicleSupplyLiftMode.Raised)
                SpawnVehicle(lift);
            else
                StoreVehicle(lift);

            comp.ToggledAt = null;
            comp.Busy = false;
            Dirty(lift);
            return true;
        }

        return false;
    }

    private void SpawnVehicle(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        var pending = comp.PendingVehicle;
        if (string.IsNullOrWhiteSpace(pending))
            return;

        var key = Normalize(pending);
        if (comp.PendingVehicleEntity is { } pendingEntity && Exists(pendingEntity))
        {
            var moverCoords = _transform.GetMoverCoordinates(lift);
            var mapCoords = _transform.ToMapCoordinates(moverCoords);
            _transform.SetMapCoordinates(pendingEntity, mapCoords);

            comp.ActiveVehicle = pendingEntity;
            comp.ActiveVehicleId = pending;
            comp.PendingVehicle = string.Empty;
            comp.PendingVehicleEntity = null;
            comp.Deployed.Add(key);
            return;
        }

        comp.PendingVehicleEntity = null;
        if (TryPopStoredEntity(comp, key, out var stored))
        {
            var moverCoords = _transform.GetMoverCoordinates(lift);
            var mapCoords = _transform.ToMapCoordinates(moverCoords);
            _transform.SetMapCoordinates(stored, mapCoords);

            comp.ActiveVehicle = stored;
            comp.ActiveVehicleId = pending;
            comp.PendingVehicle = string.Empty;
            comp.Deployed.Add(key);
            return;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(pending, out _))
        {
            AddStored(comp, key);
            comp.PendingVehicle = string.Empty;
            return;
        }

        var spawnCoords = _transform.GetMoverCoordinates(lift);
        var vehicle = SpawnAtPosition(pending, spawnCoords);

        comp.ActiveVehicle = vehicle;
        comp.ActiveVehicleId = pending;
        comp.PendingVehicle = string.Empty;
        comp.Deployed.Add(key);
    }

    private void StoreVehicle(Entity<VehicleSupplyLiftComponent> lift)
    {
        var comp = lift.Comp;
        if (comp.ActiveVehicle == null)
            return;

        var active = comp.ActiveVehicle.Value;
        if (!IsOnLift(lift, active))
            return;

        if (!string.IsNullOrWhiteSpace(comp.ActiveVehicleId))
        {
            var key = Normalize(comp.ActiveVehicleId);
            comp.Deployed.Remove(key);
            AddStored(comp, key);
            AddStoredEntity(comp, key, active);

            EnsureVehicleInConsoles(lift, comp.ActiveVehicleId);
        }

        _transform.SetParent(active, EntityUid.Invalid);
        comp.ActiveVehicle = null;
        comp.ActiveVehicleId = string.Empty;
    }

    private bool IsOnLift(Entity<VehicleSupplyLiftComponent> lift, EntityUid entity)
    {
        if (!TryComp(lift.Owner, out TransformComponent? liftXform) ||
            !TryComp(entity, out TransformComponent? entityXform))
        {
            return false;
        }

        var liftCoords = _transform.GetMapCoordinates(lift.Owner, liftXform);
        var entityCoords = _transform.GetMapCoordinates(entity, entityXform);
        if (liftCoords.MapId != entityCoords.MapId)
            return false;

        var radius = lift.Comp.Radius;
        return (entityCoords.Position - liftCoords.Position).LengthSquared() <= radius * radius;
    }

    private void SendConsoleStateAll()
    {
        var query = EntityQueryEnumerator<VehicleSupplyConsoleComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            SendConsoleState(uid, comp);
        }
    }

    private void SendConsoleState(EntityUid uid, VehicleSupplyConsoleComponent? console = null)
    {
        if (!Resolve(uid, ref console, logMissing: false))
            return;

        var available = new List<VehicleSupplyEntryState>();

        VehicleSupplyLiftMode? mode = null;
        var busy = false;
        string? activeId = null;
        string? selectedId = string.IsNullOrWhiteSpace(console.SelectedVehicle) ? null : console.SelectedVehicle;
        var selectedCopyIndex = console.SelectedVehicleCopyIndex;
        VehicleSupplyPreviewState? preview = null;

        var hasLift = TryGetLift(uid, console, out var lift);
        if (hasLift)
        {
            mode = lift.Comp.Mode;
            busy = lift.Comp.Busy;
            activeId = string.IsNullOrWhiteSpace(lift.Comp.ActiveVehicleId) ? null : lift.Comp.ActiveVehicleId;
            selectedId = SanitizeSelectedVehicle(console, lift.Comp);
            selectedCopyIndex = console.SelectedVehicleCopyIndex;

            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                var key = Normalize(selectedId);
                var layers = new List<VehicleHardpointLayerState>();
                var overlays = new List<VehicleSupplyPreviewOverlay>();

                if (TryGetStoredEntity(lift.Comp, key, selectedCopyIndex, out var stored))
                {
                    layers = BuildPreviewLayers(stored);
                    overlays = BuildPreviewOverlays(stored);
                }

                preview = new VehicleSupplyPreviewState(selectedId, layers, overlays);
            }
        }

        foreach (var entry in console.Vehicles)
        {
            if (hasLift)
            {
                var key = Normalize(entry.Vehicle.Id);
                var count = GetStoredCount(lift.Comp, key);
                if (count <= 0)
                    continue;

                available.Add(new VehicleSupplyEntryState(entry.Vehicle.Id, GetEntryName(entry), count));
                continue;
            }

            available.Add(new VehicleSupplyEntryState(entry.Vehicle.Id, GetEntryName(entry), 1));
        }

        console.Ui = new VehicleSupplyUiState(mode, busy, activeId, selectedId, selectedCopyIndex, preview, available);
        Dirty(uid, console);
    }

    private string? SanitizeSelectedVehicle(
        VehicleSupplyConsoleComponent console,
        VehicleSupplyLiftComponent lift)
    {
        if (!string.IsNullOrWhiteSpace(console.SelectedVehicle) &&
            TryGetEntry(console, console.SelectedVehicle, out var selectedEntry))
        {
            var selectedKey = Normalize(selectedEntry.Vehicle.Id);
            var selectedCount = GetStoredCount(lift, selectedKey);
            if (selectedCount > 0)
            {
                console.SelectedVehicle = selectedEntry.Vehicle.Id;
                console.SelectedVehicleCopyIndex = Math.Clamp(console.SelectedVehicleCopyIndex, 0, selectedCount - 1);
                return console.SelectedVehicle;
            }
        }

        foreach (var entry in console.Vehicles)
        {
            var key = Normalize(entry.Vehicle.Id);
            if (GetStoredCount(lift, key) <= 0)
                continue;

            console.SelectedVehicle = entry.Vehicle.Id;
            console.SelectedVehicleCopyIndex = 0;
            return console.SelectedVehicle;
        }

        console.SelectedVehicle = string.Empty;
        console.SelectedVehicleCopyIndex = 0;
        return null;
    }

    public bool TryGetAnyLift(out Entity<VehicleSupplyLiftComponent> lift)
    {
        var query = EntityQueryEnumerator<VehicleSupplyLiftComponent>();
        if (query.MoveNext(out var uid, out var comp))
        {
            lift = (uid, comp);
            return true;
        }

        lift = default;
        return false;
    }

    public bool DebugAddVehicleToStorage(EntityUid liftUid, string vehicleId, out string? reason)
    {
        reason = null;

        if (!TryComp(liftUid, out VehicleSupplyLiftComponent? lift))
        {
            reason = $"Entity {liftUid} does not have VehicleSupplyLiftComponent.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            reason = "Vehicle id is empty.";
            return false;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(vehicleId, out _))
        {
            reason = $"Unknown vehicle prototype '{vehicleId}'.";
            return false;
        }

        var key = Normalize(vehicleId);

        AddStored(lift, key);

        Dirty(liftUid, lift);
        SendConsoleStateAll();
        return true;
    }

    public bool DebugEnsureVehicleOnAnyLift(string vehicleId, out string? reason)
    {
        reason = null;

        if (!TryGetAnyLift(out var lift))
        {
            reason = "No vehicle lift found.";
            return false;
        }

        var result = DebugEnsureVehicleInStorage(lift.Owner, vehicleId, out reason);
        if (result)
            DebugEnsureVehicleInConsoles(lift.Owner, vehicleId);

        return result;
    }

    public bool DebugEnsureVehicleInStorage(EntityUid liftUid, string vehicleId, out string? reason)
    {
        reason = null;

        if (!TryComp(liftUid, out VehicleSupplyLiftComponent? lift))
        {
            reason = $"Entity {liftUid} does not have VehicleSupplyLiftComponent.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(vehicleId))
        {
            reason = "Vehicle id is empty.";
            return false;
        }

        if (!_prototypes.TryIndex<EntityPrototype>(vehicleId, out _))
        {
            reason = $"Unknown vehicle prototype '{vehicleId}'.";
            return false;
        }

        var key = Normalize(vehicleId);

        var alreadyAvailable =
            GetStoredCount(lift, key) > 0 ||
            lift.Deployed.Contains(key) ||
            (!string.IsNullOrWhiteSpace(lift.PendingVehicle) && Normalize(lift.PendingVehicle) == key) ||
            (!string.IsNullOrWhiteSpace(lift.ActiveVehicleId) && Normalize(lift.ActiveVehicleId) == key);

        if (!alreadyAvailable)
            AddStored(lift, key);

        Dirty(liftUid, lift);
        SendConsoleStateAll();
        return true;
    }

    private void EnsureVehicleInConsoles(Entity<VehicleSupplyLiftComponent> lift, string vehicleId)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(vehicleId, out var proto))
            return;

        var query = EntityQueryEnumerator<VehicleSupplyConsoleComponent>();
        while (query.MoveNext(out var uid, out var console))
        {
            if (!TryGetLift(uid, console, out var consoleLift) || consoleLift.Owner != lift.Owner)
                continue;

            if (TryGetEntry(console, vehicleId, out _))
                continue;

            console.Vehicles.Add(new VehicleSupplyEntry
            {
                Vehicle = vehicleId,
                Name = proto.Name,
            });
        }
    }

    public void DebugEnsureVehicleInConsoles(EntityUid liftUid, string vehicleId)
    {
        if (!_prototypes.TryIndex<EntityPrototype>(vehicleId, out var proto))
            return;

        var mapId = _transform.GetMapId(liftUid);
        var query = EntityQueryEnumerator<VehicleSupplyConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var console, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            if (TryGetEntry(console, vehicleId, out _))
                continue;

            console.Vehicles.Add(new VehicleSupplyEntry
            {
                Vehicle = vehicleId,
                Name = proto.Name
            });

            SendConsoleState(uid, console);
        }
    }

    private bool TryGetLift(EntityUid consoleUid, VehicleSupplyConsoleComponent console, out Entity<VehicleSupplyLiftComponent> lift)
    {
        if (console.Lift is { } cached &&
            !Deleted(cached) &&
            TryComp(cached, out VehicleSupplyLiftComponent? cachedComp))
        {
            lift = (cached, cachedComp);
            return true;
        }

        console.Lift = null;
        lift = default;
        var found = false;
        var bestDistance = float.MaxValue;

        var consoleCoords = _transform.GetMapCoordinates(consoleUid);
        var query = EntityQueryEnumerator<VehicleSupplyLiftComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            var liftCoords = _transform.GetMapCoordinates(uid, xform);
            if (liftCoords.MapId != consoleCoords.MapId)
                continue;

            var distance = (liftCoords.Position - consoleCoords.Position).LengthSquared();
            if (distance > console.LiftSearchRange * console.LiftSearchRange)
                continue;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            lift = (uid, comp);
            found = true;
        }

        if (found)
            console.Lift = lift.Owner;

        return found;
    }

    private bool TryGetEntry(VehicleSupplyConsoleComponent console, string vehicleId, out VehicleSupplyEntry entry)
    {
        var key = Normalize(vehicleId);
        foreach (var candidate in console.Vehicles)
        {
            if (Normalize(candidate.Vehicle.Id) == key)
            {
                entry = candidate;
                return true;
            }
        }

        entry = default!;
        return false;
    }

    private string GetEntryName(VehicleSupplyEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Name))
            return entry.Name;

        return GetPrototypeName(entry.Vehicle.Id);
    }

    private string GetPrototypeName(string protoId)
    {
        if (_prototypes.TryIndex<EntityPrototype>(protoId, out var proto))
            return proto.Name;

        return protoId;
    }

    private List<VehicleHardpointLayerState> BuildPreviewLayers(
        EntityUid vehicle,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return new List<VehicleHardpointLayerState>();

        var layers = new List<VehicleHardpointLayerState>(hardpoints.Slots.Count);
        var indexByLayer = new Dictionary<string, int>();

        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            var layer = slot.VisualLayer;
            if (string.IsNullOrWhiteSpace(layer))
                continue;

            var state = string.Empty;
            var usesOverlay = false;
            if (_itemSlots.TryGetSlot(vehicle, slot.Id, out var itemSlot, itemSlots) && itemSlot.HasItem)
            {
                var item = itemSlot.Item!.Value;
                state = ResolveVisualState(item, out usesOverlay);
            }

            var key = layer.ToLowerInvariant();
            if (indexByLayer.TryGetValue(key, out var existingIndex))
            {
                if (!string.IsNullOrWhiteSpace(state))
                    layers[existingIndex] = new VehicleHardpointLayerState(layer, state);
                continue;
            }

            indexByLayer[key] = layers.Count;
            if (usesOverlay)
                state = string.Empty;
            layers.Add(new VehicleHardpointLayerState(layer, state));
        }

        return layers;
    }

    private List<VehicleSupplyPreviewOverlay> BuildPreviewOverlays(
        EntityUid vehicle,
        HardpointSlotsComponent? hardpoints = null,
        ItemSlotsComponent? itemSlots = null)
    {
        if (!Resolve(vehicle, ref hardpoints, ref itemSlots, logMissing: false))
            return new List<VehicleSupplyPreviewOverlay>();

        var overlays = new List<VehicleSupplyPreviewOverlay>();
        var turretOffsets = new Dictionary<string, PreviewOffset>();

        foreach (var slot in hardpoints.Slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
                continue;

            if (!_itemSlots.TryGetSlot(vehicle, slot.Id, out var itemSlot, itemSlots) || !itemSlot.HasItem)
                continue;

            var item = itemSlot.Item!.Value;
            if (TryGetTurretOverlay(item, 0, out var overlay, out var offset))
            {
                overlays.Add(overlay);
                turretOffsets[slot.Id] = offset;
            }

            if (!TryComp(item, out HardpointSlotsComponent? attachedSlots) ||
                !TryComp(item, out ItemSlotsComponent? attachedItemSlots))
            {
                continue;
            }

            foreach (var turretSlot in attachedSlots.Slots)
            {
                if (string.IsNullOrWhiteSpace(turretSlot.Id))
                    continue;

                if (!_itemSlots.TryGetSlot(item, turretSlot.Id, out var turretItemSlot, attachedItemSlots) ||
                    !turretItemSlot.HasItem)
                {
                    continue;
                }

                var child = turretItemSlot.Item!.Value;
                if (!TryGetTurretOverlay(child, 1, out var childOverlay, out var childOffset))
                    continue;

                if (turretOffsets.TryGetValue(slot.Id, out var parentOffset))
                {
                    var combined = CombineOffsets(parentOffset, childOffset);
                    childOverlay = new VehicleSupplyPreviewOverlay(
                        childOverlay.Rsi,
                        childOverlay.State,
                        childOverlay.Order,
                        combined.Base,
                        combined.UseDirectional,
                        combined.North,
                        combined.East,
                        combined.South,
                        combined.West);
                }

                overlays.Add(childOverlay);
            }
        }

        return overlays;
    }

    private bool TryGetTurretOverlay(
        EntityUid item,
        int order,
        out VehicleSupplyPreviewOverlay overlay,
        out PreviewOffset offset)
    {
        overlay = default!;
        offset = default;

        if (!TryComp(item, out VehicleTurretComponent? turret))
            return false;

        if (!turret.ShowOverlay || string.IsNullOrWhiteSpace(turret.OverlayState) || string.IsNullOrWhiteSpace(turret.OverlayRsi))
            return false;

        offset = new PreviewOffset(
            turret.PixelOffset,
            turret.UseDirectionalOffsets,
            turret.PixelOffsetNorth,
            turret.PixelOffsetEast,
            turret.PixelOffsetSouth,
            turret.PixelOffsetWest);

        overlay = new VehicleSupplyPreviewOverlay(
            turret.OverlayRsi,
            turret.OverlayState,
            order,
            offset.Base,
            offset.UseDirectional,
            offset.North,
            offset.East,
            offset.South,
            offset.West);
        return true;
    }

    private static PreviewOffset CombineOffsets(PreviewOffset a, PreviewOffset b)
    {
        var useDirectional = a.UseDirectional || b.UseDirectional;
        var north = (a.UseDirectional ? a.North : Vector2.Zero) + (b.UseDirectional ? b.North : Vector2.Zero);
        var east = (a.UseDirectional ? a.East : Vector2.Zero) + (b.UseDirectional ? b.East : Vector2.Zero);
        var south = (a.UseDirectional ? a.South : Vector2.Zero) + (b.UseDirectional ? b.South : Vector2.Zero);
        var west = (a.UseDirectional ? a.West : Vector2.Zero) + (b.UseDirectional ? b.West : Vector2.Zero);
        return new PreviewOffset(a.Base + b.Base, useDirectional, north, east, south, west);
    }

    private string ResolveVisualState(EntityUid item, out bool usesOverlay, int depth = 0)
    {
        return _hardpointVisuals.ResolveVisualState(item, out usesOverlay, depth);
    }
}

