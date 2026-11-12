# 活体巨岩动作维护源

活体巨岩的全部游戏内动作（49 段，含普通／愤怒、左右手版本）由本目录生成，不再手工逐帧修改 `cave_god.spjson`。骨架、插槽、蒙皮和旧素材自带的未使用动画仍来自 `source_assets/monsters/cave_god_motion/contact_base.spjson`。

| 文件 | 作用 |
| --- | --- |
| `clips.py` | 每段动作的编排：身体通道的姿态关键帧、两只手腕的世界坐标路径、手腕弯折、开合手时间、打击时刻 |
| `motion.py` | Hermite 关键帧曲线、呼吸／颤抖等可循环波形、二阶跟随与胡须摆动 |
| `rig.py` | Spine 4.2 正向运动学和两骨 IK（与官方运行时误差 < 0.001 px） |
| `build.py` | 240 Hz 推导次级动作，120 Hz 求解、精简关键帧，写出动作基准 |
| `verify.mjs` / `compare_contact.mjs` / `snatch_metrics.mjs` | 官方运行时检查：首尾与循环衔接、逐帧跳变、网格三角翻折；接触 IK 复现误差；偷牌手腕指标 |
| `render_native.gd` / `sheet.py` | Godot 原生 Spine 按实战两层合成渲染（身体在平台后、手臂在平台前，死亡时平台在最前），拼图与 MP4 |
| `inspect_pose.py` / `pose_dump.mjs` | 查看指定时刻的肩、肘、腕世界坐标 |

## 重建

从项目根目录依次运行：

```powershell
python tools/CaveGodRig/build.py                      # -> build/cavegod_motion/motion_base.spjson
node scripts/build_cavegod_contact.mjs                # -> animations/monsters/cave_god/cave_god.spjson 与接触配置
node tools/CaveGodRig/compare_contact.mjs build/cavegod_motion/motion_base.spjson animations/monsters/cave_god/cave_god.spjson
node tools/CaveGodRig/verify.mjs animations/monsters/cave_god/cave_god.spjson
node tools/CaveGodRig/snatch_metrics.mjs
```

`build_cavegod_contact.mjs` 把出击手臂转为 IK 加世界方向手腕，生成 `CaveGodContactProfiles.cs` 与 `contact_profiles.json`。没有运行时瞄准偏移时，它必须复现编排结果（当前最大误差 0.14 px）。

原生预览（需要 Spine 插件的 Godot 4.5.1）：

```powershell
godot --path . --rendering-method gl_compatibility --resolution 960x540 --script res://tools/CaveGodRig/render_native.gd -- <输出目录> <spjson 绝对路径> 20 idle_front,central_slam 0.5
python tools/CaveGodRig/sheet.py <输出目录> idle_front,central_slam <输出>.mp4 --reel
```

片段名写成 `clip@0.3|0.64` 时只渲染这些时刻。

## 编排约束

- 打击帧、动作时长和事件由玩法固定（`CaveGodAnimTiming`、`card_snatch_motion.json`、`scripts/verify_project.py`），改动作时不要移动。
- 静止姿态等于旧 `idle_front` 第 0 帧，手臂血条和目标锚点位置不变。所有攻击都精确回到对应待机首帧。
- 这个正面机位下肩膀接近画面顶边：举起的拳头要在肩膀内侧上抬，让肘部朝外下垂；拳头在肩外侧时要保持低位或手臂接近伸直，否则前臂会从画面外垂下来。`build.py` 会报告肘部出画或悬垂。
- 手腕按相对前臂的向内弯折编排（0 为静止），限制在 −8° 到 38°，这是两侧手掌网格都不翻折三角形的范围。
- 偷牌冲刺从静止缓入缓出，`CaveGodCardSnatchRegression` 限制手腕逐帧速度变化 ≤ 3 px；拳击、砸击才在命中帧硬停。
- `hit_recoil` 只存相对静止姿态的增量，由控制器以 MixBlend.add 叠加在当前动作上；肩膀保持世界位置，按地的拳头不滑动。
- `grab_hold*` 是新增的抓握循环；`grab_player` 从 1.90 秒起停在同一握持姿态，`grab_slam` 从该姿态起手。
