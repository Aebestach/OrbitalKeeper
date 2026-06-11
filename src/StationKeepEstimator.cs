using System;
using System.Reflection;

namespace OrbitalKeeper
{
    /// <summary>
    /// Builds low-cost lifetime estimates for station-keeping resources.
    /// Decay cadence is provided by SWAOD through its public API, discovered by
    /// reflection so OrbitalKeeper can still load when SWAOD is not installed.
    /// </summary>
    public static class StationKeepEstimator
    {
        public struct EstimateResult
        {
            public bool Available;
            public string UnavailableReason;
            public bool UsesSwaodDecay;
            public bool UsesCurrentCorrection;
            public bool IsStormEstimate;
            public double DeltaVPerCorrection;
            public double SecondsPerCorrection;
            public double EstimatedLifetimeSeconds;
            public double AvailableCorrections;
            public double DecayRate;
            public ResourceManager.ResourceBudgetResult Budget;
        }

        public struct EditorEstimateResult
        {
            public bool Available;
            public string UnavailableReason;
            public double DeltaVPerCorrection;
            public double SecondsPerCorrection;
            public double EstimatedLifetimeSeconds;
            public double AvailableCorrections;
            public double DecayRate;
            public double CraftMass;
            public ResourceManager.EngineInfo EngineInfo;
            public ResourceManager.ResourceBudgetResult Budget;
        }

        private struct SwaodCadence
        {
            public bool Available;
            public bool ApiFound;
            public bool IsStormEstimate;
            public double SecondsToTolerance;
            public double DecayRate;
            public double DeltaVToRestoreToleranceDrop;
        }

        private static bool swaodApiResolved;
        private static MethodInfo tryEstimateStationKeepingCadence;
        private static MethodInfo tryEstimateStationKeepingCadenceForOrbit;
        private static MethodInfo tryEstimateCurrentDecayRates;
        private static FieldInfo apiAvailableField;
        private static FieldInfo apiIsStormEstimateField;
        private static FieldInfo apiSecondsToToleranceField;
        private static FieldInfo apiDecayRateField;
        private static FieldInfo apiDeltaVToRestoreToleranceDropField;
        private static FieldInfo apiCurrentRatesAvailableField;
        private static FieldInfo apiCurrentRatesIsStormEstimateField;
        private static FieldInfo apiCurrentRatesDecayRateField;
        private static FieldInfo apiCurrentRatesDaDtField;
        private static FieldInfo apiCurrentRatesPeriapsisDaDtField;

        public static EstimateResult Estimate(Vessel vessel, VesselKeepData data)
        {
            var result = new EstimateResult
            {
                Available = false,
                UnavailableReason = Loc.Unit_NA
            };

            if (vessel == null || data == null)
                return result;

            if (!VesselKeepModule.IsValidOrbitForKeeping(vessel))
            {
                result.UnavailableReason = Loc.EstimateUnavailableInvalidOrbit;
                return result;
            }

            // Use target-orbit decay for cadence so lifetime/interval estimates stay
            // stable while the vessel drifts inside tolerance between corrections.
            SwaodCadence cadence = TryGetSwaodPlanningCadence(vessel, data);
            if (!cadence.ApiFound)
            {
                result.UnavailableReason = Loc.EstimateUnavailableSwaodMissing;
                return result;
            }

            if (!cadence.Available)
            {
                result.UnavailableReason = Loc.EstimateUnavailableSwaodUnavailable;
                return result;
            }

            ResourceManager.EngineInfo engineInfo = vessel.loaded
                ? ResourceManager.FindBestEngine(vessel, data.EngineMode, data.AllowRcsEngines)
                : ResourceManager.FindBestEngineUnloaded(
                    vessel.protoVessel,
                    data.EngineMode,
                    data.AllowRcsEngines);

            if (!engineInfo.Found)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoEngine;
                return result;
            }

            double targetToleranceDrop = EstimateTargetToleranceDrop(data);
            if (targetToleranceDrop <= 0.0 || cadence.DecayRate <= 1e-12)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoCorrection;
                return result;
            }

            double secondsPerCorrection = RoundUpToCheckInterval(
                targetToleranceDrop / cadence.DecayRate,
                data.CheckInterval);
            // Per-correction Δv is fixed from target orbit + tolerance only.
            // Decay rate affects timing (secondsPerCorrection), not burn size.
            double deltaV = EstimateDeltaVToRestoreTargetDrop(
                vessel,
                data,
                targetToleranceDrop);

            deltaV = Math.Min(deltaV, OrbitalKeepSettings.MaxCorrectionDeltaV);

            if (deltaV <= 0.01)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoCorrection;
                return result;
            }

            ResourceManager.ResourceBudgetResult budget =
                ResourceManager.EstimateResourceBudget(vessel, deltaV, engineInfo);

            result.Available = true;
            result.UsesSwaodDecay = true;
            result.IsStormEstimate = cadence.IsStormEstimate;
            result.DeltaVPerCorrection = deltaV;
            result.SecondsPerCorrection = secondsPerCorrection;
            result.DecayRate = cadence.DecayRate;
            result.Budget = budget;
            result.AvailableCorrections = budget.AvailableCorrections;
            result.EstimatedLifetimeSeconds = budget.AvailableCorrections * secondsPerCorrection;

            return result;
        }

        public static bool TryEstimateCurrentDecayRate(
            Vessel vessel,
            out double decayRate,
            out bool stormEstimate)
        {
            decayRate = 0.0;
            stormEstimate = false;

            if (vessel == null || !ResolveSwaodApi() || tryEstimateCurrentDecayRates == null)
                return false;

            try
            {
                object[] args =
                {
                    vessel,
                    null
                };

                bool methodResult = (bool)tryEstimateCurrentDecayRates.Invoke(null, args);
                object apiRates = args[1];
                bool available = methodResult && GetBoolField(apiRates, apiCurrentRatesAvailableField);
                if (!available)
                    return false;

                stormEstimate = GetBoolField(apiRates, apiCurrentRatesIsStormEstimateField);

                double periapsisDaDt = GetDoubleField(apiRates, apiCurrentRatesPeriapsisDaDtField);
                if (periapsisDaDt < -1e-12)
                    decayRate = -periapsisDaDt;
                else
                    decayRate = GetDoubleField(apiRates, apiCurrentRatesDecayRateField);

                if (decayRate <= 1e-12)
                    decayRate = Math.Abs(GetDoubleField(apiRates, apiCurrentRatesDaDtField));
                return decayRate > 1e-12;
            }
            catch
            {
                decayRate = 0.0;
                stormEstimate = false;
                return false;
            }
        }

        public static EditorEstimateResult EstimateEditor(
            ShipConstruct ship,
            CelestialBody body,
            double targetApoapsis,
            double targetPeriapsis,
            double tolerancePercent,
            double checkInterval,
            bool allowRcs = false)
        {
            var result = new EditorEstimateResult
            {
                Available = false,
                UnavailableReason = Loc.Unit_NA
            };

            if (ship?.parts == null || ship.parts.Count == 0)
            {
                result.UnavailableReason = Loc.EditorEstimateNoCraft;
                return result;
            }

            if (body == null || targetApoapsis < targetPeriapsis || targetPeriapsis <= 0.0)
            {
                result.UnavailableReason = Loc.EstimateUnavailableInvalidOrbit;
                return result;
            }

            result.CraftMass = ResourceManager.GetEditorShipMass(ship);
            if (result.CraftMass <= 0.001)
            {
                result.UnavailableReason = Loc.EditorEstimateNoCraft;
                return result;
            }

            SwaodCadence cadence = TryGetSwaodEditorCadence(
                body,
                targetApoapsis,
                targetPeriapsis,
                tolerancePercent,
                result.CraftMass);

            if (!cadence.ApiFound)
            {
                result.UnavailableReason = Loc.EstimateUnavailableSwaodMissing;
                return result;
            }

            if (!cadence.Available)
            {
                result.UnavailableReason = Loc.EstimateUnavailableSwaodUnavailable;
                return result;
            }

            ResourceManager.EngineInfo engineInfo = ResourceManager.FindBestEngineInEditor(ship, allowRcs);
            if (!engineInfo.Found)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoEngine;
                return result;
            }

            double targetToleranceDrop = EstimateTargetToleranceDrop(
                targetApoapsis,
                targetPeriapsis,
                tolerancePercent);
            if (targetToleranceDrop <= 0.0 || cadence.DecayRate <= 1e-12)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoCorrection;
                return result;
            }

            double secondsPerCorrection = RoundUpToCheckInterval(
                targetToleranceDrop / cadence.DecayRate,
                checkInterval);
            double deltaV = EstimateDeltaVToRestoreTargetDrop(
                body,
                targetApoapsis,
                targetPeriapsis,
                targetToleranceDrop);

            deltaV = Math.Min(deltaV, OrbitalKeepSettings.MaxCorrectionDeltaV);
            if (deltaV <= 0.01)
            {
                result.UnavailableReason = Loc.EstimateUnavailableNoCorrection;
                return result;
            }

            ResourceManager.ResourceBudgetResult budget =
                ResourceManager.EstimateEditorResourceBudget(ship, deltaV, engineInfo);

            result.Available = true;
            result.DeltaVPerCorrection = deltaV;
            result.SecondsPerCorrection = secondsPerCorrection;
            result.DecayRate = cadence.DecayRate;
            result.EngineInfo = engineInfo;
            result.Budget = budget;
            result.AvailableCorrections = budget.AvailableCorrections;
            result.EstimatedLifetimeSeconds = budget.AvailableCorrections * secondsPerCorrection;
            return result;
        }

        private static double EstimateTargetToleranceDrop(VesselKeepData data)
        {
            double tolerance = Math.Max(0.01, data.Tolerance / 100.0);
            double apBand = GetToleranceBand(data.TargetApoapsis, tolerance);
            double peBand = GetToleranceBand(data.TargetPeriapsis, tolerance);
            return Math.Max(1.0, Math.Min(apBand, peBand));
        }

        private static double EstimateTargetToleranceDrop(
            double targetApoapsis,
            double targetPeriapsis,
            double tolerancePercent)
        {
            double tolerance = Math.Max(0.01, tolerancePercent / 100.0);
            double apBand = GetToleranceBand(targetApoapsis, tolerance);
            double peBand = GetToleranceBand(targetPeriapsis, tolerance);
            return Math.Max(1.0, Math.Min(apBand, peBand));
        }

        private static double GetToleranceBand(double targetAltitude, double tolerance)
        {
            if (Math.Abs(targetAltitude) < 1.0)
                return 1000.0 * tolerance;
            return Math.Abs(targetAltitude) * tolerance;
        }

        private static double RoundUpToCheckInterval(double secondsToTolerance, double checkInterval)
        {
            double interval = Math.Max(60.0, checkInterval);
            if (secondsToTolerance <= interval)
                return interval;
            return Math.Ceiling(secondsToTolerance / interval) * interval;
        }

        private static double EstimateDeltaVToRestoreTargetDrop(
            Vessel vessel,
            VesselKeepData data,
            double dropMeters)
        {
            if (vessel == null || vessel.orbit == null || dropMeters <= 0.0)
                return 0.0;

            CelestialBody body = vessel.orbit.referenceBody;
            if (body == null)
                return 0.0;

            double targetApR = body.Radius + data.TargetApoapsis;
            double targetPeR = body.Radius + data.TargetPeriapsis;
            double a = (targetApR + targetPeR) * 0.5;
            if (a <= 0.0)
                return 0.0;

            double r = Math.Max(targetPeR, body.Radius + 1.0);
            double vSq = body.gravParameter * (2.0 / r - 1.0 / a);
            double speed = Math.Sqrt(Math.Max(0.0, vSq));

            if (speed <= 1e-6)
                return 0.0;

            return Math.Abs((body.gravParameter / (2.0 * a * a * speed)) * dropMeters);
        }

        private static double EstimateDeltaVToRestoreTargetDrop(
            CelestialBody body,
            double targetApoapsis,
            double targetPeriapsis,
            double dropMeters)
        {
            if (body == null || dropMeters <= 0.0)
                return 0.0;

            double targetApR = body.Radius + targetApoapsis;
            double targetPeR = body.Radius + targetPeriapsis;
            double a = (targetApR + targetPeR) * 0.5;
            if (a <= 0.0)
                return 0.0;

            double r = Math.Max(targetPeR, body.Radius + 1.0);
            double vSq = body.gravParameter * (2.0 / r - 1.0 / a);
            double speed = Math.Sqrt(Math.Max(0.0, vSq));

            if (speed <= 1e-6)
                return 0.0;

            return Math.Abs((body.gravParameter / (2.0 * a * a * speed)) * dropMeters);
        }

        private static SwaodCadence TryGetSwaodPlanningCadence(Vessel vessel, VesselKeepData data)
        {
            CelestialBody body = vessel?.mainBody ?? vessel?.orbit?.referenceBody;
            if (body == null || data == null)
            {
                return new SwaodCadence { ApiFound = ResolveSwaodApi() };
            }

            SwaodCadence cadence = TryGetSwaodEditorCadence(
                body,
                data.TargetApoapsis,
                data.TargetPeriapsis,
                data.Tolerance,
                ResourceManager.GetVesselMass(vessel));

            if (cadence.Available)
                return cadence;

            return TryGetSwaodCadence(vessel, data);
        }

        private static SwaodCadence TryGetSwaodCadence(Vessel vessel, VesselKeepData data)
        {
            var cadence = new SwaodCadence
            {
                ApiFound = ResolveSwaodApi()
            };

            if (!cadence.ApiFound)
                return cadence;

            try
            {
                object[] args =
                {
                    vessel,
                    data.TargetApoapsis,
                    data.TargetPeriapsis,
                    data.Tolerance,
                    null
                };

                bool methodResult = (bool)tryEstimateStationKeepingCadence.Invoke(null, args);
                object apiEstimate = args[4];

                cadence.Available = methodResult && GetBoolField(apiEstimate, apiAvailableField);
                cadence.IsStormEstimate = GetBoolField(apiEstimate, apiIsStormEstimateField);
                cadence.SecondsToTolerance = GetDoubleField(apiEstimate, apiSecondsToToleranceField);
                cadence.DecayRate = GetDoubleField(apiEstimate, apiDecayRateField);
                cadence.DeltaVToRestoreToleranceDrop =
                    GetDoubleField(apiEstimate, apiDeltaVToRestoreToleranceDropField);
            }
            catch
            {
                cadence.Available = false;
            }

            return cadence;
        }

        private static SwaodCadence TryGetSwaodEditorCadence(
            CelestialBody body,
            double targetApoapsis,
            double targetPeriapsis,
            double tolerancePercent,
            double craftMass)
        {
            var cadence = new SwaodCadence
            {
                ApiFound = ResolveSwaodApi() && tryEstimateStationKeepingCadenceForOrbit != null
            };

            if (!cadence.ApiFound)
                return cadence;

            try
            {
                object[] args =
                {
                    body,
                    targetApoapsis,
                    targetPeriapsis,
                    tolerancePercent,
                    craftMass,
                    null
                };

                bool methodResult = (bool)tryEstimateStationKeepingCadenceForOrbit.Invoke(null, args);
                object apiEstimate = args[5];

                cadence.Available = methodResult && GetBoolField(apiEstimate, apiAvailableField);
                cadence.IsStormEstimate = GetBoolField(apiEstimate, apiIsStormEstimateField);
                cadence.SecondsToTolerance = GetDoubleField(apiEstimate, apiSecondsToToleranceField);
                cadence.DecayRate = GetDoubleField(apiEstimate, apiDecayRateField);
                cadence.DeltaVToRestoreToleranceDrop =
                    GetDoubleField(apiEstimate, apiDeltaVToRestoreToleranceDropField);
            }
            catch
            {
                cadence.Available = false;
            }

            return cadence;
        }

        private static bool ResolveSwaodApi()
        {
            if (swaodApiResolved)
                return tryEstimateStationKeepingCadence != null;

            swaodApiResolved = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type apiType = assembly.GetType(
                    "SpaceWeatherAndAtmosphericOrbitalDecay.OrbitalDecayApi",
                    false);
                if (apiType == null)
                    continue;

                Type estimateType = apiType.GetNestedType("StationKeepingEstimate", BindingFlags.Public);
                if (estimateType == null)
                    return false;
                Type currentDecayRatesType = apiType.GetNestedType("CurrentDecayRates", BindingFlags.Public);
                if (currentDecayRatesType == null)
                    return false;

                tryEstimateStationKeepingCadence = apiType.GetMethod(
                    "TryEstimateStationKeepingCadence",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Vessel),
                        typeof(double),
                        typeof(double),
                        typeof(double),
                        estimateType.MakeByRefType()
                    },
                    null);

                tryEstimateStationKeepingCadenceForOrbit = apiType.GetMethod(
                    "TryEstimateStationKeepingCadenceForOrbit",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(CelestialBody),
                        typeof(double),
                        typeof(double),
                        typeof(double),
                        typeof(double),
                        estimateType.MakeByRefType()
                    },
                    null);

                tryEstimateCurrentDecayRates = apiType.GetMethod(
                    "TryEstimateCurrentDecayRates",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[]
                    {
                        typeof(Vessel),
                        currentDecayRatesType.MakeByRefType()
                    },
                    null);

                apiAvailableField = estimateType.GetField("Available");
                apiIsStormEstimateField = estimateType.GetField("IsStormEstimate");
                apiSecondsToToleranceField = estimateType.GetField("SecondsToTolerance");
                apiDecayRateField = estimateType.GetField("DecayRate");
                apiDeltaVToRestoreToleranceDropField =
                    estimateType.GetField("DeltaVToRestoreToleranceDrop");
                apiCurrentRatesAvailableField = currentDecayRatesType.GetField("Available");
                apiCurrentRatesIsStormEstimateField = currentDecayRatesType.GetField("IsStormEstimate");
                apiCurrentRatesDecayRateField = currentDecayRatesType.GetField("DecayRate");
                apiCurrentRatesDaDtField = currentDecayRatesType.GetField("DaDt");
                apiCurrentRatesPeriapsisDaDtField = currentDecayRatesType.GetField("PeriapsisDaDt");

                return tryEstimateStationKeepingCadence != null &&
                       tryEstimateCurrentDecayRates != null &&
                       apiAvailableField != null &&
                       apiIsStormEstimateField != null &&
                       apiSecondsToToleranceField != null &&
                       apiDecayRateField != null &&
                       apiDeltaVToRestoreToleranceDropField != null &&
                       apiCurrentRatesAvailableField != null &&
                       apiCurrentRatesIsStormEstimateField != null &&
                       apiCurrentRatesDecayRateField != null &&
                       apiCurrentRatesDaDtField != null &&
                       apiCurrentRatesPeriapsisDaDtField != null;
            }

            return false;
        }

        private static bool GetBoolField(object instance, FieldInfo field)
        {
            if (instance == null || field == null)
                return false;
            object value = field.GetValue(instance);
            return value is bool b && b;
        }

        private static double GetDoubleField(object instance, FieldInfo field)
        {
            if (instance == null || field == null)
                return 0.0;
            object value = field.GetValue(instance);
            return value is double d ? d : 0.0;
        }
    }
}
