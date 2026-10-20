using System;

namespace IceCoast.Simulation.Config
{
    public enum LightSize : byte
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>Tunable stats for one size of trail light (plain data, editable in the inspector via SimConfigAsset).</summary>
    [Serializable]
    public class LightDefinition
    {
        public LightSize Size;
        public string DisplayName;
        public long Cost;
        /// <summary>
        /// Light reaches this far (tiles), fading with distance. A lone light lights tiles out to about 80% of
        /// this; overlapping lights add up.
        /// </summary>
        public float RadiusTiles;
        /// <summary>Visual only: how tall the pole is drawn.</summary>
        public float PoleHeightMeters;
        public float PowerKw;
        /// <summary>Power cost per hour while the lights are on.</summary>
        public long OperatingCostPerHour;
    }
}
