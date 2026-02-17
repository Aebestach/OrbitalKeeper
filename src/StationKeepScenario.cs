using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// ScenarioModule that persists all vessel orbital station-keeping data
    /// across save/load cycles. Also drives the automatic station-keeping loop
    /// for UNLOADED (on-rails) vessels via FixedUpdate.
    ///
    /// Loaded vessels are NOT automatically corrected — station-keeping only
    /// applies while the vessel is in the background (unloaded / on-rails),
    /// which is the natural state affected by orbital decay.
    /// Manual corrections for loaded vessels are available through the UI.
    /// </summary>
    [KSPScenario(
        ScenarioCreationOptions.AddToAllGames | ScenarioCreationOptions.AddToExistingGames,
        GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class StationKeepScenario : ScenarioModule
    {
        public static StationKeepScenario Instance { get; private set; }

        /// <summary>
        /// Dictionary of vessel station-keeping data, keyed by vessel ID.
        /// </summary>
        private Dictionary<Guid, VesselKeepData> vesselData = new Dictionary<Guid, VesselKeepData>();

        public override void OnAwake()
        {
            base.OnAwake();

            Instance = this;

            // Load global settings and localization on first scenario creation
            OrbitalKeepSettings.LoadSettings();
            Loc.Load();

            // Subscribe to vessel events for cleanup
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.onVesselTerminated.Add(OnVesselTerminated);

            Debug.Log("[OrbitalKeeper] StationKeepScenario initialized.");
        }

        private void OnDestroy()
        {
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onVesselTerminated.Remove(OnVesselTerminated);

            if (Instance == this)
                Instance = null;
        }

        // ======================================================================
        //  AUTOMATIC STATION-KEEPING LOOP (unloaded vessels only)
        // ======================================================================

        /// <summary>
        /// Drives the automatic station-keeping loop for UNLOADED vessels.
        /// ScenarioModule.FixedUpdate runs every physics frame regardless of which
        /// vessels are loaded, so it can service background (on-rails) vessels that
        /// VesselModule.FixedUpdate cannot reach.
        ///
        /// Loaded vessels are skipped — orbital decay primarily affects
        /// unloaded vessels, and loaded vessels are under the player's direct control.
        /// </summary>
        private void FixedUpdate()
        {
            // Only run in flight or tracking station
            if (!HighLogic.LoadedSceneIsFlight && HighLogic.LoadedScene != GameScenes.TRACKSTATION)
                return;

            double currentTime = Planetarium.GetUniversalTime();

            // Iterate over a snapshot of keys to avoid modification during enumeration
            var vesselIds = vesselData.Keys.ToList();

            foreach (Guid vesselId in vesselIds)
            {
                if (!vesselData.TryGetValue(vesselId, out VesselKeepData data))
                    continue;

                // Skip vessels without auto-keep enabled
                if (!data.AutoKeepEnabled)
                {
                    data.Status = KeepStatus.Disabled;
                    continue;
                }

                // Check interval
                if (currentTime - data.LastCheckTime < data.CheckInterval)
                    continue;

                data.LastCheckTime = currentTime;

                // Find the vessel
                Vessel vessel = FlightGlobals.FindVessel(vesselId);
                if (vessel == null)
                    continue;

                // SKIP loaded vessels — only service unloaded (on-rails) vessels
                if (vessel.loaded)
                    continue;

                // Safety check: must be in valid orbit
                if (!VesselKeepModule.IsValidOrbitForKeeping(vessel))
                {
                    data.Status = KeepStatus.InvalidOrbit;
                    continue;
                }

                // Perform the full check-and-correct cycle
                try
                {
                    VesselKeepModule.PerformOrbitCheckForVessel(vessel, data);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OrbitalKeeper] Error checking vessel {vessel.vesselName}: {ex.Message}");
                }
            }
        }

        // ======================================================================
        //  PERSISTENCE
        // ======================================================================

        /// <summary>
        /// Load vessel data from the save file.
        /// </summary>
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            vesselData.Clear();

            ConfigNode[] vesselNodes = node.GetNodes("VESSEL_KEEP");
            if (vesselNodes == null)
                return;

            foreach (ConfigNode vNode in vesselNodes)
            {
                try
                {
                    VesselKeepData vData = VesselKeepData.Load(vNode);
                    vesselData[vData.VesselId] = vData;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OrbitalKeeper] Failed to load vessel data: {ex.Message}");
                }
            }

            Debug.Log($"[OrbitalKeeper] Loaded station-keeping data for {vesselData.Count} vessel(s).");
        }

        /// <summary>
        /// Save vessel data to the save file.
        /// </summary>
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);

            foreach (var kvp in vesselData)
            {
                node.AddNode(kvp.Value.Save());
            }

            Debug.Log($"[OrbitalKeeper] Saved station-keeping data for {vesselData.Count} vessel(s).");
        }

        // ======================================================================
        //  PUBLIC API
        // ======================================================================

        /// <summary>
        /// Gets the station-keeping data for a vessel. Returns null if not configured.
        /// </summary>
        public VesselKeepData GetVesselData(Guid vesselId)
        {
            vesselData.TryGetValue(vesselId, out VesselKeepData data);
            return data;
        }

        /// <summary>
        /// Gets the station-keeping data for a vessel, creating a default entry if it doesn't exist.
        /// </summary>
        public VesselKeepData GetOrCreateVesselData(Vessel vessel)
        {
            if (!vesselData.TryGetValue(vessel.id, out VesselKeepData data))
            {
                data = VesselKeepData.CreateFromCurrentOrbit(vessel);
                data.Tolerance = OrbitalKeepSettings.DefaultTolerance;
                data.CheckInterval = OrbitalKeepSettings.DefaultCheckInterval;
                data.EngineMode = OrbitalKeepSettings.DefaultEngineMode;
                vesselData[vessel.id] = data;
            }
            return data;
        }

        /// <summary>
        /// Sets/updates the station-keeping data for a vessel.
        /// </summary>
        public void SetVesselData(VesselKeepData data)
        {
            vesselData[data.VesselId] = data;
        }

        /// <summary>
        /// Removes station-keeping data for a vessel.
        /// </summary>
        public void RemoveVesselData(Guid vesselId)
        {
            vesselData.Remove(vesselId);
        }

        /// <summary>
        /// Gets all vessel data entries (for the fleet overview UI).
        /// </summary>
        public IEnumerable<VesselKeepData> GetAllVesselData()
        {
            return vesselData.Values;
        }

        /// <summary>
        /// Returns the number of tracked vessels.
        /// </summary>
        public int TrackedVesselCount => vesselData.Count;

        // ======================================================================
        //  EVENT HANDLERS
        // ======================================================================

        private void OnVesselRecovered(ProtoVessel protoVessel, bool quick)
        {
            if (protoVessel != null)
                vesselData.Remove(protoVessel.vesselID);
        }

        private void OnVesselTerminated(ProtoVessel protoVessel)
        {
            if (protoVessel != null)
                vesselData.Remove(protoVessel.vesselID);
        }
    }
}
