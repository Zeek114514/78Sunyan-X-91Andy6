# 游戏界面字体

主字体为用户指定的 HYWenHei Extended，资源名 HYWenHei-85W，内部字体族名 Genshin-Impact。

- 来源：https://github.com/cawa-93/HYWenHei-Extended-Font
- 原文件：HYWenHei Extended.ttf
- 下载日期：2026-10-21
- SHA256：9f88c19a8d0e68d97d97fb5b15865d569fe758aae51aed63b8f37698f16a44be
- 仓库附带许可保存在 HYWenHei-LICENSE.txt。
- VoxelTypography.GameFont 优先加载 Fonts/HYWenHei-85W；缺失时加载 NotoSansSC-Medium。
- 保留 Noto Sans SC Medium 及其 OFL.txt 作为备用字体。
- 运行时 IMGUI 在创建样式之前调用 VoxelTypography.Apply，标题也使用同一字体入口。
