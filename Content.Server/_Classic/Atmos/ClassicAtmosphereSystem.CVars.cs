using Content.Server.Atmos.Components;
using Content.Shared._Classic.CCVar;
using Content.Shared.Atmos;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private static readonly GasMixture DisabledAtmosphere = CreateDisabledAtmosphere();

    public bool AtmosEnabled { get; private set; } = true;
    private bool AtmosDisabled => !AtmosEnabled;

    private void InitializeClassicCVars()
    {
        Subs.CVar(_cfg, ClassicCCVars.AtmosEnabled, SetAtmosEnabled, true);
    }

    private void SetAtmosEnabled(bool value)
    {
        if (AtmosEnabled == value)
            return;

        AtmosEnabled = value;

        if (value)
        {
            var query = EntityQueryEnumerator<GridAtmosphereComponent, MapGridComponent>();
            while (query.MoveNext(out var uid, out var atmosphere, out var grid))
            {
                InvalidateAllTiles((uid, grid, atmosphere));
            }

            return;
        }

        _currentRunAtmosphereIndex = 0;
        _currentRunAtmosphere.Clear();
        _simulationPaused = false;

        var clearQuery = EntityQueryEnumerator<GridAtmosphereComponent>();
        while (clearQuery.MoveNext(out _, out var atmosphere))
        {
            ClearGridAtmosphere(atmosphere);
        }
    }

    private static GasMixture CreateDisabledAtmosphere()
    {
        var mixture = new GasMixture(Atmospherics.CellVolume)
        {
            Temperature = Atmospherics.T20C,
        };

        mixture.AdjustMoles(Gas.Oxygen, Atmospherics.OxygenMolesStandard);
        mixture.AdjustMoles(Gas.Nitrogen, Atmospherics.NitrogenMolesStandard);
        mixture.MarkImmutable();
        return mixture;
    }

    private static GasMixture?[] GetDisabledTileMixtures(int count)
    {
        var mixtures = new GasMixture?[count];
        Array.Fill(mixtures, DisabledAtmosphere);
        return mixtures;
    }

    private static void ClearGridAtmosphere(GridAtmosphereComponent atmosphere)
    {
        atmosphere.ProcessingPaused = false;
        atmosphere.Timer = 0f;
        atmosphere.State = AtmosphereProcessingState.Revalidate;

        atmosphere.Tiles.Clear();
        atmosphere.MapTiles.Clear();
        atmosphere.ActiveTiles.Clear();
        atmosphere.ExcitedGroups.Clear();
        atmosphere.HotspotTiles.Clear();
        atmosphere.SuperconductivityTiles.Clear();
        atmosphere.HighPressureDelta.Clear();
        atmosphere.DeltaPressureEntities.Clear();
        atmosphere.DeltaPressureEntityLookup.Clear();
        atmosphere.DeltaPressureCursor = 0;

        while (atmosphere.DeltaPressureDamageResults.TryDequeue(out _))
        {
        }

        atmosphere.CurrentRunTiles.Clear();
        atmosphere.CurrentRunExcitedGroups.Clear();
        atmosphere.CurrentRunPipeNet.Clear();
        atmosphere.CurrentRunAtmosDevices.Clear();
        atmosphere.InvalidatedCoords.Clear();
        atmosphere.CurrentRunInvalidatedTiles.Clear();
        atmosphere.PossiblyDisconnectedTiles.Clear();
    }
}
