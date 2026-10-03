using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonProjection
{
    // Ping-pong pair of R16 buffers holding last frame's per-pixel NdotL for paint-layer
    // threshold hysteresis. Owned by the renderer feature (persists across frames),
    // imported into the Render Graph each frame as RTHandles, resized with the internal
    // resolution.
    public sealed class CartoonPaintHistory : IDisposable
    {
        private readonly RTHandle[] buffers = new RTHandle[2];
        private int frameIndex;

        public int width;
        public int height;

        private bool Valid => buffers[0] != null && buffers[0].rt != null &&
                              buffers[0].rt.width == width && buffers[0].rt.height == height;

        public RTHandle Previous
        {
            get
            {
                Ensure();
                return buffers[frameIndex & 1];
            }
        }

        public RTHandle Current
        {
            get
            {
                Ensure();
                return buffers[(frameIndex + 1) & 1];
            }
        }

        public void Swap() => frameIndex++;

        private void Ensure()
        {
            if (Valid)
                return;
            Release();
            for (int i = 0; i < buffers.Length; i++)
            {
                var texture = new RenderTexture(new RenderTextureDescriptor(width, height,
                    RenderTextureFormat.RHalf, 0)
                {
                    useMipMap = false,
                    sRGB = false
                })
                {
                    name = $"Cartoon Paint History {i}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.Create();
                // 0.5 NdotL on the first frames keeps the enter thresholds active.
                var previousActive = RenderTexture.active;
                RenderTexture.active = texture;
                GL.Clear(false, true, new Color(0.5f, 0.5f, 0.5f, 0.5f));
                RenderTexture.active = previousActive;
                buffers[i] = RTHandles.Alloc(texture);
            }
        }

        private void Release()
        {
            for (int i = 0; i < buffers.Length; i++)
            {
                if (buffers[i] == null)
                    continue;
                buffers[i].Release();
                buffers[i] = null;
            }
        }

        public void Dispose() => Release();
    }
}
