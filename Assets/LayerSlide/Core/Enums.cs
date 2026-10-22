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

    /// <summary>F1 rails: Free moves on both axes, X / Y only along that axis (GDD 01 §12).</summary>
    public enum MoveAxis : byte
    {
        Free,
        X,
        Y
    }

    /// <summary>
    /// Why a gate refuses a block. Declared in the order the UI reports problems (GDD 04 §13):
    /// the first failing check wins, so a larger value means more conditions were already met.
    /// </summary>
    public enum GateCheck : byte
    {
        WrongAxis,
        WrongColor,
        KeyRequired,
        PlatesRequired,
        DoesNotFit,
        Ok
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
