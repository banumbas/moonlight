using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Moonlight.Salvage.Fulton;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MoonlightCargoFultonComponent : Component
{
    [DataField, AutoNetworkedField]
    public TimeSpan ApplyFultonDuration = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField]
    public bool Removeable = true;

    [DataField]
    public TimeSpan FultonDuration = TimeSpan.FromSeconds(30);

    [DataField, AutoNetworkedField]
    public EntityWhitelist? Whitelist = new()
    {
        Components = new[]
        {
            "Item",
            "Anchorable"
        }
    };

    [DataField, AutoNetworkedField]
    public SoundSpecifier? FultonSound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_deploy.ogg");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? LaunchSound = new SoundPathSpecifier("/Audio/Items/Mining/fultext_launch.ogg");

    [DataField, AutoNetworkedField]
    public bool RequiresSensorTower = true;
}
