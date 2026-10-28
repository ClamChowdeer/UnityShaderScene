using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SceneLookLab
{
    internal static class ClayValidation
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception("Clay validation: " + message);
            Debug.Log("PASS: " + message);
        }

        internal static void Run(Camera camera, MeshRenderer subject, Material original)
        {
            var profile = ScriptableObject.CreateInstance<LookProfile>();
            LookPresets.Apply(profile, LookStyle.Clay);
            var material = new Material(original);
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            int mask = camera.cullingMask, layer = subject.gameObject.layer;
            var background = camera.backgroundColor;
            var clear = camera.clearFlags;
            try
            {
                subject.sharedMaterial = material;
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", new Color(0.55f, 0.36f, 0.22f));
                camera.transform.position = new Vector3(0, 2.1f, -4.5f);
                camera.transform.LookAt(subject.transform.position);
                camera.fieldOfView = 30;
                camera.cullingMask = 1 << 30;
                subject.gameObject.layer = 30;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.07f, 0.07f, 0.07f);
                using var preview = new PreviewSession();
                byte[] Capture(string name)
                {
                    preview.Capture(camera, profile, subject.transform, 800, 800);
                    var prior = RenderTexture.active;
                    var image = new Texture2D(800, 800, TextureFormat.RGB24, false);
                    try
                    {
                        RenderTexture.active = preview.Styled;
                        image.ReadPixels(new Rect(0, 0, 800, 800), 0, 0);
                        image.Apply();
                        byte[] png = image.EncodeToPNG();
                        File.WriteAllBytes(Path.GetFullPath("../Validation-0.9/" + name + ".png"), png);
                        return png;
                    }
                    finally { RenderTexture.active = prior; Object.DestroyImmediate(image); }
                }
                byte[] baseline = Capture("ClayHandcrafted");
                profile.clayDetail = 0;
                Check(!baseline.SequenceEqual(Capture("ClayNoFineDetail")), "Clay surface detail changes rendered pixels");
                LookPresets.Apply(profile, LookStyle.Clay);
                profile.clayDetailSize = 0.32f;
                Check(!baseline.SequenceEqual(Capture("ClayLargeDetail")), "Clay detail size changes rendered pixels");
                LookPresets.Apply(profile, LookStyle.Clay);
                profile.clayUnevenness = 0;
                Check(!baseline.SequenceEqual(Capture("ClayNoDents")), "Clay unevenness changes rendered pixels");
                LookPresets.Apply(profile, LookStyle.Clay);
                profile.clayShine = 0;
                Check(!baseline.SequenceEqual(Capture("ClayNoShine")), "Clay soft shine changes rendered pixels");
                profile.clayDetail = 0;
                profile.clayUnevenness = 0;
                Capture("ClaySmoothBaseline");
                LookPresets.Apply(profile, LookStyle.Clay);
                material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
                byte[] textured = Capture("ClayTextureFull");
                profile.clayTextureStrength = 0;
                Check(!textured.SequenceEqual(Capture("ClayTextureOff")), "Clay original texture strength changes rendered pixels");
                material.SetTexture("_BaseMap", Texture2D.whiteTexture);
                material.SetColor("_BaseColor", new Color(0.26f, 0.45f, 0.10f));
                Capture("ClayGreen");
                material.SetColor("_BaseColor", new Color(0.82f, 0.12f, 0.09f));
                Capture("ClayRed");
                profile.clayDetail = 0.81f;
                profile.clayDetailSize = 0.21f;
                profile.clayUnevenness = 0.63f;
                profile.clayShine = 0.71f;
                profile.clayTextureStrength = 0.27f;
                var copy = ScriptableObject.CreateInstance<LookProfile>();
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(profile), copy);
                Check(copy.clayDetail == profile.clayDetail && copy.clayDetailSize == profile.clayDetailSize &&
                    copy.clayUnevenness == profile.clayUnevenness && copy.clayShine == profile.clayShine &&
                    copy.clayTextureStrength == profile.clayTextureStrength, "All clay settings survive profile serialization");
                Object.DestroyImmediate(copy);
            }
            finally
            {
                subject.sharedMaterial = original;
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                camera.cullingMask = mask;
                camera.backgroundColor = background;
                camera.clearFlags = clear;
                subject.gameObject.layer = layer;
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(profile);
            }
        }
    }
}
