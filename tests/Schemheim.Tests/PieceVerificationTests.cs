using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlanBuild.Client;
using UnityEngine;

namespace PlanBuildTest
{
    [TestClass]
    public class PieceVerificationTests
    {
        private const int Wall = 1, Beam = 2;
        private static readonly Vector3 One = Vector3.one;

        // Quaternion.Euler is native; build a yaw rotation by hand so the test host can run it.
        private static Quaternion Yaw(float degrees)
        {
            double half = degrees * Math.PI / 360.0;
            return new Quaternion(0, (float)Math.Sin(half), 0, (float)Math.Cos(half));
        }

        private static PieceVerification.Pose Pose(int prefab, float x, float yaw = 0, float scale = 1) =>
            new PieceVerification.Pose(prefab, new Vector3(x, 0, 0), Yaw(yaw), One * scale);

        private static IEnumerable<int> All(IReadOnlyList<PieceVerification.Pose> actual) => Enumerable.Range(0, actual.Count);

        [TestMethod]
        public void ExactMatchIsBuiltAndNearMissesAreNot()
        {
            var planned = new[] { Pose(Wall, 0), Pose(Wall, 4), Pose(Wall, 8), Pose(Wall, 12) };
            var actual = new List<PieceVerification.Pose>
            {
                Pose(Wall, 0.04f),            // within tolerance
                Pose(Wall, 4.2f),             // too far: counts as nothing here
                Pose(Beam, 8),                // another piece at the spot
                Pose(Wall, 12, yaw: 22.5f),   // right piece, wrong angle
            };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            CollectionAssert.AreEqual(new[] { PieceState.Built, PieceState.Missing, PieceState.WrongPiece, PieceState.WrongRotation }, result.States);
            CollectionAssert.AreEqual(new[] { 0, -1, 2, 3 }, result.Matched);
            CollectionAssert.AreEqual(new[] { true, false, true, true }, result.ActualClaimed);
        }

        [TestMethod]
        public void PiecesThatShareAnOriginAreNotReportedAsEachOthersWrongPiece()
        {
            // A wall and a beam planned at one origin, both built: neither may be flagged.
            var planned = new[] { Pose(Wall, 0), Pose(Beam, 0) };
            var actual = new[] { Pose(Beam, 0), Pose(Wall, 0) };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            CollectionAssert.AreEqual(new[] { PieceState.Built, PieceState.Built }, result.States);
            CollectionAssert.AreEqual(new[] { 1, 0 }, result.Matched);
        }

        [TestMethod]
        public void OnlyTheBeamIsBuiltSoTheWallIsMissingNotWrong()
        {
            var planned = new[] { Pose(Wall, 0), Pose(Beam, 0) };
            var actual = new[] { Pose(Beam, 0) };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            CollectionAssert.AreEqual(new[] { PieceState.Missing, PieceState.Built }, result.States);
        }

        [TestMethod]
        public void TwoCrossingBeamsKeepTheirOwnRotations()
        {
            var planned = new[] { Pose(Beam, 0, yaw: 0), Pose(Beam, 0, yaw: 90) };
            var actual = new[] { Pose(Beam, 0, yaw: 90) };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            // The built beam satisfies the 90° plan; the 0° plan is simply missing, not misrotated.
            CollectionAssert.AreEqual(new[] { PieceState.Missing, PieceState.Built }, result.States);
        }

        [TestMethod]
        public void AWrongRotationWinsOverAWrongPieceAtTheSameSpot()
        {
            var planned = new[] { Pose(Wall, 0) };
            var actual = new[] { Pose(Beam, 0), Pose(Wall, 0, yaw: 45) };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            Assert.AreEqual(PieceState.WrongRotation, result.States[0]);
            Assert.AreEqual(1, result.Matched[0]);
            Assert.IsFalse(result.ActualClaimed[0], "The beam stays unclaimed and becomes an extra.");
        }

        [TestMethod]
        public void ScaledCopiesCountAsADifferentPiece()
        {
            var planned = new[] { Pose(Wall, 0) };
            var actual = new[] { Pose(Wall, 0, scale: 1.5f) };
            var result = PieceVerification.Verify(planned, actual, _ => All(actual));
            Assert.AreEqual(PieceState.WrongPiece, result.States[0]);
        }

        [TestMethod]
        public void OnlyNearbyCandidatesAreConsidered()
        {
            var planned = new[] { Pose(Wall, 0) };
            var actual = new[] { Pose(Wall, 0) };
            var result = PieceVerification.Verify(planned, actual, _ => Array.Empty<int>());
            Assert.AreEqual(PieceState.Missing, result.States[0]);
            Assert.IsFalse(result.ActualClaimed[0]);
        }
    }
}
