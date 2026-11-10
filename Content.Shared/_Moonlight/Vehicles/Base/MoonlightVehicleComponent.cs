using Content.Shared._Moonlight.Vehicles;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(MoonlightVehicleBaseSystem))]
public sealed partial class MoonlightVehicleComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid? Operator;

    [DataField, AutoNetworkedField]
    public EntityWhitelist? OperatorWhitelist;

    [DataField, AutoNetworkedField]
    public bool TransferDamage = true;

    [DataField, AutoNetworkedField]
    public DamageModifierSet? TransferDamageModifier;

    [DataField, AutoNetworkedField]
    public MoonlightVehicleMovementKind MovementKind = MoonlightVehicleMovementKind.Grid;

    /// <summary>Default exterior view position, in vehicle-local metres. Windows can supply their own position.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 CameraOffset;
}

[Serializable, NetSerializable]
public enum MoonlightVehicleVisuals : byte
{
    HasOperator,
    CanRun
}

[Serializable, NetSerializable]
public enum MoonlightVehicleMovementKind : byte
{
    Standard,
    Grid,
    Flight,
}

[ByRefEvent, UsedImplicitly]
public readonly record struct OnMoonlightVehicleEnteredEvent(Entity<MoonlightVehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct OnMoonlightVehicleExitedEvent(Entity<MoonlightVehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct MoonlightVehicleOperatorSetEvent(EntityUid? NewOperator, EntityUid? OldOperator);

[ByRefEvent, UsedImplicitly]
public record struct MoonlightVehicleCanRunEvent(Entity<MoonlightVehicleComponent> Vehicle, bool CanRun = true);
