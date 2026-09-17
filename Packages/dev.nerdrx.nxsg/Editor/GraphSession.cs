using UnityEngine;

namespace NXSG.Editor
{
    // Unity serializes the edit snapshot for Undo and domain reload. Canvas elements are disposable.
    public sealed class GraphSession : ScriptableObject
    {
        public string json;
    }
}
