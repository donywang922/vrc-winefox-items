using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;

namespace WineFoxItems.Editor
{
    public static class WineFoxItemBuilder
    {
        public static void Inspect()
        {
            AssetDatabase.ImportPackage("Assets/tester.unitypackage", false);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.OpenScene("Assets/WineFoxSource/main.unity");
            var s = new StringBuilder();
            foreach (var root in scene.GetRootGameObjects()) Dump(root.transform, s, "");
            foreach (var file in new[]{"Assets/WineFoxSource/酒狐合并.controller", "Assets/WineFoxSource/动作.controller"})
            {
                var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(file);
                s.AppendLine("CONTROLLER " + file);
                foreach (var p in ctrl.parameters) s.AppendLine(" PARAM " + p.name + " " + p.type + " default="+p.defaultInt+"/"+p.defaultFloat+"/"+p.defaultBool);
                foreach(var layer in ctrl.layers)
                {
                    s.AppendLine(" LAYER " + layer.name + " mask="+layer.avatarMask);
                    DumpStates(layer.stateMachine,s,"  ");
                }
            }
            File.WriteAllText("Artifacts/source-inspection.txt", s.ToString());
            Debug.Log("WINEFOX_INSPECT_OK");
        }
        public static void InspectDetails()
        {
            var s=new StringBuilder();
            foreach(var path in new[]{"Assets/WineFoxSource/StandaloneItems.fbx","Assets/WineFoxSource/酒狐合并.fbx","Assets/tester merged.prefab"})
            {
                var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);
                s.AppendLine("ASSET "+path);
                if(!asset){s.AppendLine("MISSING");continue;}
                foreach(var r in asset.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var m=r.sharedMesh; s.AppendLine("MESH "+r.name+" vertices="+m.vertexCount+" triangles="+m.triangles.Length/3+" bounds="+m.bounds+" scale="+r.transform.lossyScale);
                    s.AppendLine("BONES "+string.Join(",",r.bones.Select(b=>b?b.name:"NULL")));
                    for(int i=0;i<m.blendShapeCount;i++)
                    {
                        var d=new Vector3[m.vertexCount];m.GetBlendShapeFrameVertices(i,0,d,null,null);
                        var ids=Enumerable.Range(0,d.Length).Where(j=>d[j].sqrMagnitude>1e-16f).ToArray();
                        var b=new Bounds();if(ids.Length>0){b=new Bounds(d[ids[0]],Vector3.zero);foreach(var id in ids)b.Encapsulate(d[id]);}
                        var uv=m.uv;var uvset=ids.Select(j=>uv[j].ToString("F5")).Distinct().OrderBy(v=>v).ToArray();
                        s.AppendLine("SHAPE "+m.GetBlendShapeName(i)+" changed="+ids.Length+" deltaBounds="+b+" uv="+string.Join(";",uvset));
                    }
                    var w=m.boneWeights;foreach(var g in Enumerable.Range(0,w.Length).GroupBy(i=>w[i].boneIndex0))s.AppendLine("WEIGHT "+r.bones[g.Key]?.name+" count="+g.Count());
                }
                var a=asset.GetComponent<Animator>();if(a && a.isHuman)foreach(var b in new[]{HumanBodyBones.RightHand,HumanBodyBones.LeftHand,HumanBodyBones.Head})s.AppendLine("HUMAN "+b+" "+AnimationUtility.CalculateTransformPath(a.GetBoneTransform(b),asset.transform)+" scale="+a.GetBoneTransform(b).lossyScale);
            }
            var scene=EditorSceneManager.OpenScene("Assets/WineFoxSource/main.unity");
            var root=scene.GetRootGameObjects().First(g=>g.GetComponent<Animator>());
            foreach(var c in root.GetComponentsInChildren<Component>(true).Where(c=>c && (c.transform.name=="RightHandLocator" || c.GetType().Name=="VRCRaycast" || c.transform.name=="ItemRoot" || c.transform.name.Contains("World") || c.transform.name=="Container")))
            {
                if(c is Transform)continue;
                s.AppendLine("COMPONENT "+AnimationUtility.CalculateTransformPath(c.transform,root.transform)+" "+c.GetType().Name);
                var so=new SerializedObject(c);var p=so.GetIterator();while(p.Next(true))
                {
                    if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue)s.AppendLine("REF "+p.propertyPath+"="+(p.objectReferenceValue is Component co?AnimationUtility.CalculateTransformPath(co.transform,root.transform):AssetDatabase.GetAssetPath(p.objectReferenceValue)));
                    if(p.propertyType==SerializedPropertyType.String)s.AppendLine("STRING "+p.propertyPath+"="+p.stringValue);
                }
                s.AppendLine("JSON "+EditorJsonUtility.ToJson(c));
            }
            File.WriteAllText("Artifacts/item-details.txt",s.ToString());
            Debug.Log("WINEFOX_DETAILS_OK");
        }
        static void DumpStates(AnimatorStateMachine sm,StringBuilder s,string indent)
        {
            foreach(var state in sm.states)
            {
                s.AppendLine(indent+"STATE "+state.state.name+" motion="+AssetDatabase.GetAssetPath(state.state.motion)+" wd="+state.state.writeDefaultValues);
                foreach(var b in state.state.behaviours) s.AppendLine(indent+" BEHAVIOR "+b.GetType().FullName+" "+EditorJsonUtility.ToJson(b));
                foreach(var tr in state.state.transitions) s.AppendLine(indent+" -> "+tr.destinationState?.name+" exit="+tr.hasExitTime+" time="+tr.exitTime+" conditions="+string.Join(",",tr.conditions.Select(c=>c.parameter+" "+c.mode+" "+c.threshold)));
            }
            foreach(var c in sm.stateMachines) DumpStates(c.stateMachine,s,indent+" ");
        }
        static void Dump(Transform t,StringBuilder s,string indent)
        {
            s.AppendLine(indent+t.name+" pos="+t.localPosition.ToString("F6")+" rot="+t.localEulerAngles.ToString("F3")+" scale="+t.localScale.ToString("F4")+" active="+t.gameObject.activeSelf);
            foreach(var c in t.GetComponents<Component>())
            {
                if(c==null){s.AppendLine(indent+" MISSING SCRIPT");continue;}
                if(c is Transform)continue;
                s.AppendLine(indent+" COMPONENT "+c.GetType().FullName);
                if(c is SkinnedMeshRenderer r)
                {
                    s.AppendLine(indent+" MESH "+r.sharedMesh.name+" verts="+r.sharedMesh.vertexCount+" tris="+r.sharedMesh.triangles.Length/3);
                    s.AppendLine(indent+" BONES "+string.Join(",",r.bones.Select(b=>b?b.name:"NULL")));
                    s.AppendLine(indent+" SHAPES "+string.Join(",",Enumerable.Range(0,r.sharedMesh.blendShapeCount).Select(i=>r.sharedMesh.GetBlendShapeName(i))));
                }
                else if(c is MonoBehaviour || c is UnityEngine.Animations.IConstraint) s.AppendLine(indent+EditorJsonUtility.ToJson(c));
            }
            foreach(Transform child in t)Dump(child,s,indent+"  ");
        }
    }
}
