// Copy Tests/Portable/NegativeLiteralChecks.cs into the fixture alongside this test.
using System;
using System.Linq;
using NXSG.Backend;
using UnityEditor;
using UnityEngine;
public static class NegativeLiteralSmoke
{
    public static void Run()
    {
        try
        {
            ShaderUtil.allowAsyncCompilation=false;
            int count=0;
            foreach(var graph in NegativeLiteralChecks.Fixtures())
            {
                var emission=ShaderEmitter.Emit(graph);
                if(!emission.Succeeded)throw new Exception(graph.GraphId+": "+string.Join(";",emission.Diagnostics.Select(d=>d.Message)));
                var shader=ShaderUtil.CreateShaderAsset(emission.ShaderSource,true);
                var material=new Material(shader);
                try
                {
                    for(int pass=0;pass<material.passCount;pass++)material.SetPass(pass);
                    if(!shader.isSupported || ShaderUtil.ShaderHasError(shader))throw new Exception(graph.GraphId+": "+string.Join(";",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message)));
                    count++;
                }
                finally{UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(shader);}
            }
            Debug.Log("NXSG NEGATIVE LITERALS PASSED: "+count+" graphs");EditorApplication.Exit(0);
        }
        catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
