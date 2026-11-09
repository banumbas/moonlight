using Content.Shared._Classic.Vendors;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Classic.Vendors;

public sealed partial class ClassicAutomatedVendorSystem : SharedClassicAutomatedVendorSystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    private EntityQuery<ClassicVendorUserComponent> _userQuery;

    public override void Initialize()
    {
        base.Initialize();

        _userQuery = GetEntityQuery<ClassicVendorUserComponent>();

        SubscribeLocalEvent<ClassicAutomatedVendorComponent, BoundUIOpenedEvent>(OnUIOpened);
    }

    private void OnUIOpened(Entity<ClassicAutomatedVendorComponent> vendor, ref BoundUIOpenedEvent args)
    {
        UpdateUIState(vendor.Owner, args.Actor);
    }

    protected override void OnVendMessage(Entity<ClassicAutomatedVendorComponent> vendor, ref ClassicVendorVendBuiMsg args)
    {
        var user = args.Actor;

        if (!_userQuery.TryComp(user, out var userComp))
            return;

        if (args.Section < 0 || args.Section >= vendor.Comp.Sections.Count)
            return;

        var section = vendor.Comp.Sections[args.Section];

        if (args.Entry < 0 || args.Entry >= section.Entries.Count)
            return;

        var entry = section.Entries[args.Entry];

        var userPoints = vendor.Comp.PointsType == null
            ? userComp.Points
            : userComp.ExtraPoints?.GetValueOrDefault(vendor.Comp.PointsType) ?? 0;

        if (entry.Points != null && entry.Points.Value > 0 && userPoints < entry.Points.Value)
            return;

        if (entry.Stock != null && entry.Stock.Value <= 0)
            return;

        if (section.TakeOne != null && userComp.TakeOne.Contains(section.TakeOne))
            return;

        if (section.TakeAll != null && userComp.TakeAll.Contains((section.TakeAll, entry.Id.Id)))
            return;

        base.OnVendMessage(vendor, ref args);

        var spawnCoords = _transform.GetMoverCoordinates(vendor.Owner);

        for (var i = 0; i < entry.Spawn; i++)
        {
            var spawned = Spawn(entry.Id, spawnCoords);

            if (entry.AutoEquip)
            {
                if (!TryAutoEquip(user, spawned))
                    _hands.TryPickupAnyHand(user, spawned);

                foreach (var contentId in entry.AutoEquipContents)
                {
                    var extra = Spawn(contentId, spawnCoords);
                    if (!TryAutoEquip(user, extra))
                        _hands.TryPickupAnyHand(user, extra);
                }
            }
            else
            {
                _hands.TryPickupAnyHand(user, spawned);
            }
        }

        if (vendor.Comp.Sound != null)
            _audio.PlayPvs(vendor.Comp.Sound, vendor.Owner);

        UpdateUIState(vendor.Owner, user);
    }

    /// <summary>
    /// Tries to equip the given entity to any free inventory slot of the user.
    /// Returns true if successfully equipped, false if no slot was found.
    /// </summary>
    private bool TryAutoEquip(EntityUid user, EntityUid item)
    {
        if (!_inventory.TryGetSlots(user, out var slots))
            return false;

        // If the item can be equipped into the 'id' slot (e.g. PDA), prioritize the 'id' slot
        if (_inventory.TryGetSlot(user, "id", out _) && _inventory.CanEquip(user, item, "id", out _))
        {
            if (_inventory.TryGetSlotEntity(user, "id", out var existingId))
            {
                if (_inventory.TryUnequip(user, "id", out var unequipped, silent: true, force: true))
                {
                    if (!TryAutoEquip(user, unequipped.Value))
                        _hands.TryPickupAnyHand(user, unequipped.Value);
                }
            }

            if (_inventory.TryEquip(user, item, "id", silent: true, force: false))
                return true;
        }

        foreach (var slot in slots)
        {
            if (_inventory.TryGetSlotEntity(user, slot.Name, out _))
                continue;

            if (_inventory.TryEquip(user, item, slot.Name, silent: true, force: false))
                return true;
        }

        return false;
    }

    private void UpdateUIState(EntityUid vendor, EntityUid user)
    {
        if (!_userQuery.TryComp(user, out var userComp))
            return;

        var state = new ClassicAutomatedVendorBuiState(
            userComp.Points,
            userComp.ExtraPoints,
            userComp.Choices,
            userComp.TakeAll,
            userComp.TakeOne
        );

        _ui.SetUiState(vendor, ClassicAutomatedVendorUI.Key, state);
    }
}
