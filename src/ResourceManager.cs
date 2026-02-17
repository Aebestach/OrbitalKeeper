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
            ProtoPartSnapshot bestPart = null;
            ProtoPartModuleSnapshot bestModule = null;

            foreach (ProtoPartSnapshot pp in protoVessel.protoPartSnapshots)
            {
                Part prefab = PartLoader.getPartInfoByName(pp.partName)?.partPrefab;
                if (prefab == null)
                    continue;

                // Check each engine module on the prefab
                int moduleIndex = 0;
                foreach (PartModule pm in prefab.Modules)
                {
                    if (pm is ModuleEngines enginePrefab)
                    {
                        // Get the corresponding proto module snapshot
                        if (moduleIndex < pp.modules.Count)
                        {
                            ProtoPartModuleSnapshot protoModule = pp.modules[moduleIndex];

                            if (IsEngineEligibleProto(protoModule, mode))
                            {
                                double isp = enginePrefab.atmosphereCurve.Evaluate(0f);
                                if (isp > bestIsp)
                                {
                                    bestIsp = isp;
                                    bestPart = pp;
                                    bestModule = protoModule;
                                }
                            }
                        }
                    }
                    moduleIndex++;
                }
            }

            if (bestPart == null)
                return result;

            // Get propellant info from the prefab engine
            Part bestPrefab = PartLoader.getPartInfoByName(bestPart.partName)?.partPrefab;
            if (bestPrefab == null)
                return result;

            foreach (ModuleEngines enginePrefab in bestPrefab.FindModulesImplementing<ModuleEngines>())
            {
                double isp = enginePrefab.atmosphereCurve.Evaluate(0f);
                if (Math.Abs(isp - bestIsp) < 0.01)
                {
                    result.Found = true;
                    result.Isp = bestIsp;
                    result.MixtureDensity = enginePrefab.mixtureDensity;

                    foreach (Propellant p in enginePrefab.propellants)
                    {
                        if (p.name == "ElectricCharge")
                            continue;

                        result.Propellants.Add(new PropellantInfo
                        {
                            Name = p.name,
                            Ratio = p.ratio
                        });
                    }
                    break;
                }
            }

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
                vessel.GetConnectedResourceTotals(
                    PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id,
                    out double ecAmount, out double ecMax);
                result.AvailableEC = ecAmount;
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
                    int resId = PartResourceLibrary.Instance.GetDefinition(prop.Name).id;
                    vessel.GetConnectedResourceTotals(resId, out availableUnits, out double _);
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
                int ecId = PartResourceLibrary.Instance.GetDefinition("ElectricCharge").id;
                double ecTaken = vessel.RequestResource(vessel.rootPart, ecId, requiredEC, true);
                ecConsumed = ecTaken;
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
                    int resId = PartResourceLibrary.Instance.GetDefinition(prop.Name).id;
                    double taken = vessel.RequestResource(vessel.rootPart, resId, requiredUnits, true);
                    double density = PartResourceLibrary.Instance.GetDefinition(prop.Name).density;
                    fuelMassConsumed += taken * density;
                }
                else
                {
                    double taken = ConsumeProtoResource(vessel.protoVessel, prop.Name, requiredUnits);
                    double density = PartResourceLibrary.Instance.GetDefinition(prop.Name).density;
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
                AvailablePart partInfo = PartLoader.getPartInfoByName(pp.partName);
                if (partInfo != null)
                {
                    totalMass += partInfo.partPrefab.mass;

                    // Add resource mass
                    foreach (ProtoPartResourceSnapshot r in pp.resources)
                    {
                        PartResourceDefinition resDef = PartResourceLibrary.Instance.GetDefinition(r.resourceName);
                        if (resDef != null)
                        {
                            totalMass += r.amount * resDef.density;
                        }
                    }
                }
            }

            return totalMass;
        }
    }
}
