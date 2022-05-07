using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GridRenderingController : MonoBehaviour
{
    private static readonly int offset = Shader.PropertyToID("_Offset");
    private static readonly int gridSpacing = Shader.PropertyToID("_GridSpacing");
    private static readonly int mainColor = Shader.PropertyToID("_Color");

    private static MaterialPropertyBlock oneBeatPropertyBlock;
    private static MaterialPropertyBlock smallBeatPropertyBlock;
    private static MaterialPropertyBlock detailedBeatPropertyBlock;
    private static MaterialPropertyBlock preciseBeatPropertyBlock;
    private static MaterialPropertyBlock beatColorPropertyBlock;
    [SerializeField] private AudioTimeSyncController atsc;
    [SerializeField] private Renderer[] oneBeat;
    [SerializeField] private Renderer[] smallBeatSegment;
    [SerializeField] private Renderer[] detailedBeatSegment;
    [SerializeField] private Renderer[] preciseBeatSegment;
    [SerializeField] private Renderer[] opaqueGrids;
    [SerializeField] private Renderer[] transparentGrids;
    [SerializeField] private Renderer[] gridInterface;

    private readonly List<Renderer> allRenderers = new List<Renderer>();

    private void Awake()
    {
        oneBeatPropertyBlock = new MaterialPropertyBlock();
        smallBeatPropertyBlock = new MaterialPropertyBlock();
        detailedBeatPropertyBlock = new MaterialPropertyBlock();
        preciseBeatPropertyBlock = new MaterialPropertyBlock();
        beatColorPropertyBlock = new MaterialPropertyBlock();

        atsc.GridMeasureSnappingChanged += GridMeasureSnappingChanged;
        allRenderers.AddRange(oneBeat);
        allRenderers.AddRange(smallBeatSegment);
        allRenderers.AddRange(detailedBeatSegment);
        allRenderers.AddRange(preciseBeatSegment);
        Settings.NotifyBySettingName(nameof(Settings.TrackColor), UpdateGridColors);
        Settings.NotifyBySettingName(nameof(Settings.OneBeatWidth), UpdateOneBeat);
        Settings.NotifyBySettingName(nameof(Settings.OneBeatColor), UpdateOneBeatColor);
        Settings.NotifyBySettingName(nameof(Settings.GridInterfaceColor), UpdateGridInterfaceColor);

        UpdateOneBeat(Settings.Instance.OneBeatWidth);
        UpdateOneBeatColor(Settings.Instance.OneBeatColor);
    }

    private void OnDestroy()
    {
        atsc.GridMeasureSnappingChanged -= GridMeasureSnappingChanged;
        Settings.ClearSettingNotifications(nameof(Settings.TrackColor));
        Settings.ClearSettingNotifications(nameof(Settings.OneBeatWidth));
        Settings.ClearSettingNotifications(nameof(Settings.OneBeatColor));
        Settings.ClearSettingNotifications(nameof(Settings.GridInterfaceColor));
    }

    public void UpdateOffset(float offset)
    {
        Shader.SetGlobalFloat(GridRenderingController.offset, offset);
        if (!atsc.IsPlaying) GridMeasureSnappingChanged(atsc.GridMeasureSnapping);
    }

    private void UpdateOneBeat(object value)
    {
        foreach (var renderer in oneBeat)
            foreach (var mat in renderer.materials) mat.SetFloat("_GridThickness", (float)value);
    }

    private void UpdateOneBeatColor(object value)
    {
        foreach (var renderer in oneBeat)
            foreach (var mat in renderer.materials) mat.SetColor("_GridColour", (Color)value);
    }

    private void UpdateGridInterfaceColor(object value) {
        foreach (var renderer in gridInterface)
            foreach (var mat in renderer.materials)
            {
                Debug.Log($"We got this property maybe: {mat.HasProperty("_BASE_COLOR")}");
                mat.SetColor("_BASE_COLOR", (Color)value);
                mat.SetFloat("_OPACITY", ((Color)value).a);
            }
    }

    private void GridMeasureSnappingChanged(int snapping)
    {
        float gridSeparation = GetLowestDenominator(snapping);
        if (gridSeparation < 3) gridSeparation = 4;

        oneBeatPropertyBlock.SetFloat(gridSpacing, EditorScaleController.EditorScale / 4f);
        foreach (var g in oneBeat) g.SetPropertyBlock(oneBeatPropertyBlock);

        smallBeatPropertyBlock.SetFloat(gridSpacing, EditorScaleController.EditorScale / 4f / gridSeparation);
        foreach (var g in smallBeatSegment) g.SetPropertyBlock(smallBeatPropertyBlock);

        var useDetailedSegments = gridSeparation < snapping;
        gridSeparation *= GetLowestDenominator(Mathf.FloorToInt(snapping / gridSeparation));
        detailedBeatPropertyBlock.SetFloat(gridSpacing, EditorScaleController.EditorScale / 4f / gridSeparation);
        foreach (var g in detailedBeatSegment)
        {
            g.enabled = useDetailedSegments;
            g.SetPropertyBlock(detailedBeatPropertyBlock);
        }

        var usePreciseSegments = gridSeparation < snapping;
        gridSeparation *= GetLowestDenominator(Mathf.FloorToInt(snapping / gridSeparation));
        preciseBeatPropertyBlock.SetFloat(gridSpacing, EditorScaleController.EditorScale / 4f / gridSeparation);
        foreach (var g in preciseBeatSegment)
        {
            g.enabled = usePreciseSegments;
            g.SetPropertyBlock(preciseBeatPropertyBlock);
        }

        UpdateGridColors();
    }

    private void UpdateGridColors(object _ = null)
    {        
        var newColor = Settings.Instance.TrackColor;
        beatColorPropertyBlock.SetColor(mainColor, newColor);
        foreach (var g in transparentGrids)
        {
            g.SetPropertyBlock(beatColorPropertyBlock);
            g.enabled = !(newColor.a == 1f);
        }

        foreach (var g in opaqueGrids)
        {
            g.SetPropertyBlock(beatColorPropertyBlock);
            g.enabled = newColor.a == 1f;
        }
    }

    private int GetLowestDenominator(int a)
    {
        if (a <= 1) return 2;

        IEnumerable<int> factors = PrimeFactors(a);

        if (factors.Any()) return factors.Max();
        return a;
    }

    public static List<int> PrimeFactors(int a)
    {
        var retval = new List<int>();
        for (var b = 2; a > 1; b++)
        {
            while (a % b == 0)
            {
                a /= b;
                retval.Add(b);
            }
        }

        return retval;
    }
}
