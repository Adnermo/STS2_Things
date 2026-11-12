"""Review videos from render_native.gd captures: python make_review.py AFTER BEFORE OUTPUT_DIR

AFTER and BEFORE hold the same clips rendered at the same FPS. Writes the Chinese-labelled preview
and a left/right before/after comparison.
"""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'CaveGodRig'))
import sheet  # noqa: E402

sheet.LABELS.update({
    'idle_loop': '待机', 'attack': '撕咬', 'whip': '触角三连鞭', 'cast': '重构化', 'molt': '蜕壳',
    'power_up': '强化', 'hurt': '受击', 'die': '死亡', 'revive': '复起', 'summon': '登场',
})
CLIPS = ['summon', 'idle_loop', 'cast', 'attack', 'whip', 'molt', 'power_up', 'hurt', 'die', 'revive']
CROP = (0.05, 0.04, 0.98, 0.93)

after, before, out = (Path(arg) for arg in sys.argv[1:4])
print(sheet.reel(after, CLIPS, out / '放缩巨甲虫-动作重做预览.mp4', width=1280, crop=CROP))
print(sheet.compare(before, after, CLIPS, out / '放缩巨甲虫-重做前后对比.mp4', width=800, crop=CROP))
