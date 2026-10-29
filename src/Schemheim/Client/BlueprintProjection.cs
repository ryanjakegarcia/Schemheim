using System;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace PlanBuild.Client
{
    // Projections borrow meshes only. Never instantiate a game prefab or attach a Piece/ZNetView.
    internal sealed class BlueprintProjection : IDisposable
    {
        internal sealed class Part
        {
            public Mesh Mesh { get; }
            public Matrix4x4 LocalMatrix { get; }
            public BlueprintHitTest HitTest { get; }
            public Part(Mesh mesh, Matrix4x4 matrix, BlueprintHitTest hitTest)
            { Mesh = mesh; LocalMatrix = matrix; HitTest = hitTest; }
        }

        internal sealed class ProjectedPiece
        {
            public Blueprints.PieceEntry Entry { get; }
            public GameObject Prefab { get; }
            public int PrefabHash { get; }
            public IReadOnlyList<Part> Parts { get; }
            public Bounds LocalBounds { get; }
            public PieceState State { get; set; }
            public bool Completed => State == PieceState.Built;
            // The real piece that satisfies or conflicts with this one, for the verifier overlay.
            public Piece Actual { get; set; }
            public ProjectedPiece(Blueprints.PieceEntry entry, GameObject prefab, IReadOnlyList<Part> parts)
            {
                Entry = entry; Prefab = prefab; Parts = parts;
                PrefabHash = prefab.name.GetStableHashCode();
                LocalBounds = PartLibrary.Bounds(parts);
            }
        }

        // A real, player-built piece inside the blueprint that no planned piece accounts for.
        internal sealed class ExtraPiece
        {
            public Piece Piece { get; }
            public IReadOnlyList<Part> Parts { get; }
            public ExtraPiece(Piece piece, IReadOnlyList<Part> parts) { Piece = piece; Parts = parts; }
        }

        // Borrowed render meshes per prefab, shared by planned pieces and extras.
        internal sealed class PartLibrary
        {
            private readonly Dictionary<Mesh, BlueprintHitTest> hitTests = new Dictionary<Mesh, BlueprintHitTest>();
            private readonly Dictionary<GameObject, IReadOnlyList<Part>> parts = new Dictionary<GameObject, IReadOnlyList<Part>>();

            public IReadOnlyList<Part> For(GameObject prefab)
            {
                if (parts.TryGetValue(prefab, out var cached)) return cached;
                var found = new List<Part>();
                var lowerLods = new HashSet<Renderer>(prefab.GetComponentsInChildren<LODGroup>(true)
                    .SelectMany(group => group.GetLODs().Skip(1)).SelectMany(lod => lod.renderers));
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!filter.sharedMesh || !renderer || !renderer.enabled || lowerLods.Contains(renderer) ||
                        !ActiveBelowRoot(filter.transform, prefab.transform)) continue;
                    var mesh = filter.sharedMesh;
                    if (!hitTests.TryGetValue(mesh, out var hitTest))
                    {
                        // Some imported meshes discard CPU data. Use their individual mesh bounds
                        // as a fallback, never the empty volume around the whole piece.
                        hitTest = mesh.isReadable && Enumerable.Range(0, mesh.subMeshCount).All(i => mesh.GetTopology(i) == MeshTopology.Triangles)
                            ? new BlueprintHitTest(mesh.vertices, mesh.triangles) : null;
                        hitTests.Add(mesh, hitTest);
                    }
                    found.Add(new Part(mesh, prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix, hitTest));
                }
                parts[prefab] = found;
                return found;
            }

            public static Bounds Bounds(IReadOnlyList<Part> parts)
            {
                var bounds = new Bounds();
                bool first = true;
                foreach (var part in parts)
                {
                    var meshBounds = part.Mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        var offset = Vector3.Scale(meshBounds.extents, new Vector3(
                            (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        var point = part.LocalMatrix.MultiplyPoint3x4(meshBounds.center + offset);
                        if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                        else bounds.Encapsulate(point);
                    }
                }
                return bounds;
            }

            private static bool ActiveBelowRoot(Transform node, Transform root)
            {
                while (node != root)
                {
                    if (!node.gameObject.activeSelf) return false;
                    node = node.parent;
                }
                return true;
            }
        }

        public string Name { get; }
        public IReadOnlyList<ProjectedPiece> Pieces { get; }
        public IReadOnlyList<ExtraPiece> Extras => extras;
        public IReadOnlyList<Vector3> SnapPoints { get; }
        public BlueprintLayers Layers { get; }
        public int MissingPrefabs { get; }
        // Bounding-sphere radius around the blueprint's origin, for AssistanceRange.
        public float Radius { get; }
        // True while any piece in an active layer is still unbuilt. The delegate is held rather than
        // rebuilt, because this is read every frame.
        public bool HasWork => AssistanceWork.HasWork(Pieces.Count, pieceState, Layers);
        private readonly Func<int, (bool Completed, float Height)> pieceState;
        public Vector3 Position { get; set; }
        public float Yaw { get; set; }
        public bool Enabled { get; set; } = true;
        // Tint mismatches and draw extras. Off, the hologram shows only what is left to build.
        public bool ShowVerification { get; set; } = true;
        public Quaternion Rotation => Quaternion.Euler(0, Yaw, 0);
        private readonly PartLibrary library;
        private readonly List<ExtraPiece> extras = new List<ExtraPiece>();
        private readonly Material material;
        private readonly Material aimedMaterial;
        private readonly Material wrongPieceMaterial;
        private readonly Material wrongRotationMaterial;
        private readonly Material extraMaterial;
        private static readonly Matrix4x4 ExtraInflation = Matrix4x4.Scale(Vector3.one * 1.01f);

        private BlueprintProjection(BlueprintDocument document, List<ProjectedPiece> pieces, int missing, Material material, PartLibrary library)
        {
            Name = document.Name;
            Pieces = pieces;
            this.library = library;
            SnapPoints = document.SnapPoints.Count > 0
                ? document.SnapPoints.Select(point => point.Position).ToArray()
                : pieces.SelectMany(piece => piece.Prefab.GetComponentsInChildren<Transform>(true)
                    .Where(point => point.CompareTag("snappoint"))
                    .Select(point => piece.Entry.GetPosition() + piece.Entry.GetRotation() * Vector3.Scale(
                        piece.Entry.GetScale(), piece.Prefab.transform.InverseTransformPoint(point.position))))
                    .ToArray();
            Layers = new BlueprintLayers(pieces.Select(piece => piece.Entry.posY));
            MissingPrefabs = missing;
            foreach (var piece in pieces)
                Radius = Mathf.Max(Radius, AssistanceRange.PieceRadius(
                    piece.Entry.GetPosition(), piece.LocalBounds, piece.Entry.GetScale()));
            pieceState = index => (Pieces[index].Completed, Pieces[index].Entry.posY);
            this.material = material;
            aimedMaterial = new Material(material) { color = new Color(1f, 0.85f, 0.2f, 0.3f) };
            wrongPieceMaterial = new Material(material) { color = new Color(1f, 0.15f, 0.15f, 0.4f) };
            wrongRotationMaterial = new Material(material) { color = new Color(1f, 0.55f, 0.1f, 0.4f) };
            extraMaterial = new Material(material) { color = new Color(0.95f, 0.2f, 0.9f, 0.4f) };
        }

        public static bool TryCreate(BlueprintDocument document, out BlueprintProjection projection, out string error)
        {
            projection = null;
            error = string.Empty;
            var shader = PrefabManager.Cache.GetPrefab<Shader>("Lux Lit Particles/ Bumped");
            if (!shader)
            {
                error = "The blueprint shader is unavailable. Rejoin the world and try again.";
                return false;
            }
            var pieces = new List<ProjectedPiece>();
            int missing = 0;
            var library = new PartLibrary();
            foreach (var entry in document.Pieces)
            {
                var prefab = ZNetScene.instance.GetPrefab(entry.name);
                if (!prefab) { missing++; continue; }
                var parts = library.For(prefab);
                if (parts.Count == 0) { missing++; continue; }
                pieces.Add(new ProjectedPiece(entry, prefab, parts));
            }
            if (pieces.Count == 0)
            {
                error = "No pieces in this blueprint can be displayed. Check its required piece mods.";
                return false;
            }
            var material = new Material(shader) { color = new Color(0.15f, 0.8f, 1f, 0.3f), renderQueue = 3000 };
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.DisableKeyword("DIRECTIONAL");
            projection = new BlueprintProjection(document, pieces, missing, material, library);
            return true;
        }

        public Vector3 PiecePosition(ProjectedPiece piece) => Position + Rotation * piece.Entry.GetPosition();
        public Quaternion PieceRotation(ProjectedPiece piece) => Rotation * piece.Entry.GetRotation();
        public Matrix4x4 PieceMatrix(ProjectedPiece piece) => Matrix4x4.TRS(PiecePosition(piece), PieceRotation(piece), piece.Entry.GetScale());
        // Height above the blueprint origin, the quantity BlueprintLayers bands on.
        public float LocalHeight(Vector3 worldPosition) => (Quaternion.Inverse(Rotation) * (worldPosition - Position)).y;

        public int Count(PieceState state)
        {
            int count = 0;
            foreach (var piece in Pieces) if (piece.State == state) count++;
            return count;
        }

        // World-space box around every planned piece's mesh, for deciding what counts as "inside".
        public Bounds WorldBounds()
        {
            var bounds = new Bounds(Position, Vector3.zero);
            bool first = true;
            foreach (var piece in Pieces)
            {
                var matrix = PieceMatrix(piece);
                var local = piece.LocalBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var point = matrix.MultiplyPoint3x4(local.center + offset);
                    if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                    else bounds.Encapsulate(point);
                }
            }
            return bounds;
        }

        public void SetExtras(IEnumerable<Piece> pieces)
        {
            extras.Clear();
            foreach (var piece in pieces)
            {
                var view = piece.GetComponent<ZNetView>();
                var prefab = view && view.IsValid() ? ZNetScene.instance.GetPrefab(view.GetZDO().GetPrefab()) : null;
                var parts = prefab ? library.For(prefab) : null;
                if (parts != null && parts.Count > 0) extras.Add(new ExtraPiece(piece, parts));
            }
        }

        // Nearest hit of the camera ray on one piece's borrowed meshes, in world units along the ray.
        public bool Intersect(ProjectedPiece piece, Ray ray, out float distance, out Vector3 point)
        {
            distance = float.PositiveInfinity;
            point = default;
            var pieceMatrix = PieceMatrix(piece);
            foreach (var part in piece.Parts)
            {
                var matrix = pieceMatrix * part.LocalMatrix;
                var inverse = matrix.inverse;
                var localRay = new Ray(inverse.MultiplyPoint3x4(ray.origin), inverse.MultiplyVector(ray.direction));
                if (!part.Mesh.bounds.IntersectRay(localRay, out float local)) continue;
                if (part.HitTest != null && !part.HitTest.Intersect(localRay, out local)) continue;
                var hit = matrix.MultiplyPoint3x4(localRay.GetPoint(local));
                float world = Vector3.Dot(hit - ray.origin, ray.direction);
                if (world < 0 || world >= distance) continue;
                distance = world;
                point = hit;
            }
            return !float.IsPositiveInfinity(distance);
        }

        public bool SnapTo(Vector3 target)
        {
            if (!BlueprintSnapping.TryPosition(SnapPoints, Position, Rotation, target, out var position)) return false;
            Position = position;
            return true;
        }

        public void Draw(ProjectedPiece selected = null, ProjectedPiece aimed = null)
        {
            var camera = GameCamera.instance ? GameCamera.instance.GetComponent<Camera>() : null;
            if (!camera) return;
            foreach (var piece in Pieces)
            {
                if (piece.Completed || !Layers.Contains(piece.Entry.posY) || piece == selected) continue;
                var matrix = PieceMatrix(piece);
                var tint = piece == aimed ? aimedMaterial : MaterialFor(piece.State);
                foreach (var part in piece.Parts)
                {
                    for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                        Graphics.DrawMesh(part.Mesh, matrix * part.LocalMatrix, tint, 0, camera,
                            submesh, null, ShadowCastingMode.Off, false);
                }
            }
            if (!ShowVerification) return;
            foreach (var extra in extras)
            {
                if (!extra.Piece || !Layers.Contains(LocalHeight(extra.Piece.transform.position))) continue;
                // Inflate slightly so the tint wins the depth test against the real surface.
                var matrix = extra.Piece.transform.localToWorldMatrix * ExtraInflation;
                foreach (var part in extra.Parts)
                {
                    for (int submesh = 0; submesh < part.Mesh.subMeshCount; submesh++)
                        Graphics.DrawMesh(part.Mesh, matrix * part.LocalMatrix, extraMaterial, 0, camera,
                            submesh, null, ShadowCastingMode.Off, false);
                }
            }
        }

        private Material MaterialFor(PieceState state)
        {
            if (!ShowVerification) return material;
            return state switch
            {
                PieceState.WrongPiece => wrongPieceMaterial,
                PieceState.WrongRotation => wrongRotationMaterial,
                _ => material
            };
        }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(aimedMaterial);
            UnityEngine.Object.Destroy(wrongPieceMaterial);
            UnityEngine.Object.Destroy(wrongRotationMaterial);
            UnityEngine.Object.Destroy(extraMaterial);
        }
    }
}
