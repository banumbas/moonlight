using Content.Shared._Moonlight.Vehicles;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Moonlight.Vehicles;

[Serializable, NetSerializable]
public sealed partial class BulletBoxTransferDoAfterEvent : SimpleDoAfterEvent
{
    public readonly bool ToBox;

    public BulletBoxTransferDoAfterEvent(bool toFrom)
    {
        ToBox = toFrom;
    }
}
