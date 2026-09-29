using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        const float GraphGridSpacing = 24f;
        const string GridSnappingPreference = "NXSG.GraphWindow.GridSnapping";

        bool GridSnappingEnabled
        {
            get => EditorPrefs.GetBool(GridSnappingPreference, false);
            set => EditorPrefs.SetBool(GridSnappingPreference, value);
        }

        internal static Vector2 SnapDragDelta(Vector2 anchor, Vector2 delta, bool enabled, bool bypass)
        {
            if (!enabled || bypass) return delta;
            var target = anchor + delta;
            var snapped = new Vector2(
                Mathf.Round(target.x / GraphGridSpacing) * GraphGridSpacing,
                Mathf.Round(target.y / GraphGridSpacing) * GraphGridSpacing);
            return snapped - anchor;
        }
    }
}
