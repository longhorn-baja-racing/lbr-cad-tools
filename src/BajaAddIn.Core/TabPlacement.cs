using System;

namespace Lbr.CadTools.Core
{
    /// <summary>Which way the tab's base line runs on the tube.</summary>
    public enum TabOrientation
    {
        /// <summary>Base line along the tube axis (the usual welded bolt tab).</summary>
        AlongTube,

        /// <summary>Tab plate square to the tube axis; the base wraps around the tube when contoured.</summary>
        AcrossTube,
    }

    /// <summary>
    /// The face picked in the assembly for the tab to sit on, in assembly
    /// coordinates (meters). Either a round tube (cylinder) or a flat face
    /// (square or rectangular tube, plate).
    /// </summary>
    public sealed class TabMount
    {
        private TabMount()
        {
        }

        public bool IsRound { get; private set; }

        /// <summary>Where the face was clicked.</summary>
        public Vec3 Pick { get; private set; }

        /// <summary>Round: a point on the tube axis.</summary>
        public Vec3 AxisPoint { get; private set; }

        /// <summary>Round: tube axis. Flat: direction of the face's longest straight edge (the tube length).</summary>
        public Vec3 Axis { get; private set; }

        /// <summary>Round: outside radius.</summary>
        public double Radius { get; private set; }

        /// <summary>Flat: outward face normal.</summary>
        public Vec3 Normal { get; private set; }

        public static TabMount Round(Vec3 pick, Vec3 axisPoint, Vec3 axis, double radius) => new TabMount
        {
            IsRound = true,
            Pick = pick,
            AxisPoint = axisPoint,
            Axis = axis.Unit(),
            Radius = radius,
        };

        public static TabMount Flat(Vec3 pick, Vec3 normal, Vec3 lengthDirection)
        {
            Vec3 n = normal.Unit();
            // Keep the length direction in the face plane.
            Vec3 along = (lengthDirection - n * lengthDirection.Dot(n)).Unit();
            return new TabMount { IsRound = false, Pick = pick, Normal = n, Axis = along };
        }

        /// <summary>Distance from the pick to the tube axis; equals the radius for a click on the outside of a round tube.</summary>
        public double PickDistanceFromAxis()
        {
            Vec3 d = Pick - AxisPoint;
            return (d - Axis * d.Dot(Axis)).Length;
        }
    }

    /// <summary>Position and orientation of a tab part in an assembly.</summary>
    public sealed class TabFrame
    {
        public TabFrame(Vec3 origin, Vec3 x, Vec3 y, Vec3 z)
        {
            Origin = origin;
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Where the part origin (middle of the base line, on the Front plane) goes.</summary>
        public Vec3 Origin { get; }

        /// <summary>Part X: along the base line.</summary>
        public Vec3 X { get; }

        /// <summary>Part Y: from the base toward the bolt hole.</summary>
        public Vec3 Y { get; }

        /// <summary>Part Z: plate thickness direction.</summary>
        public Vec3 Z { get; }

        /// <summary>A point given in part coordinates, in assembly coordinates.</summary>
        public Vec3 ToAssembly(Vec3 part) => Origin + X * part.X + Y * part.Y + Z * part.Z;

        /// <summary>
        /// The 16 values of a SolidWorks MathTransform: rotation rows (the part
        /// X, Y, and Z axes in assembly space), translation, scale 1, then three zeros.
        /// </summary>
        public double[] ToSolidWorks() => new[]
        {
            X.X, X.Y, X.Z,
            Y.X, Y.Y, Y.Z,
            Z.X, Z.Y, Z.Z,
            Origin.X, Origin.Y, Origin.Z,
            1.0, 0, 0, 0,
        };
    }

    /// <summary>
    /// Where a generated tab goes on the picked tube. The tab part is built
    /// with its base line on part X centered at the origin, the hole up part
    /// Y, and the plate from Z = 0 toward +Z (two plates for double shear).
    /// </summary>
    public static class TabPlacement
    {
        /// <summary>A contoured base only fits a round tube with the tab across it.</summary>
        public static TabOrientation Effective(TabInputs t, TabMount m, TabOrientation wanted) =>
            m.IsRound && t.ContourTubeDiameter > 0 ? TabOrientation.AcrossTube : wanted;

        /// <summary>
        /// True when a double shear pair straddles a round tube: plates on either
        /// side, gap equal to the tube OD, base line level with the axis.
        /// </summary>
        public static bool Straddles(TabInputs t, TabMount m, TabOrientation o) =>
            m.IsRound && t.DoubleShear && t.GapFromTube && Effective(t, m, o) == TabOrientation.AlongTube;

        public static TabFrame Place(TabInputs t, TabMount m, TabOrientation wanted)
        {
            TabOrientation o = Effective(t, m, wanted);
            Vec3 y, along, basePoint;
            if (m.IsRound)
            {
                Vec3 d = m.Pick - m.AxisPoint;
                Vec3 foot = m.AxisPoint + m.Axis * d.Dot(m.Axis);
                Vec3 radial = m.Pick - foot;
                y = radial.Length > 1e-9 ? radial.Unit() : Perpendicular(m.Axis);
                along = m.Axis;
                basePoint = foot + y * BaseOffset(t, m, o);
            }
            else
            {
                y = m.Normal;
                along = m.Axis;
                basePoint = m.Pick;
            }

            Vec3 x, z;
            if (o == TabOrientation.AlongTube)
            {
                x = along;
                z = x.Cross(y);
            }
            else
            {
                z = along;
                x = y.Cross(z);
            }

            // Center the plate (or the pair) on the pick.
            double depth = t.DoubleShear ? 2 * t.Thickness + t.TabGap : t.Thickness;
            return new TabFrame(basePoint - z * (depth / 2), x, y, z);
        }

        /// <summary>Distance from the tube axis to the base line's middle, for a round tube.</summary>
        private static double BaseOffset(TabInputs t, TabMount m, TabOrientation o)
        {
            if (Straddles(t, m, o))
            {
                return 0;
            }
            if (t.ContourTubeDiameter > 0)
            {
                // The base arc runs through the corners (+-B/2, 0) with its center on the tube axis.
                double rt = t.ContourTubeDiameter / 2, b = t.BaseWidth / 2;
                return Math.Sqrt(Math.Max(0, rt * rt - b * b));
            }
            return m.Radius;
        }

        private static Vec3 Perpendicular(Vec3 a)
        {
            Vec3 trial = Math.Abs(a.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
            return a.Cross(trial).Unit();
        }

        /// <summary>File name (no extension) for a tab part: "Tab-LapBelt-1", the first number not taken.</summary>
        public static string FileName(TabUse use, Func<string, bool> taken)
        {
            for (int i = 1; ; i++)
            {
                string name = $"Tab-{use}-{i}";
                if (!taken(name))
                {
                    return name;
                }
            }
        }
    }
}
