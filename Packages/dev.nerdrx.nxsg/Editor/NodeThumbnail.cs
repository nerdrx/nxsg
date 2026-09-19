using UnityEditor;
using UnityEngine.UIElements;

namespace NXSG.Editor
{
    sealed class NodeThumbnail
    {
        public string NodeId, Port, Hash;
        public bool Enabled, Pending;
        public GraphPreview Preview;
        public UnityEditor.Editor Editor;
        public VisualElement Host;
        public Label Error;
        public Toggle Toggle;
    }
}
