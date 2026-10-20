namespace IceCoast.View.Rendering
{
    /// <summary>Ground surface textures, multiplied over the tile's colour.</summary>
    public enum SurfaceDetail
    {
        None = -1,
        Powder,
        Packed,
        /// <summary>Corduroy lines along grid x.</summary>
        GroomedX,
        /// <summary>Corduroy lines along grid y.</summary>
        GroomedY,
        /// <summary>Corduroy along +x+y, straight down the screen.</summary>
        GroomedDown,
        /// <summary>Corduroy along +x-y, straight across the screen.</summary>
        GroomedAcross,
        Slush,
        Ice,
        Crud,
        SnowyRock,
        Grass,
        Dirt,
        Rock,
        Scree,
        Water,
        /// <summary>Trampled snow with boot prints: walking areas.</summary>
        Walkway,
        /// <summary>Asphalt road with a dashed centre line along grid x.</summary>
        RoadX,
        /// <summary>Asphalt road with a dashed centre line along grid y.</summary>
        RoadY,
        /// <summary>Parking stalls: asphalt with painted dividers every half tile along grid x.</summary>
        ParkingStalls,
        /// <summary>The parking lot's driving aisle: plain asphalt.</summary>
        ParkingAisle,
        Count,
    }

    public enum TreeKind { Spruce, Fir, Hardwood, Count }

    public enum SkierPose
    {
        /// <summary>Skiing across the hill, facing screen right (mirror for left).</summary>
        SkiSide,
        /// <summary>Skiing straight at the camera.</summary>
        SkiFront,
        /// <summary>Back to the camera: queueing, or towed up a surface lift.</summary>
        Back,
        /// <summary>Walking with skis on the shoulder, two frames, facing right.</summary>
        Walk1,
        Walk2,
        /// <summary>Sitting on a chair, facing the camera.</summary>
        Seated,
        Fallen,
        /// <summary>Second idle frame while standing in line (a shuffle).</summary>
        BackIdle,
        Count,
    }
}
