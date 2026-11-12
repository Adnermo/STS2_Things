# 放缩巨甲虫 · Native Spine 4.2

保留 `source_assets/monsters/scale_beetle.png` 原画，以源像素作为骨骼单位。
工程使用 54 根骨骼、24 个槽位：六足、两条七节触角、头颈、双颚、分层甲壳，
以及与宿主网格完全一致的发光层。原点 `(563.5, 883)`，场景位置 `(0, 0)`、
缩放 `0.52`，与原静态图的战斗位置一致。

参考了现有三套 Boss：腐化之遗的动作滞后，盛碗虫族母的负重与落足，
始源雾菇的接缝处理。网格/导出工具从已有 rigkit 独立继承；本目录修改不影响参考 Boss。

## 动作

2026-11-09 起的动作由 `anims.py` 重新编排，设计与验证见 [docs/scale-beetle-motion-redo-20261109.md](../../docs/scale-beetle-motion-redo-20261109.md)。

| 动画 | 秒 | 设计与接入 |
| --- | ---: | --- |
| `idle_loop` | 4.8 | 慢吸快呼的负重呼吸，头部分拍点动，外侧触角领先探测，下颚两次轻扣，前足轻点，后足调整 |
| `attack` | 1.95 | 后仰抬前足、双颚大张发颤；前足先落、整身前扑；**0.68 s** 合颚咬住甩头，再分两批迈步退回 |
| `whip` | 2.75 | 身体后顿、触角后引；身体和头先停，甩动沿触角传到末端。**0.60** 近触角、**1.12** 远触角、**1.68 s** 双触角加前足踏地；0.88／1.40 的蓄力姿势可停住等待结算 |
| `cast` | 2.3 | 下沉收拢，昂起时触角分开并嗡鸣，一瞬下压；**0.76 s** 双触角朝玩家刺出、全身发光并保持共鸣，随后甲壳再亮一次后落定 |
| `molt` | 2.65 | 六足外撑低伏，身体两次抽动，甲壳绷紧、由暗转亮；**0.80 s** 猛然压下，闪光震颤，随后抬身、足回位 |
| `power_up` | 2.2 | 低头弓背、触角前指如角，近侧三足滚动跺地，光从眼传到尾；1.12 昂首张颚、全身闪光，1.42 抖震 |
| `hurt` | 0.85 | 被打得后滑抬头，前足被拖离地面，全身震颤，带超调回位 |
| `die` | 2.8 | 受击后仰、腿软、挣扎再起，触角先失力下垂；**1.45 s** 砸地反弹，两次余搐后光熄灭，保持尸体姿态 |
| `revive` | 2.2 | 从死亡终姿开始：眼先亮，六足逐条收回，臀部先撑起，再挺身抬头，抖一下落定 |
| `summon` | 2.2 | 蜷缩后挺立，前足刨空、大张双颚；**0.88 s** 重重落地前踏，甩头后起身 |

`motion.py` 是曲线与波形工具，`lint.mjs` 一次列出 `verify.mjs` 的全部断言并统计网格翻折，
`geometry.py reach` 查看每段动作各足的余量（前足和两条远足在静止时几乎伸直，身体抬高或后退前要先移足），
`render_native.gd` 和 `make_review.py` 输出未部署骨骼的原生预览和前后对比视频。

### 甲壳切口

底腹、外甲、领甲、颈盾、头、眼和双颚是从同一张原画切出的刚性分件，静止时只互相压住几个像素，
任何一块单独转动或平移都会露出切口。所以整副甲壳锁在身体上一起运动，命中时由 `F.shudder()` 全身一起震颤。
头部俯仰写成 `F.pitch()`，由 `F.done()` 分配：低头时头、颈盾和领甲作为前段绕 `(460, 280)` 转动，最多约 15°；
抬头只转头部，绕 `(310, 520)`，最多约 11°；远侧下颚绕 `(70, 660)` 开合。坐标为原画源像素，
铰链和上限来自 `python slack.py`（各组分件的最佳铰链、各方向的安全角度和限制它的分件）。
原画没有甲壳内侧，因此不能做真正的开壳。

`rebuild.ps1` 在 `verify.mjs` 之后用 `node seam_dump.mjs` 按 60 Hz 导出分件矩阵，再运行
`python seams.py check out\seam-bones.json 4.0`：任何动作露出静止时被遮住的切口超过 4 源像素（战斗中约 2 屏幕像素）
或 40 像素²，就停止导出。`seams.py report` 列出每段动作露出最深的位置，`seams.py overlay OUT.png` 画出全部隐藏切口。

`ThingsScaleBeetle` 继承 `ThingsSpineMonster`，通过 `Whip`、`Molt` 触发器接入。
伤害、命中次数、格挡、放缩层数、音效与一次原生多段攻击的语义保持一致。
命中和释放对齐 Spine 的绝对时间；快速／即时模式同样提交关键姿态。
行动结束前完成回位，外部 `ScaleTo` 继续控制实际放大。
`NThingsScaleBeetleMotion` 在两次后续鞭击的蓄力点等待上一击的原生效果结算；
命中后的缩小、多人目标或其他伤害 Hook 不会让后续命中提前空播。受击、死亡与离场会解除等待。

## 美术来源和局部编辑

`parts.py` 先拆分原画并补齐遮挡区。触角和每条腿各自使用连续索引网格，
关节附近平滑混合权重，长骨段及甲壳保持刚性。六足通过固定朝向的双骨 IK 烘焙，
触角的附加惯性以 240 Hz 模拟，最后导出 60 Hz 关键帧并在误差范围内精简。

按用户授权，通过所提供接口调用 **CLI Image API `gpt-image-2 edit` / high**，
局部补绘了 `head`、`shell_near`、`shell_far`、`body` 四张分件。
生成结果经位置校准、局部色彩匹配和蒙版合成；蒙版之外的像素与原 alpha 保持一致。
后续抠图归属修正直接搬移原画像素，例如把远前腿误带的下颌碎片归还给头部。

- 最终补绘输入：`source_assets/monsters/scale_beetle_rig_patches/`（四张 PNG、四张蒙版、清单）。
- 完整提示词：本目录 `art_edits/head.txt`、`shell_near.txt`、`shell_far.txt`、`body.txt`。
- `prepare_edits.py` / `integrate_edits.py`：准备编辑输入，以及只接收蒙版内改动。
- API 密钥不写入仓库；**正常重建无需任何图像 API 或密钥**。

## 重建

依赖：Python 3、`numpy opencv-python scipy pillow`，以及 Node.js/npm。
Spine 官方运行时锁定在 `package-lock.json` 的 `4.2.43`。

```powershell
.\tools\ScaleBeetleRig\rebuild.ps1          # 生成并验证 pkg/
.\tools\ScaleBeetleRig\rebuild.ps1 -Deploy  # 验证后更新工程内资源
.\scripts\build.ps1 -TargetVersion v111    # 导入、编译、导出并验证 PCK
```

生成目录 `parts/`、`out/`、`pkg/`、`prev/` 均已忽略，可再生成。
发布目录为 `STS2_Things/animations/monsters/scale_beetle/`。
静态场景生成器和旧切片骨架生成器均已保护当前场景，避免重建时覆盖新动画。

如需重新进行图像编辑，先运行 `python parts.py --raw` 和 `python prepare_edits.py`，
按提示词调用 imagegen 技能的 `image_gen.py edit`，再运行 `python integrate_edits.py`。
重新编辑输入使用 `tmp/imagegen/scale-beetle/`，原始生成输出使用
`output/imagegen/scale-beetle/`；最终选定补绘保存到上述 `source_assets` 子目录。

## 验证与预览

`verify.mjs` 读取实际打包的 Spine 数据，通过官方运行时采样全部动画（120 Hz），
检查待机闭环、死亡/复起衔接、足爪目标、至少三足支撑、地面滑动、头部与触角离地、甲壳刚性、发光层贴合、命中时间、动作幅度及触角出手方向。
`rebuild.ps1` 随后运行上文的切口检查。

使用项目现有的 Spine-enabled Godot 4.5.1 Mono 可执行文件：

```powershell
godot --headless --path . --script res://scripts/verify_scale_beetle_spine_scene.gd -- build/v111/STS2_Things.pck
godot --path . --rendering-method gl_compatibility --script res://scripts/render_scale_beetle_visual_probe.gd -- build/beetle-review 20 all
godot --path . --rendering-method gl_compatibility --script res://scripts/render_scale_beetle_visual_probe.gd -- build/beetle-mask 20 all silhouette
python tools/ScaleBeetleRig/verify_render.py build/beetle-mask
```

更新 PNG 后须先运行 Godot import。轮廓模式在真实网格上检查透明度，排除阴影和发光，
用于发现断开的分件及镜头裁切；最终外观仍以彩色帧和动画预览为准。
当前动作的证据位于 `build/beetle_motion/`（首版重做）和 `build/beetle_tear/`（割裂修复），最终包的探针在 `build/merchant_20261111/`；上一版大幅动作的证据位于 `build/scale-beetle-impact-20261003/`，初版素材制作记录保存在 `build/scale-beetle-animation-20261002/`。

原生战斗检查：`.\scripts\test-scale-beetle-actions.ps1`。状态栏图标可由 `python scripts/build_scale_beetle_power_icons.py` 从现有原图重建。
