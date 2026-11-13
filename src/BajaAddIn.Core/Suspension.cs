using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lbr.CadTools.Core
{
    /// <summary>Small 3D vector (meters).</summary>
    public readonly struct Vec3
    {
        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(Vec3 a, double k) => new Vec3(a.X * k, a.Y * k, a.Z * k);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);

        public double Dot(Vec3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public Vec3 Cross(Vec3 b) => new Vec3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(Dot(this));

        public Vec3 Unit()
        {
            double l = Length;
            if (l < 1e-12)
            {
                throw new ArgumentException("Zero-length vector.");
            }
            return this * (1 / l);
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.####}, {1:0.####}, {2:0.####})", X, Y, Z);
    }

    public enum Corner
    {
        FL,
        FR,
        RL,
        RR,
    }

    /// <summary>
    /// Suspension hardpoints named HP_&lt;point&gt;_&lt;corner&gt;, for example
    /// HP_UBJ_FL. The convention is a proposal until Dynamics agrees (#12).
    /// </summary>
    public static class Hardpoints
    {
        public const string Prefix = "HP_";

        /// <summary>Upper ball joint (top of the steering axis).</summary>
        public const string Ubj = "UBJ";

        /// <summary>Lower ball joint (bottom of the steering axis).</summary>
        public const string Lbj = "LBJ";

        /// <summary>Outer tie rod end (not used by the static numbers yet).</summary>
        public const string Tro = "TRO";

        /// <summary>Wheel center.</summary>
        public const string Wc = "WC";

        /// <summary>Any point on the wheel spin axis, inboard of the wheel center.</summary>
        public const string Sa = "SA";

        public static IReadOnlyList<string> Required { get; } = new[] { Ubj, Lbj, Wc, Sa };

        public static string Name(string point, Corner corner) => $"{Prefix}{point}_{corner}";

        /// <summary>Parses "HP_UBJ_FL" (any case) into its point and corner.</summary>
        public static bool TryParse(string name, out string point, out Corner corner)
        {
            point = null;
            corner = default;
            if (name == null || !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string[] parts = name.Substring(Prefix.Length).Split('_');
            if (parts.Length != 2 || !Enum.TryParse(parts[1], true, out corner) || !Enum.IsDefined(typeof(Corner), corner))
            {
                return false;
            }
            point = parts[0].ToUpperInvariant();
            return true;
        }
    }

    /// <summary>
    /// Maps model coordinates to vehicle axes (ISO 8855: X forward, Y left,
    /// Z up). Axes are given as "+X", "-Z", and so on.
    /// </summary>
    public sealed class VehicleAxes
    {
        private readonly Vec3 forward;
        private readonly Vec3 up;
        private readonly Vec3 left;

        public VehicleAxes(string forwardAxis, string upAxis)
        {
            forward = Parse(forwardAxis);
            up = Parse(upAxis);
            if (Math.Abs(forward.Dot(up)) > 1e-9)
            {
                throw new ArgumentException("Forward and up must be different axes.");
            }
            left = up.Cross(forward);
            ForwardAxis = forwardAxis;
            UpAxis = upAxis;
        }

        public string ForwardAxis { get; }
        public string UpAxis { get; }

        /// <summary>
        /// Car modeled so its nose shows in the SolidWorks Front view (which
        /// looks at the +Z side) with Y up: forward +Z, up +Y, so left is +X.
        /// </summary>
        public static VehicleAxes SolidWorksDefault { get; } = new VehicleAxes("+Z", "+Y");

        public Vec3 ToVehicle(Vec3 model) => new Vec3(model.Dot(forward), model.Dot(left), model.Dot(up));

        private static Vec3 Parse(string axis)
        {
            string a = (axis ?? "").Trim().ToUpperInvariant();
            double sign = a.StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
            switch (a.TrimStart('+', '-'))
            {
                case "X": return new Vec3(sign, 0, 0);
                case "Y": return new Vec3(0, sign, 0);
                case "Z": return new Vec3(0, 0, sign);
                default: throw new ArgumentException($"'{axis}' is not an axis; use +X, -X, +Y, -Y, +Z, or -Z.");
            }
        }
    }

    /// <summary>One corner's hardpoints in vehicle coordinates (meters).</summary>
    public sealed class CornerPoints
    {
        public Corner Corner { get; set; }
        public Vec3 UpperBallJoint { get; set; }
        public Vec3 LowerBallJoint { get; set; }
        public Vec3 WheelCenter { get; set; }

        /// <summary>A point on the spin axis, inboard of the wheel center.</summary>
        public Vec3 SpinAxisPoint { get; set; }

        /// <summary>Loaded tire radius: wheel center to ground.</summary>
        public double TireRadius { get; set; }
    }

    /// <summary>Static alignment numbers for one corner. Angles in degrees, lengths in meters.</summary>
    public sealed class CornerGeometry
    {
        public Corner Corner { get; set; }

        /// <summary>+1 for a left corner, -1 for a right one (from the wheel center's Y).</summary>
        public int Side { get; set; }

        /// <summary>Negative when the top of the wheel leans in.</summary>
        public double Camber { get; set; }

        /// <summary>Positive for toe-in.</summary>
        public double Toe { get; set; }

        /// <summary>Wheel yaw, positive to the left (a left turn steers both fronts positive).</summary>
        public double Steer { get; set; }

        /// <summary>Positive when the top of the steering axis leans rearward.</summary>
        public double Caster { get; set; }

        /// <summary>Kingpin inclination: positive when the top of the steering axis leans inboard.</summary>
        public double Kpi { get; set; }

        /// <summary>Positive when the contact patch is outboard of the steering axis at the ground.</summary>
        public double ScrubRadius { get; set; }

        /// <summary>Positive when the steering axis meets the ground ahead of the contact patch.</summary>
        public double MechanicalTrail { get; set; }

        public Vec3 ContactPatch { get; set; }

        /// <summary>Where the steering axis meets the ground plane.</summary>
        public Vec3 SteerAxisAtGround { get; set; }

        /// <summary>Spin axis direction, pointing outboard.</summary>
        public Vec3 SpinAxis { get; set; }
    }

    /// <summary>Turning numbers for the front axle. Lengths in meters, angles in degrees.</summary>
    public sealed class SteeringGeometry
    {
        public bool Straight { get; set; }
        public double InnerSteer { get; set; }
        public double OuterSteer { get; set; }

        /// <summary>0% is parallel steer, 100% is ideal Ackermann for this wheelbase and kingpin track.</summary>
        public double AckermannPercent { get; set; }

        /// <summary>Radius of the outer front contact patch's path (curb-to-curb style turning radius).</summary>
        public double TurningRadius { get; set; }

        public double Wheelbase { get; set; }
        public double KingpinTrack { get; set; }
    }

    public static class SuspensionMath
    {
        private const double Deg = 180 / Math.PI;

        public static CornerGeometry Corner(CornerPoints p)
        {
            int side = p.WheelCenter.Y >= 0 ? 1 : -1;
            Vec3 spin = (p.WheelCenter - p.SpinAxisPoint).Unit(); // outboard
            Vec3 steer = (p.UpperBallJoint - p.LowerBallJoint).Unit();
            if (steer.Z <= 0)
            {
                throw new ArgumentException("The upper ball joint must be above the lower one.");
            }

            var g = new CornerGeometry { Corner = p.Corner, Side = side, SpinAxis = spin };
            double horizontal = Math.Sqrt(spin.X * spin.X + spin.Y * spin.Y);
            g.Camber = -Math.Atan2(spin.Z, horizontal) * Deg;
            g.Toe = Math.Atan2(spin.X, Math.Abs(spin.Y)) * Deg;
            g.Steer = -side * g.Toe;
            g.Caster = Math.Atan2(-steer.X, steer.Z) * Deg;
            g.Kpi = Math.Atan2(-side * steer.Y, steer.Z) * Deg;

            // Contact patch: lowest point of the tire circle in the wheel plane.
            var down = new Vec3(0, 0, -1);
            Vec3 inPlaneDown = (down - spin * down.Dot(spin)).Unit();
            g.ContactPatch = p.WheelCenter + inPlaneDown * p.TireRadius;

            // Steering axis at the ground (the contact patch's height).
            double u = (g.ContactPatch.Z - p.LowerBallJoint.Z) / steer.Z;
            g.SteerAxisAtGround = p.LowerBallJoint + steer * u;
            g.ScrubRadius = side * (g.ContactPatch.Y - g.SteerAxisAtGround.Y);
            g.MechanicalTrail = g.SteerAxisAtGround.X - g.ContactPatch.X;
            return g;
        }

        /// <summary>
        /// Turning numbers from both front corners and the rear axle position
        /// (vehicle X of the rear wheel centers).
        /// </summary>
        public static SteeringGeometry Steering(CornerGeometry left, CornerGeometry right, double rearAxleX)
        {
            var s = new SteeringGeometry
            {
                Wheelbase = (left.ContactPatch.X + right.ContactPatch.X) / 2 - rearAxleX,
                KingpinTrack = Math.Abs(left.SteerAxisAtGround.Y - right.SteerAxisAtGround.Y),
            };

            // Steer relative to straight ahead, so static toe does not count as a turn.
            double turn = (left.Steer + right.Steer) / 2;
            if (Math.Abs(turn) < 0.5)
            {
                s.Straight = true;
                return s;
            }

            bool turningLeft = turn > 0;
            CornerGeometry inner = turningLeft ? left : right;
            CornerGeometry outer = turningLeft ? right : left;
            s.InnerSteer = Math.Abs(inner.Steer);
            s.OuterSteer = Math.Abs(outer.Steer);

            // Ideal (Ackermann) inner angle: cot(inner) = cot(outer) - track / wheelbase.
            double outerRad = s.OuterSteer / Deg;
            double idealInner = Math.Atan(1 / (1 / Math.Tan(outerRad) - s.KingpinTrack / s.Wheelbase)) * Deg;
            double idealGain = idealInner - s.OuterSteer;
            s.AckermannPercent = Math.Abs(idealGain) < 1e-9 ? 100 : 100 * (s.InnerSteer - s.OuterSteer) / idealGain;

            // Turn center: the outer wheel's axle line meets the rear axle line.
            Vec3 a = outer.SpinAxis;
            double t = (rearAxleX - outer.ContactPatch.X) / a.X;
            var center = new Vec3(rearAxleX, outer.ContactPatch.Y + a.Y * t, outer.ContactPatch.Z);
            Vec3 d = outer.ContactPatch - center;
            s.TurningRadius = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            return s;
        }

        /// <summary>Plain-text report for the result window.</summary>
        public static string Report(IReadOnlyList<CornerGeometry> corners, SteeringGeometry steering)
        {
            string F(double v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);
            string Mm(double m) => F(m * 1000, "0.0") + " mm";
            var lines = new List<string>
            {
                "Corner  Camber  Toe-in  Caster   KPI    Scrub     Trail",
            };
            foreach (CornerGeometry g in corners.OrderBy(c => c.Corner))
            {
                lines.Add($"{g.Corner,-6}  {F(g.Camber, "0.00"),6}  {F(g.Toe, "0.00"),6}  {F(g.Caster, "0.00"),6}  {F(g.Kpi, "0.00"),5}  {Mm(g.ScrubRadius),8}  {Mm(g.MechanicalTrail),8}");
            }
            lines.Add("Angles in degrees. Camber - = top in, toe + = in, caster + = top back, scrub + = patch outboard.");
            if (steering != null)
            {
                lines.Add("");
                if (steering.Straight)
                {
                    lines.Add($"Front wheels straight (wheelbase {Mm(steering.Wheelbase)}). Steer them to see turning radius and Ackermann.");
                }
                else
                {
                    lines.Add($"Steer: inner {F(steering.InnerSteer, "0.0")} deg, outer {F(steering.OuterSteer, "0.0")} deg, " +
                              $"Ackermann {F(steering.AckermannPercent, "0")}%");
                    lines.Add($"Turning radius (outer front contact patch): {F(steering.TurningRadius / Units.MetersPerInch / 12, "0.00")} ft " +
                              $"({F(steering.TurningRadius, "0.000")} m)");
                }
            }
            return string.Join("\n", lines);
        }
    }
}
