using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TemporalAntiAliasingCameraData : MonoBehaviour
{
    private int taaFrameIndex;

    private Camera Camera { get
        {
            if (_camera == null)
                _camera = GetComponent<Camera>();
            return _camera;
        } }
    private Camera _camera;

    private Matrix4x4 unjitteredProjectionMatrix;

    private RenderTexture[] historyBuffer;
    public RenderTexture prevHistory => historyBuffer[indexRead];
    public RenderTexture nextHistory => historyBuffer[indexWrite];
    private RenderTexture[] velocityBuffer;
    public RenderTexture prevMVLen => velocityBuffer[indexRead];
    public RenderTexture nextMVLen => velocityBuffer[indexWrite];

    private int indexRead;
    private int indexWrite;

    public Vector4 taaJitter;

    private Plane[] frustumPlanes = new Plane[6];






    private void UpdateState()
    {
        unjitteredProjectionMatrix = Camera.projectionMatrix;

        const int kMaxSampleCount = 8;
        if (++taaFrameIndex >= kMaxSampleCount)
            taaFrameIndex = 0;
        GeometryUtility.CalculateFrustumPlanes(Camera, frustumPlanes);

        indexRead = indexWrite;
        indexWrite = (++indexWrite) % 2;
    }

    /// <summary>
    /// This updates the state of the TAA internally and is called only once from the camera settings pass
    /// This is terrible practice, todo: not be shit
    /// </summary>
    public Matrix4x4 GetJitteredProjectionMatrix(float pixelWidth, float pixelHeight)
    {
        UpdateState();

#if UNITY_2021_2_OR_NEWER
        if (UnityEngine.FrameDebugger.enabled)
        {
            taaJitter = Vector4.zero;
            return unjitteredProjectionMatrix;
        }
#endif

        // The variance between 0 and the actual halton sequence values reveals noticeable
        // instability in Unity's shadow maps, so we avoid index 0.
        float jitterX = HaltonSequence.Get((taaFrameIndex & 1023) + 1, 2) - 0.5f;
        float jitterY = HaltonSequence.Get((taaFrameIndex & 1023) + 1, 3) - 0.5f;
        taaJitter = new Vector4(jitterX, jitterY, jitterX / pixelWidth, jitterY / pixelHeight);

        Matrix4x4 proj;

        if (Camera.orthographic)
        {
            float vertical = Camera.orthographicSize;
            float horizontal = vertical * Camera.aspect;

            var offset = taaJitter;
            offset.x *= horizontal / (0.5f * pixelWidth);
            offset.y *= vertical / (0.5f * pixelHeight);

            float left = offset.x - horizontal;
            float right = offset.x + horizontal;
            float top = offset.y + vertical;
            float bottom = offset.y - vertical;

            proj = Matrix4x4.Ortho(left, right, bottom, top, Camera.nearClipPlane, Camera.farClipPlane);
        }
        else
        {
            var planes = unjitteredProjectionMatrix.decomposeProjection;

            float vertFov = Mathf.Abs(planes.top) + Mathf.Abs(planes.bottom);
            float horizFov = Mathf.Abs(planes.left) + Mathf.Abs(planes.right);

            var planeJitter = new Vector2(jitterX * horizFov / pixelWidth,
                jitterY * vertFov / pixelHeight);

            planes.left += planeJitter.x;
            planes.right += planeJitter.x;
            planes.top += planeJitter.y;
            planes.bottom += planeJitter.y;

            // Reconstruct the far plane for the jittered matrix.
            // For extremely high far clip planes, the decomposed projection zFar evaluates to infinity.
            if (float.IsInfinity(planes.zFar))
                planes.zFar = frustumPlanes[5].distance;

            proj = Matrix4x4.Frustum(planes);
        }

        return proj;
    }


    /// <summary>
    /// An utility class to compute samples on the Halton sequence.
    /// https://en.wikipedia.org/wiki/Halton_sequence
    /// </summary>
    public static class HaltonSequence
    {
        /// <summary>
        /// Gets a deterministic sample in the Halton sequence.
        /// </summary>
        /// <param name="index">The index in the sequence.</param>
        /// <param name="radix">The radix of the sequence.</param>
        /// <returns>A sample from the Halton sequence.</returns>
        public static float Get(int index, int radix)
        {
            float result = 0f;
            float fraction = 1f / radix;

            while (index > 0)
            {
                result += (index % radix) * fraction;

                index /= radix;
                fraction /= radix;
            }

            return result;
        }
    }

    /// <summary>
    /// Returns true if new textures created
    /// </summary>
    public bool EnsureBuffers(ref RenderTextureDescriptor descriptor)
    {
        bool sizeChanged = false;
        sizeChanged |= EnsureBuffer(ref historyBuffer, ref descriptor);
        sizeChanged |= EnsureBuffer(ref velocityBuffer, ref descriptor);
        return sizeChanged;
    }


    private bool EnsureBuffer(ref RenderTexture[] buffer, ref RenderTextureDescriptor descriptor)
    {
        bool sizeChanged = false;
        sizeChanged |= EnsureArray(ref buffer, 2);
        sizeChanged |= EnsureRenderTarget(ref buffer[0], descriptor.width, descriptor.height, descriptor.colorFormat, FilterMode.Bilinear);
        sizeChanged |= EnsureRenderTarget(ref buffer[1], descriptor.width, descriptor.height, descriptor.colorFormat, FilterMode.Bilinear);
        return sizeChanged;
    }

    /// <summary>
    /// Returns true if new array created
    /// </summary>
    private bool EnsureArray<T>(ref T[] array, int size, T initialValue = default(T))
    {
        if (array == null || array.Length != size)
        {
            array = new T[size];
            for (int i = 0; i != size; i++)
                array[i] = initialValue;
            return true;
        }
        else
            return false;
    }

    /// <summary>
    /// Returns true if new target created
    /// </summary>
    private bool EnsureRenderTarget(ref RenderTexture rt, int width, int height, RenderTextureFormat format, FilterMode filterMode, int depthBits = 0, int antiAliasing = 1)
    {
        if (rt != null && (rt.width != width || rt.height != height || rt.format != format || rt.filterMode != filterMode || rt.antiAliasing != antiAliasing))
        {
            RenderTexture.ReleaseTemporary(rt);
            rt = null;
        }
        if (rt == null)
        {
            rt = RenderTexture.GetTemporary(width, height, depthBits, format, RenderTextureReadWrite.Default, antiAliasing);
            rt.filterMode = filterMode;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.enableRandomWrite = true;
            return true;// new target
        }
        return false;// same target
    }


    private void Clear(ref RenderTexture[] renderTextures)
    {
        if (renderTextures == null)
            return;

        for (int i = 0; i < renderTextures.Length; i++)
        {
            var renderTexture = renderTextures[i];

            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
                renderTexture = null;
            }
        }
        renderTextures = null;
    }
}
