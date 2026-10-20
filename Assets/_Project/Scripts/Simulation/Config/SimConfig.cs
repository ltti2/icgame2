using System;
using IceCoast.Simulation.Core;

namespace IceCoast.Simulation.Config
{
    /// <summary>
    /// All tuning numbers in one place. Grouped into nested sections so the Unity inspector
    /// (through SimConfigAsset) shows collapsible foldouts. Every value here is a starting point
    /// to be tuned; none of the systems hard-code balance numbers.
    /// </summary>
    [Serializable]
    public class SimConfig
    {
        public MapSettings Map = new MapSettings();
        public TimeSettings Time = new TimeSettings();
        public TerrainSettings Terrain = new TerrainSettings();
        public EconomySettings Economy = new EconomySettings();
        public GuestSettings Guests = new GuestSettings();
        public RunSettings Runs = new RunSettings();
        public SnowSettings Snow = new SnowSettings();
        public WeatherSettings Weather = new WeatherSettings();
        public LightingSettings Lighting = new LightingSettings();
        public ParkingSettings Parking = new ParkingSettings();
        public WalkingSettings Walking = new WalkingSettings();
        public LiftDefinition[] Lifts = DefaultLifts();
        public BuildingDefinition[] Buildings = DefaultBuildings();
        public FoodSettings Food = new FoodSettings();

        /// <summary>Sandbox mode: infinite money, everything else identical.</summary>
        public bool SandboxMode;

        /// <summary>
        /// How values are shown to the player. The simulation always computes in SI (metres, cm, km/h, Celsius);
        /// this only affects display text. Imperial (feet, inches, mph, Fahrenheit) by default.
        /// </summary>
        public UnitSystem Units = UnitSystem.Imperial;

        public static SimConfig CreateDefault() => new SimConfig();

        public LightDefinition GetLight(LightSize size)
        {
            var lights = Lighting.Lights;
            for (int i = 0; i < lights.Length; i++)
                if (lights[i].Size == size) return lights[i];
            throw new ArgumentException($"No LightDefinition configured for {size}");
        }

        public BuildingDefinition GetBuilding(BuildingType type)
        {
            if (Buildings != null)
                for (int i = 0; i < Buildings.Length; i++)
                    if (Buildings[i].Type == type) return Buildings[i];
            // Configs saved before buildings existed: fall back to the built-in numbers.
            foreach (var def in DefaultBuildings())
                if (def.Type == type) return def;
            throw new ArgumentException($"No BuildingDefinition configured for {type}");
        }

        public LiftDefinition GetLift(LiftType type)
        {
            for (int i = 0; i < Lifts.Length; i++)
                if (Lifts[i].Type == type) return Lifts[i];
            throw new ArgumentException($"No LiftDefinition configured for {type}");
        }

        [Serializable]
        public class MapSettings
        {
            public int Width = 512;
            public int Height = 512;
            public int ParcelSize = 64;
            public int Seed = 12345;
            /// <summary>
            /// Tiles of scenery generated around the playable map on every side. It continues the mountain
            /// range so the edge looks natural, but it is never owned, built on or simulated.
            /// </summary>
            public int SceneryPadding = 160;
        }

        [Serializable]
        public class TimeSettings
        {
            /// <summary>Simulation ticks per in-game minute. At 10 ticks/s real, 1 game minute = 1 real second at 1x.</summary>
            public int TicksPerGameMinute = 10;
            public int LiftsOpenMinute = 9 * 60;
            public int LiftsCloseMinute = 16 * 60;
            public int ArrivalsStartMinute = 8 * 60;
            public int ArrivalsEndMinute = 13 * 60;
            /// <summary>
            /// Early birds: a few cars trickle in from this minute until ArrivalsStartMinute. They wander the base
            /// and line up at the lifts LineUpBeforeOpeningMinutes before opening.
            /// </summary>
            public int EarlyArrivalsStartMinute = 6 * 60;
            /// <summary>Guests start queueing at lifts this long before they open.</summary>
            public int LineUpBeforeOpeningMinutes = 30;
            /// <summary>Game starts at this minute of day 0 so the first thing you see is a morning.</summary>
            public int StartMinuteOfDay = 7 * 60 + 30;

            /// <summary>
            /// Night skiing: lifts with a lit trail off the top keep running after LiftsCloseMinute until this
            /// minute. Everything else closes at LiftsCloseMinute as usual.
            /// </summary>
            public int NightCloseMinute = 21 * 60;
            /// <summary>The after-work crowd arrives in this window when there is lit terrain.</summary>
            public int NightArrivalsStartMinute = 15 * 60 + 30;
            public int NightArrivalsEndMinute = 19 * 60;

            // Where the sun rises and sets (Killington, Vermont). Used for sunrise/sunset times, which decide
            // when it is dark enough that unlit trails are skied only by those who have to.
            public float LatitudeDegrees = 43.6f;
            public float LongitudeDegrees = -72.8f;
            /// <summary>Meridian of the time zone (Eastern = -75). The clock follows US daylight saving from March.</summary>
            public float TimeZoneMeridianDegrees = -75f;
            /// <summary>Minutes after sunset (and before sunrise) until it counts as dark: civil twilight, roughly.</summary>
            public int TwilightMinutes = 30;
        }

        [Serializable]
        public class TerrainSettings
        {
            public float TileSizeMeters = 10f;
            public float MetersPerHeightStep = 1f;
            /// <summary>Max height difference between adjacent vertices, in steps. Keeps iso art valid.</summary>
            public int MaxStepPerEdge = 8;
            // Modelled on Killington, Vermont: base ~1,165 ft (355 m), summit 4,241 ft (1,293 m).
            public int BaseElevationSteps = 355;
            public int SummitElevationSteps = 1293;

            /// <summary>
            /// Above this elevation the spruce-fir forest becomes stunted (krummholz) and a little thinner.
            /// Vermont has no real treeline: even the summit is wooded.
            /// </summary>
            public int StuntedForestSteps = 1150;
            /// <summary>0..1. How much of the untouched mountain is forest. Vermont: almost all of it.</summary>
            public float ForestCover = 0.93f;

            public long TerraformCostPerStepDirt = 40;
            public long TerraformCostPerStepRock = 250;
            public long TreeClearCostPerDensity = 30;
            public long LumberIncomePerDensity = 8;
        }

        [Serializable]
        public class EconomySettings
        {
            public long StartingCash = 60_000;
            public int StartingTicketPrice = 35;
            public float StartingReputation = 30f;

            /// <summary>Guests per hour at reputation 50, ideal weather and fair prices.</summary>
            public float BaseDemandPerHour = 160f;
            /// <summary>Ticket price a guest considers fair for a resort with no runs. Grows with terrain offered.</summary>
            public float BaseFairPrice = 20f;
            public float FairPricePerRunTile = 0.01f;
            public float FairPriceMax = 220f;
            public float PriceElasticity = 1.3f;

            /// <summary>
            /// Early-bird cars per hour before the morning rush (at reputation 50, fair weather). Each minute is a
            /// dice roll, ramping up toward the rush, so they come in ones and twos rather than a steady stream.
            /// </summary>
            public float EarlyBirdCarsPerHour = 5f;

            /// <summary>Evening demand relative to the day's, when every open trail is lit (scaled by the lit share).</summary>
            public float NightDemandFactor = 0.5f;

            public float WeekendDemandMultiplier = 1.6f;
            public float HolidayDemandMultiplier = 2.0f;

            /// <summary>Fixed overhead per day: insurance, admin, a minimum crew.</summary>
            public long DailyFixedCost = 600;

            public float ReputationGainRate = 0.08f;
            public float ReputationMax = 100f;

            public int BankruptcyGraceDays = 14;
        }

        [Serializable]
        public class GuestSettings
        {
            public int MaxGuests = 4000;

            public float SkiSpeedTilesPerMinute = 4f;
            public float WalkSpeedTilesPerMinute = 1.5f;

            /// <summary>Weights for First-timer, Beginner, Intermediate, Advanced, Expert, Pro.</summary>
            public float[] SkillWeights = { 15f, 25f, 30f, 18f, 9f, 3f };

            public int MinStayMinutes = 150;
            public int MaxStayMinutes = 420;

            /// <summary>How far guests will walk to a lift from the base area (tiles).</summary>
            public float LiftSearchRadius = 60f;
            /// <summary>How far guests will walk to a lift when they are up on the mountain (tiles).</summary>
            public float LiftWalkRadius = 12f;
            /// <summary>A run "passes" a lift when its bottom station is this close to any run tile (tiles).</summary>
            public float LiftExitRadius = 6f;
            /// <summary>Within this many tiles of the entrance a guest counts as being at the base.</summary>
            public float BaseAreaRadius = 35f;
            public float RunConnectRadius = 4f;
            public float ChoiceRandomness = 0.35f;
            /// <summary>
            /// How much guests value what a lift or run leads to, relative to the run itself (0..1).
            /// Higher = more willing to ride a lower lift to reach better terrain further up.
            /// </summary>
            public float RouteLookahead = 0.75f;

            public float InjuryBaseChancePerTile = 0.00002f;
            public float InjuryGapExponent = 2.6f;
            public float FallChancePerTile = 0.0008f;

            public int NeedsUpdateIntervalTicks = 10;
        }

        /// <summary>Hunger, and how guests decide to buy food (see Buildings/BuildingSystem).</summary>
        [Serializable]
        public class FoodSettings
        {
            /// <summary>Hunger (0..100) goes up by one every this many game minutes: 30 an hour at 2.</summary>
            public int HungerMinutesPerPoint = 2;
            /// <summary>Guests arrive with hunger between 0 and this (breakfast was a while ago for some).</summary>
            public int StartingHungerMax = 40;
            /// <summary>Above this hunger guests start looking for food whenever they choose what to do next.</summary>
            public int HungryThreshold = 55;
            /// <summary>
            /// How far guests will walk to food from where they are (tiles). Anywhere on the same walking area also
            /// counts, and from anywhere in the base area, so does anywhere in the base village.
            /// </summary>
            public float FoodWalkRadius = 20f;
            /// <summary>Guests skip a food line that would take longer than this (minutes).</summary>
            public float MaxWaitMinutes = 12f;
            /// <summary>Food buildings open this many minutes before the lifts (breakfast for early birds).</summary>
            public int OpensBeforeLiftsMinutes = 60;
        }

        [Serializable]
        public class RunSettings
        {
            public int MinRunTiles = 4;
            public float UphillPenalty = 6f;
            /// <summary>
            /// Runs connect where their tiles come within this many tiles of each other, so trails painted with
            /// a small gap still join. Skiers traverse the gap.
            /// </summary>
            public float JunctionRadius = 2.5f;

            // Thresholds in percent grade. Index = Difficulty (Green..DoubleBlack); above the last = Triple Black.
            public float[] AvgSlopeLimits = { 12f, 25f, 40f, 50f };
            public float[] MaxSlopeLimits = { 18f, 35f, 55f, 70f };

            /// <summary>Below this grade skiers have to pole or skate.</summary>
            public float FlatGrade = 4f;
            /// <summary>Uphill tiles are walked/side-stepped at this fraction of walking speed.</summary>
            public float UphillSpeedFactor = 0.5f;
        }

        [Serializable]
        public class SnowSettings
        {
            /// <summary>Each owned chunk is processed once every this many ticks (staggered).</summary>
            public int UpdateIntervalTicks = 60;
            public int InitialBaseDepthCm = 35;
            public int InitialSummitDepthCm = 110;
            public float MeltCmPerDegreeHour = 0.25f;
            public float SouthAspectMeltBonus = 0.6f;
            public byte CompactionPerSkier = 2;
            public byte CrudThreshold = 40;
            public byte PackedThreshold = 110;
            public byte IceThreshold = 225;
            public int BareDepthCm = 5;
        }

        [Serializable]
        public class WeatherSettings
        {
            public float LapseRateCPerMeter = 0.0065f;
            /// <summary>Elevation the weather's base temperature refers to (Killington base, 1,165 ft).</summary>
            public float BaseElevationMeters = 355f;
            public int MinStateHours = 4;
            public int MaxStateHours = 30;
            public int ForecastLength = 4;
        }

        [Serializable]
        public class LightingSettings
        {
            public LightDefinition[] Lights = DefaultLights();
            /// <summary>Light level (0..255) a tile needs to count as lit.</summary>
            public int LitThreshold = 96;
            /// <summary>Share of a trail's tiles that must be lit for the trail to count as lit at night.</summary>
            public float RunLitFraction = 0.8f;
            /// <summary>Lights switch on this many minutes before sunset (when night skiing is on).</summary>
            public int LightsOnBeforeSunsetMinutes = 20;
            /// <summary>
            /// How much less guests want a trail in the dark. Big enough that they only ski a dark trail when it
            /// is the only way down.
            /// </summary>
            public float DarkRunPenalty = 3f;
        }

        [Serializable]
        public class ParkingSettings
        {
            /// <summary>Spaces in the lot the resort starts with.</summary>
            public int DefaultLotCapacity = 150;
            /// <summary>Guests per car: weights for 1, 2, 3 and 4 people.</summary>
            public float[] PartySizeWeights = { 25f, 40f, 15f, 20f };
            /// <summary>Cars drive in and out at this speed (time-compressed like skiers).</summary>
            public float CarSpeedTilesPerMinute = 24f;
            /// <summary>Reputation lost per guest turned away by a full lot (capped per day).</summary>
            public float TurnAwayReputationPenalty = 0.02f;
            public float TurnAwayReputationMaxPerDay = 3f;
        }

        [Serializable]
        public class WalkingSettings
        {
            /// <summary>Packing and grading a tile of walking area. Trees on it are cleared at the usual rate.</summary>
            public long CostPerTile = 25;
            /// <summary>Steepest grade a walking area can be laid on.</summary>
            public float MaxSlopePercent = 30f;
            /// <summary>A lift station within this many tiles of a walking area opens onto it.</summary>
            public float LiftLinkRadius = 2.5f;
            /// <summary>A trail whose bottom is within this many tiles of a walking area ends on it.</summary>
            public float RunEndRadius = 3f;
            /// <summary>Steepest grade a building footprint can sit on (buildings need walking areas).</summary>
            public float BuildingMaxSlopePercent = 12f;
        }

        public static BuildingDefinition[] DefaultBuildings() => new[]
        {
            new BuildingDefinition
            {
                Type = BuildingType.WaffleCabin, DisplayName = "Waffle Cabin", FootprintWidth = 1, FootprintHeight = 1,
                BuildCost = 18_000, OperatingCostPerHour = 30, IngredientCostPerSale = 2,
                DefaultPrice = 8, FairPrice = 8, MaxPrice = 20,
                ServiceMinutes = 0.75f, EatMinutes = 6f, HappinessBoost = 8f, EnergyRestore = 10, MaxQueue = 14,
                RequiredReputation = 0f,
            },
        };

        public static LightDefinition[] DefaultLights() => new[]
        {
            new LightDefinition { Size = LightSize.Small, DisplayName = "Small light", Cost = 1_500, RadiusTiles = 5f, PoleHeightMeters = 6f,
                PowerKw = 2f, OperatingCostPerHour = 1 },
            new LightDefinition { Size = LightSize.Medium, DisplayName = "Medium light", Cost = 3_800, RadiusTiles = 8f, PoleHeightMeters = 11f,
                PowerKw = 6f, OperatingCostPerHour = 3 },
            new LightDefinition { Size = LightSize.Large, DisplayName = "Large light", Cost = 8_500, RadiusTiles = 13f, PoleHeightMeters = 18f,
                PowerKw = 15f, OperatingCostPerHour = 7 },
        };

        /// <remarks>
        /// Line speeds are slow enough to watch at 1x (a chair takes about 30 real seconds to climb 100 tiles),
        /// while capacities stay realistic. Carrier spacing in tiles = speed x seats x 60 / capacity: about 0.6
        /// for T-bars and chairs, 0.8 for bubbles and 1.2 for gondola cabins. Going much slower at the same
        /// capacity packs carriers into a solid column; lower the capacity too if you slow a line further.
        /// </remarks>
        public static LiftDefinition[] DefaultLifts() => new[]
        {
            new LiftDefinition { Type = LiftType.RopeTow, DisplayName = "Rope Tow", CapacityPerHour = 200, SeatsPerCarrier = 1, LoadingEfficiency = 1f, SpeedTilesPerMinute = 1.2f,
                BaseCost = 12_000, CostPerTile = 120, OperatingCostPerHour = 35, MinLengthTiles = 6, MaxLengthTiles = 45, MaxGradePercent = 30f,
                TowerSpacingTiles = 10, CableHeightMeters = 1f, PowerDrawKw = 15, StaffRequired = 1, WindLimitKmh = 90, Comfort = 0.2f,
                RequiredReputation = 0 },
            new LiftDefinition { Type = LiftType.MagicCarpet, DisplayName = "Magic Carpet", CapacityPerHour = 300, SeatsPerCarrier = 1, LoadingEfficiency = 1f, SpeedTilesPerMinute = 0.6f,
                BaseCost = 20_000, CostPerTile = 400, OperatingCostPerHour = 30, MinLengthTiles = 3, MaxLengthTiles = 15, MaxGradePercent = 15f,
                TowerSpacingTiles = 0, CableHeightMeters = 0f, PowerDrawKw = 10, StaffRequired = 1, WindLimitKmh = 120, Comfort = 0.5f,
                RequiredReputation = 10 },
            new LiftDefinition { Type = LiftType.TBar, DisplayName = "T-Bar", CapacityPerHour = 450, SeatsPerCarrier = 2, LoadingEfficiency = 0.8f, SpeedTilesPerMinute = 2.2f,
                BaseCost = 110_000, CostPerTile = 600, OperatingCostPerHour = 70, MinLengthTiles = 15, MaxLengthTiles = 140, MaxGradePercent = 45f,
                TowerSpacingTiles = 12, CableHeightMeters = 4f, PowerDrawKw = 60, StaffRequired = 2, WindLimitKmh = 80, Comfort = 0.3f,
                RequiredReputation = 35 },
            new LiftDefinition { Type = LiftType.FixedGripChair, DisplayName = "Fixed-Grip Chair", CapacityPerHour = 1000, SeatsPerCarrier = 3, LoadingEfficiency = 0.8f, SpeedTilesPerMinute = 3.2f,
                BaseCost = 550_000, CostPerTile = 2_500, OperatingCostPerHour = 160, MinLengthTiles = 30, MaxLengthTiles = 220, MaxGradePercent = 80f,
                TowerSpacingTiles = 8, CableHeightMeters = 9f, PowerDrawKw = 250, StaffRequired = 3, WindLimitKmh = 65, Comfort = 0.55f,
                RequiredReputation = 50 },
            new LiftDefinition { Type = LiftType.DetachableChair, DisplayName = "Detachable Chair", CapacityPerHour = 2000, SeatsPerCarrier = 4, LoadingEfficiency = 0.85f, SpeedTilesPerMinute = 4.8f,
                BaseCost = 2_400_000, CostPerTile = 6_000, OperatingCostPerHour = 380, MinLengthTiles = 40, MaxLengthTiles = 300, MaxGradePercent = 80f,
                TowerSpacingTiles = 8, CableHeightMeters = 10f, PowerDrawKw = 600, StaffRequired = 4, WindLimitKmh = 60, Comfort = 0.7f,
                RequiredReputation = 70 },
            new LiftDefinition { Type = LiftType.DetachableBubble, DisplayName = "Bubble Chair", CapacityPerHour = 2200, SeatsPerCarrier = 6, LoadingEfficiency = 0.85f, SpeedTilesPerMinute = 4.8f,
                BaseCost = 3_400_000, CostPerTile = 8_000, OperatingCostPerHour = 420, MinLengthTiles = 40, MaxLengthTiles = 300, MaxGradePercent = 80f,
                TowerSpacingTiles = 8, CableHeightMeters = 10f, PowerDrawKw = 650, StaffRequired = 4, WindLimitKmh = 70, Comfort = 0.85f,
                WeatherProtected = true, RequiredReputation = 78 },
            new LiftDefinition { Type = LiftType.Gondola, DisplayName = "Gondola", CapacityPerHour = 1600, SeatsPerCarrier = 8, LoadingEfficiency = 0.75f, SpeedTilesPerMinute = 4f,
                BaseCost = 6_000_000, CostPerTile = 12_000, OperatingCostPerHour = 600, MinLengthTiles = 60, MaxLengthTiles = 500, MaxGradePercent = 100f,
                TowerSpacingTiles = 14, CableHeightMeters = 18f, PowerDrawKw = 900, StaffRequired = 5, WindLimitKmh = 75, Comfort = 0.95f,
                WeatherProtected = true, CanTurn = true, RequiredReputation = 85 },
        };
    }
}
