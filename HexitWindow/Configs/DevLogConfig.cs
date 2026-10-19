using UnityEngine;

namespace HexitEngine
{
    public class DevLogConfig : HexitEngineTapConfig<DevLogConfig>
    {
        public Color defaultColor = Color.cyan;
        public Color warningColor = Color.yellow;
        public Color errorColor = Color.red;

        [HideInInspector] public string defaultHex, warningHex, errorHex;

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();

            defaultHex = ColorUtility.ToHtmlStringRGB(defaultColor);
            warningHex = ColorUtility.ToHtmlStringRGB(warningColor);
            errorHex = ColorUtility.ToHtmlStringRGB(errorColor);

            Dev.ClearCache();
        }
#endif
    }
}