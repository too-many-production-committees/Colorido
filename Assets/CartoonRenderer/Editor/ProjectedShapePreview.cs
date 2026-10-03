using UnityEditor;
using UnityEngine;

namespace CartoonProjection.Editor
{
    [InitializeOnLoad]
    public static class ProjectedShapePreview
    {
        static double next;
        static ProjectedShapePreview() { EditorApplication.update += Tick; }
        static void Tick()
        {
            if(EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup<next) return;
            next=EditorApplication.timeSinceStartup+.125;
            var feature=Find();
            if(feature && feature.isActive && feature.settings.enabled && feature.settings.projectedShapes)
            { EditorApplication.QueuePlayerLoopUpdate(); SceneView.RepaintAll(); }
        }
        static CartoonRendererFeature Find()
        {
            var assets=AssetDatabase.LoadAllAssetsAtPath("Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset");
            foreach(var a in assets) if(a is CartoonRendererFeature f) return f;
            return null;
        }
        [MenuItem("Tools/Cartoon Projection/Projection 2D/Enable")]
        static void Enable() { var f=Find(); if(!f)return; Undo.RecordObject(f,"Enable projected shapes"); f.settings.enabled=true; f.settings.projectedShapes=true; f.SetActive(true); EditorUtility.SetDirty(f); AssetDatabase.SaveAssets(); }
        [MenuItem("Tools/Cartoon Projection/Projection 2D/Disable Effect")]
        static void Disable() { var f=Find(); if(!f)return; Undo.RecordObject(f,"Disable projected shapes"); f.settings.enabled=false; EditorUtility.SetDirty(f); AssetDatabase.SaveAssets(); }
        [MenuItem("Tools/Cartoon Projection/Projection 2D/Report Statistics")]
        static void Report() => Debug.Log("Projection 2D: "+ProjectedShapePass.LastStatistics);
        [MenuItem("Tools/Cartoon Projection/Projection 2D/Validate Geometry")]
        static void Validate()
        {
            const int size=32;
            var colors=new Color32[size*size]; var ids=new Color32[colors.Length];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            { int i=y*size+x; bool inside=x>=8&&x<24&&y>=8&&y<24; colors[i]=inside?new Color32(200,60,40,255):new Color32(40,180,120,255); ids[i]=new Color32(1,0,0,255); }
            var r=ProjectedShapeBuilder.Build(colors,ids,size,size,32,1,1.5f);
            double area=0;
            for(int i=0;i<r.triangles.Length;i+=3) { var a=r.vertices[r.triangles[i]]; var b=r.vertices[r.triangles[i+1]]; var c=r.vertices[r.triangles[i+2]]; area+=System.Math.Abs((b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x))*.5; }
            if(r.regions!=2 || System.Math.Abs(area-1)>0.001 || r.simplifiedEdges>=r.sourceEdges) throw new System.Exception($"Projection test failed: regions={r.regions},area={area},edges={r.simplifiedEdges}");
            Debug.Log($"Projection geometry PASS: two original-color regions, hole and frame coverage area={area}, contour edges {r.sourceEdges} → {r.simplifiedEdges}. "+ProjectedShapePass.LastStatistics);
        }
    }
}
