using System;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Ranged;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GunSpinupComponent : Component
{
    [DataField, AutoNetworkedField]
    public float BaseShotDelay = 0.7f;

    [DataField, AutoNetworkedField]
    public float BaseScatter = 18f;

    [DataField, AutoNetworkedField]
    public float SpinUpTime = 10f;

    [DataField, AutoNetworkedField]
    public float GraceAfterStop = 2f;

    [DataField, AutoNetworkedField]
    public float SpinDownTime = 3f;

    [DataField, AutoNetworkedField]
    public float MinSpinLevel = 1f;

    [DataField, AutoNetworkedField]
    public float MaxSpinLevel = 11f;

    [DataField, AutoNetworkedField]
    public int[] RateTiers = [1, 1, 2, 2, 3, 3, 3, 4, 4, 4, 5];

    [DataField, AutoNetworkedField]
    public SoundSpecifier? StartSound = new SoundPathSpecifier("/Audio/_Classic/Vehicle/weapons/minigun_start.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? LoopSound = new SoundPathSpecifier("/Audio/Weapons/Guns/Gunshots/minigun.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? StopSound = new SoundPathSpecifier("/Audio/_Classic/Vehicle/weapons/minigun_stop.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? SelectSound = new SoundPathSpecifier("/Audio/_Classic/Vehicle/weapons/minigun_select.ogg");

    [DataField, AutoNetworkedField]
    public float LoopSoundCooldown = 0.2f;

    [DataField, AutoNetworkedField]
    public float FireWindowPadding = 0.12f;

    [DataField, AutoNetworkedField]
    public float InitialWindupDelay = 0f;

    [DataField, AutoNetworkedField]
    public float InitialWindupResetGap = 0.2f;
}

[KeyFunctions]
public static class VehicleKeyFunctions
{
    public static readonly BoundKeyFunction VehicleUniqueAction = "VehicleUniqueAction";
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleFlamerAmmoProviderComponent : Component, IShootable
{
    [DataField, AutoNetworkedField]
    public string ContainerId = "gun_magazine";

    [DataField, AutoNetworkedField]
    public TimeSpan DelayPer = TimeSpan.FromSeconds(0.05);

    [DataField, AutoNetworkedField]
    public FixedPoint2 CostPer = FixedPoint2.New(1);

    [DataField, AutoNetworkedField]
    public TimeSpan CantShootPopupCooldown = TimeSpan.FromSeconds(0.25);
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleFlamerTankComponent : Component
{
    [DataField, AutoNetworkedField]
    public string SolutionId = "vehicle_flamer_tank";

    [DataField, AutoNetworkedField]
    public int MaxIntensity = 40;

    [DataField, AutoNetworkedField]
    public int MaxDuration = 30;

    [DataField, AutoNetworkedField]
    public int MaxRange = 5;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class RemoveComponentsComponent : Component
{
    [DataField]
    public ComponentRegistry? Components;
}

public sealed class RemoveComponentsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<RemoveComponentsComponent, ComponentInit>(OnRemoveComponentsInit);
    }

    private void OnRemoveComponentsInit(Entity<RemoveComponentsComponent> ent, ref ComponentInit args)
    {
        if (ent.Comp.Components != null)
            EntityManager.RemoveComponents(ent, ent.Comp.Components);
    }
}
