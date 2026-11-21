using Godot;

namespace STS2_Things.Visuals;

/// <summary>
/// Baked static mesh data for the Living Megalith front-fist grab overlay (<c>arm1_3</c> and <c>arm2_3</c>).
/// Extracted directly from <c>animations/monsters/cave_god/cave_god.spjson</c> and
/// <c>animations/monsters/cave_god/cave_god.atlas</c> (<c>cave_god.png</c>, 2033x1734, unrotated):
/// - <c>arm1_3</c>: bounds=(1732, 695, 196, 182), offsets=(0, 0, 196, 183) => topTrim=1, atlas origin=(1732, 694)
/// - <c>arm2_3</c>: bounds=(822, 2, 200, 175), offsets=(0, 1, 200, 182) => topTrim=6, atlas origin=(822, -4)
/// Eliminates runtime 7.38 MiB JSON parsing and GPU-to-CPU <c>GetImage()</c> readback stalls on first grab.
/// </summary>
internal static class CaveGodFistOverlayData
{
    public const string AtlasTexturePath = "res://animations/monsters/cave_god/cave_god.png";
    public const float AtlasWidth = 2033f;
    public const float AtlasHeight = 1734f;

    internal readonly record struct VertexWeight(int LocalBone, float Vx, float Vy, float Weight);

    // arm1_3 (left fist): 30 vertices, 40 triangles; LocalBone 0=arm1_2, 1=arm1_3, 2=fist1
    public static readonly Vector2[] LeftAtlasUvs =
    [
        new(1852.02256f, 698.00221f), new(1883.07484f, 701.87998f), new(1928f, 755.97478f),
        new(1928f, 768.69328f), new(1928f, 784.79179f), new(1928f, 835.20829f),
        new(1928f, 853.32712f), new(1877.20072f, 877f), new(1859.08248f, 877f),
        new(1829.1866f, 877f), new(1780.71972f, 867.82255f), new(1751.71172f, 856.79131f),
        new(1743.57576f, 811.65436f), new(1732f, 766.7608f), new(1732f, 728.44243f),
        new(1732f, 694f), new(1773.91656f, 694f), new(1819.96284f, 694f),
        new(1883.39628f, 772.55641f), new(1821.572f, 766.43872f), new(1820.73312f, 728.72608f),
        new(1883.25124f, 740.66866f), new(1773.26388f, 728.57419f), new(1772.63472f, 766.6144f),
        new(1852.24992f, 734.74678f), new(1851.67172f, 769.41796f), new(1778.0012f, 822.52639f),
        new(1825.1098f, 826.6018f), new(1855.45844f, 826.14979f), new(1884.4488f, 831.58489f),
    ];

    public static readonly int[] LeftTriangles =
    [
        11, 26, 10, 6, 7, 29, 10, 27, 9, 7, 8, 29,
        9, 28, 8, 16, 22, 15, 20, 17, 0, 22, 16, 17,
        24, 0, 1, 22, 14, 15, 20, 22, 17, 20, 0, 24,
        2, 21, 1, 24, 1, 21, 19, 20, 24, 23, 14, 22,
        23, 22, 20, 23, 20, 19, 13, 14, 23, 21, 2, 3,
        25, 19, 24, 25, 24, 21, 18, 21, 3, 25, 21, 18,
        18, 3, 4, 12, 13, 23, 26, 23, 19, 12, 23, 26,
        28, 25, 18, 27, 19, 25, 27, 25, 28, 26, 19, 27,
        29, 18, 4, 28, 18, 29, 29, 4, 5, 29, 5, 6,
        11, 12, 26, 10, 26, 27, 9, 27, 28, 8, 28, 29,
    ];

    public static readonly VertexWeight[][] LeftWeights =
    [
        [new(1, -5.43f, 43.02f, 0.19999f), new(0, 350.724f, 41.85f, 0.8f)],
        [new(1, 0.59f, 73.73f, 0.39999f), new(0, 359.384f, 71.92f, 0.6f)],
        [new(1, 57.68f, 114.78f, 0.68444f), new(0, 419.814f, 107.88f, 0.31555f)],
        [new(1, 70.37f, 113.9f, 0.86933f), new(0, 432.374f, 105.9f, 0.13066f)],
        [new(1, 86.43f, 112.78f, 1f)],
        [new(1, 136.72f, 109.27f, 0.75f), new(2, -35.26f, 99.47f, 0.25f)],
        [new(1, 154.8f, 108.01f, 0.14999f), new(2, -17.19f, 98.21f, 0.85f)],
        [new(2, 2.88f, 45.89f, 1f)],
        [new(2, 1.62f, 27.82f, 1f)],
        [new(2, -0.45f, -2f, 1f)],
        [new(2, -12.97f, -49.71f, 1f)],
        [new(1, 145.99f, -68.08f, 0.14999f), new(2, -25.99f, -77.88f, 0.85f)],
        [new(1, 100.4f, -73.06f, 1f)],
        [new(1, 54.81f, -81.48f, 1f)],
        [new(1, 16.58f, -78.82f, 0.72962f), new(0, 362.104f, -81.43f, 0.27037f)],
        [new(1, -17.77f, -76.42f, 0.39999f), new(0, 328.084f, -76.07f, 0.6f)],
        [new(1, -14.85f, -34.61f, 0.19999f), new(0, 334.604f, -34.67f, 0.8f)],
        [new(1, -11.65f, 11.32f, 0.19999f), new(0, 341.774f, 10.81f, 0.8f)],
        [new(1, 71.12f, 69.13f, 1f)],
        [new(1, 60.71f, 7.89f, 1f)],
        [new(1, 23.04f, 9.67f, 0.6884f), new(0, 376.194f, 6.17f, 0.3116f)],
        [new(1, 39.3f, 71.21f, 0.77919f), new(0, 397.724f, 66.06f, 0.2208f)],
        [new(1, 19.58f, -37.66f, 0.6925f), new(0, 368.664f, -40.69f, 0.30749f)],
        [new(1, 57.49f, -40.94f, 1f)],
        [new(1, 31.23f, 40.69f, 0.704f), new(0, 387.054f, 36.36f, 0.296f)],
        [new(1, 65.78f, 37.71f, 1f)],
        [new(1, 113.64f, -39.47f, 0.99999f)],
        [new(1, 120.98f, 7.23f, 0.99999f)],
        [new(1, 122.64f, 37.54f, 0.99999f)],
        [new(1, 130.08f, 66.08f, 0.75f), new(2, -41.91f, 56.28f, 0.25f)],
    ];

    // arm2_3 (right fist): 26 vertices, 33 triangles; LocalBone 0=arm2_2, 1=arm2_3, 2=fist2
    public static readonly Vector2[] RightAtlasUvs =
    [
        new(1022f, 26.7398f), new(1022f, 57.6525f), new(1009.202f, 117.2393f),
        new(989.964f, 164.79226f), new(949.308f, 178f), new(886.146f, 178f),
        new(850.9f, 165.55666f), new(822f, 155.35374f), new(822f, 136.11452f),
        new(822f, 63.76952f), new(838.378f, 37.34312f), new(852.786f, 14.09444f),
        new(864f, -4f), new(899.098f, -4f), new(937.094f, -4f),
        new(981.208f, -4f), new(1022f, -4f), new(927.434f, 65.05808f),
        new(932.202f, 30.9622f), new(979.4f, 28.74362f), new(978.274f, 61.07592f),
        new(877.636f, 49.56078f), new(889.882f, 21.97322f), new(915.186f, 124.13528f),
        new(965.716f, 120.42976f), new(864.054f, 130.70912f),
    ];

    public static readonly int[] RightTriangles =
    [
        6, 7, 8, 6, 25, 5, 5, 23, 4, 4, 24, 3,
        25, 6, 8, 22, 13, 18, 18, 14, 19, 18, 13, 14,
        19, 14, 15, 15, 16, 0, 22, 12, 13, 4, 23, 24,
        5, 25, 23, 3, 24, 2, 8, 9, 25, 25, 9, 21,
        25, 21, 23, 21, 9, 10, 23, 17, 24, 23, 21, 17,
        24, 20, 2, 24, 17, 20, 2, 20, 1, 17, 21, 18,
        17, 18, 20, 18, 21, 22, 20, 19, 1, 20, 18, 19,
        19, 0, 1, 10, 11, 21, 21, 11, 22, 19, 15, 0,
        11, 12, 22,
    ];

    public static readonly VertexWeight[][] RightWeights =
    [
        [new(1, -4.55f, 72.59f, 0.68824f), new(0, 330.3315f, 74.8f, 0.31175f)],
        [new(1, 25.81f, 78.36f, 1f)],
        [new(1, 86.73f, 76.91f, 1f)],
        [new(1, 137.04f, 66.88f, 0.1f), new(2, -25.73f, 70.17f, 0.9f)],
        [new(2, -5.17f, 32.69f, 1f)],
        [new(2, 6.6f, -29.35f, 1f)],
        [new(2, 0.96f, -66.3f, 1f)],
        [new(2, -3.66f, -96.59f, 1f)],
        [new(1, 140.21f, -103.47f, 0.72f), new(2, -22.56f, -100.18f, 0.28f)],
        [new(1, 69.14f, -116.97f, 1f)],
        [new(1, 40.12f, -105.82f, 1f)],
        [new(1, 14.59f, -96f, 0.68824f), new(0, 318.6915f, -94.48f, 0.31175f)],
        [new(1, -5.27f, -88.36f, 0.39f), new(0, 300.5315f, -83.37f, 0.61f)],
        [new(1, -11.82f, -53.88f, 0.19999f), new(0, 300.3215f, -48.27f, 0.8f)],
        [new(1, -18.91f, -16.55f, 0.19999f), new(0, 300.0915f, -10.28f, 0.8f)],
        [new(1, -27.14f, 26.78f, 0.19999f), new(0, 299.8315f, 33.83f, 0.8f)],
        [new(1, -34.75f, 66.85f, 0.39f), new(0, 299.5915f, 74.62f, 0.61f)],
        [new(1, 50.73f, -13.15f, 1f)],
        [new(1, 16.34f, -14.83f, 0.63522f), new(0, 335.0815f, -14.96f, 0.36477f)],
        [new(1, 5.35f, 31.11f, 0.64342f), new(0, 332.5815f, 32.21f, 0.35657f)],
        [new(1, 37.33f, 36.04f, 1f)],
        [new(1, 44.8f, -64.97f, 1f)],
        [new(1, 15.41f, -58.08f, 0.64342f), new(0, 326.3515f, -57.33f, 0.35657f)],
        [new(1, 111.06f, -14.16f, 1f)],
        [new(1, 97.98f, 34.78f, 1f)],
        [new(1, 127.05f, -63.17f, 0.72f), new(2, -35.72f, -59.88f, 0.28f)],
    ];
}
