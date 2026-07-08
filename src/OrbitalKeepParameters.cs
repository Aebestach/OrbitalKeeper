using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

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
        public override int SectionOrder => 1;
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

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamUiScaleAuto",
            toolTip = "#LOC_OrbKeep_ParamUiScaleAuto_tip")]
        public bool uiScaleAuto = true;

        [GameParameters.CustomFloatParameterUI(
            "#LOC_OrbKeep_ParamUiScalePercent",
            toolTip = "#LOC_OrbKeep_ParamUiScalePercent_tip",
            minValue = 50f,
            maxValue = 150f,
            stepCount = 100,
            displayFormat = "N0")]
        public float uiScalePercent = 100f;

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamEnableToolbarButton",
            toolTip = "#LOC_OrbKeep_ParamEnableToolbarButton_tip")]
        public bool enableToolbarButton = true;

        [GameParameters.CustomStringParameterUI(
            "#LOC_OrbKeep_ParamHotkeyKey",
            toolTip = "#LOC_OrbKeep_ParamHotkeyKey_tip")]
        public string hotkeyKey = "O";

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamHotkeyAlt",
            toolTip = "#LOC_OrbKeep_ParamHotkeyAlt_tip")]
        public bool hotkeyAlt = true;

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamHotkeyCtrl",
            toolTip = "#LOC_OrbKeep_ParamHotkeyCtrl_tip")]
        public bool hotkeyCtrl = false;

        [GameParameters.CustomParameterUI(
            "#LOC_OrbKeep_ParamHotkeyShift",
            toolTip = "#LOC_OrbKeep_ParamHotkeyShift_tip")]
        public bool hotkeyShift = false;

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

        internal KeyCode ResolveHotkeyKey()
        {
            if (string.IsNullOrEmpty(hotkeyKey) || string.Equals(hotkeyKey, "None", StringComparison.OrdinalIgnoreCase))
                return KeyCode.None;

            return Enum.TryParse(hotkeyKey, true, out KeyCode parsed) ? parsed : KeyCode.O;
        }

        internal bool IsHotkeyPressed()
        {
            KeyCode key = ResolveHotkeyKey();
            if (key == KeyCode.None || !Input.GetKeyDown(key))
                return false;
            if (hotkeyAlt && !(Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)))
                return false;
            if (hotkeyCtrl && !(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
                return false;
            if (hotkeyShift && !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                return false;
            return true;
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
            bool hadAutoFlag = node != null && node.HasValue("uiScaleAuto");
            bool hadUiScale = node != null && node.HasValue("uiScalePercent");
            base.OnLoad(node);
            instance = null;

            if (!hadAutoFlag)
            {
                uiScaleAuto = !hadUiScale ||
                    Mathf.Approximately(uiScalePercent, 100f) ||
                    Mathf.Approximately(uiScalePercent, 80f) ||
                    Mathf.Approximately(uiScalePercent, 75f);
            }

            ApplyAutoUiScale();
            TryMigrateLegacyGeneralSettings(node);
        }

        internal void ApplyAutoUiScale()
        {
            if (!uiScaleAuto)
                return;
            uiScalePercent = UIScale.DefaultUiScalePercent;
        }

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            var orbKeep = parameters?.CustomParams<OrbitalKeepParameters>();
            if (orbKeep != null && orbKeep.uiScaleAuto)
                orbKeep.ApplyAutoUiScale();
            return true;
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            var orbKeep = parameters?.CustomParams<OrbitalKeepParameters>();
            if (member.Name == "uiScalePercent" && orbKeep != null && orbKeep.uiScaleAuto)
                return false;
            return true;
        }

        private void TryMigrateLegacyGeneralSettings(ConfigNode node)
        {
            if (node.HasValue("ecPerDeltaV"))
                return;

            ConfigNode[] nodes = GameDatabase.Instance?.GetConfigNodes("ORBITAL_KEEPER_SETTINGS");
            if (nodes == null || nodes.Length == 0)
                return;

            ConfigNode settings = nodes[0];
            settings.TryGetValue("ecPerDeltaV", ref ecPerDeltaV);
            settings.TryGetValue("maxCorrectionDeltaV", ref maxCorrectionDeltaV);

            if (settings.HasValue("enableToolbarButton"))
                bool.TryParse(settings.GetValue("enableToolbarButton"), out enableToolbarButton);
        }

        public override IList ValidValues(MemberInfo member)
        {
            if (member.Name != "hotkeyKey")
                return null;

            var keys = new List<string> { "None" };
            for (char letter = 'A'; letter <= 'Z'; letter++)
                keys.Add(letter.ToString());
            return keys;
        }
    }
}
