namespace LayerSlide.Core
{
    public enum LayerColor : byte
    {
        Red,
        Blue,
        Yellow
    }

    /// <summary>Board edge a gate is attached to. Order matters: the solver expands directions in this order (GDD 06 §8).</summary>
    public enum GateSide : byte
    {
        Left,
        Right,
        Bottom,
        Top
    }

    public enum Axis : byte
    {
        X,
        Y
    }

    public static class GateSideExtensions
    {
        public static Axis GetAxis(this GateSide side) =>
            side == GateSide.Left || side == GateSide.Right ? Axis.X : Axis.Y;

        /// <summary>-1 for Left/Bottom, +1 for Right/Top.</summary>
        public static int GetSign(this GateSide side) =>
            side == GateSide.Left || side == GateSide.Bottom ? -1 : 1;

        public static GateSide FromAxis(Axis axis, int sign) =>
            axis == Axis.X
                ? (sign < 0 ? GateSide.Left : GateSide.Right)
                : (sign < 0 ? GateSide.Bottom : GateSide.Top);
    }
}
