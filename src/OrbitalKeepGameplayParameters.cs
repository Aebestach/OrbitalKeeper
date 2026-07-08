using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace OrbitalKeeper
{
    /// <summary>
    /// Gameplay defaults migrated from OrbitalKeeper.cfg.
    /// </summary>
    public class OrbitalKeepGameplayParameters : GameParameters.CustomParameterNode
    {
        public override string Title => Localizer.Format("#LOC_OrbKeep_ParamGameplayTitle");
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.ANY;
        public override string Section => Localizer.Format("#LOC_OrbKeep_ParamSection");
        public override string DisplaySection => Section;
        public override int SectionOrder => 0;
        public override bool HasPresets => false;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamDefaultTolerance",
            toolTip = "#LOC_OrbKeep_ParamDefaultTolerance_tip",
            minValue = 1f,
            maxValue = 20f,
            stepCount = 19,
            displayFormat = "F1")]
        public float defaultTolerance = 5f;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamDefaultCheckInterval",
            toolTip = "#LOC_OrbKeep_ParamDefaultCheckInterval_tip",
            minValue = 60f,
            maxValue = 86400f,
            stepCount = 100,
            displayFormat = "N0")]
        public float defaultCheckInterval = 3600f;

        [GameParameters.CustomStringParameterUI(
            "#LOC_OrbKeep_ParamDefaultEngineMode",
            toolTip = "#LOC_OrbKeep_ParamDefaultEngineMode_tip")]
        public string defaultEngineMode = "IgnitedOnly";

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamMinSafeAltitudeMargin",
            toolTip = "#LOC_OrbKeep_ParamMinSafeAltitudeMargin_tip",
            minValue = 0f,
            maxValue = 50000f,
            stepCount = 100,
            displayFormat = "N0")]
        public float minSafeAltitudeMargin = 10000f;

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamShowCorrectionMessages",
            toolTip = "#LOC_OrbKeep_ParamShowCorrectionMessages_tip")]
        public bool showCorrectionMessages = true;

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamShowResourceWarnings",
            toolTip = "#LOC_OrbKeep_ParamShowResourceWarnings_tip")]
        public bool showResourceWarnings = true;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamMessageDuration",
            toolTip = "#LOC_OrbKeep_ParamMessageDuration_tip",
            minValue = 1f,
            maxValue = 30f,
            stepCount = 29,
            displayFormat = "F1")]
        public float messageDuration = 5f;

        private static OrbitalKeepGameplayParameters instance;

        public static OrbitalKeepGameplayParameters Instance
        {
            get
            {
                if (instance == null && HighLogic.CurrentGame != null)
                    instance = HighLogic.CurrentGame.Parameters.CustomParams<OrbitalKeepGameplayParameters>();
                return instance;
            }
        }

        internal EngineSelectionMode ResolveDefaultEngineMode()
        {
            if (Enum.TryParse(defaultEngineMode, out EngineSelectionMode mode))
                return mode;
            return EngineSelectionMode.IgnitedOnly;
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            instance = null;

            if (!node.HasValue("defaultTolerance"))
                TryMigrateFromLegacyCfg();
        }

        private void TryMigrateFromLegacyCfg()
        {
            ConfigNode[] nodes = GameDatabase.Instance?.GetConfigNodes("ORBITAL_KEEPER_SETTINGS");
            if (nodes == null || nodes.Length == 0)
                return;

            ConfigNode settings = nodes[0];
            settings.TryGetValue("defaultTolerance", ref defaultTolerance);
            settings.TryGetValue("defaultCheckInterval", ref defaultCheckInterval);
            settings.TryGetValue("minSafeAltitudeMargin", ref minSafeAltitudeMargin);
            settings.TryGetValue("messageDuration", ref messageDuration);

            if (settings.HasValue("defaultEngineMode"))
                defaultEngineMode = settings.GetValue("defaultEngineMode");

            if (settings.HasValue("showCorrectionMessages"))
                bool.TryParse(settings.GetValue("showCorrectionMessages"), out showCorrectionMessages);

            if (settings.HasValue("showResourceWarnings"))
                bool.TryParse(settings.GetValue("showResourceWarnings"), out showResourceWarnings);
        }

        public override IList ValidValues(MemberInfo member)
        {
            if (member.Name != "defaultEngineMode")
                return null;

            return new List<string> { "IgnitedOnly", "ActiveNotShutdown" };
        }
    }
}
