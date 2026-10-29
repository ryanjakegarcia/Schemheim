using System.Collections.Generic;
using UnityEngine;

namespace PlanBuild.Client
{
    // Litematica's placement restriction: while a blueprint is loaded, the hammer may only put a
    // piece where the blueprint wants that piece. Pure; the Harmony hook lives in PlacementGuard.
    internal static class PlacementRestriction
    {
        // Looser than verification: a hand-placed piece lands on vanilla's snap grid and the
        // hammer's 22.5° steps, so small drift must still count as the intended spot.
        public const float PositionTolerance = 0.1f;
        public const float RotationTolerance = 5f;

        public enum Verdict { Allowed, WrongRotation, WrongPiece, AlreadyBuilt, NotInBlueprint }

        public readonly struct Planned
        {
            public int PrefabHash { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public PieceState State { get; }
            public Planned(int prefabHash, Vector3 position, Quaternion rotation, PieceState state)
            { PrefabHash = prefabHash; Position = position; Rotation = rotation; State = state; }
        }

        // `match` receives the index of the planned piece the verdict refers to, or -1.
        public static Verdict Evaluate(int prefabHash, Vector3 position, Quaternion rotation,
            IReadOnlyList<Planned> planned, bool insideBlueprint, out int match)
        {
            match = -1;
            var verdict = insideBlueprint ? Verdict.NotInBlueprint : Verdict.Allowed;
            for (int i = 0; i < planned.Count; i++)
            {
                var entry = planned[i];
                if (Vector3.Distance(entry.Position, position) > PositionTolerance) continue;
                bool samePiece = entry.PrefabHash == prefabHash;
                bool sameRotation = Quaternion.Angle(entry.Rotation, rotation) <= RotationTolerance;
                Verdict candidate;
                if (entry.State == PieceState.Built) candidate = samePiece && sameRotation ? Verdict.AlreadyBuilt : Verdict.WrongPiece;
                else if (samePiece && sameRotation) { match = i; return Verdict.Allowed; }
                else if (samePiece) candidate = Verdict.WrongRotation;
                else candidate = Verdict.WrongPiece;
                // Prefer the most specific explanation when several planned pieces share the spot.
                if (Rank(candidate) < Rank(verdict) || match < 0) { verdict = candidate; match = i; }
            }
            return verdict;
        }

        private static int Rank(Verdict verdict) => verdict switch
        {
            Verdict.WrongRotation => 0,
            Verdict.AlreadyBuilt => 1,
            Verdict.WrongPiece => 2,
            Verdict.NotInBlueprint => 3,
            _ => 4
        };
    }
}
