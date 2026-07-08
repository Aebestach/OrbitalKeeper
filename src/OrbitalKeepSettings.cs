using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// Runtime accessors for Orbital Keeper settings stored in difficulty parameters.
    /// </summary>
    public static class OrbitalKeepSettings
    {
        private const double FallbackTolerance = 5.0;
        private const double FallbackCheckInterval = 3600.0;
        private const double FallbackEcPerDeltaV = 5.0;
        private const double FallbackMaxCorrectionDeltaV = 100.0;
        private const double FallbackMinSafeAltitudeMargin = 10000.0;
        private const float FallbackMessageDuration = 5.0f;

        public static double DefaultTolerance =>
            OrbitalKeepGameplayParameters.Instance?.defaultTolerance ?? FallbackTolerance;

        public static double DefaultCheckInterval =>
            OrbitalKeepGameplayParameters.Instance?.defaultCheckInterval ?? FallbackCheckInterval;

        public static EngineSelectionMode DefaultEngineMode =>
            OrbitalKeepGameplayParameters.Instance?.ResolveDefaultEngineMode() ?? EngineSelectionMode.IgnitedOnly;

        public static double ECPerDeltaV =>
            OrbitalKeepParameters.Instance?.ecPerDeltaV ?? FallbackEcPerDeltaV;

        public static double MinSafeAltitudeMargin =>
            OrbitalKeepGameplayParameters.Instance?.minSafeAltitudeMargin ?? FallbackMinSafeAltitudeMargin;

        public static double MaxCorrectionDeltaV =>
            OrbitalKeepParameters.Instance?.maxCorrectionDeltaV ?? FallbackMaxCorrectionDeltaV;

        public static bool ShowCorrectionMessages =>
            OrbitalKeepGameplayParameters.Instance?.showCorrectionMessages ?? true;

        public static bool ShowResourceWarnings =>
            OrbitalKeepGameplayParameters.Instance?.showResourceWarnings ?? true;

        public static float MessageDuration =>
            OrbitalKeepGameplayParameters.Instance?.messageDuration ?? FallbackMessageDuration;

        public static void SyncFromParameters()
        {
            if (OrbitalKeepGameplayParameters.Instance == null && OrbitalKeepParameters.Instance == null)
            {
                Debug.Log("[OrbitalKeeper] No difficulty parameters loaded, using defaults.");
                return;
            }

            Debug.Log($"[OrbitalKeeper] Settings loaded: Tolerance={DefaultTolerance}%, CheckInterval={DefaultCheckInterval}s, EC/dV={ECPerDeltaV}, MaxDV={MaxCorrectionDeltaV}m/s");
        }
    }
}
