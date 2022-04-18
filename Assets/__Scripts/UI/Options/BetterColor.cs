using UnityEngine;

// Class used to create interaction with settings binder and nested color picker component
public class BetterColor : MonoBehaviour
{
    private NestedColorPickerComponent ncpComponent;
    private ColorPickerSettingsBinder cpsBinder;

    private void Start()
    {
        if (TryGetComponent<ColorPickerSettingsBinder>(out var settingsBinder))
        {
            this.cpsBinder = settingsBinder;
            var colorSetting = settingsBinder.RetrieveValueFromSettings().ToString() ?? "#FFFFFFFF";
            ColorUtility.TryParseHtmlString(colorSetting, out var convertedColorSetting);

            if (TryGetComponent<NestedColorPickerComponent>(out var ncpc))
            {
                this.ncpComponent = ncpc;
                ncpComponent.Value = convertedColorSetting;
                ncpComponent.SetOnValueChanged(onChanged);
            }
        }
    }

    private void onChanged(Color color)
    {
        if (cpsBinder != null) cpsBinder.SendValueToSettings(color);
    }
}
