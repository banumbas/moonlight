using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

using Robust.Shared.Map;

namespace Content.Shared._Moonlight.PMC;

[Serializable, NetSerializable]
public sealed partial class OrbitalDesignatorDoAfterEvent : DoAfterEvent
{
    public NetCoordinates TargetPosition;

    public OrbitalDesignatorDoAfterEvent(NetCoordinates targetPosition) => TargetPosition = targetPosition;

    public override DoAfterEvent Clone() => this;
}
