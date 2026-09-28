#if UNITY_EDITOR
using NXSG.Core;
using UnityEngine;

namespace NXSG.Editor
{
    public sealed partial class GraphWindow
    {
        void AddIndirectLightingControls(GraphNode node)
        {
            AddBoundedNumber(node,"bentStrength","Bent normal influence",0,1,1,"bentStrength","Connect a decoded tangent-space bent normal to Bent Normal. Guides ambient light and PBR reflection occlusion; it does not cast shadows between body parts.");
            AddBoundedNumber(node,"lightDirectionStrength","Light direction override",0,1,0,"lightDirectionStrength","Blend from each real light direction toward the chosen direction. Distance, cookies and cast shadows still come from the real light.");
            AddDirectionField(node,"lightDirection","Direction toward light",Vector3.up);
            AddIndexedChoice(node,"lightDirectionSpace","Direction space",new[]{"World","Object"});
        }
    }
}
#endif
