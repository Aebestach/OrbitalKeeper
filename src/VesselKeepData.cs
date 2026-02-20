using System;

namespace OrbitalKeeper
{
    /// <summary>
    /// Defines the engine selection strategy for station-keeping burns.
    /// </summary>
    public enum EngineSelectionMode
    {
        /// <summary>Only engines that are currently ignited (EngineIgnited = True).</summary>
        IgnitedOnly,
        /// <summary>Engines that are staged/active and not manually shut down.</summary>
        ActiveNotShutdown
    }

    /// <summary>
    /// Defines the current status of orbital station-keeping for a vessel.
    /// </summary>
    public enum KeepStatus
    {
        /// <summary>Station-keeping is disabled for this vessel.</summary>
        Disabled,
        /// <summary>Orbit is within tolerance, no correction needed.</summary>
        Nominal,
        /// <summary>Orbit has drifted outside tolerance, correction pending.</summary>
        Drifting,
        /// <summary>Correction is being performed.</summary>
        Correcting,
        /// <summary>Insufficient resources (EC or propellant) to perform correction.</summary>
        InsufficientResources,
        /// <summary>No suitable engine found on the vessel.</summary>
        NoEngine,
        /// <summary>Vessel is not in a valid orbit (landed, suborbital, escaping, etc.).</summary>
        InvalidOrbit
    }

    /// <summary>
    /// Data class holding all orbital station-keeping parameters and state for a single vessel.
    /// Persisted via StationKeepScenario.
    /// </summary>
    public class VesselKeepData
    {
        /// <summary>The vessel's persistent ID.</summary>
        public Guid VesselId { get; set; }

        // --- Target orbital parameters ---

        /// <summary>Target apoapsis in meters (above sea level).</summary>
        public double TargetApoapsis { get; set; }

        /// <summary>Target periapsis in meters (above sea level).</summary>
        public double TargetPeriapsis { get; set; }

        /// <summary>Target orbital inclination in degrees.</summary>
        public double TargetInclination { get; set; }

        // --- Configuration ---

        /// <summary>Tolerance percentage (0-100). Orbit is considered drifted when
        /// any parameter deviates more than this percentage from target.</summary>
        public double Tolerance { get; set; } = 5.0;

        /// <summary>Whether automatic station-keeping is enabled.</summary>
        public bool AutoKeepEnabled { get; set; }

        /// <summary>How often to check orbit parameters, in game seconds.</summary>
        public double CheckInterval { get; set; } = 3600.0;

        /// <summary>Which engines are eligible for station-keeping burns.</summary>
        public EngineSelectionMode EngineMode { get; set; } = EngineSelectionMode.IgnitedOnly;

        // --- Runtime state (not persisted unless noted) ---

        /// <summary>Current station-keeping status.</summary>
        public KeepStatus Status { get; set; } = KeepStatus.Disabled;

        /// <summary>Game time of the last orbit check.</summary>
        public double LastCheckTime { get; set; }

        /// <summary>Cumulative delta-v spent on station-keeping (m/s). Persisted.</summary>
        public double TotalDeltaVSpent { get; set; }

        /// <summary>Cumulative EC spent on station-keeping. Persisted.</summary>
        public double TotalECSpent { get; set; }

        /// <summary>
        /// Creates a default VesselKeepData for a given vessel ID.
        /// </summary>
        public VesselKeepData(Guid vesselId)
        {
            VesselId = vesselId;
        }

        /// <summary>
        /// Creates a VesselKeepData initialized from the vessel's current orbit.
        /// </summary>
        public static VesselKeepData CreateFromCurrentOrbit(Vessel vessel)
        {
            var data = new VesselKeepData(vessel.id)
            {
                TargetApoapsis = vessel.orbit.ApA,
                TargetPeriapsis = vessel.orbit.PeA,
                TargetInclination = vessel.orbit.inclination,
                AutoKeepEnabled = false
            };
            return data;
        }

        /// <summary>
        /// Saves this data into a ConfigNode for persistence.
        /// </summary>
        public ConfigNode Save()
        {
            var node = new ConfigNode("VESSEL_KEEP");
            node.AddValue("vesselId", VesselId.ToString());
            node.AddValue("targetAp", TargetApoapsis);
            node.AddValue("targetPe", TargetPeriapsis);
            node.AddValue("targetInc", TargetInclination);
            node.AddValue("tolerance", Tolerance);
            node.AddValue("autoEnabled", AutoKeepEnabled);
            node.AddValue("checkInterval", CheckInterval);
            node.AddValue("engineMode", EngineMode.ToString());
            node.AddValue("lastCheckTime", LastCheckTime);
            node.AddValue("totalDvSpent", TotalDeltaVSpent);
            node.AddValue("totalECSpent", TotalECSpent);
            return node;
        }

        /// <summary>
        /// Loads data from a ConfigNode.
        /// </summary>
        public static VesselKeepData Load(ConfigNode node)
        {
            Guid id = new Guid(node.GetValue("vesselId"));
            var data = new VesselKeepData(id);

            data.TargetApoapsis = ParseDouble(node, "targetAp", 0);
            data.TargetPeriapsis = ParseDouble(node, "targetPe", 0);
            data.TargetInclination = ParseDouble(node, "targetInc", 0);
            data.Tolerance = ParseDouble(node, "tolerance", 5.0);
            data.CheckInterval = ParseDouble(node, "checkInterval", 3600.0);
            data.LastCheckTime = ParseDouble(node, "lastCheckTime", 0.0);
            data.TotalDeltaVSpent = ParseDouble(node, "totalDvSpent", 0);
            data.TotalECSpent = ParseDouble(node, "totalECSpent", 0);

            bool.TryParse(node.GetValue("autoEnabled") ?? "False", out bool autoEn);
            data.AutoKeepEnabled = autoEn;

            if (node.HasValue("engineMode"))
            {
                if (Enum.TryParse(node.GetValue("engineMode"), out EngineSelectionMode mode))
                    data.EngineMode = mode;
            }

            return data;
        }

        private static double ParseDouble(ConfigNode node, string key, double defaultValue)
        {
            string val = node.GetValue(key);
            if (val != null && double.TryParse(val, out double result))
                return result;
            return defaultValue;
        }
    }
}
