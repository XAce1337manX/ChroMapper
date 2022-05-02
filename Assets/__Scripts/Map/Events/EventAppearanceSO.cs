﻿using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "EventAppearanceSO", menuName = "Map/Appearance/Event Appearance SO")]
public class EventAppearanceSO : ScriptableObject
{
    [FormerlySerializedAs("LaserSpeedPrefab")] [Space(5)] [SerializeField] private GameObject laserSpeedPrefab;

    [Space(5)] [Header("Default Colors")] public Color RedColor;
    public Color BlueColor;
    public Color RedBoostColor;
    public Color BlueBoostColor;
    [FormerlySerializedAs("OffColor")] [SerializeField] private Color offColor;

    [FormerlySerializedAs("RingEventsColor")]
    [Header("Other Event Colors")]
    [SerializeField]
    private Color ringEventsColor;

    [FormerlySerializedAs("OtherColor")]
    [Tooltip("Example: Ring rotate/Ring zoom/Light speed change events")]
    [SerializeField]
    private Color otherColor;

    public void SetEventAppearance(BeatmapEventContainer e, bool final = true, bool boost = false)
    {
        var color = Color.white;
        e.UpdateOffset(Vector3.zero, false);
        e.UpdateAlpha(final ? 1.0f : 0.6f, false);
        e.UpdateScale(final ? 0.75f : 0.6f);
        e.ChangeSpotlightSize(1f, false);
        if (e.EventData.IsRotationEvent || e.EventData.IsLaserSpeedEvent || e.EventData.IsInterscopeEvent)
        {
            if (e.EventData.IsRotationEvent)
            {
                var rotation = e.EventData.GetRotationDegreeFromValue();
                e.UpdateTextDisplay(true, rotation != null ? $"{rotation}°" : "Invalid Rotation");
            }
            else if (e.EventData.IsLaserSpeedEvent || e.EventData.IsInterscopeEvent)
            {
                float speed = e.EventData.Value;
                if (e.EventData.CustomData != null)
                {
                    if (e.EventData.CustomData.HasKey("_preciseSpeed"))
                        speed = e.EventData.CustomData["_preciseSpeed"].AsFloat;
                    else if (e.EventData.CustomData.HasKey("_speed"))
                        speed = e.EventData.CustomData["_speed"].AsFloat;
                }

                e.UpdateTextDisplay(true, speed.ToString());
            }
        }
        // Display floatValue only where used
        else if (Settings.Instance.DisplayFloatValueText && !e.EventData.IsUtilityEvent
                && e.EventData.Value != 0 && Mathf.Abs(e.EventData.FloatValue - 1f) > 0.0001f)
        {
            e.UpdateTextDisplay(true, e.EventData.FloatValue.ToString("n2"));
        }
        else
        {
            e.UpdateTextDisplay(false);
        }

        if (e.EventData.IsUtilityEvent)
        {
            e.EventModel = EventModelType.Block;

            if (e.EventData.IsRingEvent)
            {
                e.ChangeColor(ringEventsColor, false);
                e.ChangeBaseColor(ringEventsColor, false);
            }
            else if (e.EventData.Type == MapEvent.EventTypeBoostLights)
            {
                if (e.EventData.Value == 1)
                {
                    e.ChangeBaseColor(RedBoostColor, false);
                    e.ChangeColor(BlueBoostColor, false);
                }
                else
                {
                    e.ChangeBaseColor(RedColor, false);
                    e.ChangeColor(BlueColor, false);
                }

                e.UpdateOffset(Vector3.forward * 1.05f, false);
                e.ChangeFadeSize(e.BoostEventFadeSize, false);
                e.UpdateMaterials();
                return;
            }
            else
            {
                e.ChangeColor(otherColor, false);
                e.ChangeBaseColor(otherColor, false);
            }

            e.UpdateOffset(Vector3.zero, false);
            e.UpdateGradientRendering();
            e.UpdateMaterials();
            return;
        }

        if (e.EventData.Value >= ColourManager.RgbintOffset)
        {
            color = ColourManager.ColourFromInt(e.EventData.Value);
            e.UpdateAlpha(final ? 0.9f : 0.6f, false);
        }
        else if (e.EventData.Value <= 4)
        {
            color = boost ? BlueBoostColor : BlueColor;
        }
        else if (e.EventData.Value <= 8)
        {
            color = boost ? RedBoostColor : RedColor;
        }
        if (Settings.Instance.EmulateChromaLite && e.EventData.CustomData?["_color"] != null && e.EventData.Value > 0)
            color = e.EventData.CustomData["_color"];

        e.EventModel = Settings.Instance.EventModel;
        e.ChangeColor(color, false);
        e.ChangeBaseColor(Color.black, false);
        switch (e.EventData.Value)
        {
            case MapEvent.LightValueOff:
                e.ChangeColor(offColor, false);
                e.ChangeBaseColor(offColor, false);
                e.UpdateOffset(Vector3.zero, false);
                ChangeTransitionColor(e, boost);
                break;
            case MapEvent.LightValueBlueON:
                e.UpdateOffset(Vector3.zero, false);
                e.ChangeBaseColor(color, false);
                ChangeTransitionColor(e, boost);
                break;
            case MapEvent.LightValueBlueFlash:
                e.UpdateOffset(e.FlashShaderOffset, false);
                break;
            case MapEvent.LightValueBlueFade:
                e.UpdateOffset(e.FadeShaderOffset, false);
                break;
            case MapEvent.LightValueBlueTransition:
                e.UpdateOffset(e.TransitionShaderOffset, false);
                e.ChangeBaseColor(offColor, false);
                ChangeTransitionColor(e, boost);
                break;
            case MapEvent.LightValueRedON:
                e.UpdateOffset(Vector3.zero, false);
                e.ChangeBaseColor(color, false);
                ChangeTransitionColor(e, boost);
                break;
            case MapEvent.LightValueRedFlash:
                e.UpdateOffset(e.FlashShaderOffset, false);
                break;
            case MapEvent.LightValueRedFade:
                e.UpdateOffset(e.FadeShaderOffset, false);
                break;
            case MapEvent.LightValueRedTransition:
                e.UpdateOffset(e.TransitionShaderOffset, false);
                e.ChangeBaseColor(offColor, false);
                ChangeTransitionColor(e, boost);
                break;
        }

        e.ChangeFadeSize(e.DefaultFadeSize, false);

        if (Settings.Instance.VisualizeChromaGradients) e.UpdateGradientRendering(e, this, boost);

        e.UpdateMaterials();
    }

    private void ChangeTransitionColor(BeatmapEventContainer e, bool boost) {
        if (e.EventsContainer == null || e.EventsContainer.EventsSplitByType == null) return;
        
        List<MapEvent> eventTypeList;
        Dictionary<int, List<MapEvent>> eventTypeDict;
        
        // Return if there's no nextEvent
        if (Settings.Instance.EmulateChromaAdvanced) {
            if (!e.EventsContainer.EventsSplitByTypeAndLightID.TryGetValue(e.EventData.Type, out eventTypeDict))
                return;
            int lightID = (e.EventData.CustomData != null && e.EventData.CustomData["_lightID"] != null) 
                ? e.EventData.LightId.First()
                : int.MinValue;
            if (!eventTypeDict.TryGetValue(lightID, out eventTypeList))
                return;
        } else {
            if (!e.EventsContainer.EventsSplitByType.TryGetValue(e.EventData.Type, out eventTypeList))
                return;
        }

        MapEvent nextEvent = eventTypeList.FirstOrDefault(x => x.Time > e.EventData.Time);
        e.EventData.nextEvent = nextEvent;
        if (nextEvent == null) return;

        if (Settings.Instance.EmulateChromaLite && nextEvent.CustomData != null && nextEvent.CustomData["_color"] != null)
            e.ChangeBaseColor(nextEvent.CustomData["_color"]);
        else if (nextEvent.Value == MapEvent.LightValueBlueTransition)
            e.ChangeBaseColor(boost ? BlueBoostColor : BlueColor);
        else if (nextEvent.Value == MapEvent.LightValueRedTransition)
            e.ChangeBaseColor(boost ? RedBoostColor : RedColor);
    }
}

public enum EventModelType
{
    Block = 0,
    Pyramid = 1,
    FlatPyramid = 2
}
