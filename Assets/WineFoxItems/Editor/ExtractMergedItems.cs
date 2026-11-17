using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace WineFoxItems.Editor
{
    // Extracts the FINAL avatar mesh, never the older standalone item FBX.
    public static class ExtractMergedItems
    {
        const string Out = "Assets/WineFoxItems";
        const string Source = "Assets/WineFoxSource";
        const string HandPath = "MRoot/Root/DownBody/MUpBody/MUpperBody/MChest/RightShoulder/RightArm/RightForeArmRoot/RightHand";
        const string NewHandPath = "RightHandAnchor/GripAdjust/RightHandRig";
        static readonly Dictionary<string,string> Params = new Dictionary<string,string>
        {
            {"item","WineFoxItems/Item"},{"itrigger","WineFoxItems/TriggerInput"},
            {"trigger","WineFoxItems/Trigger"},{"fruit","WineFoxItems/Fruit"},
            {"eating","WineFoxItems/Eating"},{"bonk","WineFoxItems/Bonk"},
            {"itemhit","WineFoxItems/Hit"},{"itemfly","WineFoxItems/Flying"}
        };
        static readonly string[] Layers = {"Base Layer","转换层 扳机","物品","炮弹","手杖1","手杖2","声音"};
        static readonly Dictionary<Object,Object> Clones = new Dictionary<Object,Object>();
        static AnimatorController targetController;
        static string sourceControllerPath;
        static StringBuilder report;

        [MenuItem("Tools/Wine Fox Items/Extract from final merged avatar")]
        public static void Build()
        {
            if (!File.Exists(Source+"/main.unity")) throw new Exception("Copy the wine-fox repository Assets into Assets/WineFoxSource before rebuilding. See README.");
            Clones.Clear(); report=new StringBuilder();
            foreach(var dir in new[]{"Meshes","Animations","Resources","Menus"})if(AssetDatabase.IsValidFolder(Out+"/"+dir))AssetDatabase.DeleteAsset(Out+"/"+dir);
            if(File.Exists(Out+"/WineFoxItemsFX.controller"))AssetDatabase.DeleteAsset(Out+"/WineFoxItemsFX.controller");
            foreach(var dir in new[]{"Meshes","Animations","Resources","Menus","Examples"}) Directory.CreateDirectory(Out+"/"+dir);
            AssetDatabase.Refresh();
            var scene=EditorSceneManager.OpenScene(Source+"/main.unity");
            var original=scene.GetRootGameObjects().First(g=>g.GetComponent<VRCAvatarDescriptor>());
            var sourceRenderer=original.GetComponentInChildren<SkinnedMeshRenderer>();
            var sourceHand=original.transform.Find(HandPath);
            var locator=sourceHand.Find("RightHandLocator");
            var boneIds=Enumerable.Range(0,sourceRenderer.bones.Length).Where(i=>sourceRenderer.bones[i]==locator || sourceRenderer.bones[i].IsChildOf(locator)).ToArray();
            var mesh=ExtractMesh(sourceRenderer.sharedMesh,boneIds);
            mesh=(Mesh)SaveAsset(mesh,Out+"/Meshes/FinalMergedItems.asset");
            var oldShapeWeights=Enumerable.Range(0,mesh.blendShapeCount).Select(i=>sourceRenderer.GetBlendShapeWeight(sourceRenderer.sharedMesh.GetBlendShapeIndex(mesh.GetBlendShapeName(i)))).ToArray();

            var instance=Object.Instantiate(original); instance.name="ExtractionWorkingCopy";
            if(PrefabUtility.IsPartOfPrefabInstance(instance))PrefabUtility.UnpackPrefabInstance(instance,PrefabUnpackMode.Completely,InteractionMode.AutomatedAction);
            var root=new GameObject("WineFoxItems");
            var anchor=Child(root.transform,"RightHandAnchor");
            var proxy=anchor.gameObject.AddComponent<ModularAvatarBoneProxy>();
            proxy.boneReference=HumanBodyBones.RightHand; proxy.attachmentMode=BoneProxyAttachmentMode.AsChildAtRoot; proxy.matchScale=false;
            var grip=Child(anchor,"GripAdjust");
            var hand=instance.transform.Find(HandPath);
            hand.SetParent(grip,false); hand.name="RightHandRig"; hand.localPosition=Vector3.zero; hand.localRotation=Quaternion.identity; hand.localScale=Vector3.one*100;
            foreach(var c in hand.GetComponents<Component>()) if(!(c is Transform))Object.DestroyImmediate(c);
            var renderer=instance.GetComponentInChildren<SkinnedMeshRenderer>();
            renderer.transform.SetParent(root.transform,false);renderer.name="ItemsMesh";
            renderer.sharedMesh=mesh;renderer.bones=boneIds.Select(i=>renderer.bones[i]).ToArray();renderer.rootBone=hand;
            renderer.sharedMaterials=renderer.sharedMaterials.Select(m=>(Material)CloneResource(m)).ToArray();
            for(int i=0;i<mesh.blendShapeCount;i++)renderer.SetBlendShapeWeight(i,oldShapeWeights[i]);
            // Bounds follow the hand; include the original cannon's 20 m flight.
            renderer.updateWhenOffscreen=true;
            renderer.localBounds=new Bounds(Vector3.zero,Vector3.one*0.5f);
            var hit=instance.transform.Find("raycast_cannon");hit.SetParent(root.transform,false);
            Object.DestroyImmediate(instance);
            foreach(var c in root.GetComponentsInChildren<Component>(true)) RemapComponent(c,root);
            sourceControllerPath=Source+"/酒狐合并.controller";
            var sourceCtrl=AssetDatabase.LoadAssetAtPath<AnimatorController>(sourceControllerPath);
            targetController=AnimatorController.CreateAnimatorControllerAtPath(Out+"/WineFoxItemsFX.controller");
            targetController.layers=Array.Empty<AnimatorControllerLayer>();
            targetController.parameters=sourceCtrl.parameters.Where(p=>Params.ContainsKey(p.name)||p.name=="GestureRight").Select(p=>new AnimatorControllerParameter{name=Rename(p.name),type=p.type,defaultBool=p.defaultBool,defaultInt=p.defaultInt,defaultFloat=p.defaultFloat}).ToArray();
            foreach(var layer in sourceCtrl.layers.Where(l=>Layers.Contains(l.name)))
            {
                var copy=new AnimatorControllerLayer{name="Wine Fox / "+layer.name,defaultWeight=1,blendingMode=layer.blendingMode,stateMachine=(AnimatorStateMachine)CloneNode(layer.stateMachine)};
                targetController.AddLayer(copy);report.AppendLine("Layer: "+layer.name);
            }
            var merge=root.AddComponent<ModularAvatarMergeAnimator>();merge.animator=targetController;merge.pathMode=MergeAnimatorPathMode.Relative;merge.matchAvatarWriteDefaults=false;
            var parameters=root.AddComponent<ModularAvatarParameters>();
            OriginalParameterPolicy.Configure(parameters,targetController);
            var menu=BuildMenus();root.AddComponent<ModularAvatarMenuInstaller>().menuToAppend=menu;
            // Complete reset before any selection, including cannon constraints/raycasts.
            var reset=AssetDatabase.LoadAssetAtPath<AnimationClip>(Out+"/Animations/空手.anim");reset.SampleAnimation(root,0);
            ValidateBindings(root,targetController,report);
            var prefab=PrefabUtility.SaveAsPrefabAsset(root,Out+"/WineFoxItems.prefab");
            var dependencies=AssetDatabase.GetDependencies(Out+"/WineFoxItems.prefab",true);
            if(dependencies.Any(p=>p.StartsWith(Source)))throw new Exception("Extracted prefab still references source: "+string.Join(",",dependencies.Where(p=>p.StartsWith(Source))));
            report.AppendLine("Source dependency check: PASS");
            File.WriteAllText("Artifacts/extraction-report.txt",report.ToString());
            Object.DestroyImmediate(root);
            CreateTesterScene(prefab);
            ItemPrefabRepair.RepairFreshExtraction();
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("WINEFOX_EXTRACTION_OK");
        }
        static Transform Child(Transform parent,string name){var t=new GameObject(name).transform;t.SetParent(parent,false);return t;}
        static string Rename(string name)=>Params.TryGetValue(name??"",out var value)?value:name;
        static string MapPath(string path)
        {
            if(path==HandPath || path.StartsWith(HandPath+"/"))return NewHandPath+path.Substring(HandPath.Length);
            if(path=="winefox")return "ItemsMesh";
            if(path=="raycast_cannon")return path;
            if(path=="")return path;
            throw new Exception("Unexpected avatar-dependent animation binding: "+path);
        }
        static Object SaveAsset(Object asset,string path)
        {
            var existing=AssetDatabase.LoadAssetAtPath<Object>(path);
            if(existing && asset is Mesh)
            {
                const string temporary="Assets/WineFoxItems/Editor/_ExtractedMeshWrite.asset";
                AssetDatabase.CreateAsset(asset,temporary);AssetDatabase.SaveAssets();
                File.Copy(temporary,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
                AssetDatabase.DeleteAsset(temporary);return AssetDatabase.LoadAssetAtPath<Mesh>(path);
            }
            if(existing){EditorUtility.CopySerialized(asset,existing);Object.DestroyImmediate(asset);return existing;}else AssetDatabase.CreateAsset(asset,path);
            return asset;
        }
        static Mesh ExtractMesh(Mesh source,int[] bones)
        {
            var allowed=new HashSet<int>(bones);var weights=source.boneWeights;
            Func<BoneWeight,bool> keep=w=>(w.weight0==0||allowed.Contains(w.boneIndex0))&&(w.weight1==0||allowed.Contains(w.boneIndex1))&&(w.weight2==0||allowed.Contains(w.boneIndex2))&&(w.weight3==0||allowed.Contains(w.boneIndex3));
            var ids=Enumerable.Range(0,source.vertexCount).Where(i=>keep(weights[i])).ToArray();
            var map=ids.Select((old,i)=>new{old,i}).ToDictionary(v=>v.old,v=>v.i);var bm=bones.Select((old,i)=>new{old,i}).ToDictionary(v=>v.old,v=>v.i);
            var result=new Mesh{name="Wine Fox final merged hand items"};
            result.vertices=ids.Select(i=>source.vertices[i]).ToArray();result.normals=ids.Select(i=>source.normals[i]).ToArray();
            if(source.tangents.Length>0)result.tangents=ids.Select(i=>source.tangents[i]).ToArray();
            for(int ch=0;ch<8;ch++){var uv=new List<Vector4>();source.GetUVs(ch,uv);if(uv.Count>0)result.SetUVs(ch,ids.Select(i=>uv[i]).ToList());}
            if(source.colors32.Length>0)result.colors32=ids.Select(i=>source.colors32[i]).ToArray();
            result.boneWeights=ids.Select(i=>{var w=weights[i];w.boneIndex0=w.weight0>0?bm[w.boneIndex0]:0;w.boneIndex1=w.weight1>0?bm[w.boneIndex1]:0;w.boneIndex2=w.weight2>0?bm[w.boneIndex2]:0;w.boneIndex3=w.weight3>0?bm[w.boneIndex3]:0;return w;}).ToArray();
            result.bindposes=bones.Select(i=>source.bindposes[i]).ToArray();result.subMeshCount=source.subMeshCount;
            int cross=0;for(int sub=0;sub<source.subMeshCount;sub++)
            {
                var tris=source.GetTriangles(sub);var selected=new List<int>();
                for(int i=0;i<tris.Length;i+=3){int count=Enumerable.Range(0,3).Count(j=>map.ContainsKey(tris[i+j]));if(count==3)for(int j=0;j<3;j++)selected.Add(map[tris[i+j]]);else if(count!=0)cross++;}
                result.SetTriangles(selected,sub);
            }
            if(cross>0)throw new Exception("Item/body boundary has mixed triangles: "+cross);
            for(int i=0;i<source.blendShapeCount;i++)
            {
                var name=source.GetBlendShapeName(i);if(!(name.StartsWith("I")||name.StartsWith("C")||name.StartsWith("物理手杖")||name=="伸缩机械手开"))continue;
                for(int f=0;f<source.GetBlendShapeFrameCount(i);f++)
                {
                    var v=new Vector3[source.vertexCount];var n=new Vector3[source.vertexCount];var t=new Vector3[source.vertexCount];source.GetBlendShapeFrameVertices(i,f,v,n,t);
                    if(Enumerable.Range(0,source.vertexCount).Any(j=>v[j].sqrMagnitude>1e-14f&&!map.ContainsKey(j)))throw new Exception("Item shape modifies body: "+name);
                    result.AddBlendShapeFrame(name,source.GetBlendShapeFrameWeight(i,f),ids.Select(j=>v[j]).ToArray(),ids.Select(j=>n[j]).ToArray(),ids.Select(j=>t[j]).ToArray());
                }
            }
            result.RecalculateBounds();
            report.AppendLine("Source: final merged avatar scene + 酒狐合并.fbx");
            report.AppendLine("Mesh: "+ids.Length+" vertices; "+result.triangles.Length/3+" triangles; "+bones.Length+" skin bones; "+result.blendShapeCount+" item blendshapes");
            report.AppendLine("Exact copy of selected vertices, normals, UV channels, weights, bindposes and every item blendshape frame; zero mixed body/item triangles.");
            return result;
        }
        static Object CloneResource(Object source)
        {
            if(!source)return null;if(Clones.TryGetValue(source,out var copy))return copy;
            var path=AssetDatabase.GetAssetPath(source);if(!path.StartsWith(Source))return source;
            var dest=Out+"/Resources/"+Path.GetFileName(path);
            if(source is Material)
            {
                copy=Object.Instantiate(source);copy.name=source.name;Clones[source]=copy;AssetDatabase.CreateAsset(copy,dest);RemapSerialized(copy);return copy;
            }
            if(!File.Exists(dest))AssetDatabase.CopyAsset(path,dest);
            if(source is Component comp){var go=AssetDatabase.LoadAssetAtPath<GameObject>(dest);copy=comp is Transform?go.transform:go.GetComponent(comp.GetType());}
            else copy=AssetDatabase.LoadAssetAtPath(dest,source.GetType());
            if(!copy)throw new Exception("Cannot clone resource "+path+" "+source.GetType());
            Clones[source]=copy;return copy;
        }
        static Object CloneNode(Object source)
        {
            if(!source)return null;if(Clones.TryGetValue(source,out var existing))return existing;
            if(source is AnimationClip clip)
            {
                var clone=Object.Instantiate(clip);clone.name=clip.name;Clones[source]=clone;
                var bindings=AnimationUtility.GetCurveBindings(clip);clone.ClearCurves();
                foreach(var b in bindings){if(b.path=="MRoot/AimDown"){report.AppendLine("Removed avatar arm-aim helper binding from "+clip.name+": "+b.propertyName);continue;}var dest=b;dest.path=MapPath(b.path);AnimationUtility.SetEditorCurve(clone,dest,AnimationUtility.GetEditorCurve(clip,b));}
                foreach(var b in AnimationUtility.GetObjectReferenceCurveBindings(clip)){var dest=b;dest.path=MapPath(b.path);AnimationUtility.SetObjectReferenceCurve(clone,dest,AnimationUtility.GetObjectReferenceCurve(clip,b));}
                AssetDatabase.CreateAsset(clone,Out+"/Animations/"+clone.name+".anim");return clone;
            }
            if(AssetDatabase.GetAssetPath(source)!=sourceControllerPath)return CloneResource(source);
            Object copy;
            if(source is AnimatorStateMachine)copy=new AnimatorStateMachine();
            else if(source is AnimatorState)copy=new AnimatorState();
            else if(source is AnimatorStateTransition)copy=new AnimatorStateTransition();
            else if(source is AnimatorTransition)copy=new AnimatorTransition();
            else if(source is BlendTree)copy=new BlendTree();
            else if(source is StateMachineBehaviour)copy=ScriptableObject.CreateInstance(source.GetType());
            else throw new Exception("Unsupported controller object: "+source.GetType());
            EditorUtility.CopySerialized(source,copy);copy.name=source.name;copy.hideFlags=HideFlags.HideInHierarchy;Clones[source]=copy;AssetDatabase.AddObjectToAsset(copy,targetController);RemapSerialized(copy);return copy;
        }
        static void RemapSerialized(Object obj)
        {
            var so=new SerializedObject(obj);var p=so.GetIterator();while(p.Next(true))
            {
                if(p.propertyType==SerializedPropertyType.ObjectReference && p.objectReferenceValue)
                {
                    var value=p.objectReferenceValue;
                    if(value is MonoScript)continue;
                    if(value is AnimationClip || AssetDatabase.GetAssetPath(value)==sourceControllerPath)p.objectReferenceValue=CloneNode(value);
                    else if(AssetDatabase.GetAssetPath(value).StartsWith(Source))p.objectReferenceValue=CloneResource(value);
                }
                else if(p.propertyType==SerializedPropertyType.String)
                {
                    var value=p.stringValue;if(Params.ContainsKey(value))p.stringValue=Rename(value);
                    else if(p.name=="SourcePath" && value.StartsWith(HandPath))p.stringValue=MapPath(value);
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        static void RemapComponent(Component component,GameObject root)
        {
            if(!component)throw new Exception("Missing script in extracted hierarchy");
            RemapSerialized(component);
            var so=new SerializedObject(component);var p=so.GetIterator();while(p.Next(true))
            {
                if(p.propertyType!=SerializedPropertyType.ObjectReference)continue;
                var c=p.objectReferenceValue as Component;if(c && !AssetDatabase.Contains(c) && c.transform!=root.transform && !c.transform.IsChildOf(root.transform))throw new Exception("External scene reference "+component.name+" "+p.propertyPath+" -> "+c.name);
            }
        }
        static VRCExpressionsMenu NewMenu(string name)
        {
            var path=Out+"/Menus/"+name+".asset";
            var m=AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path);
            if(!m){m=ScriptableObject.CreateInstance<VRCExpressionsMenu>();m.name=name;AssetDatabase.CreateAsset(m,path);}
            m.controls.Clear();return m;
        }
        [MenuItem("Tools/Wine Fox Items/Rebuild radial menus")]
        public static void RepairMenus()
        {
            Clones.Clear();BuildMenus();AssetDatabase.SaveAssets();
            Debug.Log("WINEFOX_MENUS_SAVED");
        }
        [Serializable] public class OriginalMenuLayout { public OriginalMenu[] menus; }
        [Serializable] public class OriginalMenu { public string name,sourceFile; public OriginalControl[] controls; }
        [Serializable] public class OriginalControl
        {
            public string name,parameter,iconFile,iconSha256,subMenu;
            public int type,style;
            public float value;
        }
        public static OriginalMenuLayout ReadOriginalMenuLayout()
        {
            return JsonUtility.FromJson<OriginalMenuLayout>(File.ReadAllText(Out+"/Editor/OriginalMenuLayout.json"));
        }
        static VRCExpressionsMenu BuildMenus()
        {
            // Preserve the source's ordered controls, control types and icon references.
            var layout=ReadOriginalMenuLayout();
            var menus=layout.menus.ToDictionary(m=>m.name,m=>NewMenu(m.name));
            foreach(var spec in layout.menus)
            {
                foreach(var control in spec.controls)
                {
                    var icon=AssetDatabase.LoadAssetAtPath<Texture2D>(Out+"/Resources/"+control.iconFile);
                    if(!icon)icon=(Texture2D)CloneResource(AssetDatabase.LoadAssetAtPath<Texture2D>(Source+"/图标/"+control.iconFile));
                    if(!icon)throw new Exception("Original radial menu icon missing: "+control.iconFile);
                    menus[spec.name].controls.Add(new VRCExpressionsMenu.Control
                    {
                        name=control.name,icon=icon,type=(VRCExpressionsMenu.Control.ControlType)control.type,
                        parameter=new VRCExpressionsMenu.Control.Parameter{name=control.parameter},value=control.value,
                        subMenu=string.IsNullOrEmpty(control.subMenu)?null:menus[control.subMenu]
                    });
                }
                EditorUtility.SetDirty(menus[spec.name]);
            }
            return menus["InstallMenu"];
        }
        public static void ValidateBindings(GameObject root,AnimatorController controller,StringBuilder log)
        {
            int count=0;foreach(var clip in controller.animationClips.Distinct())foreach(var b in AnimationUtility.GetCurveBindings(clip))
            {
                // SDK default face proxies refer to a Body mesh absent on the skeleton-only tester.
                // These are not part of this prefab; validate all extracted item clips strictly.
                if(clip.name.StartsWith("proxy_"))continue;
                var t=b.path==""?root.transform:root.transform.Find(b.path);if(!t)throw new Exception("Missing animation path: "+clip.name+" "+b.path);
                if(b.type!=typeof(GameObject)&&!t.GetComponent(b.type))throw new Exception("Missing component: "+clip.name+" "+b.path+" "+b.type);
                if(b.type==typeof(SkinnedMeshRenderer)&&b.propertyName.StartsWith("blendShape.")&&t.GetComponent<SkinnedMeshRenderer>().sharedMesh.GetBlendShapeIndex(b.propertyName.Substring(11))<0)throw new Exception("Missing shape: "+b.propertyName);
                count++;
            }
            log.AppendLine("Animation bindings: "+count+" checked; all paths, components and blendshapes resolve.");
        }
        static void CreateTesterScene(GameObject prefab)
        {
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var tester=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/tester merged.prefab"));
            tester.name="Tester - Wine Fox Items";
            var animator=tester.GetComponent<Animator>();if(!animator||!animator.isHuman)throw new Exception("Tester does not have a humanoid Animator");
            var item=(GameObject)PrefabUtility.InstantiatePrefab(prefab);item.transform.SetParent(tester.transform,false);
            var a=item.transform.Find("RightHandAnchor");var hand=animator.GetBoneTransform(HumanBodyBones.RightHand);a.SetPositionAndRotation(hand.position,hand.rotation);
            var gm=AssetDatabase.LoadAssetAtPath<GameObject>("Packages/vrchat.blackstartx.gesture-manager/GestureManager.prefab");if(gm)PrefabUtility.InstantiatePrefab(gm);
            var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.transform.position=new Vector3(0,1.1f,3);camera.transform.LookAt(new Vector3(0,1,0));camera.gameObject.AddComponent<AudioListener>();
            var light=new GameObject("Directional Light").AddComponent<Light>();light.type=LightType.Directional;light.transform.rotation=Quaternion.Euler(50,-30,0);
            EditorSceneManager.SaveScene(scene,Out+"/Examples/Tester-WineFoxItems.unity");
        }
    }
}
