# 酒狐手持物品 · Modular Avatar

包含六种工具、五种食品和六种炮弹形态。

## 资源占用

| 项目 | 占用 |
| --- | --- |
| 表达参数 | 2 个，共 **9 bit** |
| 渲染器 / 材质 | 1 个 SkinnedMeshRenderer / 1 个材质 |
| 主贴图 | 128 × 64 |
| 顶点 / 三角面 | 7,130 / 4,272 |
| 蒙皮骨骼 / 形态键 | 12 / 35 |
| FX 动画层 | 7 层 |

未选物品完全隐藏，空手关闭渲染器。

## 安装

使用 Unity 2022.3.22f1，安装 VRChat Avatars SDK、Modular Avatar 及其 NDMF 依赖。目标 avatar 需要有效的 Humanoid 配置。

1. 导入 `WineFoxItems-MA.unitypackage`。
2. 将 `Assets/WineFoxItems/WineFoxItems.prefab` 放到 **avatar 根节点下面**，每个 avatar 一份。
3. 预制件根节点保持本地位置、旋转为零，缩放为一。MA 自动绑定右手，并合并菜单、FX 和参数。
4. 握持位置不合适时，调整 `RightHandAnchor/GripAdjust`；`RightHandRig` 保持原始缩放。

## 使用

在「物品」或「食品」菜单中选择物品，再次点击当前选项即可收起。「扳机」开关或右手 RockNRoll 手势用于操作加农炮、机械手和物理手杖；食品靠近头部时触发食用。

## 来源

物品素材来自 Create、Create: Aeronautics 和 Minecraft。VRChat 转换与动画来自[酒狐工程](https://github.com/donywang922/vrc-wine-fox)，改编者 YWsuoyi。作者与许可见 [LICENSE.txt](LICENSE.txt)。
