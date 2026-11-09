using Content.Shared._Classic.Vehicles;
using System;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Vehicles;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleSmashableComponent : Component
{
    [DataField]
    public bool DeleteOnHit = true;

    [DataField]
    public double DamageOnHit = 1000;

    [DataField]
    public float SlowdownMultiplier = 0.5f;

    [DataField]
    public float SlowdownDuration = 0f;

    [DataField]
    public SoundSpecifier? SmashSound;

    [DataField]
    public bool RequiresDoorUnpowered;
}
