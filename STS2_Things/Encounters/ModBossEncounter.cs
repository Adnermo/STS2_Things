using MegaCrit.Sts2.Core.Models;

namespace STS2_Things.Encounters;

/// <summary>
///     Mod Boss 遭遇基类 — 统一地图图标路径为 res://images/map/{IconName}_boss_icon
/// </summary>
public abstract class ModBossEncounter : EncounterModel
{
    protected abstract string IconName { get; }

    public override string BossNodePath =>
        $"res://images/map/{IconName}_boss_icon";
}