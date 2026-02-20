using System;

namespace OrbitalKeeper
{
    /// <summary>
    /// Utility class for calculating delta-v requirements for orbital corrections.
    /// Uses vis-viva equation for in-plane corrections and simple plane-change formula
    /// for inclination corrections.
    /// </summary>
    public static class DeltaVCalculator
    {
        /// <summary>Standard gravity constant (m/s^2).</summary>
        public const double G0 = 9.80665;

        /// <summary>
        /// Result of a delta-v calculation for orbital correction.
        /// </summary>
        public struct CorrectionResult
        {
            /// <summary>Total delta-v required (m/s).</summary>
            public double TotalDeltaV;

            /// <summary>Delta-v for in-plane correction (Ap/Pe/eccentricity) (m/s).</summary>
            public double InPlaneDeltaV;

            /// <summary>Delta-v for inclination correction (m/s).</summary>
            public double InclinationDeltaV;

            /// <summary>Whether any correction is needed.</summary>
            public bool NeedsCorrection;

            /// <summary>Human-readable description of what needs correction.</summary>
            public string Description;
        }

        /// <summary>
        /// Calculates the delta-v required to correct the vessel's orbit to match target parameters.
        /// </summary>
        /// <param name="vessel">The vessel to evaluate.</param>
        /// <param name="data">The target orbital parameters.</param>
        /// <returns>CorrectionResult with delta-v breakdown.</returns>
        public static CorrectionResult CalculateCorrection(Vessel vessel, VesselKeepData data)
        {
            var result = new CorrectionResult();
            Orbit orbit = vessel.orbit;
            CelestialBody body = orbit.referenceBody;

            // Current orbital parameters
            double currentAp = orbit.ApA;
            double currentPe = orbit.PeA;
            double currentInc = orbit.inclination;
            double currentEcc = orbit.eccentricity;

            // Tolerance fraction
            double tolFrac = data.Tolerance / 100.0;

            bool apDrifted = IsOutOfTolerance(currentAp, data.TargetApoapsis, tolFrac);
            bool peDrifted = IsOutOfTolerance(currentPe, data.TargetPeriapsis, tolFrac);
            bool incDrifted = IsOutOfToleranceAbsolute(currentInc, data.TargetInclination, tolFrac);
            bool eccDrifted = IsOutOfToleranceAbsolute(currentEcc, data.TargetEccentricity, tolFrac);

            string desc = "";

            // --- In-plane correction (Ap/Pe) ---
            if (apDrifted || peDrifted || eccDrifted)
            {
                result.InPlaneDeltaV = CalculateInPlaneDeltaV(
                    currentAp, currentPe,
                    data.TargetApoapsis, data.TargetPeriapsis,
                    body);

                if (apDrifted) desc += Loc.Format(Loc.DescApDrift, currentAp.ToString("F0"), data.TargetApoapsis.ToString("F0")) + " ";
                if (peDrifted) desc += Loc.Format(Loc.DescPeDrift, currentPe.ToString("F0"), data.TargetPeriapsis.ToString("F0")) + " ";
                if (eccDrifted) desc += Loc.Format(Loc.DescEccDrift, currentEcc.ToString("F4"), data.TargetEccentricity.ToString("F4")) + " ";
            }

            // --- Inclination correction ---
            if (incDrifted)
            {
                result.InclinationDeltaV = CalculateInclinationDeltaV(
                    currentInc, data.TargetInclination,
                    orbit, body);

                desc += Loc.Format(Loc.DescIncDrift, currentInc.ToString("F2"), data.TargetInclination.ToString("F2")) + " ";
            }

            // Total is the RSS (root sum of squares) for combined maneuvers
            // This is an approximation; in reality they could be combined at nodes
            result.TotalDeltaV = Math.Sqrt(
                result.InPlaneDeltaV * result.InPlaneDeltaV +
                result.InclinationDeltaV * result.InclinationDeltaV);

            result.NeedsCorrection = result.TotalDeltaV > 0.01; // > 1 cm/s threshold
            result.Description = desc.Trim();

            return result;
        }

        /// <summary>
        /// Calculates delta-v for in-plane Hohmann-like transfer from current orbit to target orbit.
        /// Uses vis-viva equation at both burns.
        /// </summary>
        private static double CalculateInPlaneDeltaV(
            double currentAp, double currentPe,
            double targetAp, double targetPe,
            CelestialBody body)
        {
            double mu = body.gravParameter;
            double bodyRadius = body.Radius;

            // Convert altitudes to radii
            double rAp1 = currentAp + bodyRadius;
            double rPe1 = currentPe + bodyRadius;
            double rAp2 = targetAp + bodyRadius;
            double rPe2 = targetPe + bodyRadius;

            // Semi-major axes
            double sma1 = (rAp1 + rPe1) / 2.0;
            double sma2 = (rAp2 + rPe2) / 2.0;

            // If orbits are essentially the same, no correction needed
            if (Math.Abs(sma1 - sma2) < 1.0)
                return 0;

            double totalDv = 0;

            // --- Burn 1: at periapsis, change apoapsis ---
            if (Math.Abs(rAp1 - rAp2) > 1.0)
            {
                // Transfer orbit: same periapsis, new apoapsis
                double smaTransfer = (rPe1 + rAp2) / 2.0;

                // Velocity at periapsis on current orbit
                double v1AtPe = Math.Sqrt(mu * (2.0 / rPe1 - 1.0 / sma1));
                // Velocity at periapsis on transfer orbit
                double vTransferAtPe = Math.Sqrt(mu * (2.0 / rPe1 - 1.0 / smaTransfer));

                totalDv += Math.Abs(vTransferAtPe - v1AtPe);
            }

            // --- Burn 2: at apoapsis, change periapsis ---
            if (Math.Abs(rPe1 - rPe2) > 1.0)
            {
                // After burn 1, we're on an orbit with correct apoapsis but old periapsis
                // At the new apoapsis, burn to change periapsis
                double smaIntermediate = (rPe1 + rAp2) / 2.0;
                double smaFinal = (rPe2 + rAp2) / 2.0;

                // Only if apoapsis was also changed; otherwise use current orbit
                if (Math.Abs(rAp1 - rAp2) <= 1.0)
                {
                    smaIntermediate = sma1;
                }

                double vAtAp = Math.Sqrt(mu * (2.0 / rAp2 - 1.0 / smaIntermediate));
                double vFinalAtAp = Math.Sqrt(mu * (2.0 / rAp2 - 1.0 / smaFinal));

                totalDv += Math.Abs(vFinalAtAp - vAtAp);
            }

            return totalDv;
        }

        /// <summary>
        /// Calculates delta-v for inclination change at the ascending/descending node.
        /// Formula: dv = 2 * v * sin(delta_i / 2)
        /// Uses the orbital velocity at the ascending node for better accuracy.
        /// </summary>
        private static double CalculateInclinationDeltaV(
            double currentInc, double targetInc,
            Orbit orbit, CelestialBody body)
        {
            double deltaInc = Math.Abs(targetInc - currentInc);
            if (deltaInc < 0.001)
                return 0;

            double deltaIncRad = deltaInc * Math.PI / 180.0;

            // Orbital velocity at the node (approximate using average velocity)
            double mu = body.gravParameter;
            double sma = orbit.semiMajorAxis;
            double r = sma; // Approximation: use semi-major axis distance

            double v = Math.Sqrt(mu / r); // Circular orbit approximation

            return 2.0 * v * Math.Sin(deltaIncRad / 2.0);
        }

        /// <summary>
        /// Checks if a value has drifted outside a percentage tolerance of the target.
        /// For values like Ap/Pe where the target may be large.
        /// </summary>
        public static bool IsOutOfTolerance(double current, double target, double toleranceFraction)
        {
            if (Math.Abs(target) < 1.0)
            {
                // For very small targets (near-zero), use absolute tolerance
                return Math.Abs(current - target) > 1000.0 * toleranceFraction;
            }

            double ratio = current / target;
            return ratio < (1.0 - toleranceFraction) || ratio > (1.0 + toleranceFraction);
        }

        /// <summary>
        /// Checks if a value has drifted outside tolerance using absolute comparison.
        /// For values like inclination and eccentricity where percentage doesn't work well near zero.
        /// </summary>
        public static bool IsOutOfToleranceAbsolute(double current, double target, double toleranceFraction)
        {
            // For inclination: tolerance of 5% means +-5 degrees if target is ~100°,
            // but we use a minimum absolute tolerance for small values
            double absTolerance = Math.Max(Math.Abs(target) * toleranceFraction, toleranceFraction);

            // For eccentricity (0-1 range), the tolerance fraction acts directly
            // For inclination (0-180), scale accordingly
            if (Math.Abs(target) > 1.0)
            {
                // Inclination-like (degrees)
                absTolerance = Math.Max(target * toleranceFraction, 0.5); // Min 0.5 degree
            }
            else
            {
                // Eccentricity-like (0-1)
                absTolerance = Math.Max(toleranceFraction * 0.1, 0.001); // Min 0.001
            }

            return Math.Abs(current - target) > absTolerance;
        }

        /// <summary>
        /// Calculates fuel mass required using the Tsiolkovsky rocket equation.
        /// dm = m_total * (1 - e^(-dv / (Isp * g0)))
        /// </summary>
        /// <param name="deltaV">Required delta-v in m/s.</param>
        /// <param name="isp">Specific impulse in seconds.</param>
        /// <param name="totalMass">Total vessel mass in tonnes.</param>
        /// <returns>Required fuel mass in tonnes.</returns>
        public static double CalculateFuelMass(double deltaV, double isp, double totalMass)
        {
            if (isp <= 0 || totalMass <= 0 || deltaV <= 0)
                return 0;

            double exhaustVelocity = isp * G0;
            double massRatio = Math.Exp(deltaV / exhaustVelocity);
            double dryMass = totalMass / massRatio;
            return totalMass - dryMass;
        }
    }
}
