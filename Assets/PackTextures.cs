using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaos
{


    public static class PackTextures
    {
        private static ComputeShader _computeShader;
        private static int _kernelId;

        public static void LoadComputeShader()
        {
            if (null != _computeShader)
            {
                return;
            }
            _computeShader = Resources.Load<ComputeShader>("PackTextures");
            _kernelId = _computeShader.FindKernel("PackTextures");
        }

        public static void UnloadComputeShader()
        {
            Resources.UnloadAsset(_computeShader);
        }

        private static int PowerOfTwo(int n)
        {
            if ((n & (n - 1)) != 0)
            {
                n |= (n >> 1);
                n |= (n >> 2);
                n |= (n >> 4);
                n |= (n >> 8);
                n |= (n >> 16);
                n++;
                if (n < 0)
                {
                    n >>= 1;
                }
            }
            return n;
        }

        public static Texture PackTexturesComputeInRects(IList<Texture2D> textures, Rect[] rects, int width, int height)
        {
            RenderTexture rt = new RenderTexture(width, height, 0)
            {
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false
            };
            rt.Create();


            ComputeShader cs = _computeShader;
            cs.SetTexture(_kernelId, "Target", rt);
            int len = textures.Count;
            for (int i = 0; i < len; i++)
            {
                Texture2D tex = textures[i];
#if DEBUG
                if (null == tex)
                {
                    continue;
                }
#endif
                Rect rect = rects[i];
                float scale = rect.width * width / tex.width;
                cs.SetFloat("Scale", 1 / scale);
                cs.SetTexture(_kernelId, "Pixels", tex);
                cs.SetInts("Offset", (int)(rect.x * width), (int)(rect.y * height));
                cs.Dispatch(_kernelId, (int)(rect.width * width) / 8, (int)(rect.height * height) / 8, 1);
            }

            rt.GenerateMips();

            return rt;
        }

        public static Rect[] PackTexturesCompute(out Texture result, IList<Texture2D> textures,
            int maximumAtlasSize = 2048)
        {
            int width;
            int height;
            Packer.Block[] blocks = ComputePack(textures, out width, out height);

            if (width < height)
            {
                throw new Exception("error");
            }

            float scale = 1;
            if (width > maximumAtlasSize)
            {
                scale = (float) maximumAtlasSize/width;
                width = maximumAtlasSize;
                height = (int) (height*scale);
            }
            width = PowerOfTwo(width);
            height = PowerOfTwo(height);

            RenderTexture rt = new RenderTexture(width, height, 0)
            {
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false
            };
            rt.Create();

            ComputeShader cs = _computeShader;
            cs.SetTexture(_kernelId, "Target", rt);
            cs.SetFloat("Scale", 1/scale);
            int len = textures.Count;
            Rect[] ret = new Rect[len];
            for (int i = 0; i < len; i++)
            {
                Packer.Block block = blocks[i];
                Texture2D tex = textures[block.i];
                cs.SetTexture(_kernelId, "Pixels", tex);
                block *= scale;
                cs.SetInts("Offset", block.x, block.y);
                cs.Dispatch(_kernelId, block.w/8, block.h/8, 1);
                ret[block.i] = new Rect(block.x/(float) width, block.y/(float) height, block.w/(float) width,
                    block.h/(float) height);
            }

            rt.GenerateMips();
            result = rt;

            //result = new Texture2D(width, height);
            //RenderTexture tmp = RenderTexture.active;
            //RenderTexture.active = rt;
            ////TODO: ！！！
            //result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            //UnityEngine.Profiling.Profiler.BeginSample("ReadPixels");
            //result.Apply(false, true);
            //UnityEngine.Profiling.Profiler.EndSample();
            //RenderTexture.active = tmp;


            return ret;
        }

        private struct BlockComparer : IComparer<Packer.Block>
        {
            public int Compare(Packer.Block a, Packer.Block b)
            {
                int ret = b.h - a.h;
                return ret == 0 ? b.w - a.w : ret;
            }
        }

        private static Packer.Block[] ComputePack(IList<Texture2D> textures, out int width, out int height)
        {
            int num = textures.Count;
            Packer.Block[] blocks = new Packer.Block[num];
            for (int i = 0; i < num; i++)
            {
                Texture2D tex = textures[i];
                blocks[i] = new Packer.Block() {w = tex.width, h = tex.height, x = 0, y = 0, i = i,};
            }
            Array.Sort(blocks, new BlockComparer());

            Packer packer = new Packer();
            packer.Fit(blocks, out width, out height);

            return blocks;
        }
    }
}