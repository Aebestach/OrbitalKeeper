using System;
using System.Collections;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// VesselModule attached to every vessel. Provides:
    /// 1. The public API for manual correction (called from UI).
    /// 2. Status refresh for UI display.
    /// 3. Static helper methods used by StationKeepScenario for automatic
    ///    background correction of unloaded vessels.
    ///
    /// Automatic station-keeping only runs on UNLOADED (on-rails) vessels,
    /// driven by StationKeepScenario.FixedUpdate. This module does NOT
    /// run its own FixedUpdate loop for auto-correction.
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
        //  STATIC CORRECTION LOGIC (used by Scenario for unloaded vessels)
        // ======================================================================

        /// <summary>
        /// Entry point called by StationKeepScenario for unloaded vessels.
        /// Evaluates orbit drift, checks resources, and applies correction.
        /// </summary>
        public static void PerformOrbitCheckForVessel(Vessel vessel, VesselKeepData data)
        {
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

            // 2. Find eligible engine (unloaded path)
            ResourceManager.EngineInfo engineInfo =
                ResourceManager.FindBestEngineUnloaded(vessel.protoVessel, data.EngineMode);

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

            // 4. Consume resources and apply orbital change
            data.Status = KeepStatus.Correcting;

            bool consumed = ResourceManager.ConsumeResources(
                vessel, deltaV, engineInfo,
                out double ecConsumed, out double fuelMassConsumed);

            if (!consumed)
            {
                data.Status = KeepStatus.InsufficientResources;
                return;
            }

            // Apply orbit change (vessel is unloaded / on-rails, direct element modification works)
            ApplyOrbitalChangeOnRails(vessel, data);

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
            double meanAnomalyAtEpoch = orbit.meanAnomalyAtEpoch;
            double epoch = orbit.epoch;

            orbit.semiMajorAxis = targetSMA;
            orbit.eccentricity = targetEcc;
            orbit.inclination = data.TargetInclination;
            orbit.LAN = lan;
            orbit.argumentOfPeriapsis = argPe;
            orbit.meanAnomalyAtEpoch = meanAnomalyAtEpoch;
            orbit.epoch = epoch;
            orbit.Init();
            orbit.UpdateFromUT(ut);
        }

        // ======================================================================
        //  PUBLIC API (for UI / manual triggers on loaded vessels)
        // ======================================================================

        /// <summary>
        /// Manually triggers a station-keeping correction for this vessel.
        /// Called from the UI. For loaded vessels, temporarily switches to rails,
        /// applies the target orbit, then restores physics on the next fixed step.
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
            ResourceManager.EngineInfo engineInfo;
            if (vessel.loaded)
                engineInfo = ResourceManager.FindBestEngine(vessel, keepData.EngineMode);
            else
                engineInfo = ResourceManager.FindBestEngineUnloaded(vessel.protoVessel, keepData.EngineMode);

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

            // Consume resources
            bool consumed = ResourceManager.ConsumeResources(
                vessel, deltaV, engineInfo,
                out double ecConsumed, out double fuelMassConsumed);

            if (!consumed)
                return false;

            // Apply orbit change.
            // For loaded vessels, avoid immediate GoOffRails in the same frame,
            // which can re-synchronize to stale physics state and corrupt orbit.
            if (vessel.loaded)
            {
                try
                {
                    vessel.GoOnRails();
                    ApplyOrbitalChangeOnRails(vessel, keepData);
                    vessel.orbitDriver.UpdateOrbit();
                    StartCoroutine(RestorePhysicsNextFixedStep(vessel));
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[OrbitalKeeper] Error applying manual correction " +
                                   $"to {vessel.vesselName}: {ex.Message}");
                    try { vessel.GoOffRails(); } catch { /* best effort */ }
                    return false;
                }
            }
            else
            {
                ApplyOrbitalChangeOnRails(vessel, keepData);
            }

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

        private IEnumerator RestorePhysicsNextFixedStep(Vessel targetVessel)
        {
            // Wait one physics tick so the new rails orbit becomes the authority
            // before switching back to loaded simulation.
            yield return new WaitForFixedUpdate();

            if (targetVessel == null || !targetVessel.loaded)
                yield break;

            try
            {
                targetVessel.orbitDriver.UpdateOrbit();
                targetVessel.GoOffRails();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[OrbitalKeeper] Error restoring physics for {targetVessel.vesselName}: {ex.Message}");
            }
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
                engineInfo = ResourceManager.FindBestEngine(vessel, keepData.EngineMode);
            else
                engineInfo = ResourceManager.FindBestEngineUnloaded(vessel.protoVessel, keepData.EngineMode);

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
