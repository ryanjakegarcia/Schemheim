using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlanBuild.Client;
using UnityEngine;
using static PlanBuild.Client.PlacementRestriction;

namespace PlanBuildTest
{
    [TestClass]
    public class PlacementRestrictionTests
    {
        private const int Wall = 1, Beam = 2;

        private static Quaternion Yaw(float degrees)
        {
            double half = degrees * Math.PI / 360.0;
            return new Quaternion(0, (float)Math.Sin(half), 0, (float)Math.Cos(half));
        }

        private static Planned Plan(int prefab, float x, float yaw = 0, PieceState state = PieceState.Missing) =>
            new Planned(prefab, new Vector3(x, 0, 0), Yaw(yaw), state);

        [TestMethod]
        public void AMatchingMissingPieceIsAllowedAndNamed()
        {
            var planned = new[] { Plan(Beam, 0), Plan(Wall, 0) };
            Assert.AreEqual(Verdict.Allowed, Evaluate(Wall, new Vector3(0.08f, 0, 0), Yaw(3), planned, true, out int match));
            Assert.AreEqual(1, match);
        }

        [TestMethod]
        public void OutsideTheBlueprintAnythingGoes()
        {
            var planned = new[] { Plan(Wall, 0) };
            Assert.AreEqual(Verdict.Allowed, Evaluate(Beam, new Vector3(50, 0, 0), Yaw(0), planned, false, out int match));
            Assert.AreEqual(-1, match);
        }

        [TestMethod]
        public void InsideTheBlueprintAnUnplannedSpotIsRefused()
        {
            var planned = new[] { Plan(Wall, 0) };
            Assert.AreEqual(Verdict.NotInBlueprint, Evaluate(Wall, new Vector3(2, 0, 0), Yaw(0), planned, true, out _));
        }

        [TestMethod]
        public void WrongRotationAndWrongPieceAreExplained()
        {
            var planned = new[] { Plan(Wall, 0) };
            Assert.AreEqual(Verdict.WrongRotation, Evaluate(Wall, Vector3.zero, Yaw(22.5f), planned, true, out int match));
            Assert.AreEqual(0, match);
            Assert.AreEqual(Verdict.WrongPiece, Evaluate(Beam, Vector3.zero, Yaw(0), planned, true, out match));
            Assert.AreEqual(0, match);
        }

        [TestMethod]
        public void TheMostSpecificVerdictWinsWhenPlansShareASpot()
        {
            var planned = new[] { Plan(Beam, 0), Plan(Wall, 0) };
            Assert.AreEqual(Verdict.WrongRotation, Evaluate(Wall, Vector3.zero, Yaw(45), planned, true, out int match));
            Assert.AreEqual(1, match, "Name the wall the player is holding, not the beam.");
        }

        [TestMethod]
        public void ABuiltSpotRefusesADuplicateButStillAllowsAnotherPlannedPieceThere()
        {
            var planned = new[] { Plan(Wall, 0, state: PieceState.Built), Plan(Beam, 0) };
            Assert.AreEqual(Verdict.AlreadyBuilt, Evaluate(Wall, Vector3.zero, Yaw(0), planned, true, out _));
            Assert.AreEqual(Verdict.Allowed, Evaluate(Beam, Vector3.zero, Yaw(0), planned, true, out int match));
            Assert.AreEqual(1, match);
        }
    }
}
