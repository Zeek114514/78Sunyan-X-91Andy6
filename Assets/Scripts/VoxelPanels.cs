using UnityEngine;

/// <summary>
/// 面板总入口：谁占着屏幕、谁占着输入，全项目只问这一个地方。
///
/// 以前每加一个面板（背包、角色…）都要在十来个文件里补一句
/// "!VoxelChat.IsOpen &amp;&amp; !VoxelGacha.IsOpen &amp;&amp; …"，漏一处就会出现两个面板抢按键。
/// 现在各系统的判断统一换成下面三个入口，新增面板只要在这个文件里加一项。
///
/// 【两类面板】
///   · 全屏面板（ModalOpen）：开幕动画 / 菜单 / 背包 / 角色 / 祈愿 / 大地图 —— 它们开着时屏幕归它们，
///     HUD 收起来，玩家的移动 / 攻击 / 交互 / 开箱全停；
///   · 对话框（ChatOpen）：只有一条输入框，但键盘焦点在它身上，也算"占着输入"。
/// </summary>
public static class VoxelPanels
{
    /// <summary>有没有全屏面板开着（新增面板在这里加一项即可）</summary>
    public static bool ModalOpen =>
        VoxelTutorial.PanelOpen || TutorialPortal.IsOpen ||
        TitleScreen.IsOpen || VoxelMenu.IsOpen || VoxelInventory.IsOpen ||
        VoxelCharacterScreen.IsOpen || VoxelGacha.IsOpen || WorldMap.IsOpen;

    /// <summary>对话框（T）开着没有</summary>
    public static bool ChatOpen => VoxelChat.IsOpen;

    /// <summary>有面板占着输入（全屏面板或对话框）—— 按 T / P / B / C / M / Esc 之前问它</summary>
    public static bool InputBlocked => ModalOpen || ChatOpen;

    /// <summary>现在能不能玩游戏：鼠标锁着、也没有面板占着输入。
    /// 移动 / 攻击 / 交互 / 开箱 / 点亮机关之前都问它</summary>
    public static bool CanUseInput => Cursor.lockState == CursorLockMode.Locked && !InputBlocked;
}
