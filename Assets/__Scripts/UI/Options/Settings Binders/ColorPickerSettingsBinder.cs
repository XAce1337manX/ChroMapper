using UnityEngine;

/// <summary>
///     A Settings Binder that handles colours
/// </summary>
public class ColorPickerSettingsBinder : SettingsBinder
{
    protected override object SettingsToUIValue(object input) => $"#{ColorUtility.ToHtmlStringRGBA((Color)input)}";

    protected override object UIValueToSettings(object input) => input;
}
