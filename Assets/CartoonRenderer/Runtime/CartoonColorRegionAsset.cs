using System;
using System.Collections.Generic;
using UnityEngine;

namespace CartoonProjection
{
    [Serializable]
    public sealed class CartoonColorRegion
    {
        public int regionId;
        public Color color = Color.white;
        public string materialGuid;
        public string materialName;
        public CartoonRegionPolicy policy;
        public int triangleCount;
        public float surfaceArea;
        public bool protectedRegion;
    }

    [Serializable]
    public sealed class CartoonRegionMeshBinding
    {
        public string rendererPath;
        public string rendererName;
        public bool isSkinned;
        public string sourceMeshGuid;
        public Mesh generatedMesh;
        public int subMeshCount;
        public int regionCount;
        public int sourceVertexCount;
        public int generatedVertexCount;
        public int sourceTriangleCount;
    }

    [CreateAssetMenu(menuName = "Cartoon Projection/Color Region Asset", fileName = "ColorRegions")]
    public sealed class CartoonColorRegionAsset : ScriptableObject
    {
        public string sourceRootName;
        public string sourcePrefabGuid;
        public List<string> sourceMeshGuids = new();
        public List<string> sourceMaterialGuids = new();
        public string dependencyHash;
        public List<CartoonRegionMeshBinding> meshes = new();
        public List<CartoonColorRegion> regions = new();
        public List<CartoonMaterialRegionRule> rules = new();
        public int bakeVersion = 1;
        public float globalColorMergeMultiplier = 1f;
        public string bakedAt;
        public bool hasSkinnedMeshes;
        public bool hasBlendShapes;
        public bool hasAlphaMaterials;
        public List<string> warnings = new();

        public bool IsStale(CartoonColorRegionAsset other) => other == null || dependencyHash != other.dependencyHash;
    }
}
