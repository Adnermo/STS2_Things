using System.Collections;
using System.Reflection;
using Godot;
using System.Text.Json;

public partial class PortraitCompatibilityProbeNode
{
    private void VerifyLuolinFeatures(Assembly foreign)
    {
        Type animations = foreign.GetType("LuolinCard.AnimationReplacementRegistry", true)!;
        IDictionary entries = (IDictionary)animations.GetField("Entries", PrivateStatic)!.GetValue(null)!;
        int animationCount = entries.Count;
        using JsonDocument sourceImages = JsonDocument.Parse(_primaryReplacementJson);
        string[] imagePaths = sourceImages.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("image").GetString()!).Distinct().Take(2).ToArray();
        Require(imagePaths.Length == 2, "Two actual workshop images are available for feature fixtures.");
        // The shipped 0.5.0 tables have no enabled animation/random records.
        // Use private test-only records with real pack images and the original
        // registry implementation; do not claim these are shipped animations.
        string fixtureId = "ThingsProbe.LuolinFeatureFixture";
        if (entries.Count == 0)
        {
            Type entryType = animations.GetNestedType("AnimationEntry", BindingFlags.NonPublic)!;
            Type frameType = animations.GetNestedType("AnimationFrame", BindingFlags.NonPublic)!;
            object entry = Activator.CreateInstance(entryType, true)!;
            Set(entry, "CardId", fixtureId);
            Set(entry, "Loop", true);
            IList frames = (IList)entryType.GetProperty("Frames")!.GetValue(entry)!;
            foreach (string path in imagePaths)
            {
                object frame = Activator.CreateInstance(frameType, true)!;
                Set(frame, "Image", path);
                Set(frame, "DurationMs", 100);
                frames.Add(frame);
            }
            entries[fixtureId] = entry;
        }
        if (entries.Count > 0)
        {
            string id = (string)entries.Keys.Cast<object>().First();
            object entry = entries[id]!;
            IList frames = (IList)entry.GetType().GetProperty("Frames")!.GetValue(entry)!;
            Require(frames.Count > 0, "Workshop animation contains real frames.");
            var rect = new TextureRect();
            AddChild(rect);
            Require((bool)animations.GetMethod("Bind")!.Invoke(null, [id, new TextureRect?[] { rect }])!, "Actual animation targets still bind.");
            Require(GodotObject.IsInstanceValid(rect.Texture), "Actual animation frame is displayed.");
            string initialPath = rect.Texture!.ResourcePath;
            animations.GetMethod("Tick", PrivateStatic)!.Invoke(null, [.15d]);
            Require(GodotObject.IsInstanceValid(rect.Texture) && rect.Texture!.ResourcePath != initialPath,
                "Original animation registry advances to a distinct real image.");
            animations.GetMethod("UnbindTargets")!.Invoke(null, [new TextureRect?[] { rect }]);
            RemoveChild(rect);
            rect.Free();
            animations.GetMethod("Tick", PrivateStatic)!.Invoke(null, [.5d]);
            Cases.Add("luolin-animation-bind-unbind");
        }

        Type random = foreign.GetType("LuolinCard.RandomReplacementRegistry", true)!;
        IDictionary records = (IDictionary)random.GetField("Records", PrivateStatic)!.GetValue(null)!;
        int randomCount = records.Count;
        if (records.Count == 0)
        {
            Type recordType = random.GetNestedType("RandomRecord", BindingFlags.NonPublic)!;
            Type faceType = random.GetNestedType("RandomFace", BindingFlags.NonPublic)!;
            object record = Activator.CreateInstance(recordType, true)!;
            Set(record, "CardId", fixtureId);
            Set(record, "PlaybackMode", "Sequential");
            IList faces = (IList)recordType.GetProperty("Faces")!.GetValue(record)!;
            IList previews = (IList)recordType.GetProperty("PreviewFaceIds")!.GetValue(record)!;
            for (int i = 0; i < imagePaths.Length; i++)
            {
                object face = Activator.CreateInstance(faceType, true)!;
                Set(face, "Id", "face" + i);
                Set(face, "SourcePath", imagePaths[i]);
                faces.Add(face);
                previews.Add("face" + i);
            }
            records[fixtureId] = record;
        }
        if (records.Count > 0)
        {
            string id = (string)records.Keys.Cast<object>().First();
            object?[] first = [id, 1001UL, 1001, null];
            Require((bool)random.GetMethod("TryGetTexture")!.Invoke(null, first)!, "Real random card-face record resolves.");
            Require(first[3] is Texture2D texture && GodotObject.IsInstanceValid(texture), "Random replacement is a living texture.");
            random.GetMethod("AdvanceSequentialAfterPlayed")!.Invoke(null, [id, 1001]);
            object?[] next = [id, 1001UL, 1001, null];
            Require((bool)random.GetMethod("TryGetTexture")!.Invoke(null, next)!, "Random/sequential resolution remains available after card-play advancement.");
            Require(((Texture2D)first[3]!).ResourcePath != ((Texture2D)next[3]!).ResourcePath, "Sequential face advances to a distinct workshop image.");
            Cases.Add("luolin-random-resolution");
        }
        entries.Remove(fixtureId);
        records.Remove(fixtureId);
        GD.Print($"LUOLIN_FEATURES actual_animation_records={animationCount} actual_random_records={randomCount}");
    }

    private static void Set(object value, string name, object propertyValue) => value.GetType().GetProperty(name)!.SetValue(value, propertyValue);
}
