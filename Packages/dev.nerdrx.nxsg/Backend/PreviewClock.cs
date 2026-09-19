namespace NXSG.Backend
{
    internal static class PreviewClock
    {
        internal const string Properties = "[HideInInspector] _NXSG_PreviewClock (\"Preview clock\", Float) = 0\n[HideInInspector] _NXSG_PreviewTime (\"Preview seconds\", Float) = 0";
        internal const string Hlsl = "float _NXSG_PreviewClock, _NXSG_PreviewTime; float NXSG_Time() { return _NXSG_PreviewClock > .5 ? _NXSG_PreviewTime : _Time.y; }";
    }
}
