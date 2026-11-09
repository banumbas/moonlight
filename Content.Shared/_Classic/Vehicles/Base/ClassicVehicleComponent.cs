using Content.Shared._Classic.Vehicles;
using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Whitelist;
using JetBrains.Annotations;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ClassicVehicleBaseSystem))]
public sealed partial class ClassicVehicleComponent : Component
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
    public ClassicVehicleMovementKind MovementKind = ClassicVehicleMovementKind.Grid;

    /// <summary>Default exterior view position, in vehicle-local metres. Windows can supply their own position.</summary>
    [DataField, AutoNetworkedField]
    public Vector2 CameraOffset;
}

[Serializable, NetSerializable]
public enum ClassicVehicleVisuals : byte
{
    HasOperator,
    CanRun
}

[Serializable, NetSerializable]
public enum ClassicVehicleMovementKind : byte
{
    Standard,
    Grid,
    Flight,
}

[ByRefEvent, UsedImplicitly]
public readonly record struct OnClassicVehicleEnteredEvent(Entity<ClassicVehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct OnClassicVehicleExitedEvent(Entity<ClassicVehicleComponent> Vehicle, EntityUid Operator);

[ByRefEvent, UsedImplicitly]
public readonly record struct ClassicVehicleOperatorSetEvent(EntityUid? NewOperator, EntityUid? OldOperator);

[ByRefEvent, UsedImplicitly]
public record struct ClassicVehicleCanRunEvent(Entity<ClassicVehicleComponent> Vehicle, bool CanRun = true);
