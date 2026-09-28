using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NXSG.Editor
{
    /// <summary>Creates a tiny, dependency-free signed-distance atlas for numeric overlays.</summary>
    public static class TextAtlasUtility
    {
        public const int CellSize = 64;
        public const int Columns = 4;
        public const int Rows = 3;
        public const int DistanceRange = 8;

        /// <summary>Writes a 4x3 atlas: digits 0-9, minus and decimal point. Returns project-relative asset path.</summary>
        public static string CreateDigitAtlas(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a PNG path inside Assets.", nameof(assetPath));
            var texture = new Texture2D(CellSize * Columns, CellSize * Rows, TextureFormat.RGBA32, false, true)
            { name = "NXSG Numeric SDF Atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, anisoLevel = 1 };
            var pixels = new Color32[texture.width * texture.height];
            for (var glyph = 0; glyph < 12; glyph++)
            {
                var gx = glyph % Columns * CellSize;
                var gy = (glyph / Columns) * CellSize;
                for (var y = 0; y < CellSize; y++)
                for (var x = 0; x < CellSize; x++)
                {
                    var d = GlyphDistance(glyph, x + .5f, y + .5f);
                    var encoded = Mathf.Clamp01(.5f + d / (2f * DistanceRange));
                    var value = (byte)Mathf.RoundToInt(encoded * 255f);
                    pixels[(gy + y) * texture.width + gx + x] = new Color32(value, value, value, 255);
                }
            }
            texture.SetPixels32(pixels); texture.Apply(false, false);
            var fullPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), assetPath));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllBytes(fullPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false; importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear; importer.wrapMode = TextureWrapMode.Clamp; importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }
            return assetPath;
        }

        /// <summary>Positive inside, negative outside; distances are exact to each vector segment.</summary>
        public static float GlyphDistance(int glyph, float x, float y)
        {
            if (glyph == 11) return 5f - Vector2.Distance(new Vector2(x, y), new Vector2(32, 8));
            var segments = Segments(glyph);
            var unionDistance = float.PositiveInfinity;
            for (var i = 0; i < segments.Length; i++)
            {
                var s = segments[i];
                var qx = Mathf.Abs(x - (s.xMin + s.xMax) * .5f) - (s.xMax - s.xMin) * .5f;
                var qy = Mathf.Abs(y - (s.yMin + s.yMax) * .5f) - (s.yMax - s.yMin) * .5f;
                var sd = Mathf.Min(Mathf.Max(qx, qy), 0) + new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
                unionDistance = Mathf.Min(unionDistance, sd);
            }
            return -unionDistance;
        }

        static Rect[] Segments(int digit)
        {
            const float t = 6, left = 8, right = 50, top = 54, middle = 29, bottom = 5;
            var a = new Rect(14, top, 36, t); var g = new Rect(14, middle, 36, t); var d = new Rect(14, bottom, 36, t);
            var f = new Rect(left, 34, t, 18); var b = new Rect(right, 34, t, 18); var e = new Rect(left, 10, t, 18); var c = new Rect(right, 10, t, 18);
            var minus = new Rect(14, middle, 36, t);
            switch (digit)
            {
                case 0: return new[] { a, b, c, d, e, f };
                case 1: return new[] { b, c };
                case 2: return new[] { a, b, g, e, d };
                case 3: return new[] { a, b, g, c, d };
                case 4: return new[] { f, g, b, c };
                case 5: return new[] { a, f, g, c, d };
                case 6: return new[] { a, f, g, e, c, d };
                case 7: return new[] { a, b, c };
                case 8: return new[] { a, b, c, d, e, f, g };
                case 9: return new[] { a, b, c, d, f, g };
                default: return new[] { minus };
            }
        }
    }
}
