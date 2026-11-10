namespace Content.Server._Moonlight.Geyser;

[RegisterComponent, Access(typeof(MoonlightGeyserSystem))]
public sealed partial class MoonlightGeyserGeneratorComponent : Component
{
    [DataField]
    public float IdleSupply;

    [ViewVariables]
    public TimeSpan PoweredUntil = TimeSpan.Zero;

    [ViewVariables]
    public bool PoweredByGeyser;
}
