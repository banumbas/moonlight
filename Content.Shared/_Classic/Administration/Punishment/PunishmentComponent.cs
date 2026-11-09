using Content.Shared.Chat;
using Robust.Shared.GameStates;

namespace Content.Shared._Classic.Administration.Punishment;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class PunishmentComponent : Component
{
    [DataField, AutoNetworkedField]
    public ChatChannel MutedChannels = ChatChannel.None;

    [DataField, AutoNetworkedField]
    public bool PaperMuted;

    [DataField, AutoNetworkedField]
    public bool ForcedPacifism;

    [DataField, AutoNetworkedField]
    public string? Reason;
}
