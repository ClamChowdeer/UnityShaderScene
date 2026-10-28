using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class HatchValidation
    {
        internal static void Run(Camera camera, Transform scope)
        {
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            var copy = ScriptableObject.CreateInstance<LookProfile>();
            try
            {
                LookPresets.Apply(profile, LookStyle.InkHatching);
                using var preview = new PreviewSession();
                byte[] Capture(float degrees)
                {
                    profile.hatchRotation = degrees;
                    preview.Capture(camera, profile, scope, 960, 540);
                    var prior = RenderTexture.active;
                    var texture = new Texture2D(960, 540, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = preview.Styled;
                        texture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                        texture.Apply();
                        var bytes = texture.EncodeToPNG();
                        File.WriteAllBytes(Path.GetFullPath("../Validation-0.9/Hatching" + degrees + ".png"), bytes);
                        return bytes;
                    }
                    finally { RenderTexture.active = prior; Object.DestroyImmediate(texture); }
                }
                var diagonal = Capture(0);
                var aligned = Capture(45);
                var rotated = Capture(90);
                Check(!diagonal.SequenceEqual(aligned), "Hatching rotation changes diagonals to horizontal/vertical crosshatching");
                Check(!diagonal.SequenceEqual(rotated), "90-degree rotation swaps primary hatch direction");
                Check(diagonal.SequenceEqual(Capture(180)), "Hatching 180 degrees returns to original orientation");
                profile.hatchRotation = 37;
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), copy);
                Check(copy.hatchRotation == 37, "Hatching rotation survives profile serialization");
                LookPresets.Apply(copy, LookStyle.InkHatching);
                Check(copy.hatchRotation == 0, "Hatching default preserves original diagonal orientation");
            }
            finally { Object.DestroyImmediate(profile); Object.DestroyImmediate(copy); }
        }

        static void Check(bool success, string message)
        {
            if (!success) throw new Exception(message);
            Debug.Log("PASS: " + message);
        }
    }
}
