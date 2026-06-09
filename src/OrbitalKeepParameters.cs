using KSP.Localization;

namespace OrbitalKeeper
{
    /// <summary>
    /// Per-save difficulty settings for Orbital Keeper.
    /// Appears under Difficulty Settings when creating or editing a game.
    /// </summary>
    public class OrbitalKeepParameters : GameParameters.CustomParameterNode
    {
        public override string Title => Localizer.Format("#LOC_OrbKeep_ParamTitle");
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override string Section => Localizer.Format("#LOC_OrbKeep_ParamSection");
        public override string DisplaySection => Section;
        public override int SectionOrder => 0;
        public override bool HasPresets => true;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamEcPerDeltaV",
            toolTip = "#LOC_OrbKeep_ParamEcPerDeltaV_tip",
            minValue = 0f,
            maxValue = 20f,
            stepCount = 20,
            displayFormat = "N0")]
        public float ecPerDeltaV = 5f;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamMaxCorrectionDeltaV",
            toolTip = "#LOC_OrbKeep_ParamMaxCorrectionDeltaV_tip",
            minValue = 1f,
            maxValue = 200f,
            stepCount = 199,
            displayFormat = "N0")]
        public float maxCorrectionDeltaV = 100f;

        private static OrbitalKeepParameters instance;

        public static OrbitalKeepParameters Instance
        {
            get
            {
                if (instance == null && HighLogic.CurrentGame != null)
                    instance = HighLogic.CurrentGame.Parameters.CustomParams<OrbitalKeepParameters>();
                return instance;
            }
        }

        public override void SetDifficultyPreset(GameParameters.Preset preset)
        {
            switch (preset)
            {
                case GameParameters.Preset.Easy:
                    ecPerDeltaV = 0f;
                    maxCorrectionDeltaV = 200f;
                    break;
                case GameParameters.Preset.Normal:
                    ecPerDeltaV = 5f;
                    maxCorrectionDeltaV = 100f;
                    break;
                case GameParameters.Preset.Moderate:
                    ecPerDeltaV = 10f;
                    maxCorrectionDeltaV = 50f;
                    break;
                case GameParameters.Preset.Hard:
                    ecPerDeltaV = 20f;
                    maxCorrectionDeltaV = 25f;
                    break;
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            instance = null;
        }
    }
}
