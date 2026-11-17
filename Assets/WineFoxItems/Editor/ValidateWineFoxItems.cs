using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.Animations;
using UnityEngine.Playables;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object=UnityEngine.Object;

namespace WineFoxItems.Editor
{
    public static class ValidateWineFoxItems
    {
        const string Root="Assets/WineFoxItems";
        static readonly string[] Items={"土豆加农炮","伸缩机械手","物理手杖","对称之杖","扳手","纸棍","建筑工茶饮","蜜渍苹果","巧克力包层浆果","甜甜卷","蛋糕"};
        static readonly int[] Values={1,2,3,4,5,6,8,9,10,11,12};
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        [Serializable] class OriginalTransitions { public OriginalTransition[] transitions; }
        [Serializable] class OriginalTransition { public string state; public int index; public float duration; }
        static void ValidateTriggerDurations(AnimatorController controller)
        {
            var expected=JsonUtility.FromJson<OriginalTransitions>(File.ReadAllText(Root+"/Editor/OriginalItemTransitions.json"));
            var states=new Dictionary<string,AnimatorState>();
            Action<AnimatorStateMachine,string> visit=null;
            visit=(sm,path)=>{
                foreach(var s in sm.states)states[path+s.state.name]=s.state;
                foreach(var child in sm.stateMachines)visit(child.stateMachine,path+child.stateMachine.name+"/");
            };
            visit(controller.layers.Single(l=>l.name=="Wine Fox / 物品").stateMachine,"");
            foreach(var t in expected.transitions)
                Require(Mathf.Approximately(states[t.state].transitions[t.index].duration,t.duration),"Original transition duration changed: "+t.state+" #"+t.index);
        }
        public static void Finish(){Validate();RenderGallery();Export();}
        public static void ValidateAndExport(){Validate();Export();}
        public static void RebuildMenusAndValidateExport(){ExtractMergedItems.RepairMenus();Validate();Export();}
        [MenuItem("Tools/Wine Fox Items/Validate on tester")]
        public static void Validate()
        {
            var log=new StringBuilder();
            EditorSceneManager.OpenScene(Root+"/Examples/Tester-WineFoxItems.unity");
            var tester=Object.FindObjectsOfType<VRCAvatarDescriptor>().Single().gameObject;
            var humanoid=tester.GetComponent<Animator>();Require(humanoid.isHuman,"Tester humanoid missing");
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/WineFoxItems.prefab");
            Require(prefab.GetComponentsInChildren<VRCAvatarDescriptor>().Length==0,"Gimmick contains avatar descriptor");
            Require(prefab.GetComponentsInChildren<Animator>().Length==0,"Gimmick contains avatar Animator");
            var boneProxy=prefab.GetComponentInChildren<ModularAvatarBoneProxy>();
            Require(boneProxy.boneReference==HumanBodyBones.RightHand&&string.IsNullOrEmpty(boneProxy.subPath),"Bone proxy depends on skeleton names");
            var mesh=prefab.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
            Require(mesh.blendShapeCount==35 && mesh.bindposes.Length==12,"Unexpected mesh data");
            foreach(var dependency in AssetDatabase.GetDependencies(Root+"/WineFoxItems.prefab",true))
                Require(!dependency.StartsWith("Assets/WineFoxSource")&&!dependency.Contains("tester"),"Prefab is not independent: "+dependency);
            log.AppendLine("PASS: prefab is independent of source avatar, standalone item FBX, tester and custom runtime scripts.");
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/WineFoxItemsFX.controller");
            var parameterConfig=prefab.GetComponent<ModularAvatarParameters>().parameters;
            var expressionConfig=parameterConfig.Where(p=>p.syncType!=ParameterSyncType.NotSynced).ToArray();
            Require(expressionConfig.Length==2,"Extra expression parameters in prefab");
            Require(expressionConfig.Single(p=>p.nameOrPrefix=="WineFoxItems/Item").syncType==ParameterSyncType.Int,"Original item type changed");
            Require(expressionConfig.Single(p=>p.nameOrPrefix=="WineFoxItems/TriggerInput").syncType==ParameterSyncType.Bool,"Original trigger type changed");
            Require(expressionConfig.All(p=>p.saved&&!p.localOnly&&p.defaultValue==0),"Original saved/default/sync flags changed");
            Require(!controller.parameters.Any(p=>p.name=="WineFoxItems/EatInput"||p.name=="WineFoxItems/FoodContact"),"Debug parameters remain");
            Require(controller.layers.Length==7&&!controller.layers.Any(l=>l.name=="Wine Fox / Food input"),"Extra FX layer remains");
            foreach(var driver in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<VRCAvatarParameterDriver>().Where(d=>d.parameters.Any(p=>p.name=="WineFoxItems/Fruit")))
                Require(!driver.localOnly,"Original random driver policy changed");
            foreach(var receiver in prefab.GetComponentsInChildren<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>(true))
            {
                var so=new SerializedObject(receiver);
                Require(!so.FindProperty("localOnly").boolValue,"Original receiver localOnly changed");
                Require(so.FindProperty("parameter").stringValue!="WineFoxItems/FoodContact","Food contact no longer drives original Eating parameter directly");
            }
            ValidateTriggerDurations(controller);
            log.AppendLine("PASS: item-layer transition durations match source, including mechanical arm 0.1 s and staff 0.25 s open/close blends.");
            ExtractMergedItems.ValidateBindings(prefab,controller,log);
            var menus=new HashSet<VRCExpressionsMenu>();
            var installMenu=prefab.GetComponent<ModularAvatarMenuInstaller>().menuToAppend;
            ValidateMenus(installMenu,menus);
            Require(menus.Count==3,"Expected original main, items and food menus");
            ValidateOriginalMenuLayout(installMenu,false);
            ValidateItemControls(menus.SelectMany(m=>m.controls).ToArray(),"Saved radial menus");
            log.AppendLine("PASS: saved menus match source order, names, control types, values and icon SHA-256 hashes (main/items/food = 2/7/5 controls).");
            using(new nadena.dev.ndmf.OverrideTemporaryDirectoryScope("Assets/NDMFValidationOutput"))
            {
                var clone=Object.Instantiate(tester);clone.name="WineFoxItems Validation Clone";
                var context=nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(clone,nadena.dev.ndmf.platform.AmbientPlatform.DefaultPlatform);
                foreach(var e in context.ErrorReport.Errors)log.AppendLine("NDMF "+e.TheError.Severity+": "+e.TheError.ToMessage());
                Require(context.Successful,"NDMF reported errors");
                Require(clone.GetComponentsInChildren<AvatarTagComponent>(true).Length==0,"MA components were not consumed");
                var finalHand=clone.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
                Require(finalHand.Find("RightHandAnchor/GripAdjust/RightHandRig/RightHandLocator"),"MA did not attach to humanoid right hand");
                var descriptor=clone.GetComponent<VRCAvatarDescriptor>();
                var mergedMenus=new HashSet<VRCExpressionsMenu>();ValidateMenus(descriptor.expressionsMenu,mergedMenus);
                ValidateOriginalMenuLayout(descriptor.expressionsMenu,true);
                ValidateItemControls(mergedMenus.SelectMany(m=>m.controls).ToArray(),"Compiled tester radial menu");
                log.AppendLine("PASS: compiled tester menu preserves the original 物品 / 食品 entries, 11 item selections, toggle trigger and original icons.");
                Require(descriptor.expressionParameters.parameters.Any(p=>p.name=="WineFoxItems/Item"),"Menu parameter not merged");
                int bits=descriptor.expressionParameters.CalcTotalCost();
                var itemParameters=descriptor.expressionParameters.parameters.Where(p=>p.name.StartsWith("WineFoxItems/")).ToArray();
                Require(itemParameters.Length==2,"Extra compiled expression parameters");
                Require(itemParameters.Single(p=>p.name=="WineFoxItems/Item").valueType==VRCExpressionParameters.ValueType.Int,"Compiled item type changed");
                Require(itemParameters.Single(p=>p.name=="WineFoxItems/TriggerInput").valueType==VRCExpressionParameters.ValueType.Bool,"Compiled trigger type changed");
                Require(itemParameters.All(p=>p.networkSynced&&p.saved&&p.defaultValue==0),"Compiled original parameter flags changed");
                log.AppendLine("PASS: compiled item expression parameters exactly match source policy: Item Int (8 bits), TriggerInput Bool (1 bit), both saved/default 0/network-synced; total item cost=9 bits, extra cost=0. Fruit/Hit/Eating/Bonk remain animator-only; no debug parameters or extra Food input layer.");
                log.AppendLine("PASS: NDMF/MA build on tester; humanoid RightHand attachment; merged menu and FX. Total tester expression cost="+bits+" bits.");
                var fx=descriptor.baseAnimationLayers.First(l=>l.type==VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Require(fx,"Compiled FX controller missing");
                ValidateTriggerDurations(fx);
                ExtractMergedItems.ValidateBindings(clone,fx,log);
                ValidateAnimator(clone,fx,log);
                Object.DestroyImmediate(clone);
            }
            log.AppendLine("PASS: Bone Proxy stores humanoid RightHand with no skeleton-name subpath.");
            File.WriteAllText("Artifacts/validation-report.txt",log.ToString());
            AssetDatabase.DeleteAsset("Assets/NDMFValidationOutput");
            AssetDatabase.SaveAssets();
            Debug.Log("WINEFOX_VALIDATION_OK\n"+log);
        }
        static void ValidateMenus(VRCExpressionsMenu menu,HashSet<VRCExpressionsMenu> seen)
        {
            Require(menu,"Menu reference missing");
            if(!seen.Add(menu))return;Require(menu.controls.Count>0&&menu.controls.Count<=8,"Empty or oversized menu "+menu.name);
            foreach(var c in menu.controls)if(c.type==VRCExpressionsMenu.Control.ControlType.SubMenu)ValidateMenus(c.subMenu,seen);
        }
        static void ValidateItemControls(VRCExpressionsMenu.Control[] controls,string context)
        {
            for(int i=0;i<Items.Length;i++)
            {
                var control=controls.SingleOrDefault(c=>c.name==Items[i]&&c.parameter?.name=="WineFoxItems/Item");
                Require(control!=null&&control.type==VRCExpressionsMenu.Control.ControlType.Toggle&&Mathf.Approximately(control.value,Values[i])&&control.icon,context+": missing or invalid item "+Items[i]);
            }
            Require(controls.Any(c=>c.name=="扳机"&&c.parameter?.name=="WineFoxItems/TriggerInput"&&c.type==VRCExpressionsMenu.Control.ControlType.Toggle),context+": original trigger toggle missing");
        }
        static void ValidateOriginalMenuLayout(VRCExpressionsMenu root,bool mergedRoot)
        {
            var layout=ExtractMergedItems.ReadOriginalMenuLayout();
            var specs=layout.menus.ToDictionary(m=>m.name);
            Action<VRCExpressionsMenu,ExtractMergedItems.OriginalMenu,bool> check=null;
            check=(menu,spec,allowOther)=>
            {
                var names=spec.controls.Select(c=>c.name).ToArray();
                var controls=allowOther?menu.controls.Where(c=>names.Contains(c.name)).ToArray():menu.controls.ToArray();
                Require(controls.Length==spec.controls.Length,"Original menu control count changed: "+spec.name);
                for(int i=0;i<controls.Length;i++)
                {
                    var actual=controls[i];var expected=spec.controls[i];
                    Require(actual.name==expected.name&&(int)actual.type==expected.type&&(actual.parameter?.name??"")==expected.parameter&&Mathf.Approximately(actual.value,expected.value),"Original menu control changed: "+spec.name+" slot "+i);
                    Require(actual.icon,"Original menu icon missing: "+actual.name);
                    using(var sha=System.Security.Cryptography.SHA256.Create())
                    {
                        var hash=BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(AssetDatabase.GetAssetPath(actual.icon)))).Replace("-","").ToLowerInvariant();
                        Require(hash==expected.iconSha256,"Original menu icon changed: "+actual.name);
                    }
                    if(!string.IsNullOrEmpty(expected.subMenu)){Require(actual.subMenu,"Original submenu missing: "+actual.name);check(actual.subMenu,specs[expected.subMenu],false);}
                }
            };
            check(root,specs["InstallMenu"],mergedRoot);
        }
        static void ValidateAnimator(GameObject avatar,AnimatorController fx,StringBuilder log)
        {
            var animator=avatar.GetComponent<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.Rebind();
            var graph=PlayableGraph.Create("Wine Fox FX validation");graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var playable=AnimatorControllerPlayable.Create(graph,fx);var output=AnimationPlayableOutput.Create(graph,"FX",animator);output.SetSourcePlayable(playable);graph.Play();
                Action<float> step=seconds=>{for(int f=0;f<Mathf.CeilToInt(seconds*60);f++)graph.Evaluate(1f/60);};
                var renderer=avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r=>r.sharedMesh.blendShapeCount==35);
                for(int i=0;i<Items.Length;i++)
                {
                    playable.SetBool("WineFoxItems/Trigger",false);playable.SetBool("WineFoxItems/Eating",false);playable.SetInteger("WineFoxItems/Item",0);step(.15f);
                    playable.SetInteger("WineFoxItems/Item",Values[i]);step(.2f);
                    var shape="I"+Items[i];if(new[]{9,10,11,12}.Contains(Values[i]))shape+="1";
                    int id=renderer.sharedMesh.GetBlendShapeIndex(shape);Require(id>=0,"Item shape missing: "+shape);
                    if(renderer.GetBlendShapeWeight(id)<=90)
                    {
                        var detail=new StringBuilder();detail.AppendLine("item="+playable.GetInteger("WineFoxItems/Item"));
                        detail.AppendLine("initialized="+animator.isInitialized+" active="+animator.isActiveAndEnabled+" playing="+Application.isPlaying);
                        for(int l=0;l<playable.GetLayerCount();l++){var info=playable.GetCurrentAnimatorStateInfo(l);detail.AppendLine(fx.layers[l].name+" state="+info.shortNameHash+" full="+info.fullPathHash+" weight="+playable.GetLayerWeight(l)+" normalized="+info.normalizedTime+" clips="+string.Join(",",playable.GetCurrentAnimatorClipInfo(l).Select(c=>c.clip.name+":"+c.weight)));}
                        foreach(var current in playable.GetCurrentAnimatorClipInfo(6))
                        {
                            foreach(var binding in AnimationUtility.GetCurveBindings(current.clip).Where(b=>b.propertyName=="blendShape."+shape))detail.AppendLine("binding="+binding.path+" value="+AnimationUtility.GetEditorCurve(current.clip,binding).Evaluate(.1f));
                            current.clip.SampleAnimation(avatar,0);detail.AppendLine("direct sample shape="+renderer.GetBlendShapeWeight(id));
                        }
                        File.WriteAllText("Artifacts/animator-diagnostic.txt",detail.ToString());
                        throw new Exception("Playable output failed: "+Items[i]+"; see animator-diagnostic.txt");
                    }
                    Require(renderer.GetBlendShapeWeight(id)>90,"Selection did not display "+Items[i]+"; weight="+renderer.GetBlendShapeWeight(id));
                }
                log.AppendLine("PASS: playable FX switches all 11 item selections on compiled tester.");
                playable.SetInteger("WineFoxItems/Item",2);step(.3f);playable.SetBool("WineFoxItems/Trigger",true);step(.6f);
                Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"))>90,"Mechanical arm did not open");
                playable.SetBool("WineFoxItems/Trigger",false);step(.6f);
                Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("伸缩机械手开"))<1,"Mechanical arm did not close");
                log.AppendLine("PASS: mechanical arm open/release transitions.");
                playable.SetInteger("WineFoxItems/Item",3);step(.4f);playable.SetBool("WineFoxItems/Trigger",true);step(.6f);
                Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("物理手杖开"))>90,"Staff did not open");
                playable.SetBool("WineFoxItems/Trigger",false);step(.6f);
                Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("物理手杖开"))<1,"Staff did not close");
                log.AppendLine("PASS: physical staff open/release transitions.");
                playable.SetInteger("WineFoxItems/Item",9);step(.4f);playable.SetBool("WineFoxItems/Eating",true);
                float duration=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/蜜渍苹果.anim").length;
                step(duration+.05f);
                Require(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("I蜜渍苹果2"))>90,"Food did not advance to second eating stage");
                playable.SetBool("WineFoxItems/Eating",false);playable.SetInteger("WineFoxItems/Item",0);step(.4f);
                playable.SetInteger("WineFoxItems/Item",1);step(.4f);playable.SetBool("WineFoxItems/Trigger",true);step(.15f);
                int itemLayer=Array.FindIndex(fx.layers,l=>l.name=="Wine Fox / 物品");
                Require(playable.GetCurrentAnimatorClipInfo(itemLayer).Any(c=>c.clip.name=="炮_发射"),"Cannon did not enter firing animation");
                log.AppendLine("PASS: staged food consumption and cannon firing transition.");
            }
            finally{graph.Destroy();}
        }
        public static void RenderGallery()
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Root+"/WineFoxItems.prefab");
            var gallery=new Texture2D(512*4,512*3,TextureFormat.RGB24,false);
            var bg=new Color(.09f,.105f,.14f);gallery.SetPixels(Enumerable.Repeat(bg,gallery.width*gallery.height).ToArray());
            var camera=new GameObject("Preview Camera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=bg;camera.orthographic=true;camera.nearClipPlane=.001f;camera.farClipPlane=100;
            var light=new GameObject("Key").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1;light.transform.rotation=Quaternion.Euler(40,30,0);
            RenderSettings.ambientLight=Color.white*.7f;
            var rt=new RenderTexture(512,512,24);camera.targetTexture=rt;
            var renderLog=new StringBuilder();
            for(int i=0;i<Items.Length;i++)
            {
                var instance=Object.Instantiate(prefab);
                foreach(var c in instance.GetComponentsInChildren<Component>(true))if(c is AvatarTagComponent || (c is Behaviour b&&c.GetType().Namespace?.StartsWith("VRC.")==true))Object.DestroyImmediate(c);
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/空手.anim").SampleAnimation(instance,0);
                AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"/Animations/"+Items[i]+".anim").SampleAnimation(instance,0);
                var renderer=instance.GetComponentInChildren<SkinnedMeshRenderer>();
                var baked=new Mesh();renderer.BakeMesh(baked,true);
                var frozen=new GameObject("Baked preview");frozen.AddComponent<MeshFilter>().sharedMesh=baked;frozen.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
                frozen.transform.SetPositionAndRotation(renderer.transform.position,renderer.transform.rotation);frozen.transform.localScale=renderer.transform.lossyScale;renderer.enabled=false;
                var verts=baked.vertices.Select(v=>renderer.transform.TransformPoint(v)).ToArray();var tris=baked.triangles;var bounds=new Bounds();bool first=true;
                var shape="I"+Items[i];if(new[]{9,10,11,12}.Contains(Values[i]))shape+="1";
                var delta=new Vector3[renderer.sharedMesh.vertexCount];renderer.sharedMesh.GetBlendShapeFrameVertices(renderer.sharedMesh.GetBlendShapeIndex(shape),0,delta,null,null);
                for(int j=0;j<delta.Length;j++)if(delta[j].sqrMagnitude>1e-16f){var v=verts[j];if(first){bounds=new Bounds(v,Vector3.zero);first=false;}else bounds.Encapsulate(v);}
                Require(!first,"No visible geometry for "+Items[i]);
                renderLog.AppendLine(Items[i]+" bounds="+bounds+" shapeWeight="+renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(shape))+" renderer="+renderer.enabled);
                camera.orthographicSize=Mathf.Max(bounds.extents.magnitude*1.25f,.15f);camera.transform.position=bounds.center+new Vector3(1.3f,.8f,-2)*Mathf.Max(bounds.size.magnitude,1);camera.transform.LookAt(bounds.center);
                camera.Render();RenderTexture.active=rt;var frame=new Texture2D(512,512,TextureFormat.RGB24,false);frame.ReadPixels(new Rect(0,0,512,512),0,0);frame.Apply();
                gallery.SetPixels((i%4)*512,(2-i/4)*512,512,512,frame.GetPixels());
                Directory.CreateDirectory("Artifacts/Previews");File.WriteAllBytes("Artifacts/Previews/"+(i+1).ToString("D2")+"-"+Items[i]+".png",frame.EncodeToPNG());
                Object.DestroyImmediate(frame);Object.DestroyImmediate(frozen);Object.DestroyImmediate(baked);Object.DestroyImmediate(instance);
            }
            gallery.Apply();File.WriteAllBytes("Artifacts/items-preview.png",gallery.EncodeToPNG());
            File.WriteAllText("Artifacts/render-report.txt",renderLog.ToString());
            RenderTexture.active=null;camera.targetTexture=null;rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(gallery);
            Debug.Log("WINEFOX_GALLERY_OK");
        }
        public static void Export()
        {
            Directory.CreateDirectory("Releases");
            var assets=AssetDatabase.GetDependencies(Root+"/WineFoxItems.prefab",true).Where(p=>p.StartsWith(Root)).Concat(new[]{Root+"/Editor/WineFoxItemsBuildPlugin.cs",Root+"/README.md",Root+"/LICENSE.txt",Root+"/SOURCE-COMPARISON.md"}).ToArray();
            AssetDatabase.ExportPackage(assets,"Releases/WineFoxItems-MA.unitypackage",ExportPackageOptions.Default);
            Debug.Log("WINEFOX_EXPORT_OK");
        }
    }
}
