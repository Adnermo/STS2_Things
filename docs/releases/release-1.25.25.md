# 尖塔：琐事 1.25.25 / STS2 Things 1.25.25

- 修复余影魔「借影」漏算间接伤害。故障机器人的电球（被动和激发）、猎人的中毒，以及荆棘等有玩家来源的伤害，现在都会给对应玩家返还等量格挡。
- 格挡按实际失去的生命计算，已格挡的伤害、超杀和无玩家来源的伤害不计。多人共同叠毒时，按仍生效的毒层归属分摊格挡，不重复返还给队友。
- 支持游戏 v0.107.1 / v0.111.0。

---

- Fixed Fleeting Echo's Borrowed Shadow: Lightning Orb passives and evokes, Poison, Thorns, and other player-sourced damage now return Block to the responsible player.
- Only actual HP loss counts; blocked damage, overkill, and damage without a player source do not count. In co-op, shared Poison damage is split by the remaining stacks from each player, without duplicating Block across the party.
- Supports game versions v0.107.1 / v0.111.0.
