import fs from 'node:fs';
import * as S from '../ScaleBeetleRig/node_modules/@esotericsoftware/spine-core/dist/index.js';

export function load(jsonPath, atlasPath) {
  const json = JSON.parse(fs.readFileSync(jsonPath, 'utf8').replace(/^\uFEFF/, ''));
  const atlas = new S.TextureAtlas(fs.readFileSync(atlasPath, 'utf8'));
  for (const page of atlas.pages) {
    page.setTexture({
      setFilters() {},
      setWraps() {},
      dispose() {},
      getImage() { return { width: page.width, height: page.height }; },
    });
  }
  return new S.SkeletonJson(new S.AtlasAttachmentLoader(atlas)).readSkeletonData(json);
}

export function pose(data, animation, time = 0, skinName = null) {
  const skeleton = new S.Skeleton(data);
  if (skinName) {
    skeleton.setSkinByName(skinName);
  }
  skeleton.setToSetupPose();
  if (animation) {
    data.findAnimation(animation).apply(
      skeleton, 0, time, false, [], 1, S.MixBlend.setup, S.MixDirection.mixIn
    );
  }
  skeleton.updateWorldTransform(S.Physics.none);
  return skeleton;
}

export function worldVertices(slot, attachment = slot.attachment) {
  if (attachment instanceof S.RegionAttachment) {
    const vertices = new Float32Array(8);
    attachment.computeWorldVertices(slot, vertices, 0, 2);
    return Array.from(vertices);
  }
  if (attachment instanceof S.MeshAttachment) {
    const vertices = new Float32Array(attachment.worldVerticesLength);
    attachment.computeWorldVertices(slot, 0, vertices.length, vertices, 0, 2);
    return Array.from(vertices);
  }
  return [];
}
