using System.Linq;
using Content.Shared._Moonlight.Vendors;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static System.StringComparison;
using static Robust.Client.UserInterface.Controls.LineEdit;

namespace Content.Client._Moonlight.Vendors;

public sealed partial class MoonlightAutomatedVendorBui : BoundUserInterface
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IResourceCache _resource = default!;

    private MoonlightAutomatedVendorWindow? _window;
    private MoonlightAutomatedVendorBuiState? _lastState;

    public MoonlightAutomatedVendorBui(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<MoonlightAutomatedVendorWindow>();
        _window.Title = EntMan.GetComponentOrNull<MetaDataComponent>(Owner)?.EntityName ?? "Vendor";
        _window.ReagentsContainer.Visible = false;

        if (EntMan.TryGetComponent(Owner, out MoonlightAutomatedVendorComponent? vendor))
            RebuildSections(vendor);

        _window.Search.OnTextChanged += OnSearchChanged;
        Refresh();
    }

    private void RebuildSections(MoonlightAutomatedVendorComponent vendor)
    {
        if (_window == null)
            return;

        _window.Sections.DisposeAllChildren();

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];
            var uiSection = new MoonlightAutomatedVendorSection { Section = section };
            uiSection.Label.SetMessage(GetSectionName(section));

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];
                var uiEntry = new MoonlightAutomatedVendorEntry();

                if (_prototype.TryIndex(entry.Id, out var entity))
                {
                    if (entry.Icon != null)
                    {
                        var spriteSys = EntMan.System<SpriteSystem>();
                        uiEntry.Texture.Textures = new List<Texture> { spriteSys.Frame0(entry.Icon) };
                    }
                    else
                    {
                        var displayId = entry.DisplayEntity ?? entry.Id;
                        if (_prototype.TryIndex(displayId, out var iconEntity))
                        {
                            uiEntry.Texture.Textures = SpriteComponent.GetPrototypeTextures(iconEntity, _resource)
                                .Select(o => o.Default)
                                .ToList();
                            if (iconEntity.TryGetComponent<SpriteComponent>("Sprite", out var entitySprites) && entitySprites.AllLayers.Any())
                                uiEntry.Texture.Modulate = entitySprites.AllLayers.First().Color;
                        }
                    }

                    var entryName = entry.Name != null ? Loc.GetString(entry.Name) : null;
                    uiEntry.Panel.Button.Label.Text = entryName?.Replace("\\n", "\n") ?? entity.Name;

                    var name = entryName ?? entity.Name;
                    var color = MoonlightAutomatedVendorPanel.DefaultColor;
                    var borderColor = MoonlightAutomatedVendorPanel.DefaultBorderColor;
                    var hoverColor = MoonlightAutomatedVendorPanel.DefaultBorderColor;

                    if (section.TakeAll != null || section.TakeOne != null)
                    {
                        name = $"Mandatory: {name}";
                        color = Color.FromHex("#251A0C");
                        borderColor = Color.FromHex("#805300");
                        hoverColor = Color.FromHex("#805300");
                    }
                    else if (entry.Recommended)
                    {
                        uiEntry.Panel.Button.Label.Text = $"★ {uiEntry.Panel.Button.Label.Text}";
                        name = $"Recommended: {name}";
                        color = Color.FromHex("#102919");
                        borderColor = Color.FromHex("#3A9B52");
                        hoverColor = Color.FromHex("#3A9B52");
                    }

                    uiEntry.Panel.Color = color;
                    uiEntry.Panel.BorderColor = borderColor;
                    uiEntry.Panel.HoveredColor = hoverColor;

                    var entryDescription = entry.Description != null ? Loc.GetString(entry.Description) : entity.Description;

                    var msg = new FormattedMessage();
                    msg.AddText(name);
                    msg.PushNewline();

                    if (!string.IsNullOrWhiteSpace(entryDescription))
                        msg.AddText(entryDescription);

                    var tooltip = new Tooltip();
                    tooltip.SetMessage(msg);

                    uiEntry.TooltipLabel.ToolTip = entryDescription;
                    uiEntry.TooltipLabel.TooltipDelay = 0;
                    uiEntry.TooltipLabel.TooltipSupplier = _ => tooltip;

                    var sectionI = sectionIndex;
                    var entryI = entryIndex;
                    uiEntry.Panel.Button.OnPressed += _ => OnButtonPressed(sectionI, entryI);
                }

                uiSection.Entries.AddChild(uiEntry);
            }

            _window.Sections.AddChild(uiSection);
        }
    }

    private void OnButtonPressed(int sectionIndex, int entryIndex)
    {
        var msg = new MoonlightVendorVendBuiMsg(sectionIndex, entryIndex);
        SendPredictedMessage(msg);
        Refresh();
    }

    private void OnSearchChanged(LineEditEventArgs args)
    {
        ApplySearchFilter(args.Text);
    }

    private void ApplySearchFilter(string? text)
    {
        if (_window == null)
            return;

        foreach (var sectionControl in _window.Sections.Children)
        {
            if (sectionControl is not MoonlightAutomatedVendorSection section)
                continue;

            var any = false;
            foreach (var entriesControl in section.Entries.Children)
            {
                if (entriesControl is not MoonlightAutomatedVendorEntry entry)
                    continue;

                if (string.IsNullOrWhiteSpace(text))
                    entry.Visible = true;
                else
                    entry.Visible = entry.Panel.Button.Label.Text?.Contains(text, OrdinalIgnoreCase) ?? false;

                if (entry.Visible)
                    any = true;
            }

            section.Visible = any;
        }
    }

    public void Refresh()
    {
        if (_window == null)
            return;

        if (!EntMan.TryGetComponent(Owner, out MoonlightAutomatedVendorComponent? vendor))
            return;

        var user = _player.LocalEntity;
        MoonlightVendorUserComponent? userComp = user != null ? EntMan.GetComponentOrNull<MoonlightVendorUserComponent>(user.Value) : null;

        if (_lastState == null && userComp == null)
            return;

        var points = _lastState?.Points ?? userComp?.Points ?? 0;
        var extraPoints = _lastState?.ExtraPoints ?? userComp?.ExtraPoints;
        var choices = _lastState?.Choices ?? userComp?.Choices ?? new();
        var takeAll = _lastState?.TakeAll ?? userComp?.TakeAll ?? new();
        var takeOne = _lastState?.TakeOne ?? userComp?.TakeOne ?? new();

        var anyEntryWithPoints = false;
        var userPoints = vendor.PointsType == null
            ? points
            : extraPoints?.GetValueOrDefault(vendor.PointsType) ?? 0;

        for (var sectionIndex = 0; sectionIndex < vendor.Sections.Count; sectionIndex++)
        {
            var section = vendor.Sections[sectionIndex];

            if (sectionIndex >= _window.Sections.ChildCount)
                continue;

            var uiSection = (MoonlightAutomatedVendorSection)_window.Sections.GetChild(sectionIndex);

            var sectionDisabled = false;
            if (section.Choices is { } choicesDef)
            {
                if (choices.GetValueOrDefault(choicesDef.Id) >= choicesDef.Amount)
                {
                    sectionDisabled = true;
                }
            }

            var anyAmount = false;
            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                var entry = section.Entries[entryIndex];

                if (entryIndex >= uiSection.Entries.ChildCount)
                    continue;

                var uiEntry = (MoonlightAutomatedVendorEntry)uiSection.Entries.GetChild(entryIndex);
                var disabled = sectionDisabled || (entry.Amount.HasValue && entry.Amount <= 0);

                if (section.TakeAll is { } takeAllId)
                {
                    if (takeAll.Contains((takeAllId, entry.Id.Id)))
                        disabled = true;
                }
                if (section.TakeOne is { } takeOneId)
                {
                    if (takeOne.Contains(takeOneId))
                        disabled = true;
                }
                if (entry.Stock is { } stock)
                {
                    if (stock <= 0)
                        disabled = true;
                }

                if (entry.Points != null && entry.Points.Value > 0)
                {
                    anyEntryWithPoints = true;
                    uiEntry.Amount.Text = $"{entry.Points}P";

                    if (userPoints < entry.Points.Value)
                        disabled = true;
                }
                else
                {
                    if (entry.Stock != null)
                        uiEntry.Amount.Text = $"{entry.Stock.Value}";
                    else if (entry.Amount != null)
                        uiEntry.Amount.Text = $"{entry.Amount.Value}";
                    else
                        uiEntry.Amount.Text = string.Empty;
                }

                uiEntry.Amount.Modulate = disabled ? Color.Red : Color.White;
                uiEntry.Panel.Button.Disabled = disabled;

                if (!string.IsNullOrWhiteSpace(uiEntry.Amount.Text))
                    anyAmount = true;
            }

            for (var entryIndex = 0; entryIndex < section.Entries.Count; entryIndex++)
            {
                if (entryIndex >= uiSection.Entries.ChildCount)
                    continue;
                var uiEntry = (MoonlightAutomatedVendorEntry)uiSection.Entries.GetChild(entryIndex);
                uiEntry.Amount.Visible = anyAmount;
            }
        }

        ApplySearchFilter(_window.Search.Text);
        _window.PointsLabel.Text = anyEntryWithPoints ? $"Points Remaining: {userPoints}" : string.Empty;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        if (message is MoonlightVendorRefreshBuiMsg)
        {
            Refresh();
        }
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is MoonlightAutomatedVendorBuiState vendorState)
        {
            _lastState = vendorState;
            Refresh();
        }
    }

    private FormattedMessage GetSectionName(MoonlightVendorSection section)
    {
        var user = _player.LocalEntity;
        MoonlightVendorUserComponent? userComp = user != null ? EntMan.GetComponentOrNull<MoonlightVendorUserComponent>(user.Value) : null;
        var choices = _lastState?.Choices ?? userComp?.Choices;

        var name = new FormattedMessage();
        name.PushTag(new MarkupNode("bold", new MarkupParameter(section.Name.ToUpperInvariant()), null));
        name.AddText(section.Name.ToUpperInvariant());

        if (section.TakeAll != null)
            name.AddText(" (TAKE ALL)");
        else if (section.TakeOne != null)
            name.AddText(" (TAKE ONE)");
        else if (section.Choices is { } choicesDef)
        {
            var left = choicesDef.Amount - (choices?.GetValueOrDefault(choicesDef.Id) ?? 0);
            if (left > 0)
                name.AddText($" (CHOOSE {left})");
        }

        name.Pop();
        return name;
    }
}
