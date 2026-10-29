namespace PlanBuild.Client
{
    // What the loaded world holds at a planned piece's position. Only Built removes a piece from
    // the hologram; the two mismatch states stay visible so the player can see what to tear down.
    internal enum PieceState
    {
        Missing,
        Built,
        WrongPiece,
        WrongRotation
    }
}
