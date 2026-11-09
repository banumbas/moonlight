using Content.Shared.Inventory;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Classic.Clothing.Components;

/// <summary>
/// Prevents the wearer from catching fire (accumulating fire stacks, igniting) when equipped in a valid slot.
/// Also immediately extinguishes the wearer when equipped.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FireproofClothingComponent : Component, IClothingSlots
{
    [DataField]
    public SlotFlags Slots { get; set; } = SlotFlags.OUTERCLOTHING;

    [DataField]
    public LocId ExamineMessage = "fireproof-clothing-examine";
}
