using System;
using System.Collections.Generic;

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

        // ======================================================================
        //  ENGINE SEARCH
        // ======================================================================

        /// <summary>
        /// Finds the best eligible engine on a loaded vessel.
        /// </summary>
        public static EngineInfo FindBestEngine(Vessel vessel, EngineSelectionMode mode)
        {
            var result = new EngineInfo { Propellants = new List<PropellantInfo>() };

            if (vessel == null || vessel.parts == null)
                return result;

            double bestIsp = -1;
            ModuleEngines bestEngine = null;

            foreach (Part part in vessel.parts)
            {
                foreach (ModuleEngines engine in part.FindModulesImplementing<ModuleEngines>())
                {
                    if (!IsEngineEligible(engine, mode))
                        continue;

                    double isp = engine.atmosphereCurve.Evaluate(0f);
                    if (isp > bestIsp)
                    {
                        bestIsp = isp;
                        bestEngine = engine;
                    }
                }
            }

            if (bestEngine == null)
                return result;

            result.Found = true;
            result.Isp = bestIsp;
            result.MixtureDensity = bestEngine.mixtureDensity;

            foreach (Propellant p in bestEngine.propellants)
            {
                // Skip ElectricCharge in propellant list (handled separately)
                if (p.name == "ElectricCharge")
                    continue;

                result.Propellants.Add(new PropellantInfo
                {
                    Name = p.name,
                    Ratio = p.ratio
                });
            }

            return result;
        }

        /// <summary>
        /// Finds the best eligible engine on an unloaded vessel via ProtoVessel.
        /// </summary>
        public static EngineInfo FindBestEngineUnloaded(ProtoVessel protoVessel, EngineSelectionMode mode)
        {
            var result = new EngineInfo { Propellants = new List<PropellantInfo>() };

            if (protoVessel == null)
                return result;

            double bestIsp = -1;
            UnloadedEngineCandidate bestCandidate = null;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                List<UnloadedEngineCandidate> candidates = GetUnloadedEngineCandidates(pp.partName);
                if (candidates.Count == 0)
                    continue;

                foreach (UnloadedEngineCandidate candidate in candidates)
                {
                    if (candidate.ModuleIndex < 0 || candidate.ModuleIndex >= pp.modules.Count)
                        continue;

                    ProtoPartModuleSnapshot protoModule = pp.modules[candidate.ModuleIndex];
                    if (!IsEngineEligibleProto(protoModule, mode))
                        continue;

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

            return result;
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

        /// <summary>
        /// Checks if a vessel has sufficient resources for a station-keeping correction.
        /// </summary>
        public static ResourceCheckResult CheckResources(
            Vessel vessel, double deltaV, EngineInfo engineInfo)
        {
            var result = new ResourceCheckResult();

            // Calculate required EC
            result.RequiredEC = deltaV * OrbitalKeepSettings.ECPerDeltaV;

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

            if (result.AvailableEC < result.RequiredEC)
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

            double requiredEC = deltaV * OrbitalKeepSettings.ECPerDeltaV;
            double totalMass = vessel.loaded ? vessel.GetTotalMass() : GetProtoVesselMass(vessel.protoVessel);
            double requiredFuelMass = DeltaVCalculator.CalculateFuelMass(deltaV, engineInfo.Isp, totalMass);

            // Consume EC
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

            // Consume propellants
            foreach (var prop in engineInfo.Propellants)
            {
                double requiredUnits = requiredFuelMass / engineInfo.MixtureDensity * prop.Ratio;

                if (vessel.loaded)
                {
                    PartResourceDefinition def = GetResourceDefinition(prop.Name);
                    if (def == null)
                        continue;
                    double taken = vessel.RequestResource(vessel.rootPart, def.id, requiredUnits, true);
                    double density = def.density;
                    fuelMassConsumed += taken * density;
                }
                else
                {
                    double taken = ConsumeProtoResource(vessel.protoVessel, prop.Name, requiredUnits);
                    PartResourceDefinition def = GetResourceDefinition(prop.Name);
                    double density = def != null ? def.density : 0.0;
                    fuelMassConsumed += taken * density;
                }
            }

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
                    if (!(prefab.Modules[moduleIndex] is ModuleEngines enginePrefab))
                        continue;

                    var propellants = new List<PropellantInfo>();
                    foreach (Propellant p in enginePrefab.propellants)
                    {
                        if (p.name == "ElectricCharge")
                            continue;
                        propellants.Add(new PropellantInfo
                        {
                            Name = p.name,
                            Ratio = p.ratio
                        });
                    }

                    candidates.Add(new UnloadedEngineCandidate
                    {
                        ModuleIndex = moduleIndex,
                        Isp = enginePrefab.atmosphereCurve.Evaluate(0f),
                        MixtureDensity = enginePrefab.mixtureDensity,
                        Propellants = propellants
                    });
                }
            }

            UnloadedEngineCache[partName] = candidates;
            return candidates;
        }
    }
}
