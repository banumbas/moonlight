using Content.Shared._Classic.PMC;
using Content.Shared.Overlays;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client.Overlays;

public sealed partial class ShowPMCIconsSystem : EquipmentHudSystem<ShowPMCIconsComponent>
{
    [Dependency] private readonly IPrototypeManager _proto = default!;

    private static readonly ProtoId<FactionIconPrototype> FactionIconPMCAssault = "FactionIconPMCAssault";
    private static readonly ProtoId<FactionIconPrototype> FactionIconPMCMedic = "FactionIconPMCMedic";
    private static readonly ProtoId<FactionIconPrototype> FactionIconPMCSpecialist = "FactionIconPMCSpecialist";
    private static readonly ProtoId<FactionIconPrototype> FactionIconPMCCommander = "FactionIconPMCCommander";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PMCAssaultComponent, GetStatusIconsEvent>(OnGetPMCAssaultStatusIconsEvent);
        SubscribeLocalEvent<PMCMedicComponent, GetStatusIconsEvent>(OnGetPMCMedicStatusIconsEvent);
        SubscribeLocalEvent<PMCSpecialistComponent, GetStatusIconsEvent>(OnGetPMCSpecialistStatusIconsEvent);
        SubscribeLocalEvent<PMCCommanderComponent, GetStatusIconsEvent>(OnGetPMCCommanderStatusIconsEvent);
    }

    private void OnGetPMCAssaultStatusIconsEvent(Entity<PMCAssaultComponent> ent, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_proto.TryIndex(FactionIconPMCAssault, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }

    private void OnGetPMCMedicStatusIconsEvent(Entity<PMCMedicComponent> ent, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_proto.TryIndex(FactionIconPMCMedic, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }

    private void OnGetPMCSpecialistStatusIconsEvent(Entity<PMCSpecialistComponent> ent, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_proto.TryIndex(FactionIconPMCSpecialist, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }

    private void OnGetPMCCommanderStatusIconsEvent(Entity<PMCCommanderComponent> ent, ref GetStatusIconsEvent ev)
    {
        if (!IsActive)
            return;

        if (_proto.TryIndex(FactionIconPMCCommander, out var iconPrototype))
            ev.StatusIcons.Add(iconPrototype);
    }
}
