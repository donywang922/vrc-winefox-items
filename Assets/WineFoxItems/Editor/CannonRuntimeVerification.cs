using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using nadena.dev.modular_avatar.core;
using BlackStartX.GestureManager;
using BlackStartX.GestureManager.Editor.Modules.Vrc3;
using Object=UnityEngine.Object;

namespace WineFoxItems.Editor
{
    // Runs the actual Gesture Manager driver callbacks, SDK constraints and raycasts in Play Mode.
    [InitializeOnLoad]
    public static class CannonRuntimeVerification
    {
        const string Key="WineFoxItems.RuntimeVerification";
        const string Root="Assets/WineFoxItems";
        static IEnumerator<Wait> routine;
        static Wait waiting;
        static int lastFrame=-1;
        static double deadline;
        static StringBuilder log;
        static GestureManager manager;
        static ModuleVrc3 module;
        static GameObject avatar;
        static SkinnedMeshRenderer renderer;
        static Transform itemRoot,item,muzzle;
        static VRCParentConstraint freeze;
        static ItemPrefabRepair.Region[] regions;
        static float maxHiddenArea;
        class Wait{public float until;public int frame;}
        static CannonRuntimeVerification(){EditorApplication.playModeStateChanged+=OnMode;}
        public static void Start()
        {
            ValidateWineFoxItems.Validate();
            StartDebug();
        }
        public static void StartDebug()
        {
            EditorSceneManager.OpenScene(Root+"/Examples/Tester-WineFoxItems.unity");
            SessionState.SetBool(Key,true);EditorApplication.isPlaying=true;
        }
        static void OnMode(PlayModeStateChange mode)
        {
            if(!SessionState.GetBool(Key,false)||mode!=PlayModeStateChange.EnteredPlayMode)return;
            log=new StringBuilder();routine=Run();waiting=null;lastFrame=-1;deadline=EditorApplication.timeSinceStartup+90;
            EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            try
            {
                if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Play Mode test timed out; frame="+Time.frameCount);
                if(Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
                if(waiting!=null&&(Time.time<waiting.until||Time.frameCount<waiting.frame))return;
                if(!routine.MoveNext()){Finish(null);return;}waiting=routine.Current;
            }
            catch(Exception error){Finish(error);}
        }
        static Wait Delay(float seconds)=>new Wait{until=Time.time+seconds,frame=Time.frameCount+2};
        static void Require(bool value,string message){if(!value)throw new Exception(message);}
        static void Set(string name,float value){Require(module.Params.ContainsKey(name),"Runtime parameter missing: "+name);module.Params[name].Set(module,value);}
        static float Get(string name)=>module.Params[name].FloatValue();
        static IEnumerator<Wait> Run()
        {
            yield return Delay(.2f);
            avatar=Object.FindObjectsOfType<VRCAvatarDescriptor>().Single().gameObject;
            if(avatar.GetComponentsInChildren<AvatarTagComponent>(true).Length>0)
            {
                nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(avatar);
                // Match NDMF Apply on Play: Rebind alone keeps stale pre-Bone-Proxy transform handles.
                var apply=typeof(nadena.dev.ndmf.AvatarProcessor).Assembly.GetType("nadena.dev.ndmf.ApplyOnPlay");
                apply.GetMethod("RecreateAnimators",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static).Invoke(null,new object[]{avatar.transform});
            }
            manager=Object.FindObjectOfType<GestureManager>();Require(manager,"Gesture Manager missing");
            module=new ModuleVrc3(avatar.GetComponent<VRCAvatarDescriptor>());manager.SetModule(module);
            Require(manager.Module==module,"Gesture Manager did not attach");
            renderer=avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh.blendShapeCount==35);
            itemRoot=avatar.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="ItemRoot");item=itemRoot.Find("Item");
            freeze=itemRoot.GetComponent<VRCParentConstraint>();muzzle=freeze.GetSource(0).SourceTransform;
            regions=JsonUtility.FromJson<ItemPrefabRepair.Regions>(File.ReadAllText(Root+"/Editor/ItemGeometryRegions.json")).regions;
            yield return Delay(.35f);
            Set("WineFoxItems/Item",0);Set("WineFoxItems/TriggerInput",0);
            yield return Delay(.2f);
            Require(!renderer.enabled,"Empty hand still renders geometry");
            CheckMesh(0,false);
            log.AppendLine("PASS: empty hand disables the only renderer.");
            foreach(var value in new[]{1,2,3,4,5,6,8,9,10,11,12})
            {
                Set("WineFoxItems/Item",value);yield return Delay(.2f);
                Require(renderer.enabled,"Selected item renderer is disabled: "+value);
                CheckMesh(value,false);
            }
            log.AppendLine("PASS: all 11 live menu selections have zero-area inactive item/stage triangles; ammo is zero-scale outside flight.");
            Set("WineFoxItems/Item",2);yield return Delay(.15f);
            Set("WineFoxItems/TriggerInput",1);yield return Delay(.05f);
            float armMid=renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"));
            Require(armMid>1&&armMid<99,"Mechanical arm trigger snaps instead of blending: "+armMid);
            yield return Delay(.35f);
            Require(Get("WineFoxItems/Trigger")>0.5f,"Menu trigger input did not reach the real parameter driver");
            Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"))>90,"Real menu trigger did not open mechanical arm");
            Set("WineFoxItems/TriggerInput",0);yield return Delay(.05f);
            float armCloseMid=renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"));
            Require(armCloseMid>1&&armCloseMid<99,"Mechanical arm release snaps instead of blending: "+armCloseMid);
            yield return Delay(.25f);
            Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"))<1,"Menu trigger did not close arm");
            log.AppendLine("PASS: actual menu TriggerInput -> SDK parameter driver -> mechanical arm open/close.");
            Set("WineFoxItems/Item",3);yield return Delay(.3f);
            Set("WineFoxItems/TriggerInput",1);yield return Delay(.1f);
            float staffMid=renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("物理手杖开"));
            Require(staffMid>1&&staffMid<99,"Staff trigger snaps instead of blending: "+staffMid);
            yield return Delay(.3f);
            Set("WineFoxItems/TriggerInput",0);yield return Delay(.1f);
            float staffCloseMid=renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("物理手杖开"));
            Require(staffCloseMid>1&&staffCloseMid<99,"Staff release snaps instead of blending: "+staffCloseMid);
            yield return Delay(.3f);
            log.AppendLine("PASS: real trigger/release produce intermediate blendshape weights: arm="+armMid.ToString("F1")+"/"+armCloseMid.ToString("F1")+", staff="+staffMid.ToString("F1")+"/"+staffCloseMid.ToString("F1")+"; original smooth transitions preserved.");
            avatar.transform.position+=Vector3.up*10;
            Set("WineFoxItems/Item",1);yield return Delay(.3f);
            var ray=itemRoot.GetComponent<VRCRaycast>();var rayData=new SerializedObject(ray);
            log.AppendLine("Ray collision mode="+rayData.FindProperty("collisionMode").enumDisplayNames[rayData.FindProperty("collisionMode").enumValueIndex]);
            var origin=muzzle.position;var forward=muzzle.up;
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Runtime test target";wall.transform.position=origin+forward*5;
            wall.transform.rotation=Quaternion.LookRotation(forward);wall.transform.localScale=new Vector3(2,2,.1f);Physics.SyncTransforms();
            Set("WineFoxItems/TriggerInput",1);yield return Delay(.075f);
            Require(Get("WineFoxItems/Trigger")>0.5f,"Cannon trigger driver did not run");
            Require(freeze.FreezeToWorld,"Cannon did not freeze its launch frame");
            if(item.localScale.x<=.5f)DumpRuntimeState();
            Require(item.localScale.x>.5f,"Cannon projectile remained hidden after trigger");
            CheckMesh(1,true);
            var launch=item.position;var frozen=itemRoot.position;var frozenRotation=itemRoot.rotation;
            Require(Vector3.Distance(frozen,origin)<.1f,"Cannon teleported away from its own muzzle: "+Vector3.Distance(frozen,origin));
            Require(Vector3.Distance(launch,origin)<2.5f,"Projectile did not spawn near muzzle: "+Vector3.Distance(launch,origin));
            Set("WineFoxItems/TriggerInput",0);
            avatar.transform.position+=new Vector3(2,1,0);
            avatar.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand).rotation*=Quaternion.Euler(0,35,0);
            yield return Delay(.12f);
            Require(Vector3.Distance(itemRoot.position,frozen)<.003f,"Frozen projectile frame follows avatar movement");
            Require(Quaternion.Angle(itemRoot.rotation,frozenRotation)<.2f,"Frozen projectile frame follows hand rotation");
            var travel=Vector3.Dot(item.position-launch,forward);
            log.AppendLine("Flight diagnostic: delta="+(item.position-launch).ToString("G9")+" expectedForward="+forward.ToString("G9")+" rootUp="+itemRoot.up.ToString("G9")+" rootScale="+itemRoot.lossyScale.ToString("G9")+" localPosition="+item.localPosition.ToString("G9"));
            if(travel<=.4f)DumpRuntimeState();
            Require(travel>.4f,"Projectile did not move forward: "+travel);
            log.AppendLine("PASS: real menu toggle fires a visible projectile from prefab muzzle; launch frame stays in world space after avatar movement; measured forward travel="+travel.ToString("F3")+" m.");
            bool hit=false;float until=Time.time+1.8f;
            while(Time.time<until)
            {
                if(Get("WineFoxItems/Hit")>.5f){hit=true;break;}
                yield return Delay(.015f);
            }
            Require(hit,"Projectile never hit the raycast target; root="+itemRoot.position+" item="+item.position+" ray="+ray.enabled);
            log.AppendLine("PASS: SDK raycast/contact chain reports a hit on a 5 m test target.");
            yield return Delay(1.7f);
            Require(item.localScale.sqrMagnitude<1e-10f,"Ammo remains rendered after shot recovery");
            Require(!freeze.FreezeToWorld,"Cannon launch frame did not unfreeze on recovery");
            CheckMesh(1,false);
            log.AppendLine("PASS: firing/impact completes and resets to following muzzle with fully hidden ammo.");
            Object.Destroy(wall);
            foreach(int fruit in Enumerable.Range(0,6))
            {
                Set("WineFoxItems/Item",1);yield return Delay(.15f);
                Set("WineFoxItems/Fruit",fruit);yield return Delay(.03f);
                Set("WineFoxItems/TriggerInput",1);yield return Delay(.10f);
                Require(freeze.FreezeToWorld,"Repeated shot failed to freeze: "+fruit);
                CheckMesh(1,true);
                Set("WineFoxItems/TriggerInput",0);Set("WineFoxItems/Item",5);yield return Delay(.12f);
                Require(!freeze.FreezeToWorld,"Item switch did not cancel flight: "+fruit);
                CheckMesh(5,false);
            }
            log.AppendLine("PASS: all six projectile variants render alone; switching items during each shot cancels flight, unfreezes and hides ammo completely.");
            Set("WineFoxItems/Item",9);Set("WineFoxItems/Eating",1);yield return Delay(.65f);
            CheckMesh(9,false);Set("WineFoxItems/Eating",0);
            Set("WineFoxItems/Item",0);yield return Delay(.2f);Require(!renderer.enabled,"Renderer not hidden on deselection");CheckMesh(0,false);
            log.AppendLine("PASS: food stages and final deselection leave no hidden-item remnants; maximum inactive triangle area="+maxHiddenArea.ToString("E3")+" m².");
        }
        static void CheckMesh(int itemValue,bool flight)
        {
            var mesh=renderer.sharedMesh;
            string expected=itemValue==1?"I土豆加农炮":itemValue==2?"I伸缩机械手":itemValue==3?"I物理手杖":itemValue==4?"I对称之杖":itemValue==5?"I扳手":itemValue==6?"I纸棍":itemValue==8?"I建筑工茶饮":itemValue==9?"I蜜渍苹果":itemValue==10?"I巧克力包层浆果":itemValue==11?"I甜甜卷":"I蛋糕";
            var baked=new Mesh();renderer.BakeMesh(baked,true);
            try
            {
                var v=baked.vertices.Select(p=>renderer.transform.TransformPoint(p)).ToArray();var triangles=baked.triangles;float selectedArea=0,ammoArea=0;
                foreach(var region in regions)
                {
                    float weight=renderer.GetBlendShapeWeight(mesh.GetBlendShapeIndex(region.shape));bool ammo=region.shape.StartsWith("C");
                    bool hidden=weight<.001f||(ammo&&!flight);float area=0;
                    foreach(int t in region.triangles)area=Mathf.Max(area,Vector3.Cross(v[triangles[t*3+1]]-v[triangles[t*3]],v[triangles[t*3+2]]-v[triangles[t*3]]).magnitude*.5f);
                    if(hidden)
                    {
                        maxHiddenArea=Mathf.Max(maxHiddenArea,area);
                        if(area>=1e-9f)
                        {
                            var data=new StringBuilder();data.AppendLine("mesh="+mesh.name+" path="+AssetDatabase.GetAssetPath(mesh)+" weight="+weight);
                            var source=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"/Meshes/FinalMergedItems.asset");
                            foreach(int t in region.triangles.Take(2))foreach(int j in new[]{triangles[t*3],triangles[t*3+1],triangles[t*3+2]})data.AppendLine("v="+j+" runtimeBase="+mesh.vertices[j].ToString("G9")+" savedBase="+source.vertices[j].ToString("G9")+" baked="+v[j].ToString("G9")+" bone="+mesh.boneWeights[j].boneIndex0);
                            File.WriteAllText("Artifacts/hidden-geometry-diagnostic.txt",data.ToString());
                        }
                        Require(area<1e-9f,"Inactive geometry is not fully hidden: "+region.shape+" area="+area.ToString("E6")+" item="+itemValue);
                    }
                    else if(!ammo){Require(itemValue>0&&region.shape.StartsWith(expected),"Unselected handheld shape expanded: "+region.shape);selectedArea=Mathf.Max(selectedArea,area);}
                    else ammoArea=Mathf.Max(ammoArea,area);
                }
                if(itemValue>0)Require(selectedArea>1e-6f,"Selected item has no rendered surface");
                if(flight)Require(ammoArea>1e-6f,"Fired ammo has no rendered surface");
                else Require(item.localScale.sqrMagnitude<1e-10f,"Inactive ammo still has nonzero scale");
            }
            finally{Object.DestroyImmediate(baked);}
        }
        static void Finish(Exception error)
        {
            EditorApplication.update-=Tick;SessionState.SetBool(Key,false);
            if(error!=null){log.AppendLine("FAIL: "+error);Debug.LogException(error);}
            else log.AppendLine("WINEFOX_RUNTIME_VERIFICATION_OK (real Play Mode, Gesture Manager, native SDK constraints/raycasts)");
            File.WriteAllText("Artifacts/runtime-verification.txt",log.ToString());Debug.Log(log.ToString());
            if(manager)manager.UnlinkModule();
            EditorApplication.Exit(error==null?0:1);
        }
        static void DumpRuntimeState()
        {
            var detail=new StringBuilder();detail.AppendLine("Item="+Get("WineFoxItems/Item")+" Fruit="+Get("WineFoxItems/Fruit")+" Hit="+Get("WineFoxItems/Hit")+" scale="+item.localScale.ToString("G9")+" path="+AnimationUtility.CalculateTransformPath(item,avatar.transform));
            var fxLayer=avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers.First(l=>l.type==VRCAvatarDescriptor.AnimLayerType.FX);
            foreach(var mask in new[]{fxLayer.mask,(fxLayer.animatorController as AnimatorController)?.layers[0].avatarMask})
            {
                detail.AppendLine("mask="+(mask?mask.name:"NULL"));
                if(mask)for(int m=0;m<mask.transformCount;m++)detail.AppendLine(" maskTransform "+mask.GetTransformPath(m)+"="+mask.GetTransformActive(m));
            }
            var graph=(PlayableGraph)typeof(ModuleVrc3).GetField("_playableGraph",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(module);
            var seen=new HashSet<PlayableHandle>();Action<Playable> dump=null;dump=p=>
            {
                if(!p.IsValid()||!seen.Add(p.GetHandle()))return;
                if(p.GetPlayableType()==typeof(AnimatorControllerPlayable))
                {
                    var ctrl=(AnimatorControllerPlayable)p;
                    for(int l=0;l<ctrl.GetLayerCount();l++)if(ctrl.GetLayerName(l).StartsWith("Wine Fox"))
                    {
                        var info=ctrl.GetCurrentAnimatorStateInfo(l);detail.AppendLine(ctrl.GetLayerName(l)+" time="+info.normalizedTime);
                        foreach(var c in ctrl.GetCurrentAnimatorClipInfo(l))
                        {
                            detail.AppendLine(" clip="+c.clip.name+" weight="+c.weight);
                            foreach(var b in AnimationUtility.GetCurveBindings(c.clip).Where(b=>b.path.EndsWith("ItemRoot/Item")&&b.type==typeof(Transform)))detail.AppendLine("  "+b.path+" "+b.propertyName+"="+AnimationUtility.GetEditorCurve(c.clip,b).Evaluate(info.normalizedTime*c.clip.length));
                        }
                    }
                }
                for(int k=0;k<p.GetInputCount();k++)dump(p.GetInput(k));
            };
            for(int k=0;k<graph.GetRootPlayableCount();k++)dump(graph.GetRootPlayable(k));File.WriteAllText("Artifacts/cannon-flight-diagnostic.txt",detail.ToString());
        }
    }
}
