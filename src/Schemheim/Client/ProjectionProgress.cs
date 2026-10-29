using System.Collections.Generic;
using UnityEngine;

namespace PlanBuild.Client
{
    internal static class ProjectionProgress
    {
        private const float ExtraMargin = 0.25f;

        // Index loaded, real pieces so a large blueprint does not scan the world once per entry.
        public static void Refresh(BlueprintProjection projection)
        {
            var cells = new Dictionary<Vector3Int, List<int>>();
            var actual = new List<PieceVerification.Pose>();
            var actualPieces = new List<Piece>();
            foreach (var piece in Piece.s_allPieces)
            {
                if (!piece || !piece.IsPlacedByPlayer()) continue;
                var view = piece.GetComponent<ZNetView>();
                if (!view || !view.IsValid()) continue;
                var cell = Vector3Int.FloorToInt(piece.transform.position);
                if (!cells.TryGetValue(cell, out var bucket)) cells[cell] = bucket = new List<int>();
                bucket.Add(actual.Count);
                actual.Add(new PieceVerification.Pose(view.GetZDO().GetPrefab(), piece.transform.position,
                    piece.transform.rotation, piece.transform.lossyScale));
                actualPieces.Add(piece);
            }
            var planned = new List<PieceVerification.Pose>(projection.Pieces.Count);
            foreach (var piece in projection.Pieces)
                planned.Add(new PieceVerification.Pose(piece.PrefabHash, projection.PiecePosition(piece),
                    projection.PieceRotation(piece), piece.Entry.GetScale()));

            IEnumerable<int> Nearby(int index)
            {
                var center = Vector3Int.FloorToInt(planned[index].Position);
                for (int x = -1; x <= 1; x++)
                    for (int y = -1; y <= 1; y++)
                        for (int z = -1; z <= 1; z++)
                        {
                            if (!cells.TryGetValue(center + new Vector3Int(x, y, z), out var bucket)) continue;
                            foreach (int candidate in bucket) yield return candidate;
                        }
            }

            var result = PieceVerification.Verify(planned, actual, Nearby);
            for (int i = 0; i < projection.Pieces.Count; i++)
            {
                projection.Pieces[i].State = result.States[i];
                projection.Pieces[i].Actual = result.Matched[i] >= 0 ? actualPieces[result.Matched[i]] : null;
            }

            var bounds = projection.WorldBounds();
            bounds.Expand(ExtraMargin * 2);
            var extras = new List<Piece>();
            for (int i = 0; i < actualPieces.Count; i++)
                if (!result.ActualClaimed[i] && bounds.Contains(actual[i].Position)) extras.Add(actualPieces[i]);
            projection.SetExtras(extras);
        }
    }
}
