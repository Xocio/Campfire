using System;

namespace Campfire.Controls
{
    /// <summary>
    /// A damped spring that pulls <see cref="Value"/> towards <see cref="Target"/>. Unlike a timed animation it keeps
    /// its speed when the target changes in mid-flight, so a movement can be redirected without a visible seam.
    /// Damping below the critical value lets the value overshoot a little before it settles.
    /// </summary>
    internal sealed class Spring
    {
        private readonly double _stiffness;
        private readonly double _damping;

        public Spring(double stiffness, double damping)
        {
            _stiffness = stiffness;
            _damping = damping;
        }

        public double Value;
        public double Target;
        public double Velocity;

        /// <summary>Jumps to <paramref name="value"/> and stays there.</summary>
        public void Snap(double value)
        {
            Value = value;
            Target = value;
            Velocity = 0;
        }

        /// <summary>Advances the spring by <paramref name="seconds"/>, in small steps so a slow frame cannot blow it up.</summary>
        public void Step(double seconds)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(seconds / 0.004));
            double h = seconds / steps;
            for (int i = 0; i < steps; i++)
            {
                double acceleration = -_stiffness * (Value - Target) - _damping * Velocity;
                Velocity += acceleration * h;
                Value += Velocity * h;
            }
        }
    }
}
