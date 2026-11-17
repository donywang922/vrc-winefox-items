using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using Object=UnityEngine.Object;

namespace WineFoxItems.Editor
{
    public static class OriginalParameterPolicy
    {
        const string Root="Assets/WineFoxItems";
        public static void Configure(ModularAvatarParameters target,AnimatorController controller)
        {
            target.parameters.Clear();
            foreach(var p in controller.parameters.Where(p=>p.name!="GestureRight"))
            {
                bool item=p.name=="WineFoxItems/Item", trigger=p.name=="WineFoxItems/TriggerInput";
                target.parameters.Add(new ParameterConfig {
                    nameOrPrefix=p.name,
                    syncType=item?ParameterSyncType.Int:trigger?ParameterSyncType.Bool:ParameterSyncType.NotSynced,
                    localOnly=false,saved=item||trigger,hasExplicitDefaultValue=true,defaultValue=0
                });
            }
            EditorUtility.SetDirty(target);
        }
        public static void ApplyAndVerify(){Apply();CannonRuntimeVerification.Start();}
        [MenuItem("Tools/Wine Fox Items/Restore original parameter policy")]
        public static void Apply()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"/WineFoxItemsFX.controller");
            int index=Array.FindIndex(controller.layers,l=>l.name=="Wine Fox / Food input");
            if(index>=0)
            {
                var sm=controller.layers[index].stateMachine;
                var nodes=sm.states.SelectMany(s=>s.state.transitions.Cast<Object>().Concat(s.state.behaviours).Append(s.state)).Append(sm).ToArray();
                controller.RemoveLayer(index);
                foreach(var node in nodes)if(node)Object.DestroyImmediate(node,true);
            }
            controller.parameters=controller.parameters.Where(p=>p.name!="WineFoxItems/EatInput"&&p.name!="WineFoxItems/FoodContact").ToArray();
            foreach(var driver in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<VRCAvatarParameterDriver>()
                .Where(d=>d.parameters.Any(p=>p.name=="WineFoxItems/Fruit")))
            {driver.localOnly=false;EditorUtility.SetDirty(driver);}
            EditorUtility.SetDirty(controller);
            var prefab=PrefabUtility.LoadPrefabContents(Root+"/WineFoxItems.prefab");
            try
            {
                Configure(prefab.GetComponent<ModularAvatarParameters>(),controller);
                foreach(var receiver in prefab.GetComponentsInChildren<VRCContactReceiver>(true))
                {
                    var so=new SerializedObject(receiver);
                    var parameter=so.FindProperty("parameter");
                    if(parameter.stringValue=="WineFoxItems/FoodContact")parameter.stringValue="WineFoxItems/Eating";
                    so.FindProperty("localOnly").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(prefab,Root+"/WineFoxItems.prefab");
            }
            finally{PrefabUtility.UnloadPrefabContents(prefab);}
            AssetDatabase.SaveAssets();
            Debug.Log("WINEFOX_ORIGINAL_PARAMETERS_OK: Item Int + TriggerInput Bool, saved as source; all other parameters animator-only; original random/contact policy restored; debug parameters/layer removed.");
        }
    }
}
