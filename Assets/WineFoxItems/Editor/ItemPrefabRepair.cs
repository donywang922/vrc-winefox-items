using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Constraint.Components;
using Object=UnityEngine.Object;

namespace WineFoxItems.Editor
{
    public static class ItemPrefabRepair
    {
        const string Root="Assets/WineFoxItems";
        public const string Locator="RightHandAnchor/GripAdjust/RightHandRig/RightHandLocator";
        const string RawMesh=Root+"/Editor/SourceMergedItems.asset";
        [Serializable] public class Regions { public Region[] regions; }
        [Serializable] public class Region { public string shape;public int[] vertices,triangles; }

        [MenuItem("Tools/Wine Fox Items/Repair cannon and hidden geometry")]
        public static void Repair(){Apply(false);}
        public static void RepairFreshExtraction(){Apply(true);}
        static void Apply(bool fresh)
        {
            var log=new StringBuilder();
            var meshPath=Root+"/Meshes/FinalMergedItems.asset";
            var current=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if(fresh&&AssetDatabase.LoadAssetAtPath<Mesh>(RawMesh)){
                AssetDatabase.SaveAssets();File.Copy(meshPath,RawMesh,true);AssetDatabase.ImportAsset(RawMesh,ImportAssetOptions.ForceUpdate);
            }
            else if(!AssetDatabase.LoadAssetAtPath<Mesh>(RawMesh))AssetDatabase.CopyAsset(meshPath,RawMesh);
            var raw=AssetDatabase.LoadAssetAtPath<Mesh>(RawMesh);
            var corrected=FullyHiddenMesh.Build(raw,log);
            const string temporaryMesh=Root+"/Editor/_HiddenMeshWrite.asset";
            AssetDatabase.CreateAsset(corrected,temporaryMesh);AssetDatabase.SaveAssets();
            File.Copy(temporaryMesh,meshPath,true);AssetDatabase.ImportAsset(meshPath,ImportAssetOptions.ForceUpdate);AssetDatabase.DeleteAsset(temporaryMesh);
            current=AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var savedVertices=current.vertices;var savedTriangles=current.triangles;
            for(int t=0;t<savedTriangles.Length;t+=3)if(savedVertices[savedTriangles[t]]!=savedVertices[savedTriangles[t+1]]||savedVertices[savedTriangles[t]]!=savedVertices[savedTriangles[t+2]])throw new Exception("Saved hidden basis did not persist: "+t/3);
            log.AppendLine("Reloaded serialized mesh: every hidden bind-space triangle has coincident vertices.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/WineFoxItemsFX.controller");
            var prefab=PrefabUtility.LoadPrefabContents(Root+"/WineFoxItems.prefab");
            try
            {
                var locator=prefab.transform.Find(Locator);
                var itemRoot=locator.Find("ItemRoot");
                var muzzle=locator.Find("CannonMuzzleAnchor");
                if(!muzzle)
                {
                    muzzle=new GameObject("CannonMuzzleAnchor").transform;muzzle.SetParent(locator,false);
                    muzzle.localPosition=itemRoot.localPosition;muzzle.localRotation=itemRoot.localRotation;muzzle.localScale=Vector3.one;
                }
                var constraint=itemRoot.GetComponent<VRCParentConstraint>();
                while(constraint.SourceCount>0)constraint.RemoveSource(0);
                constraint.AddSource(new VRCConstraintSource(muzzle,1){ParentPositionOffset=Vector3.zero,ParentRotationOffset=Vector3.zero});
                constraint.TargetTransform=null;constraint.GlobalWeight=1;constraint.IsActive=true;constraint.Locked=true;
                constraint.FreezeToWorld=false;constraint.RebakeOffsetsWhenUnfrozen=false;
                constraint.PositionAtRest=itemRoot.localPosition;constraint.RotationAtRest=itemRoot.localEulerAngles;
                constraint.ApplyConfigurationChanges();
                foreach(var name in new[]{"World Constraint","Reset Target"}){var old=locator.Find(name);if(old)Object.DestroyImmediate(old.gameObject);}
                var renderer=prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                renderer.sharedMesh=current;renderer.enabled=false;renderer.updateWhenOffscreen=true;renderer.localBounds=current.bounds;
                locator.Find("ItemRoot/Item").localScale=Vector3.zero;
                for(int i=0;i<current.blendShapeCount;i++)renderer.SetBlendShapeWeight(i,0);
                foreach(var path in AssetDatabase.FindAssets("t:AnimationClip",new[]{Root+"/Animations"}).Select(AssetDatabase.GUIDToAssetPath))
                {
                    var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    PatchClip(clip,log);
                }
                PatchInterrupts(controller);ResetStaffAxes(controller);
                PrefabUtility.SaveAsPrefabAsset(prefab,Root+"/WineFoxItems.prefab");
                log.AppendLine("Cannon ItemRoot: native VRCParentConstraint -> prefab-local CannonMuzzleAnchor, zero source offsets; FreezeToWorld animated for each shot.");
                log.AppendLine("Removed original world-reference hierarchy and absolute scene offsets. Empty hand disables renderer; projectile scale is exactly zero outside flight/shatter.");
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Artifacts");File.WriteAllText("Artifacts/cannon-hidden-repair.txt",log.ToString());
            Debug.Log("WINEFOX_CANNON_HIDDEN_REPAIR_OK\n"+log);
        }
        static void Set(AnimationClip clip,string path,Type type,string property,float value)
        {AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(path,type,property),AnimationCurve.Constant(0,Mathf.Max(clip.length,1f/60),value));}
        static void PatchClip(AnimationClip clip,StringBuilder log)
        {
            var bindings=AnimationUtility.GetCurveBindings(clip);
            var oldBinding=bindings.FirstOrDefault(b=>b.path==Locator+"/World Constraint/Container"&&b.propertyName=="IsActive");
            if(!string.IsNullOrEmpty(oldBinding.path))
            {
                var old=AnimationUtility.GetEditorCurve(clip,oldBinding);AnimationUtility.SetEditorCurve(clip,oldBinding,null);
                var keys=old.keys;for(int k=0;k<keys.Length;k++){keys[k].value=1-keys[k].value;keys[k].inTangent=-keys[k].inTangent;keys[k].outTangent=-keys[k].outTangent;}
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve(Locator+"/ItemRoot",typeof(VRCParentConstraint),"FreezeToWorld"),new AnimationCurve(keys));
            }
            bool selects=bindings.Any(b=>b.path=="ItemsMesh"&&b.propertyName.StartsWith("blendShape.I"));
            if(!selects)return;
            bool flight=clip.name=="炮_发射"||clip.name=="炮_物品破碎";
            bool empty=clip.name=="空手";
            Set(clip,"ItemsMesh",typeof(SkinnedMeshRenderer),"m_Enabled",empty?0:1);
            Set(clip,Locator+"/ItemRoot",typeof(VRCParentConstraint),"IsActive",1);
            if(!flight)
            {
                Set(clip,Locator+"/ItemRoot",typeof(VRCParentConstraint),"FreezeToWorld",0);
                foreach(var axis in new[]{"x","y","z"})Set(clip,Locator+"/ItemRoot/Item",typeof(Transform),"m_LocalScale."+axis,0);
            }
            else if(clip.name=="炮_物品破碎")Set(clip,Locator+"/ItemRoot",typeof(VRCParentConstraint),"FreezeToWorld",1);
            if(clip.name=="炮_发射")
            {
                // Let VRCRaycast place its result before enabling the contact receiver.
                // Otherwise its stale idle position can report a hit on the first frame.
                var ready = new AnimationCurve(new Keyframe(0,0),new Keyframe(.05f,1));
                for(int k=0;k<ready.length;k++){
                    AnimationUtility.SetKeyLeftTangentMode(ready,k,AnimationUtility.TangentMode.Constant);
                    AnimationUtility.SetKeyRightTangentMode(ready,k,AnimationUtility.TangentMode.Constant);
                }
                AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("raycast_cannon",typeof(VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver),"m_Enabled"),ready);
            }
            if(!flight)
            {
                var rayType=typeof(VRC.SDK3.Avatars.Components.VRCRaycast);
                Set(clip,Locator+"/ItemRoot",rayType,"m_Enabled",0);
                Set(clip,"raycast_cannon",typeof(VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver),"m_Enabled",0);
            }
            EditorUtility.SetDirty(clip);
        }
        static void PatchInterrupts(AnimatorController controller)
        {
            var sm=controller.layers.Single(l=>l.name=="Wine Fox / 物品").stateMachine;
            var states=States(sm).ToArray();
            foreach(var state in states)
            {
                if(!(state.motion is AnimationClip clip))continue;
                var visible=AnimationUtility.GetCurveBindings(clip).Where(b=>b.path=="ItemsMesh"&&b.propertyName.StartsWith("blendShape.I")&&AnimationUtility.GetEditorCurve(clip,b).keys.Any(k=>k.value>50)).Select(b=>b.propertyName.Substring(12)).ToArray();
                int item=visible.Any(n=>n.StartsWith("土豆加农炮"))?1:visible.Any(n=>n.StartsWith("伸缩机械手"))?2:visible.Any(n=>n.StartsWith("物理手杖"))?3:visible.Any(n=>n.StartsWith("对称之杖"))?4:visible.Any(n=>n.StartsWith("扳手"))?5:visible.Any(n=>n.StartsWith("纸棍"))?6:visible.Any(n=>n.StartsWith("建筑工茶饮"))?8:visible.Any(n=>n.StartsWith("蜜渍苹果"))?9:visible.Any(n=>n.StartsWith("巧克力包层浆果"))?10:visible.Any(n=>n.StartsWith("甜甜卷"))?11:visible.Any(n=>n.StartsWith("蛋糕"))?12:0;
                if(item>0&&!state.transitions.Any(t=>t.conditions.Any(c=>c.parameter=="WineFoxItems/Item"&&c.mode==AnimatorConditionMode.NotEqual&&Mathf.Approximately(c.threshold,item))))
                {
                    var exit=state.AddExitTransition();exit.hasExitTime=false;exit.duration=0;exit.AddCondition(AnimatorConditionMode.NotEqual,item,"WineFoxItems/Item");EditorUtility.SetDirty(exit);
                }
                // Preserve source trigger blending. Only newly added item-change exits
                // are immediate, so cancelling a shot never leaves hidden ammo visible.
                EditorUtility.SetDirty(state);
            }
            EditorUtility.SetDirty(controller);
        }
        static void ResetStaffAxes(AnimatorController controller)
        {
            foreach(var pair in new[]{new{layer="Wine Fox / 手杖1",axis="x"},new{layer="Wine Fox / 手杖2",axis="y"}})
            {
                var path=Root+"/Animations/StaffAxisIdle_"+pair.axis+".anim";var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if(!clip){clip=new AnimationClip{name="Staff axis idle "+pair.axis};AssetDatabase.CreateAsset(clip,path);}else clip.ClearCurves();
                foreach(var sign in new[]{"","-"})Set(clip,"ItemsMesh",typeof(SkinnedMeshRenderer),"blendShape.物理手杖L"+sign+pair.axis,0);
                foreach(var state in States(controller.layers.Single(l=>l.name==pair.layer).stateMachine).Where(s=>s.name=="New State")){state.motion=clip;EditorUtility.SetDirty(state);}
                EditorUtility.SetDirty(clip);
            }
        }
        static IEnumerable<AnimatorState> States(AnimatorStateMachine sm)
        {foreach(var s in sm.states)yield return s.state;foreach(var child in sm.stateMachines)foreach(var s in States(child.stateMachine))yield return s;}
    }
}
