using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    /// <summary>Real editor-window smoke. Run with -executeMethod NXSG.Editor.CreatorPlaygroundSmoke.Run.</summary>
    public static class CreatorPlaygroundSmoke
    {
        private static int _ticks;
        private static MaterialPlayground _window;
        private static Material _source;
        private static object _oldSlotImage;

        public static void Run()
        {
            try
            {
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("Unlit/Color unavailable.");
                _source = new Material(shader) { name = "NXSG creator smoke source", color = Color.red };
                var graph=NXSG.Core.GraphSamples.CreateDefault();
                using(var original=GraphPreview.Create(graph,_source)){MaterialPlayground.ShowGraph(graph,original.Material);}
                // Closing the graph preview above must not invalidate the playground shader.
                _window = Resources.FindObjectsOfTypeAll<MaterialPlayground>()[0];
                _window.position=new Rect(50,50,850,800);
                _window.Repaint();
                EditorApplication.delayCall += WaitForRender;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void WaitForRender()
        {
            try
            {
                if (++_ticks < 4) { _window.Repaint(); EditorApplication.delayCall += WaitForRender; return; }
                var image = (Texture)GetField("_lastImage");
                if (image == null || image.width == 0 || image.height == 0) throw new InvalidOperationException("Preview did not render an image.");
                InvokeCapture("A");
                var slots = (IList)GetField("_slots");
                if (slots.Count != 1) throw new InvalidOperationException("Snapshot A was not retained.");
                var old = slots[0];
                _oldSlotImage = old.GetType().GetField("Image", BindingFlags.Public | BindingFlags.Instance).GetValue(old);
                InvokeCapture("A");
                if (slots.Count != 1 || !IsDestroyed(_oldSlotImage)) throw new InvalidOperationException("Snapshot replacement leaked retained resources.");
                if (_source.color != Color.red) throw new InvalidOperationException("Preview changed source material.");
                var capture=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("gamescopectl","screenshot /tmp/nxsg-creator-playground.png"){UseShellExecute=false});
                capture.WaitForExit(5000);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(MaterialPlayground).GetMethod("SetMaterial",flags).Invoke(_window,new object[]{_source});
                if(GetField("_lastImage")!=null)throw new Exception("Material switch retained stale rendered image");
                InvokeCapture("B");if(slots.Count!=1)throw new Exception("Snapshot captured previous material before fresh repaint");
                typeof(MaterialPlayground).GetField("_motionVelocity",flags).SetValue(_window,new Vector3(4,3,2));
                typeof(MaterialPlayground).GetField("_time",flags).SetValue(_window,7f);
                typeof(MaterialPlayground).GetMethod("ResetControls",flags).Invoke(_window,null);
                if((Vector3)GetField("_motionVelocity")!=Vector3.zero || (float)GetField("_time")!=0 || slots.Count!=1)throw new Exception("Reset controls failed or discarded comparison snapshot");
                _window.Close();
                if (_window != null) throw new InvalidOperationException("Preview window did not close.");
                UnityEngine.Object.DestroyImmediate(_source);
                Debug.Log("NXSG creator playground smoke passed.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static object GetField(string name) => typeof(MaterialPlayground).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(_window);
        private static void InvokeCapture(string name) => typeof(MaterialPlayground).GetMethod("Capture", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(_window, new object[] { name });
        private static bool IsDestroyed(object value) => value is UnityEngine.Object unityObject && unityObject == null;

        private static void Fail(Exception exception)
        {
            if (_window != null) _window.Close();
            if (_source != null) UnityEngine.Object.DestroyImmediate(_source);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}
