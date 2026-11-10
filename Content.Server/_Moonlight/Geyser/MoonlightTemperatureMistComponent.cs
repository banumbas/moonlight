using Content.Shared.Damage;

namespace Content.Server._Moonlight.Geyser;

[RegisterComponent, Access(typeof(MoonlightGeyserSystem))]
public sealed partial class MoonlightTemperatureMistComponent : Component
{
    [DataField]
    public float Radius = 0.8f;

    [DataField]
    public DamageSpecifier Damage = new();

    [DataField]
    public float HeatPerSecond = 30000f;

    [DataField]
    public float TickInterval = 1f;

    [DataField]
    public bool IgnoreResistances;

    [ViewVariables]
    public TimeSpan NextTick = TimeSpan.Zero;
}
