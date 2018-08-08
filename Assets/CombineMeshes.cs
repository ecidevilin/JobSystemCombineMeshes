using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Chaos
{

    public static class SkinnedMeshCombination
    {
        private static readonly string[] DefaultTextureNames = new string[]
        {
            "_MainTex",
            "_BumpMap",
            "_SpecGlossMap",
        };
        private static List<Transform> _bones;
        private static List<Texture2D> _textures;
        private static List<Matrix4x4> _bindPoses; 

        private static Texture PackAllTextures(List<Texture2D> textures, ref Rect[] rects, ref int width, ref int height)
        {
            Texture ret;
            if (SystemInfo.supportsComputeShaders)
            {
                PackTextures.LoadComputeShader();
                if (rects == null)
                {
                    rects = PackTextures.PackTexturesCompute(out ret, textures);
                    width = ret.width;
                    height = ret.height;
                }
                else
                {
                    ret = PackTextures.PackTexturesComputeInRects(textures, rects, width, height);
                }
            }
            else
            {
                Texture2D tex = new Texture2D(0, 0);
                rects = tex.PackTextures(textures.ToArray(), 0);
                ret = tex;
            }
            return ret;
        }

        public static Transform Combine(Transform root, SkinnedMeshRenderer[] allRenderers, string[] textureNames = null)
        {
            int len = 0;
            if (null == allRenderers || 0 == (len = allRenderers.Length))
            {
                return root;
            }
            textureNames = textureNames ?? DefaultTextureNames;
            // Meshes
            /*美术制作上避免使用子网格*/
            Matrix4x4 matrix = root.worldToLocalMatrix;

            // Bones
            _bones = _bones ?? new List<Transform>(allRenderers[0].bones.Length);
            _bones.Clear();

            int vcount = 0;
            int meshCount = 0;
            bool needSort = false;
            for (int i = 0, imax = len; i < imax; i++)
            {
                SkinnedMeshRenderer smr = allRenderers[i];
                if (!smr.enabled || smr.material.mainTexture == null)
                {
                    continue;
                }
                vcount += smr.sharedMesh.vertexCount;
                meshCount += smr.sharedMesh.subMeshCount;
                if (smr.bones.Length == 0)
                {
                    needSort = true;
                }
            }
            if (needSort)
            {
                int start = 0;
                int end = allRenderers.Length - 1;
                while (start < end)
                {
                    if (allRenderers[end].bones.Length == 0)
                    {
                        end--;
                    }
                    if (allRenderers[start].bones.Length > 0)
                    {
                        start++;
                    }
                    SkinnedMeshRenderer tmp = allRenderers[start];
                    allRenderers[start] = allRenderers[end];
                    allRenderers[end] = tmp;
                    start++;
                    end--;
                }
            }
            NativeArray<BoneWeight> boneWeights = new NativeArray<BoneWeight>(vcount, Allocator.Temp);
            int vi = 0;
            NativeArray<CombineInstance> instances = new NativeArray<CombineInstance>(meshCount, Allocator.Temp);
            _bindPoses = _bindPoses ?? new List<Matrix4x4>(_bones.Capacity);
            _bindPoses.Clear();

            // Textures
            _textures = _textures ?? new List<Texture2D>(len);
            _textures.Clear();
            int uvCount = 0;
            int smCount = 0;
            int bpCount = 0;
            for (int i = 0, imax = len; i < imax; i++)
            {
                SkinnedMeshRenderer smr = allRenderers[i];
                if (!smr.enabled || smr.material.mainTexture == null)
                {
                    continue;
                }

                Transform[] bones = smr.bones;
                Matrix4x4[] bps = smr.sharedMesh.bindposes;
                int blen = bones.Length;
                NativeArray<int> boneIndexMapping = new NativeArray<int>(blen, Allocator.Temp);
                for (int b = 0; b < blen; b++)
                {
                    Transform bone = bones[b];
                    int idx;
                    if (-1 == (idx = _bones.IndexOf(bone)))
                    {
                        idx = _bones.Count;
                        _bones.Add(bone);
                        _bindPoses.Add(bps[b]);
                    }
                    boneIndexMapping[b] = idx;
                }
                int vc = smr.sharedMesh.vertexCount;
                BoneWeight[] meshBoneweights = smr.sharedMesh.boneWeights;
                Transform pBone = null;
                if (null == meshBoneweights || 0 == meshBoneweights.Length)
                {
                    pBone = smr.transform.parent;
                    int pIdx = 0;
                    float pWeight = 0;
                    while (pBone != null)
                    {
                        if (-1 != (pIdx = _bones.IndexOf(pBone)))
                        {
                            pWeight = 1;
                            break;
                        }
                        pBone = pBone.parent;
                    }
                    if (pIdx < 0)
                    {
                        pIdx = 0;
                    }
                    for (int v = 0; v < vc; v++)
                    {
                        boneWeights[vi++] = new BoneWeight() {boneIndex0 = pIdx,weight0 = pWeight, };
                    }
                }
                else
                {
                    BWCopyJob job = new BWCopyJob()
                    {
                        offset = vi,
                        mapping = boneIndexMapping,
                        target = boneWeights,
                        source = new NativeArray<BoneWeight>(meshBoneweights, Allocator.TempJob),
                    };
                    JobHandle handle = job.Schedule(meshBoneweights.Length, 1);
                    handle.Complete();
                    job.source.Dispose();
                    vi += vc;
                    //foreach (BoneWeight bw in meshBoneweights)
                    //{
                    //    BoneWeight bWeight = bw;
                    //    bWeight.boneIndex0 = boneIndexMapping[bw.boneIndex0];
                    //    bWeight.boneIndex1 = boneIndexMapping[bw.boneIndex1];
                    //    bWeight.boneIndex2 = boneIndexMapping[bw.boneIndex2];
                    //    bWeight.boneIndex3 = boneIndexMapping[bw.boneIndex3];
                    //    boneWeights[vi++] = bWeight;
                    //}
                }
                boneIndexMapping.Dispose();
                // 美术制作上避免使用子网格            
                for (int sub = 0; sub < smr.sharedMesh.subMeshCount; sub++)
                {
                    CombineInstance ci = new CombineInstance();
                    ci.mesh = smr.sharedMesh;
                    ci.subMeshIndex = sub;
                    if (null != pBone)
                    {
                        ci.transform = Matrix4x4.identity;
                    }
                    else
                    {
                        ci.transform = matrix * smr.transform.localToWorldMatrix;
                    }
                    instances[smCount++] = ci;
                }

                // Textures
                //if (smr.material.mainTexture != null)
                {
                }
                uvCount += vc;
            }

            // Renderer
            SkinnedMeshRenderer r = root.gameObject.GetComponent<SkinnedMeshRenderer>();
            if (null == r)
            {
                r = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            }

            // Mesh
            Mesh sharedMesh = new Mesh();
            /*美术制作上避免使用子网格*/
            sharedMesh.CombineMeshes(instances.ToArray(), true, true);
            instances.Dispose();
            r.sharedMesh = sharedMesh;
            
            sharedMesh.bindposes = _bindPoses.ToArray();

            sharedMesh.boneWeights = boneWeights.ToArray();
            boneWeights.Dispose();

            // Bones
            r.bones = _bones.ToArray();
            sharedMesh.RecalculateBounds();

            // Material
            Material sharedMaterial = Object.Instantiate(allRenderers[0].sharedMaterial) as Material;
            r.sharedMaterial = sharedMaterial;
            // Textures
            int width = 0;
            int height = 0;
            Rect[] packingResult = null;
            foreach (string str in textureNames)
            {
                _textures.Clear();
                for (int i = 0; i < len; i++)
                {
#if DEBUG
                    if (!allRenderers[i].sharedMaterial.HasProperty(str))
                    {
                        _textures.Add(null);
                        continue;
                    }
#endif
                    _textures.Add(allRenderers[i].sharedMaterial.GetTexture(str) as Texture2D);
                }
                Texture skinnedMeshAtlas = PackAllTextures(_textures, ref packingResult, ref width, ref height);
                sharedMaterial.SetTexture(str, skinnedMeshAtlas);
            }

            NativeArray<Vector2> uvMapping = new NativeArray<Vector2>(uvCount, Allocator.Temp);
            int j = 0;
            for (int i = 0; i < len; i++)
            {
                SkinnedMeshRenderer smr = allRenderers[i];
                Vector2[] list = smr.sharedMesh.uv;
                UVLerpJob job = new UVLerpJob()
                {
                    offset = j,
                    uvList = new NativeArray<Vector2>(list, Allocator.Temp),
                    result = uvMapping,
                    rect = packingResult[i],
                };
                JobHandle handle = job.Schedule(list.Length, 1);
                handle.Complete();
                j += list.Length;
                job.uvList.Dispose();
                // Destroy
                smr.enabled = false;
            }

            sharedMesh.uv = uvMapping.ToArray();
            uvMapping.Dispose();

            return root;
        }
        private struct UVLerpJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<Vector2> uvList;
            public Rect rect;
            [NativeDisableParallelForRestriction]
            public NativeArray<Vector2> result;
            public int offset;

            public void Execute(int index)
            {
                Vector2 uv = uvList[index];
                result[index + offset] = new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, uv.x),
                    Mathf.Lerp(rect.yMin, rect.yMax, uv.y));
            }
        }

        private struct BWCopyJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<int> mapping;
            [ReadOnly]
            public NativeArray<BoneWeight> source;
            [NativeDisableParallelForRestriction]
            public NativeArray<BoneWeight> target;
            public int offset;

            public void Execute(int index)
            {
                BoneWeight bWeight = source[index];
                bWeight.boneIndex0 = mapping[bWeight.boneIndex0];
                bWeight.boneIndex1 = mapping[bWeight.boneIndex1];
                bWeight.boneIndex2 = mapping[bWeight.boneIndex2];
                bWeight.boneIndex3 = mapping[bWeight.boneIndex3];
                target[offset + index] = bWeight;
            }
        }
    }
}