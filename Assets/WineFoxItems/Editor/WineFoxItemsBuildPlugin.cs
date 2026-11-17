using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(WineFoxItems.Editor.WineFoxItemsBuildPlugin))]
namespace WineFoxItems.Editor
{
    // Only enables this prefab's generic transforms in the final FX mask.
    // Humanoid mask bits and other avatar transform entries are preserved.
    public sealed class WineFoxItemsBuildPlugin : Plugin<WineFoxItemsBuildPlugin>
    {
        public override string QualifiedName => "org.winefoxitems.fx-mask";
        public override string DisplayName => "Wine Fox Items FX mask";
        class State { public Transform[] transforms = new Transform[0]; }
        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving).BeforePlugin("nadena.dev.modular-avatar")
                .Run("Remember item transforms", ctx => {
                    ctx.GetState<State>().transforms = ctx.AvatarRootObject
                        .GetComponentsInChildren<ModularAvatarMergeAnimator>(true)
                        .Where(m => m.animator && m.animator.name == "WineFoxItemsFX")
                        .SelectMany(m => m.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
                });
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq =>
                    seq.Run("Enable item transform animations", ctx => {
                        var transforms = ctx.GetState<State>().transforms.Where(t => t).ToArray();
                        if (transforms.Length == 0) return;
                        var services = ctx.Extension<AnimatorServicesContext>();
                        var fx = services.ControllerContext.Controllers[VRCAvatarDescriptor.AnimLayerType.FX];
                        var first = fx.Layers.First();
                        // A null FX mask allows all generic transforms already.
                        if (first.AvatarMask == null) return;
                        var entries = first.AvatarMask.Elements;
                        foreach (var t in transforms)
                            entries = entries.SetItem(services.ObjectPathRemapper.GetVirtualPathForObject(t), 1);
                        first.AvatarMask.Elements = entries;
                    }));
        }
    }
}
