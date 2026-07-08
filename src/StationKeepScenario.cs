using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// ScenarioModule that persists all vessel orbital station-keeping data
    /// across save/load cycles. Also drives the automatic station-keeping loop
    /// for both loaded and unloaded vessels.
    ///
    /// The scheduler is driven by game time rather than real time so high warp
    /// cannot skip past station-keeping windows before the next real-time tick.
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
        private readonly List<Guid> vesselIdBuffer = new List<Guid>(128);
        private bool vesselIdBufferDirty = true;
        private int nextVesselIndex;

        private const int MaxVesselsScannedPerTick = 48;
        private const int MaxChecksPerTick = 8;
        private const int MaxEmergencyChecksPerTick = 4;
        private const double MinCheckIntervalSeconds = 60.0;
        private const double EmergencyLeadTimeCapSeconds = 3600.0;

        public override void OnAwake()
        {
            base.OnAwake();

            Instance = this;

            // Load global settings and localization on first scenario creation
            OrbitalKeepSettings.SyncFromParameters();
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
        //  AUTOMATIC STATION-KEEPING LOOP
        // ======================================================================

        /// <summary>
        /// Drives the automatic station-keeping loop for tracked vessels.
        /// ScenarioModule.FixedUpdate runs every physics frame regardless of which
        /// vessels are loaded, so it can service background vessels and the active
        /// vessel while time warp advances game time quickly.
        /// </summary>
        private void FixedUpdate()
        {
            // Only run in flight or tracking station
            if (!HighLogic.LoadedSceneIsFlight && HighLogic.LoadedScene != GameScenes.TRACKSTATION)
                return;

            RunScheduledChecks();
        }

        private void RunScheduledChecks()
        {
            if (vesselData.Count == 0)
                return;

            if (vesselIdBufferDirty || vesselIdBuffer.Count != vesselData.Count)
                RebuildVesselIdBuffer();

            if (vesselIdBuffer.Count == 0)
                return;

            int totalTracked = vesselIdBuffer.Count;
            double currentTime = Planetarium.GetUniversalTime();

            int checks = RunEmergencyChecks(currentTime, totalTracked);
            int scanned = 0;

            while (scanned < MaxVesselsScannedPerTick && checks < MaxChecksPerTick && totalTracked > 0)
            {
                if (nextVesselIndex >= totalTracked)
                    nextVesselIndex = 0;

                Guid vesselId = vesselIdBuffer[nextVesselIndex];
                nextVesselIndex++;
                scanned++;

                if (!vesselData.TryGetValue(vesselId, out VesselKeepData data))
                    continue;

                if (!data.AutoKeepEnabled)
                {
                    data.Status = KeepStatus.Disabled;
                    continue;
                }

                if (!IsCheckDue(data, currentTime))
                    continue;

                Vessel vessel = FlightGlobals.FindVessel(vesselId);
                if (vessel == null)
                    continue;

                if (TryRunCheck(vessel, data, currentTime))
                    checks++;
            }
        }

        private int RunEmergencyChecks(double currentTime, int totalTracked)
        {
            int checks = 0;

            for (int i = 0; i < totalTracked && checks < MaxEmergencyChecksPerTick; i++)
            {
                Guid vesselId = vesselIdBuffer[i];
                if (!vesselData.TryGetValue(vesselId, out VesselKeepData data))
                    continue;

                if (!data.AutoKeepEnabled)
                {
                    data.Status = KeepStatus.Disabled;
                    continue;
                }

                Vessel vessel = FlightGlobals.FindVessel(vesselId);
                if (!IsEmergencyCheckNeeded(vessel, data, currentTime))
                    continue;

                if (TryRunCheck(vessel, data, currentTime))
                    checks++;
            }

            return checks;
        }

        private static bool IsCheckDue(VesselKeepData data, double currentTime)
        {
            double interval = Math.Max(MinCheckIntervalSeconds, data.CheckInterval);
            if (currentTime < data.LastCheckTime)
                return true;
            return currentTime - data.LastCheckTime >= interval;
        }

        private static bool TryRunCheck(Vessel vessel, VesselKeepData data, double currentTime)
        {
            if (vessel == null)
                return false;

            if (!VesselKeepModule.IsValidOrbitForKeeping(vessel))
            {
                data.Status = KeepStatus.InvalidOrbit;
                data.LastCheckTime = currentTime;
                return true;
            }

            try
            {
                VesselKeepModule.PerformOrbitCheckForVessel(vessel, data);
                data.LastCheckTime = currentTime;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OrbitalKeeper] Error checking vessel {vessel.vesselName}: {ex.Message}");
                return false;
            }
        }

        private static bool IsEmergencyCheckNeeded(Vessel vessel, VesselKeepData data, double currentTime)
        {
            if (vessel == null || vessel.orbit == null || data == null)
                return false;
            if (!VesselKeepModule.IsValidOrbitForKeeping(vessel))
                return false;

            CelestialBody body = vessel.orbit.referenceBody;
            if (body == null || !body.atmosphere)
                return false;

            double emergencyAltitude = body.atmosphereDepth + OrbitalKeepSettings.MinSafeAltitudeMargin;
            if (vessel.orbit.PeA <= emergencyAltitude)
                return DeltaVCalculator.CalculateCorrection(vessel, data).NeedsCorrection;

            if (!StationKeepEstimator.TryEstimateCurrentDecayRate(
                vessel,
                out double decayRate,
                out _))
            {
                return false;
            }

            if (decayRate <= 1e-12)
                return false;

            double secondsToEmergency = (vessel.orbit.PeA - emergencyAltitude) / decayRate;
            double leadTime = Math.Max(
                MinCheckIntervalSeconds,
                Math.Min(Math.Max(MinCheckIntervalSeconds, data.CheckInterval), EmergencyLeadTimeCapSeconds));

            if (currentTime < data.LastCheckTime)
                return secondsToEmergency <= leadTime;

            double secondsUntilScheduledCheck = Math.Max(
                0.0,
                Math.Max(MinCheckIntervalSeconds, data.CheckInterval) - (currentTime - data.LastCheckTime));
            double waitWindow = Math.Max(leadTime, secondsUntilScheduledCheck);

            return secondsToEmergency <= waitWindow &&
                   DeltaVCalculator.CalculateCorrection(vessel, data).NeedsCorrection;
        }

        private void RebuildVesselIdBuffer()
        {
            vesselIdBuffer.Clear();
            foreach (Guid vesselId in vesselData.Keys)
            {
                vesselIdBuffer.Add(vesselId);
            }
            if (nextVesselIndex >= vesselIdBuffer.Count)
                nextVesselIndex = 0;
            vesselIdBufferDirty = false;
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
            vesselIdBufferDirty = true;
            nextVesselIndex = 0;

            ConfigNode[] vesselNodes = node.GetNodes("VESSEL_KEEP");
            if (vesselNodes == null)
                return;

            foreach (ConfigNode vNode in vesselNodes)
            {
                try
                {
                    bool hasPersistedLastCheckTime = vNode.HasValue("lastCheckTime");
                    VesselKeepData vData = VesselKeepData.Load(vNode);
                    if (!hasPersistedLastCheckTime)
                    {
                        // Backward compatibility for old saves: spread first checks over time.
                        vData.LastCheckTime = BuildStaggeredLastCheckTime(vData.VesselId, vData.CheckInterval);
                    }
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
                data.LastCheckTime = BuildStaggeredLastCheckTime(vessel.id, data.CheckInterval);
                vesselData[vessel.id] = data;
                vesselIdBufferDirty = true;
            }
            return data;
        }

        /// <summary>
        /// Sets/updates the station-keeping data for a vessel.
        /// </summary>
        public void SetVesselData(VesselKeepData data)
        {
            vesselData[data.VesselId] = data;
            vesselIdBufferDirty = true;
        }

        /// <summary>
        /// Removes station-keeping data for a vessel.
        /// </summary>
        public void RemoveVesselData(Guid vesselId)
        {
            if (vesselData.Remove(vesselId))
                vesselIdBufferDirty = true;
        }

        /// <summary>
        /// Gets all vessel data entries (for the fleet overview UI).
        /// </summary>
        public IEnumerable<VesselKeepData> GetAllVesselData()
        {
            return vesselData.Values;
        }

        // ======================================================================
        //  EVENT HANDLERS
        // ======================================================================

        private void OnVesselRecovered(ProtoVessel protoVessel, bool quick)
        {
            if (protoVessel != null)
            {
                if (vesselData.Remove(protoVessel.vesselID))
                    vesselIdBufferDirty = true;
            }
        }

        private void OnVesselTerminated(ProtoVessel protoVessel)
        {
            if (protoVessel != null)
            {
                if (vesselData.Remove(protoVessel.vesselID))
                    vesselIdBufferDirty = true;
            }
        }

        private static double BuildStaggeredLastCheckTime(Guid vesselId, double checkInterval)
        {
            double interval = Math.Max(60.0, checkInterval);
            double currentUt = Planetarium.fetch != null ? Planetarium.GetUniversalTime() : 0.0;

            // Spread next due time over roughly 5%-100% of the interval.
            int hash = vesselId.GetHashCode() & 0x7FFFFFFF;
            double fraction = ((hash % 1000) / 1000.0) * 0.95;
            return currentUt - interval * fraction;
        }
    }
}
