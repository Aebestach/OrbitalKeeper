using System;
using System.Collections.Generic;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// Handles resource checking and consumption for orbital station-keeping.
    /// Supports both loaded (physics-range) and unloaded (background) vessels.
    /// </summary>
    public static class ResourceManager
    {
        private sealed class PartPrefabCacheEntry
        {
            public bool Exists;
            public Part Prefab;
            public double DryMass;
        }

        private sealed class UnloadedEngineCandidate
        {
            public int ModuleIndex;
            public double Isp;
            public double MixtureDensity;
            public List<PropellantInfo> Propellants;
            public bool IsRcs;
        }

        private static readonly Dictionary<string, PartPrefabCacheEntry> PartPrefabCache =
            new Dictionary<string, PartPrefabCacheEntry>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PartResourceDefinition> ResourceDefinitionCache =
            new Dictionary<string, PartResourceDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<UnloadedEngineCandidate>> UnloadedEngineCache =
            new Dictionary<string, List<UnloadedEngineCandidate>>(StringComparer.Ordinal);

        /// <summary>
        /// Result of an engine search on a vessel.
        /// </summary>
        public struct EngineInfo
        {
            /// <summary>Whether a suitable engine was found.</summary>
            public bool Found;
            /// <summary>Best Isp (vacuum) in seconds.</summary>
            public double Isp;
            /// <summary>Propellant names and ratios from the best engine.</summary>
            public List<PropellantInfo> Propellants;
            /// <summary>Mixture density of the engine's propellants (kg/unit).</summary>
            public double MixtureDensity;
            /// <summary>Whether the selected engine is an RCS thruster.</summary>
            public bool IsRcs;
        }

        /// <summary>
        /// Info about a single propellant used by an engine.
        /// </summary>
        public struct PropellantInfo
        {
            public string Name;
            public float Ratio;
        }

        /// <summary>
        /// Result of a resource availability check.
        /// </summary>
        public struct ResourceCheckResult
        {
            /// <summary>Whether all required resources are available.</summary>
            public bool Sufficient;
            /// <summary>Required EC.</summary>
            public double RequiredEC;
            /// <summary>Available EC.</summary>
            public double AvailableEC;
            /// <summary>Required fuel mass (tonnes).</summary>
            public double RequiredFuelMass;
            /// <summary>Description of any shortages.</summary>
            public string ShortageDescription;
        }

        /// <summary>
        /// Non-consuming resource budget for repeated station-keeping corrections.
        /// </summary>
        public struct ResourceBudgetResult
        {
            public double RequiredEC;
            public double AvailableEC;
            public double RequiredFuelMass;
            public double AvailableFuelMass;
            public double AvailableCorrections;
            public string LimitingResource;
        }

        // ======================================================================
        //  ENGINE SEARCH
        // ======================================================================

        /// <summary>
        /// Finds the best eligible engine on a loaded vessel.
        /// </summary>
        public static EngineInfo FindBestEngine(
            Vessel vessel,
            EngineSelectionMode mode,
            bool allowRcs = false)
        {
            var result = new EngineInfo { Propellants = new List<PropellantInfo>() };

            if (vessel == null || vessel.parts == null)
                return result;

            double bestIsp = -1.0;
            EngineInfo best = result;

            foreach (Part part in vessel.parts)
            {
                foreach (ModuleEngines engine in part.FindModulesImplementing<ModuleEngines>())
                {
                    if (!IsEngineEligible(engine, mode))
                        continue;

                    double isp = engine.atmosphereCurve.Evaluate(0f);
                    TryUpdateBestEngine(
                        isp,
                        engine.mixtureDensity,
                        engine.propellants,
                        false,
                        ref bestIsp,
                        ref best);
                }

                if (!allowRcs)
                    continue;

                foreach (ModuleRCS rcs in part.FindModulesImplementing<ModuleRCS>())
                {
                    if (!IsRcsEligible(rcs, mode, vessel))
                        continue;

                    TryUpdateBestEngine(
                        GetRcsVacuumIsp(rcs),
                        0.0,
                        rcs.propellants,
                        true,
                        ref bestIsp,
                        ref best);
                }

                foreach (ModuleRCSFX rcsFx in part.FindModulesImplementing<ModuleRCSFX>())
                {
                    if (!IsRcsFxEligible(rcsFx, mode, vessel))
                        continue;

                    TryUpdateBestEngine(
                        GetRcsFxVacuumIsp(rcsFx),
                        rcsFx.mixtureDensity,
                        rcsFx.propellants,
                        true,
                        ref bestIsp,
                        ref best);
                }
            }

            return best;
        }

        /// <summary>
        /// Finds the best installed engine in the editor. Editor crafts do not have
        /// ignited/shutdown runtime state, so every configured engine is eligible.
        /// </summary>
        public static EngineInfo FindBestEngineInEditor(ShipConstruct ship, bool allowRcs = false)
        {
            var result = new EngineInfo { Propellants = new List<PropellantInfo>() };

            if (ship?.parts == null)
                return result;

            double bestIsp = -1.0;
            EngineInfo best = result;

            foreach (Part part in ship.parts)
            {
                foreach (ModuleEngines engine in part.FindModulesImplementing<ModuleEngines>())
                {
                    if (engine?.atmosphereCurve == null)
                        continue;

                    TryUpdateBestEngine(
                        engine.atmosphereCurve.Evaluate(0f),
                        engine.mixtureDensity,
                        engine.propellants,
                        false,
                        ref bestIsp,
                        ref best);
                }

                if (!allowRcs)
                    continue;

                foreach (ModuleRCS rcs in part.FindModulesImplementing<ModuleRCS>())
                {
                    if (rcs == null || !rcs.moduleIsEnabled)
                        continue;

                    TryUpdateBestEngine(
                        GetRcsVacuumIsp(rcs),
                        0.0,
                        rcs.propellants,
                        true,
                        ref bestIsp,
                        ref best);
                }

                foreach (ModuleRCSFX rcsFx in part.FindModulesImplementing<ModuleRCSFX>())
                {
                    if (rcsFx == null || !rcsFx.moduleIsEnabled)
                        continue;

                    TryUpdateBestEngine(
                        GetRcsFxVacuumIsp(rcsFx),
                        rcsFx.mixtureDensity,
                        rcsFx.propellants,
                        true,
                        ref bestIsp,
                        ref best);
                }
            }

            return best;
        }

        /// <summary>
        /// Finds the best eligible engine on an unloaded vessel via ProtoVessel.
        /// </summary>
        public static EngineInfo FindBestEngineUnloaded(
            ProtoVessel protoVessel,
            EngineSelectionMode mode,
            bool allowRcs = false)
        {
            var result = new EngineInfo { Propellants = new List<PropellantInfo>() };

            if (protoVessel == null)
                return result;

            double bestIsp = -1.0;
            UnloadedEngineCandidate bestCandidate = null;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                List<UnloadedEngineCandidate> candidates = GetUnloadedEngineCandidates(pp.partName);
                if (candidates.Count == 0)
                    continue;

                foreach (UnloadedEngineCandidate candidate in candidates)
                {
                    if (candidate.IsRcs && !allowRcs)
                        continue;
                    if (candidate.ModuleIndex < 0 || candidate.ModuleIndex >= pp.modules.Count)
                        continue;

                    ProtoPartModuleSnapshot protoModule = pp.modules[candidate.ModuleIndex];
                    if (candidate.IsRcs)
                    {
                        if (!IsRcsEligibleProto(protoModule))
                            continue;
                    }
                    else if (!IsEngineEligibleProto(protoModule, mode))
                    {
                        continue;
                    }

                    if (candidate.Isp > bestIsp)
                    {
                        bestIsp = candidate.Isp;
                        bestCandidate = candidate;
                    }
                }
            }

            if (bestCandidate == null)
                return result;

            result.Found = true;
            result.Isp = bestCandidate.Isp;
            result.MixtureDensity = bestCandidate.MixtureDensity;
            result.Propellants = new List<PropellantInfo>(bestCandidate.Propellants);
            result.IsRcs = bestCandidate.IsRcs;

            return result;
        }

        private static void TryUpdateBestEngine(
            double isp,
            double mixtureDensity,
            List<Propellant> propellants,
            bool isRcs,
            ref double bestIsp,
            ref EngineInfo best)
        {
            if (isp <= bestIsp)
                return;

            bestIsp = isp;
            best = BuildEngineInfo(isp, mixtureDensity, propellants, isRcs);
        }

        private static EngineInfo BuildEngineInfo(
            double isp,
            double mixtureDensity,
            List<Propellant> propellants,
            bool isRcs)
        {
            var result = new EngineInfo
            {
                Found = true,
                Isp = isp,
                MixtureDensity = mixtureDensity > 0.0
                    ? mixtureDensity
                    : CalculateMixtureDensity(propellants),
                Propellants = new List<PropellantInfo>(),
                IsRcs = isRcs
            };

            if (propellants == null)
                return result;

            foreach (Propellant propellant in propellants)
            {
                if (propellant == null || propellant.name == "ElectricCharge")
                    continue;

                result.Propellants.Add(new PropellantInfo
                {
                    Name = propellant.name,
                    Ratio = propellant.ratio
                });
            }

            return result;
        }

        private static double GetRcsVacuumIsp(ModuleRCS rcs)
        {
            if (rcs == null)
                return 0.0;

            if (rcs.atmosphereCurve != null)
            {
                double isp = rcs.atmosphereCurve.Evaluate(0f);
                if (isp > 0.0)
                    return isp;
            }

            return rcs.realISP > 0.0f ? rcs.realISP : 0.0;
        }

        private static double GetRcsFxVacuumIsp(ModuleRCSFX rcsFx)
        {
            if (rcsFx?.atmosphereCurve == null)
                return 0.0;
            return rcsFx.atmosphereCurve.Evaluate(0f);
        }

        private static bool IsRcsEligible(ModuleRCS rcs, EngineSelectionMode mode, Vessel vessel)
        {
            if (rcs == null || !rcs.moduleIsEnabled)
                return false;

            if (mode == EngineSelectionMode.IgnitedOnly)
                return vessel != null && vessel.ActionGroups[KSPActionGroup.RCS];

            return true;
        }

        private static bool IsRcsFxEligible(ModuleRCSFX rcsFx, EngineSelectionMode mode, Vessel vessel)
        {
            if (rcsFx == null || !rcsFx.moduleIsEnabled)
                return false;

            if (mode == EngineSelectionMode.IgnitedOnly)
                return vessel != null && vessel.ActionGroups[KSPActionGroup.RCS];

            return true;
        }

        private static bool IsRcsEligibleProto(ProtoPartModuleSnapshot protoModule)
        {
            if (protoModule?.moduleValues == null)
                return false;

            string enabled = protoModule.moduleValues.GetValue("enableState");
            if (enabled == null)
                enabled = protoModule.moduleValues.GetValue("moduleIsEnabled");

            return enabled == null || enabled.Equals("True", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a loaded engine meets the eligibility criteria.
        /// </summary>
        private static bool IsEngineEligible(ModuleEngines engine, EngineSelectionMode mode)
        {
            switch (mode)
            {
                case EngineSelectionMode.IgnitedOnly:
                    return engine.EngineIgnited;

                case EngineSelectionMode.ActiveNotShutdown:
                    return engine.isOperational && !engine.flameout;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if an unloaded engine (proto module) meets the eligibility criteria.
        /// </summary>
        private static bool IsEngineEligibleProto(ProtoPartModuleSnapshot protoModule, EngineSelectionMode mode)
        {
            if (protoModule == null || protoModule.moduleValues == null)
                return false;

            switch (mode)
            {
                case EngineSelectionMode.IgnitedOnly:
                {
                    string ignited = protoModule.moduleValues.GetValue("EngineIgnited");
                    return ignited != null && ignited.Equals("True", StringComparison.OrdinalIgnoreCase);
                }

                case EngineSelectionMode.ActiveNotShutdown:
                {
                    string ignited = protoModule.moduleValues.GetValue("EngineIgnited");
                    string shutdown = protoModule.moduleValues.GetValue("engineShutdown");
                    bool isIgnited = ignited != null && ignited.Equals("True", StringComparison.OrdinalIgnoreCase);
                    bool isShutdown = shutdown != null && shutdown.Equals("True", StringComparison.OrdinalIgnoreCase);
                    // Consider it eligible if ignited OR if not explicitly shut down
                    string staged = protoModule.moduleValues.GetValue("staged");
                    bool isStaged = staged == null || staged.Equals("True", StringComparison.OrdinalIgnoreCase);
                    return (isIgnited || isStaged) && !isShutdown;
                }

                default:
                    return false;
            }
        }

        // ======================================================================
        //  RESOURCE CHECKING
        // ======================================================================

        private static double GetRequiredEc(double deltaV, EngineInfo engineInfo)
        {
            if (!engineInfo.Found || engineInfo.IsRcs || OrbitalKeepSettings.ECPerDeltaV <= 0.0)
                return 0.0;
            return deltaV * OrbitalKeepSettings.ECPerDeltaV;
        }

        /// <summary>
        /// Checks if a vessel has sufficient resources for a station-keeping correction.
        /// </summary>
        public static ResourceCheckResult CheckResources(
            Vessel vessel, double deltaV, EngineInfo engineInfo)
        {
            var result = new ResourceCheckResult();

            result.RequiredEC = GetRequiredEc(deltaV, engineInfo);

            // Calculate required fuel mass
            double totalMass = vessel.loaded ? vessel.GetTotalMass() : GetProtoVesselMass(vessel.protoVessel);
            result.RequiredFuelMass = DeltaVCalculator.CalculateFuelMass(deltaV, engineInfo.Isp, totalMass);

            // Check EC availability
            if (vessel.loaded)
            {
                PartResourceDefinition ecDef = GetResourceDefinition("ElectricCharge");
                if (ecDef != null)
                {
                    vessel.GetConnectedResourceTotals(ecDef.id, out double ecAmount, out _);
                    result.AvailableEC = ecAmount;
                }
            }
            else
            {
                result.AvailableEC = GetProtoResourceAmount(vessel.protoVessel, "ElectricCharge");
            }

            // Check propellant availability
            bool propellantSufficient = true;
            string shortage = "";

            if (result.RequiredEC > 0.0 && result.AvailableEC < result.RequiredEC)
            {
                shortage += Loc.Format(Loc.ShortageEC,
                    result.RequiredEC.ToString("F1"), result.AvailableEC.ToString("F1")) + " ";
                propellantSufficient = false;
            }

            foreach (var prop in engineInfo.Propellants)
            {
                double requiredUnits = result.RequiredFuelMass / engineInfo.MixtureDensity * prop.Ratio;
                double availableUnits;

                if (vessel.loaded)
                {
                    PartResourceDefinition def = GetResourceDefinition(prop.Name);
                    if (def == null)
                    {
                        shortage += Loc.Format(Loc.ShortagePropellant, prop.Name, requiredUnits.ToString("F2"), "0.00") + " ";
                        propellantSufficient = false;
                        continue;
                    }
                    vessel.GetConnectedResourceTotals(def.id, out availableUnits, out double _);
                }
                else
                {
                    availableUnits = GetProtoResourceAmount(vessel.protoVessel, prop.Name);
                }

                if (availableUnits < requiredUnits)
                {
                    shortage += Loc.Format(Loc.ShortagePropellant,
                        prop.Name, requiredUnits.ToString("F2"), availableUnits.ToString("F2")) + " ";
                    propellantSufficient = false;
                }
            }

            result.Sufficient = propellantSufficient;
            result.ShortageDescription = shortage.Trim();

            return result;
        }

        /// <summary>
        /// Estimates how many corrections can be paid for without consuming resources.
        /// </summary>
        public static ResourceBudgetResult EstimateResourceBudget(
            Vessel vessel, double deltaV, EngineInfo engineInfo)
        {
            var result = new ResourceBudgetResult
            {
                AvailableCorrections = 0.0,
                LimitingResource = Loc.Unit_NA
            };

            if (vessel == null || !engineInfo.Found || deltaV <= 0.0)
                return result;

            result.RequiredEC = GetRequiredEc(deltaV, engineInfo);

            double totalMass = vessel.loaded ? vessel.GetTotalMass() : GetProtoVesselMass(vessel.protoVessel);
            result.RequiredFuelMass = DeltaVCalculator.CalculateFuelMass(deltaV, engineInfo.Isp, totalMass);

            double fuelCorrections = double.PositiveInfinity;
            double availableFuelMass = double.PositiveInfinity;

            if (result.RequiredFuelMass > 1e-12 &&
                engineInfo.Propellants != null &&
                engineInfo.Propellants.Count > 0 &&
                engineInfo.MixtureDensity > 0.0)
            {
                foreach (var prop in engineInfo.Propellants)
                {
                    if (prop.Ratio <= 0f)
                        continue;

                    double requiredUnits = result.RequiredFuelMass / engineInfo.MixtureDensity * prop.Ratio;
                    if (requiredUnits <= 1e-12)
                        continue;

                    double availableUnits = GetResourceAmount(vessel, prop.Name);
                    double propCorrections = availableUnits / requiredUnits;
                    if (propCorrections < fuelCorrections)
                    {
                        fuelCorrections = propCorrections;
                        result.LimitingResource = prop.Name;
                    }

                    double propFuelMass = availableUnits / prop.Ratio * engineInfo.MixtureDensity;
                    if (propFuelMass < availableFuelMass)
                        availableFuelMass = propFuelMass;
                }
            }

            if (double.IsPositiveInfinity(availableFuelMass))
                availableFuelMass = 0.0;

            result.AvailableFuelMass = availableFuelMass;
            result.AvailableCorrections = double.IsPositiveInfinity(fuelCorrections) ? 0.0 : fuelCorrections;

            if (double.IsNaN(result.AvailableCorrections) || result.AvailableCorrections < 0.0)
                result.AvailableCorrections = 0.0;

            return result;
        }

        /// <summary>
        /// Estimates repeated correction capacity for a craft in the editor.
        /// Uses currently configured resource amounts on the ship.
        /// </summary>
        public static ResourceBudgetResult EstimateEditorResourceBudget(
            ShipConstruct ship,
            double deltaV,
            EngineInfo engineInfo)
        {
            var result = new ResourceBudgetResult
            {
                AvailableCorrections = 0.0,
                LimitingResource = Loc.Unit_NA
            };

            if (ship == null || !engineInfo.Found || deltaV <= 0.0)
                return result;

            result.RequiredEC = GetRequiredEc(deltaV, engineInfo);

            double totalMass = GetEditorShipMass(ship);
            result.RequiredFuelMass = DeltaVCalculator.CalculateFuelMass(deltaV, engineInfo.Isp, totalMass);

            double fuelCorrections = double.PositiveInfinity;
            double availableFuelMass = double.PositiveInfinity;

            if (result.RequiredFuelMass > 1e-12 &&
                engineInfo.Propellants != null &&
                engineInfo.Propellants.Count > 0 &&
                engineInfo.MixtureDensity > 0.0)
            {
                foreach (var prop in engineInfo.Propellants)
                {
                    if (prop.Ratio <= 0f)
                        continue;

                    double requiredUnits = result.RequiredFuelMass / engineInfo.MixtureDensity * prop.Ratio;
                    if (requiredUnits <= 1e-12)
                        continue;

                    double availableUnits = GetEditorResourceAmount(ship, prop.Name);
                    double propCorrections = availableUnits / requiredUnits;
                    if (propCorrections < fuelCorrections)
                    {
                        fuelCorrections = propCorrections;
                        result.LimitingResource = prop.Name;
                    }

                    double propFuelMass = availableUnits / prop.Ratio * engineInfo.MixtureDensity;
                    if (propFuelMass < availableFuelMass)
                        availableFuelMass = propFuelMass;
                }
            }

            if (double.IsPositiveInfinity(availableFuelMass))
                availableFuelMass = 0.0;

            result.AvailableFuelMass = availableFuelMass;
            result.AvailableCorrections = double.IsPositiveInfinity(fuelCorrections) ? 0.0 : fuelCorrections;

            if (double.IsNaN(result.AvailableCorrections) || result.AvailableCorrections < 0.0)
                result.AvailableCorrections = 0.0;

            return result;
        }

        // ======================================================================
        //  RESOURCE CONSUMPTION
        // ======================================================================

        /// <summary>
        /// Consumes resources for a station-keeping correction.
        /// Returns true if all resources were successfully consumed.
        /// </summary>
        public static bool ConsumeResources(
            Vessel vessel, double deltaV, EngineInfo engineInfo,
            out double ecConsumed, out double fuelMassConsumed)
        {
            ecConsumed = 0;
            fuelMassConsumed = 0;

            double requiredEC = GetRequiredEc(deltaV, engineInfo);
            double totalMass = vessel.loaded ? vessel.GetTotalMass() : GetProtoVesselMass(vessel.protoVessel);
            double requiredFuelMass = DeltaVCalculator.CalculateFuelMass(deltaV, engineInfo.Isp, totalMass);
            string vesselName = vessel != null ? vessel.vesselName : "<null>";

            if (requiredEC > 0.0)
            {
                if (vessel.loaded)
                {
                    PartResourceDefinition ecDef = GetResourceDefinition("ElectricCharge");
                    if (ecDef != null)
                    {
                        double ecTaken = vessel.RequestResource(vessel.rootPart, ecDef.id, requiredEC, true);
                        ecConsumed = ecTaken;
                    }
                }
                else
                {
                    ecConsumed = ConsumeProtoResource(vessel.protoVessel, "ElectricCharge", requiredEC);
                }
            }

            // Consume propellants
            foreach (var prop in engineInfo.Propellants)
            {
                double requiredUnits = requiredFuelMass / engineInfo.MixtureDensity * prop.Ratio;
                double taken = 0;

                if (vessel.loaded)
                {
                    PartResourceDefinition def = GetResourceDefinition(prop.Name);
                    if (def == null)
                        continue;
                    taken = vessel.RequestResource(vessel.rootPart, def.id, requiredUnits, true);
                    double density = def.density;
                    fuelMassConsumed += taken * density;
                }
                else
                {
                    taken = ConsumeProtoResource(vessel.protoVessel, prop.Name, requiredUnits);
                    PartResourceDefinition def = GetResourceDefinition(prop.Name);
                    double density = def != null ? def.density : 0.0;
                    fuelMassConsumed += taken * density;
                }

                Debug.Log($"[OrbitalKeeper] Resource consume detail ({vesselName}): " +
                          $"dV={deltaV:F2}m/s, prop={prop.Name}, required={requiredUnits:F3}, taken={taken:F3}");
            }

            Debug.Log($"[OrbitalKeeper] Resource consume summary ({vesselName}): " +
                      $"dV={deltaV:F2}m/s, loaded={vessel.loaded}, requiredEC={requiredEC:F2}, consumedEC={ecConsumed:F2}, " +
                      $"requiredFuel={requiredFuelMass:F5}t, consumedFuel={fuelMassConsumed:F5}t");

            return true;
        }

        // ======================================================================
        //  PROTO VESSEL HELPERS (for unloaded vessels)
        // ======================================================================

        /// <summary>
        /// Gets the total amount of a resource across all parts of a ProtoVessel.
        /// </summary>
        private static double GetProtoResourceAmount(ProtoVessel protoVessel, string resourceName)
        {
            double total = 0;
            if (protoVessel?.protoPartSnapshots == null)
                return total;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                foreach (ProtoPartResourceSnapshot r in pp.resources)
                {
                    if (r.resourceName == resourceName)
                    {
                        total += r.amount;
                    }
                }
            }
            return total;
        }

        private static double GetResourceAmount(Vessel vessel, string resourceName)
        {
            if (vessel == null || string.IsNullOrEmpty(resourceName))
                return 0.0;

            if (vessel.loaded)
            {
                PartResourceDefinition def = GetResourceDefinition(resourceName);
                if (def == null)
                    return 0.0;
                vessel.GetConnectedResourceTotals(def.id, out double amount, out _);
                return amount;
            }

            return GetProtoResourceAmount(vessel.protoVessel, resourceName);
        }

        public static double GetEditorShipMass(ShipConstruct ship)
        {
            double totalMass = 0.0;
            if (ship?.parts == null)
                return totalMass;

            foreach (Part part in ship.parts)
            {
                if (part == null)
                    continue;

                totalMass += Math.Max(0.0, part.mass);
                if (part.Resources == null)
                    continue;

                foreach (PartResource resource in part.Resources)
                {
                    PartResourceDefinition definition = resource?.info;
                    if (definition == null && resource != null)
                        definition = GetResourceDefinition(resource.resourceName);
                    if (definition != null)
                        totalMass += resource.amount * definition.density;
                }
            }

            return totalMass;
        }

        private static double GetEditorResourceAmount(ShipConstruct ship, string resourceName)
        {
            double total = 0.0;
            if (ship?.parts == null || string.IsNullOrEmpty(resourceName))
                return total;

            foreach (Part part in ship.parts)
            {
                if (part?.Resources == null)
                    continue;

                PartResource resource = part.Resources[resourceName];
                if (resource != null)
                    total += resource.amount;
            }

            return total;
        }

        /// <summary>
        /// Consumes a specified amount of a resource from a ProtoVessel.
        /// Returns the actual amount consumed.
        /// </summary>
        private static double ConsumeProtoResource(ProtoVessel protoVessel, string resourceName, double amount)
        {
            double remaining = amount;
            if (protoVessel?.protoPartSnapshots == null)
                return 0;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                if (remaining <= 0)
                    break;

                foreach (ProtoPartResourceSnapshot r in pp.resources)
                {
                    if (r.resourceName == resourceName && r.amount > 0)
                    {
                        double taken = Math.Min(r.amount, remaining);
                        r.amount -= taken;
                        remaining -= taken;

                        if (remaining <= 0)
                            break;
                    }
                }
            }

            return amount - remaining;
        }

        /// <summary>
        /// Returns the total mass of a vessel in tonnes (loaded or unloaded).
        /// </summary>
        public static double GetVesselMass(Vessel vessel)
        {
            if (vessel == null)
                return 0.0;
            if (vessel.loaded)
                return vessel.GetTotalMass();
            return GetProtoVesselMass(vessel.protoVessel);
        }

        /// <summary>
        /// Estimates the total mass of an unloaded vessel from its ProtoVessel.
        /// </summary>
        private static double GetProtoVesselMass(ProtoVessel protoVessel)
        {
            double totalMass = 0;
            if (protoVessel?.protoPartSnapshots == null)
                return totalMass;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                if (TryGetPartPrefab(pp.partName, out Part prefab, out double dryMass))
                {
                    totalMass += dryMass;

                    // Add resource mass
                    foreach (ProtoPartResourceSnapshot r in pp.resources)
                    {
                        PartResourceDefinition resDef = GetResourceDefinition(r.resourceName);
                        if (resDef != null)
                        {
                            totalMass += r.amount * resDef.density;
                        }
                    }
                }
            }

            return totalMass;
        }

        private static PartResourceDefinition GetResourceDefinition(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName))
                return null;

            if (ResourceDefinitionCache.TryGetValue(resourceName, out PartResourceDefinition cached))
                return cached;

            PartResourceDefinition definition = PartResourceLibrary.Instance.GetDefinition(resourceName);
            ResourceDefinitionCache[resourceName] = definition;
            return definition;
        }

        private static bool TryGetPartPrefab(string partName, out Part prefab, out double dryMass)
        {
            if (PartPrefabCache.TryGetValue(partName, out PartPrefabCacheEntry cached))
            {
                prefab = cached.Prefab;
                dryMass = cached.DryMass;
                return cached.Exists;
            }

            AvailablePart partInfo = PartLoader.getPartInfoByName(partName);
            bool exists = partInfo != null && partInfo.partPrefab != null;
            prefab = exists ? partInfo.partPrefab : null;
            dryMass = exists ? partInfo.partPrefab.mass : 0.0;

            PartPrefabCache[partName] = new PartPrefabCacheEntry
            {
                Exists = exists,
                Prefab = prefab,
                DryMass = dryMass
            };

            return exists;
        }

        private static List<UnloadedEngineCandidate> GetUnloadedEngineCandidates(string partName)
        {
            if (UnloadedEngineCache.TryGetValue(partName, out List<UnloadedEngineCandidate> cached))
                return cached;

            var candidates = new List<UnloadedEngineCandidate>();
            if (TryGetPartPrefab(partName, out Part prefab, out double _))
            {
                for (int moduleIndex = 0; moduleIndex < prefab.Modules.Count; moduleIndex++)
                {
                    PartModule modulePrefab = prefab.Modules[moduleIndex];
                    if (modulePrefab is ModuleEngines enginePrefab)
                    {
                        candidates.Add(BuildUnloadedCandidate(
                            moduleIndex,
                            enginePrefab.atmosphereCurve.Evaluate(0f),
                            enginePrefab.mixtureDensity,
                            enginePrefab.propellants,
                            false));
                        continue;
                    }

                    if (modulePrefab is ModuleRCS rcsPrefab)
                    {
                        candidates.Add(BuildUnloadedCandidate(
                            moduleIndex,
                            GetRcsVacuumIsp(rcsPrefab),
                            0.0,
                            rcsPrefab.propellants,
                            true));
                        continue;
                    }

                    if (modulePrefab is ModuleRCSFX rcsFxPrefab)
                    {
                        candidates.Add(BuildUnloadedCandidate(
                            moduleIndex,
                            GetRcsFxVacuumIsp(rcsFxPrefab),
                            rcsFxPrefab.mixtureDensity,
                            rcsFxPrefab.propellants,
                            true));
                    }
                }
            }

            UnloadedEngineCache[partName] = candidates;
            return candidates;
        }

        private static UnloadedEngineCandidate BuildUnloadedCandidate(
            int moduleIndex,
            double isp,
            double mixtureDensity,
            List<Propellant> propellants,
            bool isRcs)
        {
            var propellantInfos = new List<PropellantInfo>();
            if (propellants != null)
            {
                foreach (Propellant propellant in propellants)
                {
                    if (propellant == null || propellant.name == "ElectricCharge")
                        continue;

                    propellantInfos.Add(new PropellantInfo
                    {
                        Name = propellant.name,
                        Ratio = propellant.ratio
                    });
                }
            }

            return new UnloadedEngineCandidate
            {
                ModuleIndex = moduleIndex,
                Isp = isp,
                MixtureDensity = mixtureDensity > 0.0
                    ? mixtureDensity
                    : CalculateMixtureDensity(propellants),
                Propellants = propellantInfos,
                IsRcs = isRcs
            };
        }

        private static double CalculateMixtureDensity(List<Propellant> propellants)
        {
            if (propellants == null || propellants.Count == 0)
                return 0.0;

            double density = 0.0;
            double ratioTotal = 0.0;
            foreach (Propellant propellant in propellants)
            {
                if (propellant == null || propellant.name == "ElectricCharge")
                    continue;

                PartResourceDefinition definition = GetResourceDefinition(propellant.name);
                if (definition == null)
                    continue;

                density += definition.density * propellant.ratio;
                ratioTotal += propellant.ratio;
            }

            return ratioTotal > 0.0 ? density / ratioTotal : 0.0;
        }
    }
}
