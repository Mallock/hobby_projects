public static class RenderOptions
{
    // Rotation applied to the final 240x240 frame before GIF encoding. Allowed: 0, 90, 180, 270
    public static volatile int RotationDegrees = 0;

    // When true, only the status bar (clock/temp) is mirrored horizontally.
    public static volatile bool MirrorStatusBar = false;

    // When true, mirror the entire rendered frame horizontally.
    public static volatile bool MirrorFrame = false;
}
