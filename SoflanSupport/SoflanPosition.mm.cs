using System;

namespace SoflanSupport
{
    public readonly struct SoflanPosition : IEquatable<SoflanPosition>
    {
        public SoflanPosition(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Soflan position must be finite");

            Value = value;
        }

        public double Value { get; }

        public double DeltaTo(SoflanPosition baseline)
        {
            return Value - baseline.Value;
        }

        public float ToGameFloatDelta(SoflanPosition baseline)
        {
            var delta = DeltaTo(baseline);
            if (double.IsNaN(delta)
                || double.IsInfinity(delta)
                || delta > float.MaxValue
                || delta < -float.MaxValue)
            {
                throw new OverflowException("Soflan position delta cannot be represented as a game float");
            }

            return (float)delta;
        }

        public bool Equals(SoflanPosition other)
        {
            return Value.Equals(other.Value);
        }

        public override bool Equals(object obj)
        {
            return obj is SoflanPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value.ToString("R");
        }

        public static bool operator ==(SoflanPosition left, SoflanPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(SoflanPosition left, SoflanPosition right)
        {
            return !left.Equals(right);
        }
    }
}
