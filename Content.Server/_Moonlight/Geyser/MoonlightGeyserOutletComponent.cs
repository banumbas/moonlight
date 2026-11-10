using Content.Shared.Decals;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Moonlight.Geyser;

[RegisterComponent, Access(typeof(MoonlightGeyserSystem))]
public sealed partial class MoonlightGeyserOutletComponent : Component
{
    [DataField]
    public EntProtoId MistPrototype = "MoonlightGeyserSteamMist";

    [DataField]
    public EntProtoId FlashPrototype = "MoonlightGeyserSteamFlash";

    [DataField]
    public ProtoId<DecalPrototype> ScarDecal = "Damaged";

    [DataField]
    public bool LeavesSnowScars;

    [DataField]
    public float MinDormantTime = 35f;

    [DataField]
    public float MaxDormantTime = 70f;

    [DataField]
    public float EruptionDuration = 8f;

    [DataField]
    public float MistSpawnInterval = 0.8f;

    [DataField]
    public float MistRadius = 2f;

    [DataField]
    public int MistBurstCount = 7;

    [DataField]
    public float GeneratorSupply = 12000f;

    [DataField]
    public float GeneratorOutputDuration = 30f;

    [DataField]
    public SoundSpecifier EruptionSound = new SoundPathSpecifier("/Audio/Ambience/Objects/gas_hiss.ogg");

    [ViewVariables]
    public TimeSpan NextEruptionTime = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan EruptionEndTime = TimeSpan.Zero;

    [ViewVariables]
    public TimeSpan NextMistTime = TimeSpan.Zero;

    [ViewVariables]
    public bool Erupting;
}
