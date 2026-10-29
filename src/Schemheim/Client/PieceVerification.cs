using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlanBuild.Client
{
    // Matches planned pieces against real ones, Litematica-verifier style. Pure so it can be tested
    // without a world: callers supply the candidate pieces near each planned piece.
    //
    // Two passes, because Valheim pieces legitimately share origins (a wall and a beam, two crossing
    // beams). Exact matches are claimed first; only real pieces nobody claimed can then count as a
    // wrong piece or wrong rotation at a still-missing planned piece. Leftover real pieces inside the
    // blueprint are extras.
    internal static class PieceVerification
    {
        public const float PositionTolerance = 0.05f;
        public const float RotationTolerance = 1f;
        public const float ScaleTolerance = 0.01f;

        public readonly struct Pose
        {
            public int PrefabHash { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            public Pose(int prefabHash, Vector3 position, Quaternion rotation, Vector3 scale)
            { PrefabHash = prefabHash; Position = position; Rotation = rotation; Scale = scale; }
        }

        public sealed class Result
        {
            public PieceState[] States { get; }
            // Index into the actual list of the piece that satisfied or conflicts with each planned
            // piece, or -1.
            public int[] Matched { get; }
            public bool[] ActualClaimed { get; }
            public Result(int planned, int actual)
            {
                States = new PieceState[planned];
                Matched = new int[planned];
                for (int i = 0; i < planned; i++) Matched[i] = -1;
                ActualClaimed = new bool[actual];
            }
        }

        public static bool SamePlace(in Pose planned, in Pose actual) =>
            Vector3.Distance(planned.Position, actual.Position) <= PositionTolerance;

        public static bool Exact(in Pose planned, in Pose actual) =>
            planned.PrefabHash == actual.PrefabHash && SamePlace(planned, actual) &&
            Quaternion.Angle(planned.Rotation, actual.Rotation) <= RotationTolerance &&
            Vector3.Distance(planned.Scale, actual.Scale) <= ScaleTolerance;

        public static Result Verify(IReadOnlyList<Pose> planned, IReadOnlyList<Pose> actual,
            Func<int, IEnumerable<int>> nearby)
        {
            var result = new Result(planned.Count, actual.Count);
            for (int p = 0; p < planned.Count; p++)
            {
                foreach (int a in nearby(p))
                {
                    if (result.ActualClaimed[a] || !Exact(planned[p], actual[a])) continue;
                    result.States[p] = PieceState.Built;
                    result.Matched[p] = a;
                    result.ActualClaimed[a] = true;
                    break;
                }
            }
            for (int p = 0; p < planned.Count; p++)
            {
                if (result.States[p] == PieceState.Built) continue;
                int conflict = -1;
                var state = PieceState.Missing;
                foreach (int a in nearby(p))
                {
                    if (result.ActualClaimed[a] || !SamePlace(planned[p], actual[a])) continue;
                    bool samePiece = planned[p].PrefabHash == actual[a].PrefabHash &&
                        Vector3.Distance(planned[p].Scale, actual[a].Scale) <= ScaleTolerance;
                    // A misrotated copy of the right piece is the more specific finding.
                    if (samePiece) { conflict = a; state = PieceState.WrongRotation; break; }
                    if (conflict < 0) { conflict = a; state = PieceState.WrongPiece; }
                }
                if (conflict < 0) continue;
                result.States[p] = state;
                result.Matched[p] = conflict;
                result.ActualClaimed[conflict] = true;
            }
            return result;
        }
    }
}
