using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Moonlight.VendingMachines;

[RegisterComponent, AutoGenerateComponentPause]
[Access(typeof(AutoRestockVendingMachineSystem))]
public sealed partial class AutoRestockVendingMachineComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Item;

    [DataField]
    public uint MaxStock = 3;

    [DataField]
    public TimeSpan RestockDelay = TimeSpan.FromSeconds(60);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextRestock;
}
