﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

public class PlatformDescriptorV3 : PlatformDescriptor 
{
    [Header("V3 Configurations")]
    [Tooltip("V3 LightsMangaers, which supports lightColorEvent/LightRotationEvent")]
    public LightsManagerV3[] LightsManagersV3;
    private Dictionary<int, int> groupIdToLaneIdx = new Dictionary<int, int>();

    private LightColorEventCallbackController lightColorEventCallback;
    private LightRotationEventCallbackController lightRotationEventCallback;
    private AudioTimeSyncController atsc;

    protected new void Start()
    {
        base.Start();
        for (int i = 0; i < LightsManagersV3.Length; ++i)
        {
            groupIdToLaneIdx[LightsManagersV3[i].GroupId] = i;
        }

        lightColorEventCallback = GameObject.Find("Vertical Grid Callback").GetComponent<LightColorEventCallbackController>();
        if (lightColorEventCallback == null)
        {
            Debug.LogError("Unable to find callback, maybe prerequisite is not met?");
        }
        lightColorEventCallback.ObjectPassedThreshold += LightColorEventPassed;

        lightRotationEventCallback = GameObject.Find("Vertical Grid Callback").GetComponent<LightRotationEventCallbackController>();
        if (lightColorEventCallback == null)
        {
            Debug.LogError("Unable to find callback, maybe prerequisite is not met?");
        }
        lightRotationEventCallback.ObjectPassedThreshold += LightRotationEventPassed;

        atsc = FindObjectOfType<AudioTimeSyncController>();
    }

    protected new void OnDestroy()
    {
        lightColorEventCallback.ObjectPassedThreshold -= LightColorEventPassed;
        groupIdToLaneIdx.Clear();
        base.OnDestroy();
    }

    /// <summary>
    /// return lane index of given groupId, return -1 if not found
    /// </summary>
    /// <param name="groupId"></param>
    /// <returns></returns>
    public int GroupIdToLaneIndex(int groupId)
    {
        if (groupIdToLaneIdx.TryGetValue(groupId, out var idx))
        {
            return idx;
        }
        return -1;
    }

    /// <summary>
    /// return groupId of given lane index, return -1 if not found
    /// </summary>
    /// <param name="idx"></param>
    /// <returns></returns>
    public int LaneIndexToGroupId(int idx)
    {
        if (idx < 0 || idx >= LightsManagersV3.Length) return -1;
        return LightsManagersV3[idx].GroupId;
    }

    public static IEnumerable<T> Partition<T>(IEnumerable<T> list, int section, int partition, bool reverse = false)
    {
        if (reverse) list = list.Reverse();
        var binSize = list.Count() / partition;
        return list.Where((x, i) => i / binSize == section);
    }

    public static IEnumerable<T> Range<T>(IEnumerable<T> list, int start, int step, bool reverse = false)
    {
        if (reverse) list = list.Reverse();
        return list.Where((x, i) => i % step == start);
    }

    public void LightColorEventPassed(bool natural, int idx, BeatmapLightColorEvent e)
    {
        var allLights = LightsManagersV3[GroupIdToLaneIndex(e.Group)].ControllingLights;
        var eb = e.EventBoxes[0];

        var filteredLights = eb.Filter.FilterType == 1 
            ? Partition(allLights, eb.Filter.Section, eb.Filter.Partition, eb.Filter.Reverse == 1)
            : Range(allLights, eb.Filter.Section, eb.Filter.Partition, eb.Filter.Reverse == 1);
        if (filteredLights.Count() == 0) return;

        float deltaAlpha = eb.BrightnessDistribution;
        if (eb.BrightnessDistributionType == 1) deltaAlpha /= filteredLights.Count();
        float deltaTime = atsc.GetSecondsFromBeat(eb.Distribution);
        if (eb.DistributionType == 1) deltaTime /= filteredLights.Count();
        foreach (var ebd in eb.EventDatas)
        {
            StartCoroutine(LightColorRoutine(filteredLights, deltaTime, deltaAlpha, ebd));
        }

    }

    public override void KillLights()
    {
        base.KillLights();
        StopAllCoroutines();
        foreach (var manager in LightsManagersV3)
        {
            if (manager != null)
                manager.ChangeAlpha(0, 1, manager.ControllingLights);
        }
    }

    private IEnumerator LightColorRoutine(IEnumerable<LightingEvent> lights, float deltaTime, float deltaAlpha, 
        BeatmapLightColorEventData data)
    {
        float afterSeconds = atsc.GetSecondsFromBeat(data.AddedBeat);
        if (afterSeconds != 0.0f) yield return new WaitForSeconds(afterSeconds);
        var color = Color.white;
        if (data.Color == 1) color = Colors.BlueColor;
        else if (data.Color == 0) color = Colors.RedColor;
        color = color.Multiply(LightsManager.HDRIntensity);
        var brightness = data.Brightness;
        foreach (var light in lights)
        {
            light.UpdateTargetColor(color, 0);
            light.UpdateTargetAlpha(brightness, 0);
            if (deltaTime != 0.0f)
                yield return new WaitForSeconds(deltaTime);
            brightness += deltaAlpha;
        }
        yield return null;
    }

    public void LightRotationEventPassed(bool natural, int idx, BeatmapLightRotationEvent e)
    {
        Debug.Log("rotation passed at" + e.Time);
    }
}
