# Unity 迁移副本

开发目录：`D:\Projects\VoxelDemo-Unity`。使用 Unity 2022.3.62f3c1，URP 14.0.12。打开 `Assets/Scenes/SampleScene.unity`。由团结项目迁入的独立开发副本，原项目仍保留。

# 78Sunyan X 91Andy6

> Sunyan's adventure, to find his favorite Andy

基于 **Unity**的类原神风格开放世界 Demo。单人练习项目，公开仅供观看与学习。

---

## 当前开发状态

本仓库提供琰神的独立 Unity 开发副本源码（2026-10-27更新）。功能进展和仍待解决的验证问题见 [开发进度](PROGRESS.md)。

- 启动流程为登录开场 → 原开放世界；出生点附近的蓝金传送门可按 E 自选进入教程，完成或提前退出后返回原世界。
- 更新蒙德全城民居、教堂等地标、怪物和营地的程序化造型；本轮C#编译与几何检查通过，新版外观、传送门往返及帧率仍待实机验收。
- 蕾米埃尔属于本地编辑器试验。受限角色模型不随仓库分发；没有该模型时，角色名单不会显示她，教程和原世界仍使用原有内容。
- 运行所需包在 `Packages/manifest.json`；不依赖本机的文件包路径。克隆后需让 Unity 重新导入资源并恢复包。

最新细节见[蒙德街区](MONDSTADT_STREET_PASS.md)、[可选教程与怪物](OPTIONAL_TUTORIAL_AND_MONSTERS.md)和[本次同步验证](Docs/UPDATE_2026-10-27.md)。

## 这是什么

一个从"体素方块"转型而来的开放世界项目。世界、地形、建筑、NPC、怪物、宝箱、解密场地**全部由 C# 脚本在运行时程序化生成**——没有手工摆放完整开放世界的预制场景，按下 Play 从登录和教程进入。

- 世界尺寸：**1800 × 1800 米**（1080 格，约 1.667 米/格），4 个地形分块
- 水面高度 3.2 米；雪线 62 米
- 主角 **孙琰（SunYan）** 在提瓦特式的世界里冒险，目标是找到 **Andy**

## 运行环境

| 项目 | 版本 |
| --- | --- |
| Unity 编辑器 | 2022.3.62f3c1 |
| 原始来源 | Tuanjie 1.10.0 / 2022.3.62t12 |
| 渲染管线 | URP 14.0.12 |

### 怎么打开跑起来

1. 用 **Unity Hub** 添加本地项目，选中本仓库根目录
2. 打开场景 `Assets/Scenes/SampleScene.unity`
3. 点 **Play**，经过登录开场后进入开放世界；需要教程时靠近出生点传送门，瞄准并按 **E** 选择进入

场景本身是空的也没关系：`Assets/Scripts/VoxelBootstrap.cs` 会在场景加载后启动登录流程并创建开放世界；教程入口会暂存并恢复现有世界。地形、建筑、怪物、宝箱和 NPC 均在运行时生成。

## 操作

| 按键 | 作用 |
| --- | --- |
| `W` `A` `S` `D` | 移动 |
| 鼠标 | 转视角 |
| `Shift` | 冲刺 |
| `空格` | 跳跃 / 飞行时上升 |
| `Ctrl` | 飞行时下降 |
| 鼠标左键 | 普攻（打野外怪） |
| `Q` | 丢道具 |
| `E` | 开宝箱 / 选择进入教程传送门 |
| `T` | 对话框 |
| `P` | 祈愿（抽卡界面） |
| `B` | 背包 |
| `C` | 角色界面 |
| 鼠标右键 | 战技 |
| `F` | 元素爆发 |
| `M` | 世界地图（`Tab` 切换选中节点，`Enter` 传送，`Esc` 关闭） |
| `Esc` | 菜单（成就 / 角色 / 地区 / 操作） |

控制台命令：`/fly` 飞行、`/eat` 恢复饱食度。

## 世界与区域

- **蒙德 · 湖心城** —— 湖畔石城、草原丘陵、风车与广场
- **璃月** —— 港城（码头、吊脚楼、牌坊、石拱桥）+ 喀斯特峰林村落
- **稻妻** —— 东南海上的群岛：鸣神岛（将军府天守、城下町）、西岸港口、两座离岛（神社、五重塔）
- **Andy 的雪顶** —— 东北方向的雪线以上雪山
- **七天神像** —— 散布各区域的元素神像，可回复并计数

## 玩法系统

- **数值**：生命值 / 饱食度
- **战斗**：鼠标左键普攻
- **野外怪**：丘丘人、史莱姆，以及丘丘人营地（固定哈希布点，避开村落与宝箱）
- **战利品宝箱**：5 个品质散落世界各处，部分需要清掉守卫才能开启
- **解密场地**：元素方碑（蒙德/野外）、石灯阵与石鼓（璃月）
- **传送锚点**：走近自动点亮，之后可在世界地图上点击传送
- **NPC**：孙琰、Andy（数据驱动的对话与行为，支持"追/躲某个名字的 NPC"）
- **成就**：7 个——Andy的港门、将军府、Andy的雪顶、湖心城、喀斯特峰林、一览众山、七天神像
- **界面**：HUD、小地图 + 可拖拽世界地图、Esc 菜单、祈愿（抽卡）页、标题开场画面

## 目录结构

```
Assets/
├─ Scenes/SampleScene.unity      # 唯一场景（空场景也能跑）
├─ Scripts/
│  ├─ VoxelBootstrap.cs          # 入口：运行时搭出整个世界
│  ├─ OpenWorld.cs               # 地形、顶点色分区、树石、湖水、雾、后处理
│  ├─ VoxelPlayer.cs             # 移动 / 视角 / 冲刺 / 飞行
│  ├─ VoxelCombat.cs             # 普攻
│  ├─ VoxelStats.cs              # 生命 / 饱食度
│  ├─ VoxelInteraction.cs        # 交互
│  ├─ VoxelHud.cs / VoxelMenu.cs / TitleScreen.cs
│  ├─ WorldMap.cs                # 大地图 + 小地图
│  ├─ MondstadtTown.cs           # 蒙德湖心城
│  ├─ WildSiteSpawner.cs / WildBuildings.cs / WildPuzzles.cs
│  ├─ LiyueSiteSpawner.cs / LiyueBuildings.cs / LiyueHarborCity.cs /
│  │  LiyueHarbor.cs / LiyuePuzzles.cs / LiyuePuzzleSpawner.cs
│  ├─ InazumaSiteSpawner.cs / InazumaIsles.cs / InazumaBuildings.cs
│  ├─ MonsterSpawner.cs / VoxelMonster.cs
│  ├─ LootChest.cs / LootChestSpawner.cs
│  ├─ PuzzleSite.cs / PuzzleSpawner.cs
│  ├─ TeleportAnchor.cs / TeleportSpawner.cs
│  ├─ VoxelNpc.cs / VoxelNpcData.cs / VoxelChat.cs
│  ├─ Achievements.cs / VoxelGacha.cs / VoxelStats.cs
│  ├─ LowPoly.cs                 # 低多边形造型工具
│  └─ VoxelWorld.cs              # 已弃用的旧体素实现（保留，未使用）
├─ Resources/                    # 3D 模型与贴图（FBX / PNG）
└─ Settings/                     # URP 管线配置
Packages/manifest.json
ProjectSettings/
```

## 已知注意事项

1. **AI 资产生成工具未包含在仓库里**。开发时用到的 Codely `TJGenerators` 扩展（提供一键生成 3D 模型 / 贴图 / 音效的编辑器工具）是**本机安装物，不随仓库发布**，其 `Packages/manifest.json` 依赖声明也已移除。**克隆后可用对应 Unity 编辑器打开并恢复包**，运行时代码不依赖它；若想在本机恢复该工具，重新安装 Codely 的 TJGenerators 扩展即可（它会自动把依赖加回去）。
2. **编辑器生成物未上传**（`Library/` `Temp/` `obj/` `Logs/` `UserSettings/` 等约 2.8 GB 已在 `.gitignore` 中忽略），clone 后需要让引擎重新导入一次。
3. 项目中的部分 3D 模型与贴图为 **AI 生成工具产出**，非原创手绘素材。

## 声明

本项目为个人学习作品，视觉风格参考《原神》。本地试验用的官方角色模型不纳入仓库；第三方字体、动作资料的来源及附带许可证保留在各资源目录。请勿将本项目当作具有完整商业授权的成品。


## 在 Unity 内使用 Codex

菜单 **VoxelDemo → Codex 聊天** 可打开内嵌面板，点击连接后即可聊天。修改代码前勾选“允许修改当前项目”。详细说明见 `Docs/Codex面板.md`。面板只在编辑器运行，不进入游戏。
