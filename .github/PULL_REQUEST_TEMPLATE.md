## 变更摘要 / Summary

- 简要说明本次 PR 的改动目的与影响范围。

## 核对清单 / Checklist

- [ ] 仅使用原版游戏 API、Godot 与 Harmony，未引入 BaseLib / RitsuLib 编译期依赖
- [ ] 中英文本地化（`STS2_Things/localization/zhs` 与 `eng`）已同步
- [ ] 已运行 `python scripts/verify_project.py` 与 `python scripts/verify_fmod.py` 并全部通过
- [ ] 涉及玩法或双版本 API 改动时，已在 `v0.111.1` 与 `v0.113.1` 下验证构建与探针
