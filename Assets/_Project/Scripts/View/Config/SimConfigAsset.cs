using IceCoast.Simulation.Config;
using UnityEngine;

namespace IceCoast.View.Config
{
    /// <summary>
    /// Inspector-editable wrapper around the engine-free <see cref="SimConfig"/>.
    /// Create via Assets > Create > Ice Coast > Sim Config. Make several (Default, Sandbox, Small Test Map)
    /// and drag one onto the GameBootstrap.
    /// </summary>
    [CreateAssetMenu(fileName = "SimConfig", menuName = "Ice Coast/Sim Config", order = 0)]
    public sealed class SimConfigAsset : ScriptableObject
    {
        [Tooltip("All tuning numbers. Changes apply to the next new game (and to loaded saves).")]
        public SimConfig Config = SimConfig.CreateDefault();

        /// <summary>Returns a deep-ish copy so runtime tweaks never write back into the asset.</summary>
        public SimConfig CreateRuntimeCopy() => JsonUtility.FromJson<SimConfig>(JsonUtility.ToJson(Config));

        private void OnValidate()
        {
            if (Config == null) Config = SimConfig.CreateDefault();
            if (Config.Lifts == null || Config.Lifts.Length == 0) Config.Lifts = SimConfig.DefaultLifts();
            if (Config.Lighting == null) Config.Lighting = new SimConfig.LightingSettings();
            if (Config.Lighting.Lights == null || Config.Lighting.Lights.Length == 0) Config.Lighting.Lights = SimConfig.DefaultLights();
            if (Config.Parking == null) Config.Parking = new SimConfig.ParkingSettings();
            if (Config.Parking.PartySizeWeights == null || Config.Parking.PartySizeWeights.Length == 0) Config.Parking.PartySizeWeights = new[] { 25f, 40f, 15f, 20f };
            if (Config.Walking == null) Config.Walking = new SimConfig.WalkingSettings();
            if (Config.Food == null) Config.Food = new SimConfig.FoodSettings();
            if (Config.Buildings == null || Config.Buildings.Length == 0) Config.Buildings = SimConfig.DefaultBuildings();
            Config.Parking.DefaultLotCapacity = Mathf.Clamp(Config.Parking.DefaultLotCapacity, 1, 1024);
            Config.Map.Width = Mathf.Max(32, Config.Map.Width / 32 * 32);
            Config.Map.Height = Mathf.Max(32, Config.Map.Height / 32 * 32);
        }
    }
}
