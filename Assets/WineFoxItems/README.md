# 酒狐手持物品 · Modular Avatar

从酒狐最终 `Assets/main.unity` 与 `Assets/酒狐合并.fbx` 提取，保留最终合并版的物品几何、UV、蒙皮权重、35 个物品形态键及相关动画。独立物品 FBX 仅用于比较，没有作为成品的数据来源。

## 安装与使用

1. 在已安装 VRChat Avatars SDK 和 Modular Avatar 的 Unity avatar 工程中，导入 `WineFoxItems-MA.unitypackage`，或复制整个 `Assets/WineFoxItems` 文件夹。
2. 将 `WineFoxItems.prefab` 拖到目标 avatar 根节点下面，保持预制件根节点的位置 / 旋转为零、缩放为一。MA Bone Proxy 会按 Humanoid 的 RightHand 绑定，不依赖原骨骼名称。
3. 打开表情菜单中的「物品」或「食品」。这两项沿用原版主菜单的名称、相对顺序和图标。
4. 「物品」页最后一项「扳机」沿用原版的开关类型；也可做右手 RockNRoll（GestureRight = 5），使用加农炮、机械手及物理手杖。食品靠近头部触发食用。再次点击已选中的物品可取消选择、收起物品。
5. 若目标 avatar 的手骨朝向、掌心位置或大小不同，调整预制件内 `RightHandAnchor/GripAdjust` 的位置、旋转、缩放。不要调整 `RightHandRig` 的 100 倍缩放；它用于保留原始 FBX 与动画的单位。

包括六种工具：土豆加农炮、伸缩机械手、物理手杖、对称之杖、扳手、纸棍；五种食品：建筑工茶饮、蜜渍苹果、巧克力包层浆果、甜甜卷、蛋糕；以及加农炮随机选择的六种炮弹形态。食品保留原版分阶段食用表现。

轮盘沿用原版排列：「物品」页依次为土豆加农炮、伸缩机械手、物理手杖、对称之杖、扳手、纸棍、扳机；「食品」页依次为建筑工茶饮、蜜渍苹果、巧克力包层浆果、甜甜卷、蛋糕。入口使用原版扳手 / 蛋糕图标，扳机使用原版 item_wand 图标；各条目的图片和导入设置均保留。不额外插入包装子菜单、收起按钮或手动食用按钮。

## 测试工程

打开 `Examples/Tester-WineFoxItems.unity`，其中已放置提供的纯骨架 tester、物品预制件及 Gesture Manager。进入 Play Mode，用 Gesture Manager 选择 tester 并操作表情菜单。编辑模式下，预制件默认收起，因此不会显示手持物品。

纯骨架测试缺少有效头部接触时，可在 Gesture Manager 中直接设置原有 Animator 参数 `WineFoxItems/Eating` 测试食用。不添加调试参数或额外 FX 层。

验证工具位于 Unity 菜单 `Tools > Wine Fox Items > Validate on tester`。它实际执行 NDMF/MA 构建，并检查编译后的动画路径、菜单、参数和骨骼挂载；菜单逐项检查原版顺序、名称、类型、参数值及图标文件 SHA-256；使用 Animator Playables 检查各物品切换与机械手开合。报告在工程根目录 `Artifacts/validation-report.txt`。

`Artifacts/runtime-verification.txt` 记录额外的实际 Play Mode 验证：通过 Gesture Manager 菜单参数驱动 SDK 行为，检查炮弹飞行、移动 avatar 后的世界固定、5 米目标命中、复位、六种炮弹及飞行中切换；对实际蒙皮后的网格检查未选中部分的三角形面积为零。

测试环境：Unity 2022.3.22f1、VRChat SDK 3.10.6、MA 1.18.7、NDMF 1.14.8。当前验证范围为 Windows 编辑器；没有进行 VRChat 客户端上传、多人同步或 Android/iOS 实机测试。原版音频与射线功能已保留；移动端使用前应移除不支持的音频组件。最终性能评级取决于目标 avatar 的总资源，无法仅凭此配件承诺 Good。

## 数据与依赖

- 1 个 SkinnedMeshRenderer，7,130 顶点，4,272 三角面，12 个蒙皮骨骼，35 个物品形态键。
- 使用原场景的 VRChat Mobile Toon Standard 材质，保留原贴图和材质 UV 缩放 / 偏移。
- 参数使用 `WineFoxItems/` 前缀避免重名；表达参数严格沿用原版，仅 `Item`（原 item，Int）与 `TriggerInput`（原 itrigger，Bool）同步并保存，默认值均为 0，共 9 bit，额外占用为 0。炮弹随机、命中、食用和敲击参数保持原版 Animator 内部参数；随机 Driver 与接触器的 localOnly 均沿用原版关闭状态。
- MA Merge Animator 使用相对路径；MA Bone Proxy 在构建时自动重定位动画。
- 物品控制器沿用原版的 Write Defaults Off；关闭 Merge Animator 的自动匹配 Write Defaults，以保留这些层之间原有的分工。
- 不包含酒狐身体、面部表情、服饰、PhysBone 或角色专属 Action 层；不需要酒狐原工程或独立物品 FBX。
- 加农炮改用 SDK 原生 VRCParentConstraint 的 FreezeToWorld，发射时固定炮弹坐标，复位后继续跟随手部，无需原版世界参考物或场景绝对坐标。
- `Editor/WineFoxItemsBuildPlugin.cs` 必须保留：它在 NDMF 构建时把本配件骨骼加入 FX 遮罩，避免目标 avatar 的默认遮罩阻止炮弹移动。不会添加运行时脚本。
- 未选中的物品和食品阶段收缩为零面积三角形，保留单个渲染器；空手关闭渲染器。炮弹在非发射状态缩放严格为零，不再依赖藏进手掌。
- 此预制件预期每个 avatar 安装一份。

## 重新提取

将原仓库的 `Assets` 内容复制到此工程的 `Assets/WineFoxSource`，然后运行 `Tools > Wine Fox Items > Extract from final merged avatar`。此操作重新生成派生网格、动画、菜单、材质和 tester 场景，会覆盖这些生成资源；请先另存自定义调整。验证后，删除临时的 `WineFoxSource` 不影响成品。

实现参考：[MA Bone Proxy](https://modular-avatar.nadena.dev/docs/reference/bone-proxy)、[MA Merge Animator](https://modular-avatar.nadena.dev/docs/reference/merge-animator)。

## 署名与许可

沿用原项目声明的 CC BY-NC-SA 4.0（署名、非商业性、相同方式共享），详见 `LICENSE.txt`。请保留署名及原始资源的来源信息。

原作者：完美冻结、星屑海螺、哥斯拉（映素团队）、白帆小喵、蓝玫瑰、墨染逝羽、Maks怜悯、浅陌菌、祸御神、羊毛、再来几斤小莫莫、Tex_offcial。原 VRChat 改编者：YWsuoyi。本目录为手持物品拆分与 MA 适配修改。

角色来源为 Minecraft 的 Touhou Little Maid；相关物品来源为 Minecraft、Create、Create: Aeronautics。世界约束来源：[VRLabs/World-Constraint](https://github.com/VRLabs/World-Constraint)。原项目的许可声明与第三方资源说明一并保留于 LICENSE.txt。
