using System;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// VesselModule attached to every vessel. Provides:
    /// 1. The public API for manual correction (called from UI).
    /// 2. Status refresh for UI display.
    /// 3. Static helper methods used by StationKeepScenario for automatic
    ///    correction of loaded and unloaded vessels.
    ///
    /// Automatic station-keeping is driven by StationKeepScenario.FixedUpdate.
    /// This module does NOT run its own FixedUpdate loop for auto-correction.
    /// </summary>
    public class VesselKeepModule : VesselModule
    {
        /// <summary>Cached reference to this vessel's station-keeping data.</summary>
        private VesselKeepData keepData;

        /// <summary>Cached last correction result for UI display.</summary>
        public DeltaVCalculator.CorrectionResult LastCorrectionResult { get; private set; }

        /// <summary>Cached last engine info for UI display.</summary>
        public ResourceManager.EngineInfo LastEngineInfo { get; private set; }

        /// <summary>Cached last resource check for UI display.</summary>
        public ResourceManager.ResourceCheckResult LastResourceCheck { get; private set; }

        // ======================================================================
        //  STATIC CORRECTION LOGIC (used by Scenario)
        // ======================================================================

        /// <summary>
        /// Entry point called by StationKeepScenario for automatic correction.
        /// Evaluates orbit drift, checks resources, and applies correction.
        /// </summary>
        public static void PerformOrbitCheckForVessel(Vessel vessel, VesselKeepData data)
        {
            if (!IsValidOrbitForKeeping(vessel))
            {
                data.Status = KeepStatus.InvalidOrbit;
                return;
            }

            // 1. Calculate required correction
            var correction = DeltaVCalculator.CalculateCorrection(vessel, data);

            if (!correction.NeedsCorrection)
            {
                data.Status = KeepStatus.Nominal;
                return;
            }

            data.Status = KeepStatus.Drifting;

            // Safety: cap correction delta-v
            double deltaV = Math.Min(correction.TotalDeltaV, OrbitalKeepSettings.MaxCorrectionDeltaV);

            // 2. Find eligible engine
            ResourceManager.EngineInfo engineInfo = FindBestEngineForVessel(vessel, data);

            if (!engineInfo.Found)
            {
                data.Status = KeepStatus.NoEngine;
                if (OrbitalKeepSettings.ShowResourceWarnings)
                    PostMessage(Loc.Format(Loc.MsgNoEngine, vessel.vesselName));
                return;
            }

            // 3. Check resources
            var resourceCheck = ResourceManager.CheckResources(vessel, deltaV, engineInfo);

            if (!resourceCheck.Sufficient)
            {
                data.Status = KeepStatus.InsufficientResources;
                if (OrbitalKeepSettings.ShowResourceWarnings)
                    PostMessage(Loc.Format(Loc.MsgInsufficientRes,
                        vessel.vesselName, resourceCheck.ShortageDescription));
                return;
            }

            // 4. Apply orbital change and then charge resources for the burn.
            data.Status = KeepStatus.Correcting;

            if (!ApplyCorrectionToVessel(vessel, data, correction))
            {
                data.Status = KeepStatus.Drifting;
                return;
            }

            bool consumed = ResourceManager.ConsumeResources(
                vessel, deltaV, engineInfo,
                out double ecConsumed, out double fuelMassConsumed);

            if (!consumed)
            {
                data.Status = KeepStatus.InsufficientResources;
                return;
            }

            // Update statistics
            data.TotalDeltaVSpent += deltaV;
            data.TotalECSpent += ecConsumed;
            data.Status = KeepStatus.Nominal;

            if (OrbitalKeepSettings.ShowCorrectionMessages)
            {
                PostMessage(Loc.Format(Loc.MsgCorrectionDone,
                    vessel.vesselName, deltaV.ToString("F2"),
                    ecConsumed.ToString("F1"), correction.Description));
            }

            Debug.Log($"[OrbitalKeeper] {vessel.vesselName}: Background correction applied. " +
                      $"dV={deltaV:F2}m/s, EC={ecConsumed:F1}, fuel={fuelMassConsumed:F4}t. " +
                      $"loaded={vessel.loaded}, packed={vessel.packed}, " +
                      $"Total dV spent: {data.TotalDeltaVSpent:F2}m/s");
        }

        /// <summary>
        /// Modifies an on-rails vessel's orbit by directly setting Keplerian elements.
        /// Only valid for UNLOADED vessels (on-rails), where the Orbit object is the
        /// sole authority for the vessel's trajectory.
        /// </summary>
        private static void ApplyOrbitalChangeOnRails(Vessel vessel, VesselKeepData data)
        {
            Orbit orbit = vessel.orbit;
            CelestialBody body = orbit.referenceBody;
            double ut = Planetarium.GetUniversalTime();

            double targetApR = data.TargetApoapsis + body.Radius;
            double targetPeR = data.TargetPeriapsis + body.Radius;
            double targetSMA = (targetApR + targetPeR) / 2.0;
            double targetEcc = 0.0;
            if (targetApR + targetPeR > 0.0)
                targetEcc = Math.Max(0.0, (targetApR - targetPeR) / (targetApR + targetPeR));

            // Preserve orientation elements
            double lan = orbit.LAN;
            double argPe = orbit.argumentOfPeriapsis;
            double meanAnomaly = orbit.meanAnomaly;

            orbit.semiMajorAxis = targetSMA;
            orbit.eccentricity = targetEcc;
            orbit.inclination = data.TargetInclination;
            orbit.LAN = lan;
            orbit.argumentOfPeriapsis = argPe;
            orbit.meanAnomalyAtEpoch = meanAnomaly;
            orbit.epoch = ut;
            orbit.Init();
            orbit.UpdateFromUT(ut);
        }

        private static ResourceManager.EngineInfo FindBestEngineForVessel(
            Vessel vessel,
            VesselKeepData data)
        {
            if (vessel.loaded)
                return ResourceManager.FindBestEngine(
                    vessel,
                    data.EngineMode,
                    data.AllowRcsEngines);

            return ResourceManager.FindBestEngineUnloaded(
                vessel.protoVessel,
                data.EngineMode,
                data.AllowRcsEngines);
        }

        private static bool ApplyCorrectionToVessel(
            Vessel vessel,
            VesselKeepData data,
            DeltaVCalculator.CorrectionResult correction)
        {
            if (vessel == null || vessel.orbit == null)
                return false;

            if (vessel.loaded && !vessel.packed)
                return ApplyLoadedVelocityCorrection(vessel, data, correction);

            ApplyOrbitalChangeOnRails(vessel, data);
            if (vessel.orbitDriver != null)
                vessel.orbitDriver.UpdateOrbit();
            return true;
        }

        private static bool ApplyLoadedVelocityCorrection(
            Vessel vessel,
            VesselKeepData data,
            DeltaVCalculator.CorrectionResult correction)
        {
            if (correction.InPlaneDeltaV <= 0.01)
            {
                // Inclination-only automatic corrections require direct orbit editing.
                // Avoid that while the vessel is under full physics simulation.
                return false;
            }

            Orbit orbit = vessel.orbit;
            CelestialBody body = orbit.referenceBody;
            if (body == null)
                return false;

            double targetApR = data.TargetApoapsis + body.Radius;
            double targetPeR = data.TargetPeriapsis + body.Radius;
            double targetSma = (targetApR + targetPeR) / 2.0;
            double currentRadius = body.Radius + Math.Max(0.0, vessel.altitude);
            if (targetSma <= 0.0 || currentRadius <= body.Radius)
                return false;

            double targetSpeedSq = body.gravParameter * (2.0 / currentRadius - 1.0 / targetSma);
            if (targetSpeedSq <= 0.0)
                return false;

            Vector3d currentVelocity = vessel.obt_velocity;
            double currentSpeed = currentVelocity.magnitude;
            if (currentSpeed <= 1e-6)
                return false;

            double deltaSpeed = Math.Sqrt(targetSpeedSq) - currentSpeed;
            if (Math.Abs(deltaSpeed) <= 1e-6)
                return false;

            vessel.ChangeWorldVelocity(currentVelocity.normalized * deltaSpeed);
            if (vessel.orbitDriver != null)
                vessel.orbitDriver.UpdateOrbit();

            return true;
        }

        // ======================================================================
        //  PUBLIC API (for UI / manual triggers on loaded vessels)
        // ======================================================================

        /// <summary>
        /// Manually triggers a station-keeping correction for this vessel.
        /// Called from the UI. Loaded vessels use the same safe correction executor
        /// as automatic station-keeping.
        /// </summary>
        /// <returns>True if correction was successfully applied.</returns>
        public bool ManualCorrection()
        {
            if (StationKeepScenario.Instance == null)
                return false;

            keepData = StationKeepScenario.Instance.GetOrCreateVesselData(vessel);

            if (!IsValidOrbitForKeeping(vessel))
            {
                PostMessage(Loc.Format(Loc.MsgInvalidOrbit, vessel.vesselName));
                return false;
            }

            // Calculate correction
            var correction = DeltaVCalculator.CalculateCorrection(vessel, keepData);
            LastCorrectionResult = correction;

            if (!correction.NeedsCorrection)
            {
                PostMessage(Loc.Format(Loc.MsgNoCorrection, vessel.vesselName));
                return false;
            }

            double deltaV = Math.Min(correction.TotalDeltaV, OrbitalKeepSettings.MaxCorrectionDeltaV);

            // Find engine
            ResourceManager.EngineInfo engineInfo = FindBestEngineForVessel(vessel, keepData);

            LastEngineInfo = engineInfo;

            if (!engineInfo.Found)
            {
                PostMessage(Loc.Format(Loc.MsgNoEngine, vessel.vesselName));
                return false;
            }

            // Check resources
            var resourceCheck = ResourceManager.CheckResources(vessel, deltaV, engineInfo);
            LastResourceCheck = resourceCheck;

            if (!resourceCheck.Sufficient)
            {
                PostMessage(Loc.Format(Loc.MsgInsufficientRes,
                    vessel.vesselName, resourceCheck.ShortageDescription));
                return false;
            }

            if (!ApplyCorrectionToVessel(vessel, keepData, correction))
            {
                Debug.LogWarning($"[OrbitalKeeper] {vessel.vesselName}: correction could not be applied in the current vessel state.");
                return false;
            }

            // Consume resources
            bool consumed = ResourceManager.ConsumeResources(
                vessel, deltaV, engineInfo,
                out double ecConsumed, out double fuelMassConsumed);

            if (!consumed)
                return false;

            // Update statistics
            keepData.TotalDeltaVSpent += deltaV;
            keepData.TotalECSpent += ecConsumed;
            keepData.Status = KeepStatus.Nominal;

            if (OrbitalKeepSettings.ShowCorrectionMessages)
            {
                PostMessage(Loc.Format(Loc.MsgCorrectionDone,
                    vessel.vesselName, deltaV.ToString("F2"),
                    ecConsumed.ToString("F1"), correction.Description));
            }

            return true;
        }

        /// <summary>
        /// Refreshes the status evaluation without applying any correction.
        /// Used by the UI to display current state.
        /// </summary>
        public void RefreshStatus()
        {
            if (StationKeepScenario.Instance == null)
                return;

            keepData = StationKeepScenario.Instance.GetVesselData(vessel.id);
            if (keepData == null)
                return;

            if (!IsValidOrbitForKeeping(vessel))
            {
                keepData.Status = KeepStatus.InvalidOrbit;
                return;
            }

            var correction = DeltaVCalculator.CalculateCorrection(vessel, keepData);
            LastCorrectionResult = correction;

            if (!correction.NeedsCorrection)
            {
                keepData.Status = keepData.AutoKeepEnabled ? KeepStatus.Nominal : KeepStatus.Disabled;
                return;
            }

            // Check engine and resources
            ResourceManager.EngineInfo engineInfo;
            if (vessel.loaded)
                engineInfo = ResourceManager.FindBestEngine(
                    vessel,
                    keepData.EngineMode,
                    keepData.AllowRcsEngines);
            else
                engineInfo = ResourceManager.FindBestEngineUnloaded(
                    vessel.protoVessel,
                    keepData.EngineMode,
                    keepData.AllowRcsEngines);

            LastEngineInfo = engineInfo;

            if (!engineInfo.Found)
            {
                keepData.Status = KeepStatus.NoEngine;
                return;
            }

            var resourceCheck = ResourceManager.CheckResources(vessel, correction.TotalDeltaV, engineInfo);
            LastResourceCheck = resourceCheck;

            keepData.Status = resourceCheck.Sufficient ? KeepStatus.Drifting : KeepStatus.InsufficientResources;
        }

        // ======================================================================
        //  HELPERS
        // ======================================================================

        /// <summary>
        /// Checks if a vessel is in a valid orbit for station-keeping.
        /// </summary>
        public static bool IsValidOrbitForKeeping(Vessel vessel)
        {
            if (vessel == null)
                return false;

            if (vessel.situation != Vessel.Situations.ORBITING)
                return false;

            if (vessel.orbit.eccentricity >= 1.0)
                return false;

            CelestialBody body = vessel.orbit.referenceBody;
            if (body.atmosphere && vessel.orbit.PeA < body.atmosphereDepth)
                return false;

            return true;
        }

        /// <summary>
        /// Posts a screen message to the player.
        /// </summary>
        public static void PostMessage(string message)
        {
            ScreenMessages.PostScreenMessage(message, OrbitalKeepSettings.MessageDuration,
                ScreenMessageStyle.UPPER_CENTER);
        }
    }
}
