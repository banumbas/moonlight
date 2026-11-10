using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Moonlight.Salvage.Fulton;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class MoonlightFultonSoldComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Effect { get; set; }

    [DataField, AutoNetworkedField]
    public TimeSpan FultonDuration = TimeSpan.FromSeconds(45);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextFulton;

    [DataField, AutoNetworkedField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_launch.ogg");

    [DataField, AutoNetworkedField]
    public bool Removeable = true;

    [ViewVariables]
    public EntityUid? SaleStation;

    [ViewVariables]
    public TimeSpan? SaleTime;

    [ViewVariables]
    public EntityCoordinates? OriginalCoordinates;
}
