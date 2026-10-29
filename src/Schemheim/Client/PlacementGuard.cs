using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace PlanBuild.Client
{
    // Refuses an ordinary hammer click that would put a piece somewhere the blueprint disagrees
    // with. Assisted placements always match a missing planned piece exactly, so they pass.
    internal sealed class PlacementGuard : IDisposable
    {
        private static PlacementGuard instance;
        private readonly Harmony harmony = new Harmony(PlanBuildPlugin.PluginGUID + ".restriction");
        private readonly Func<BlueprintProjection> restrictedProjection;
        public string LastRefusal { get; private set; }

        public PlacementGuard(Func<BlueprintProjection> restrictedProjection)
        {
            this.restrictedProjection = restrictedProjection;
            instance = this;
            harmony.PatchAll(typeof(PlacementGuard));
        }

        [HarmonyPrefix, HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        private static bool Restrict(Player __instance, Piece piece, ref bool __result)
        {
            if (instance == null || __instance != Player.m_localPlayer || !piece || !__instance.m_placementGhost) return true;
            var projection = instance.restrictedProjection();
            if (projection == null) return true;
            var ghost = __instance.m_placementGhost.transform;
            string message = instance.Describe(projection, piece, ghost.position, ghost.rotation);
            if (message == null) return true;
            instance.LastRefusal = message;
            __result = false;
            __instance.Message(MessageHud.MessageType.Center, message);
            return false;
        }

        public string Describe(BlueprintProjection projection, Piece piece, Vector3 position, Quaternion rotation)
        {
            var planned = new List<PlacementRestriction.Planned>(projection.Pieces.Count);
            foreach (var entry in projection.Pieces)
                planned.Add(new PlacementRestriction.Planned(entry.PrefabHash, projection.PiecePosition(entry),
                    projection.PieceRotation(entry), entry.State));
            var bounds = projection.WorldBounds();
            bounds.Expand(1f);
            var verdict = PlacementRestriction.Evaluate(piece.gameObject.name.GetStableHashCode(), position, rotation,
                planned, bounds.Contains(position), out int match);
            string name = Localization.instance.Localize(piece.m_name);
            string expected = match >= 0 && projection.Pieces[match].Prefab.GetComponent<Piece>() is Piece wanted
                ? Localization.instance.Localize(wanted.m_name) : null;
            return verdict switch
            {
                PlacementRestriction.Verdict.Allowed => null,
                PlacementRestriction.Verdict.WrongRotation => $"Restricted: rotate {name} to match the blueprint here.",
                PlacementRestriction.Verdict.WrongPiece => $"Restricted: the blueprint wants {expected ?? "another piece"} here, not {name}.",
                PlacementRestriction.Verdict.AlreadyBuilt => $"Restricted: {name} is already built here.",
                _ => $"Restricted: {name} is not in the blueprint here. Disable Restrict placement to build freely."
            };
        }

        public void Dispose()
        {
            harmony.UnpatchSelf();
            instance = null;
        }
    }
}
