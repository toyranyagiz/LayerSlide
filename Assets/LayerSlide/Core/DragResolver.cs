namespace LayerSlide.Core
{
    /// <summary>
    /// Pure drag math for one hold (GDD 01 §3–6): axis lock at 0.15 cell (ties pick X), clamping to the reachable
    /// range, gate arming at 0.25 cell past a reachable board edge, and snap-on-release. It never mutates the board.
    /// </summary>
    public sealed class DragResolver
    {
        Board _board;

        public int Block { get; private set; } = -1;
        public int StartX { get; private set; }
        public int StartY { get; private set; }
        public bool AxisLocked { get; private set; }
        public Axis Axis { get; private set; }
        public int Min { get; private set; }
        public int Max { get; private set; }

        /// <summary>Clamped continuous position on the locked axis.</summary>
        public float Value { get; private set; }

        /// <summary>Gate that would accept a release now, or -1.</summary>
        public int ArmedGate { get; private set; } = -1;

        /// <summary>The finger pulls outward past a reachable edge but no gate there accepts the block.</summary>
        public bool ExitIntentWithoutGate { get; private set; }
        public GateSide ExitSide { get; private set; }

        /// <summary>What the refusing gate is missing while <see cref="ExitIntentWithoutGate"/> holds.</summary>
        public GateCheck ExitCheck { get; private set; } = GateCheck.DoesNotFit;

        /// <summary>Gate closest to accepting during a refused exit, or -1.</summary>
        public int RefusingGate { get; private set; } = -1;

        /// <summary>A rail block was pulled along its forbidden axis before the allowed one locked.</summary>
        public bool ForbiddenAxisAttempt { get; private set; }

        public float PreviewX => AxisLocked && Axis == Axis.X ? Value : StartX;
        public float PreviewY => AxisLocked && Axis == Axis.Y ? Value : StartY;

        public void Begin(Board board, int block)
        {
            _board = board;
            Block = block;
            StartX = board.GetX(block);
            StartY = board.GetY(block);
            AxisLocked = false;
            ArmedGate = -1;
            ExitIntentWithoutGate = false;
            ExitCheck = GateCheck.DoesNotFit;
            RefusingGate = -1;
            ForbiddenAxisAttempt = false;
            Value = 0f;
        }

        /// <summary>
        /// Feeds the requested (unclamped, continuous) block position. Returns true when a gate became armed
        /// on this update.
        /// </summary>
        public bool Update(float requestedX, float requestedY)
        {
            if (!AxisLocked)
            {
                float ax = System.Math.Abs(requestedX - StartX), ay = System.Math.Abs(requestedY - StartY);
                var rail = _board.Level.Blocks[Block].MoveAxis;
                if (rail == MoveAxis.Free)
                {
                    if (System.Math.Max(ax, ay) + RuleConstants.Epsilon < RuleConstants.AxisLockThreshold) return false;
                    Axis = ax + RuleConstants.Epsilon >= ay ? Axis.X : Axis.Y;
                }
                else
                {
                    // A rail block only compares its allowed component; the forbidden one never locks (GDD 01 §3.5).
                    float allowed = rail == MoveAxis.X ? ax : ay, forbidden = rail == MoveAxis.X ? ay : ax;
                    if (allowed + RuleConstants.Epsilon < RuleConstants.AxisLockThreshold)
                    {
                        if (forbidden + RuleConstants.Epsilon >= RuleConstants.AxisLockThreshold)
                            ForbiddenAxisAttempt = true;
                        return false;
                    }
                    Axis = rail == MoveAxis.X ? Axis.X : Axis.Y;
                }
                AxisLocked = true;
                RuleEngine.GetSlideRange(_board, Block, Axis, out int min, out int max);
                Min = min;
                Max = max;
            }

            float requested = Axis == Axis.X ? requestedX : requestedY;
            Value = requested < Min ? Min : requested > Max ? Max : requested;

            int previousGate = ArmedGate;
            UpdateExitIntent(requested);
            return ArmedGate >= 0 && ArmedGate != previousGate;
        }

        void UpdateExitIntent(float requested)
        {
            int gate = -1;
            bool intent = false;

            if (Min - requested + RuleConstants.Epsilon >= RuleConstants.ExitIntentThreshold)
            {
                ExitSide = GateSideExtensions.FromAxis(Axis, -1);
                intent = IsOnEdge(Min, ExitSide);
                if (intent) gate = FindGate(Min, ExitSide);
            }
            else if (requested - Max + RuleConstants.Epsilon >= RuleConstants.ExitIntentThreshold)
            {
                ExitSide = GateSideExtensions.FromAxis(Axis, 1);
                intent = IsOnEdge(Max, ExitSide);
                if (intent) gate = FindGate(Max, ExitSide);
            }

            ExitIntentWithoutGate = intent && gate < 0;
            ArmedGate = gate;
            RefusingGate = -1;
            if (ExitIntentWithoutGate)
            {
                int edge = ExitSide.GetSign() < 0 ? Min : Max;
                int x = Axis == Axis.X ? edge : StartX, y = Axis == Axis.Y ? edge : StartY;
                ExitCheck = RuleEngine.DiagnoseExit(_board, Block, x, y, ExitSide, out int refusing);
                RefusingGate = refusing;
            }
        }

        /// <summary>
        /// Final target for a release. Returns false when the axis never locked (a tap): no command is produced.
        /// </summary>
        public bool TryGetRelease(out int x, out int y, out int gate)
        {
            gate = ArmedGate;
            x = StartX;
            y = StartY;
            if (!AxisLocked) return false;

            int target = gate >= 0
                ? (ExitSide.GetSign() < 0 ? Min : Max)
                : Snap(Value, Axis == Axis.X ? StartX : StartY, Min, Max);
            if (Axis == Axis.X) x = target;
            else y = target;
            return true;
        }

        /// <summary>Edge cell the invalid exit attempt was made from (valid when <see cref="ExitIntentWithoutGate"/>).</summary>
        public void GetExitEdge(out int x, out int y)
        {
            int edge = ExitSide.GetSign() < 0 ? Min : Max;
            x = Axis == Axis.X ? edge : StartX;
            y = Axis == Axis.Y ? edge : StartY;
        }

        /// <summary>Nearest cell; an exact half rounds away from where the hold started (GDD 01 §4).</summary>
        public static int Snap(float value, int start, int min, int max)
        {
            int low = (int)System.Math.Floor(value), high = low + 1;
            float fraction = value - low;
            int result;
            if (System.Math.Abs(fraction - 0.5f) <= RuleConstants.Epsilon)
                result = System.Math.Abs(high - start) >= System.Math.Abs(low - start) ? high : low;
            else
                result = fraction > 0.5f ? high : low;
            return result < min ? min : result > max ? max : result;
        }

        bool IsOnEdge(int position, GateSide side)
        {
            int x = Axis == Axis.X ? position : StartX;
            int y = Axis == Axis.Y ? position : StartY;
            return RuleEngine.IsOnEdge(_board, Block, x, y, side);
        }

        int FindGate(int position, GateSide side)
        {
            int x = Axis == Axis.X ? position : StartX;
            int y = Axis == Axis.Y ? position : StartY;
            return RuleEngine.FindExitGate(_board, Block, x, y, side);
        }
    }
}
