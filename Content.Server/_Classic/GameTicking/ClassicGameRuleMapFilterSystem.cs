using Content.Server._Starlight.StationEvents.Components;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared._Classic.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Server._Classic.GameTicking;

/// <summary>
/// Prevents game rules from loading auxiliary maps while Classic map loading is disabled.
/// </summary>
public sealed partial class ClassicGameRuleMapFilterSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    private readonly Dictionary<EntProtoId, bool> _loadsSeparateMapCache = new();
    private bool _disableShuttleEvents;

    public override void Initialize()
    {
        base.Initialize();

        Subs.CVar(_configuration, ClassicCCVars.DisableShuttleEvents, OnCVarChanged, true);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
    }

    /// <summary>
    /// Returns whether a game rule may be added with the current Classic configuration.
    /// </summary>
    public bool CanAddGameRule(EntProtoId ruleId)
    {
        if (!_disableShuttleEvents)
            return true;

        if (!_loadsSeparateMapCache.TryGetValue(ruleId, out var loadsSeparateMap))
        {
            loadsSeparateMap = LoadsSeparateMap(ruleId);
            _loadsSeparateMapCache.Add(ruleId, loadsSeparateMap);
        }

        return !loadsSeparateMap;
    }

    private bool LoadsSeparateMap(EntProtoId ruleId)
    {
        if (!_prototype.TryIndex<EntityPrototype>(ruleId, out var rule))
            return false;

        // DynamicRule and SubRule containers stay available. Their tables are flattened by their
        // respective systems, and every selected child passes through AddGameRule and this filter.
        return rule.TryComp<LoadMapRuleComponent>(out _, EntityManager.ComponentFactory) ||
               rule.TryComp<WreckSwarmComponent>(out _, EntityManager.ComponentFactory);
    }

    private void OnCVarChanged(bool value)
    {
        _disableShuttleEvents = value;
        _loadsSeparateMapCache.Clear();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        _loadsSeparateMapCache.Clear();
    }
}
