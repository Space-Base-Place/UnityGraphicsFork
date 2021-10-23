using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class TemporalAntiAliasing : ScriptableRendererFeature
{
    #region SUB CLASSES

    [System.Serializable]
    public class Settings
    {
        public enum Quality
        {
            Low,
            Medium,
            High
        }
        public Quality quality;

        [Range(0, 2)] public float taaSharpenStrength;
        [Range(0, 1)] public float taaHistorySharpening;
        [Range(0, 1)] public float taaMotionVectorRejection;
        [Range(0, 1)] public float taaAntiFlicker;
        [Range(0, 1)] public float taaBaseBlendFactor;
        [Range(0, 1)] public float taaJitterAmount;
        public bool taaAntiRinging;
    }

    class TAACameraSettingsPass : ScriptableRenderPass
    {
        Settings settings;

        public void Setup(Settings settings)
        {
            this.settings = settings;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = cameraData.camera;
            var foundData = cameraDataDict.TryGetValue(camera, out TemporalAntiAliasingPassData temporalData);

            if (!foundData) // Don't render if no data
                return;

            var cmd = CommandBufferPool.Get("TAACameraSettings");

            temporalData.UpdateState(settings.taaJitterAmount, ref renderingData);

            var viewMatrix = cameraData.GetViewMatrix();
            var projMatrix = temporalData.GetJitteredProjectionMatrix();

            cmd.SetViewProjectionMatrices(viewMatrix, projMatrix);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }
    }

    class TemporalAntiAliasingPass : ScriptableRenderPass
    {
        private Material temporalAAMaterial;
        private Settings settings;

        public static ObjectPool<MaterialPropertyBlock> MaterialPropertyBlockPool = new ObjectPool<MaterialPropertyBlock>((x) => x.Clear(), null);

        internal const float TAABaseBlendFactorMin = 0.6f;
        internal const float TAABaseBlendFactorMax = 0.95f;
        float[] taaSampleWeights = new float[9];

        private RenderTargetIdentifier renderSource;
        private RenderTargetIdentifier renderTarget;
        int temporaryRTId = Shader.PropertyToID("_TempRT");


        public void Setup(Settings settings)
        {
            this.settings = settings;

            var shader = Shader.Find("Hidden/TemporalAA");
            if (shader == null)
                return;

            temporalAAMaterial = new Material(shader);
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;

            cmd.GetTemporaryRT(temporaryRTId, renderingData.cameraData.cameraTargetDescriptor);
            renderSource = new RenderTargetIdentifier(temporaryRTId);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (temporalAAMaterial == null || temporalAAMaterial.shader == null)
                return; // dont render if not initialised

            var camera = renderingData.cameraData.camera;
            var foundData = cameraDataDict.TryGetValue(camera, out TemporalAntiAliasingPassData data);

            if (!foundData) // Don't render if no data
                return;

            PrepareTAAPassData(settings, data, ref renderingData);

            const int taaPass = 0;
            const int excludeTaaPass = 1;
            const int taauPass = 2;
            const int copyHistoryPass = 3;

            var cmd = CommandBufferPool.Get("Temporal Anti-Aliasing");

            cmd.Blit(renderTarget, renderSource);
            cmd.SetGlobalTexture(ShaderIDs._InputTexture, renderSource);

            if (data.resetPostProcessingHistory)
            {
                var historyMpb = MaterialPropertyBlockPool.Get();
                historyMpb.SetVector(ShaderIDs._TaaScales, data.taaScales);

                DrawFullScreen(cmd, data.temporalAAMaterial, data.prevHistory, historyMpb, copyHistoryPass);
                DrawFullScreen(cmd, data.temporalAAMaterial, data.nextHistory, historyMpb, copyHistoryPass);

                MaterialPropertyBlockPool.Release(historyMpb);
            }

            var mpb = MaterialPropertyBlockPool.Get();
            mpb.SetInt(ShaderIDs._StencilMask, (int)StencilUsage.ExcludeFromTAA);
            mpb.SetInt(ShaderIDs._StencilRef, (int)StencilUsage.ExcludeFromTAA);
            mpb.SetTexture(ShaderIDs._InputHistoryTexture, data.prevHistory);
            if (data.prevMVLen != null && data.motionVectorRejection)
            {
                mpb.SetTexture(ShaderIDs._InputVelocityMagnitudeHistory, data.prevMVLen);
            }

            var taaHistorySize = data.previousScreenSize;

            var taaFrameInfo = new Vector4(settings.taaSharpenStrength, 0, 0, 1);
            mpb.SetVector(ShaderIDs._TaaFrameInfo, taaFrameInfo);
            mpb.SetVector(ShaderIDs._TaaJitterStrength, data.taaJitterStrength);

            mpb.SetVector(ShaderIDs._TaaPostParameters, data.taaParameters);
            mpb.SetVector(ShaderIDs._TaaHistorySize, taaHistorySize);
            mpb.SetVector(ShaderIDs._TaaFilterWeights, data.taaFilterWeights);


            CoreUtils.SetRenderTarget(cmd, renderTarget);

            cmd.SetRandomWriteTarget(1, data.nextHistory);
            if (data.nextMVLen != null && data.motionVectorRejection)
            {
                cmd.SetRandomWriteTarget(2, data.nextMVLen);
            }

            cmd.DrawProcedural(Matrix4x4.identity, data.temporalAAMaterial, taaPass, MeshTopology.Triangles, 3, 1, mpb);
            //cmd.DrawProcedural(Matrix4x4.identity, data.temporalAAMaterial, excludeTaaPass, MeshTopology.Triangles, 3, 1, mpb);

            cmd.ClearRandomWriteTargets();
            MaterialPropertyBlockPool.Release(mpb);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        private void PrepareTAAPassData(Settings settings, TemporalAntiAliasingPassData passData, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            var camera = renderingData.cameraData.camera;


            var descriptor = new RenderTextureDescriptor(camera.scaledPixelWidth, camera.scaledPixelHeight, RenderTextureFormat.DefaultHDR, 16);

            passData.resetPostProcessingHistory = passData.EnsureBuffers(ref descriptor);

            float minAntiflicker = 0.0f;
            float maxAntiflicker = 3.5f;
            float motionRejectionMultiplier = Mathf.Lerp(0.0f, 250.0f, settings.taaMotionVectorRejection * settings.taaMotionVectorRejection * settings.taaMotionVectorRejection);

            // The anti flicker becomes much more aggressive on higher values
            float temporalContrastForMaxAntiFlicker = 0.7f - Mathf.Lerp(0.0f, 0.3f, Mathf.SmoothStep(0.5f, 1.0f, settings.taaAntiFlicker));

            float antiFlickerLerpFactor = settings.taaAntiFlicker;
            float historySharpening = settings.taaHistorySharpening;

            if (cameraData.isSceneViewCamera)
            {
                // Force settings for scene view.
                historySharpening = 0.25f;
                antiFlickerLerpFactor = 0.7f;
            }
            float antiFlicker = Mathf.Lerp(minAntiflicker, maxAntiflicker, antiFlickerLerpFactor);

            passData.taaParameters = new Vector4(historySharpening, antiFlicker, motionRejectionMultiplier, temporalContrastForMaxAntiFlicker);

            /*            this more accurate version of the filter could be upgraded seperately from the rest of the logic
             *            // Precompute weights used for the Blackman-Harris filter.
                        float totalWeight = 0;
                        for (int i = 0; i < 9; ++i)
                        {
                            float x = TAASampleOffsets[i].x - passData.taaJitterStrength.x;
                            float y = TAASampleOffsets[i].y - passData.taaJitterStrength.y;
                            float d = (x * x + y * y);

                            taaSampleWeights[i] = Mathf.Exp((-0.5f / (0.22f)) * d);
                            totalWeight += taaSampleWeights[i];
                        }

                        for (int i = 0; i < 9; ++i)
                        {
                            taaSampleWeights[i] /= totalWeight;
                        }
                        // dont use post dof override
                        //const float postDofMin = 0.4f;
                        //const float scale = (TAABaseBlendFactorMax - postDofMin) / (TAABaseBlendFactorMax - TAABaseBlendFactorMin);
                        //const float offset = postDofMin - TAABaseBlendFactorMin * scale;
                        float taaBaseBlendFactor = settings.taaBaseBlendFactor;

                        passData.taaFilterWeights = new Vector4(taaSampleWeights[1], taaSampleWeights[2], taaSampleWeights[3], taaSampleWeights[4]);
                        passData.taaFilterWeights1 = new Vector4(taaSampleWeights[5], taaSampleWeights[6], taaSampleWeights[7], taaSampleWeights[8]);
             */

            // Precompute weights used for the Blackman-Harris filter. TODO: Note that these are slightly wrong as they don't take into account the jitter size. This needs to be fixed at some point.
            float crossWeights = Mathf.Exp(-2.29f * 2);
            float plusWeights = Mathf.Exp(-2.29f);
            float centerWeight = 1;

            float totalWeight = centerWeight + (4 * plusWeights);
            if (settings.quality == Settings.Quality.High)
            {
                totalWeight += crossWeights * 4;
            }

            // Weights will be x: central, y: plus neighbours, z: cross neighbours, w: total
            passData.taaFilterWeights = new Vector4(centerWeight / totalWeight, plusWeights / totalWeight, crossWeights / totalWeight, totalWeight);



            passData.temporalAAMaterial = temporalAAMaterial;
            passData.temporalAAMaterial.shaderKeywords = null;

            //if (PostProcessEnableAlpha()) dont think i need this
            //{
            //    passData.temporalAAMaterial.EnableKeyword("ENABLE_ALPHA");
            //}

            if (settings.taaHistorySharpening == 0)
            {
                passData.temporalAAMaterial.EnableKeyword("FORCE_BILINEAR_HISTORY");
            }

            if (settings.taaHistorySharpening != 0 && settings.taaAntiRinging && settings.quality == Settings.Quality.High)
            {
                passData.temporalAAMaterial.EnableKeyword("ANTI_RINGING");
            }

            passData.motionVectorRejection = settings.taaMotionVectorRejection > 0;
            if (passData.motionVectorRejection)
            {
                passData.temporalAAMaterial.EnableKeyword("ENABLE_MV_REJECTION");
            }


            switch (settings.quality)
            {
                case Settings.Quality.Low:
                    passData.temporalAAMaterial.EnableKeyword("LOW_QUALITY");
                    break;
                case Settings.Quality.Medium:
                    passData.temporalAAMaterial.EnableKeyword("MEDIUM_QUALITY");
                    break;
                case Settings.Quality.High:
                    passData.temporalAAMaterial.EnableKeyword("HIGH_QUALITY");
                    break;
                default:
                    passData.temporalAAMaterial.EnableKeyword("MEDIUM_QUALITY");
                    break;
            }

            // we always clear history when size changes so this is redundant
            int width = renderingData.cameraData.pixelWidth;
            int height = renderingData.cameraData.pixelHeight;
            passData.previousScreenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);

            passData.taaScales = Vector4.one;
        }




        static readonly Vector2Int[] TAASampleOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 1),
            new Vector2Int(1, -1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, -1)
        };

        static class ShaderIDs
        {
            public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
            public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
            public static readonly int _InputHistoryTexture = Shader.PropertyToID("_InputHistoryTexture");
            public static readonly int _OutputHistoryTexture = Shader.PropertyToID("_OutputHistoryTexture");
            public static readonly int _InputVelocityMagnitudeHistory = Shader.PropertyToID("_InputVelocityMagnitudeHistory");
            public static readonly int _OutputVelocityMagnitudeHistory = Shader.PropertyToID("_OutputVelocityMagnitudeHistory");

            public static readonly int _TaaFrameInfo = Shader.PropertyToID("_TaaFrameInfo");
            public static readonly int _TaaJitterStrength = Shader.PropertyToID("_TaaJitterStrength");
            public static readonly int _TaaPostParameters = Shader.PropertyToID("_TaaPostParameters");
            public static readonly int _TaaPostParameters1 = Shader.PropertyToID("_TaaPostParameters1");
            public static readonly int _TaaHistorySize = Shader.PropertyToID("_TaaHistorySize");
            public static readonly int _TaaFilterWeights = Shader.PropertyToID("_TaaFilterWeights");
            public static readonly int _TaaFilterWeights1 = Shader.PropertyToID("_TaaFilterWeights1");
            public static readonly int _TaauParameters = Shader.PropertyToID("_TaauParameters");
            public static readonly int _TaaScales = Shader.PropertyToID("_TaaScales");

            public static readonly int _StencilMask = Shader.PropertyToID("_StencilMask");
            public static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
        }

        /// <summary>
        /// Draws a full screen triangle.
        /// </summary>
        /// <param name="commandBuffer">CommandBuffer used for rendering commands.</param>
        /// <param name="material">Material used on the full screen triangle.</param>
        /// <param name="properties">Optional material property block for the provided material.</param>
        /// <param name="shaderPassId">Index of the material pass.</param>
        public static void DrawFullScreen(CommandBuffer commandBuffer, Material material, RenderTargetIdentifier colorBuffer, MaterialPropertyBlock properties = null, int shaderPassId = 0)
        {
            commandBuffer.SetRenderTarget(colorBuffer);
            commandBuffer.DrawProcedural(Matrix4x4.identity, material, shaderPassId, MeshTopology.Triangles, 3, 1, properties);
        }

    }


    internal class TemporalAntiAliasingPassData
    {
        // TAA parameters from HDRP
        public Material temporalAAMaterial;
        public bool resetPostProcessingHistory;
        public Vector4 previousScreenSize;
        public Vector4 taaParameters;
        public Vector4 taaParameters1;
        public Vector4 taaFilterWeights;
        public Vector4 taaFilterWeights1;
        public bool motionVectorRejection;
        public Vector4 taauParams;
        public Rect finalViewport;
        public Rect prevFinalViewport;
        public Vector4 taaScales;
        public bool runsTAAU;

        // Addional public fields
        public Camera Camera;
        public Vector4 taaJitterStrength;

        public RenderTexture prevHistory => historyBuffer[indexRead];
        public RenderTexture nextHistory => historyBuffer[indexWrite];
        public RenderTexture prevMVLen => velocityBuffer[indexRead];
        public RenderTexture nextMVLen => velocityBuffer[indexWrite];

        // Private fields
        private Matrix4x4 unjitteredProjectionMatrix;
        private Matrix4x4 jitteredProjectionMatrix;
        private int taaFrameIndex;
        private int indexRead;
        private int indexWrite;
        private RenderTexture[] historyBuffer;
        private RenderTexture[] velocityBuffer;
        private Plane[] frustumPlanes = new Plane[6];

        public TemporalAntiAliasingPassData(Camera camera)
        {
            Camera = camera;
        }

        public void UpdateState(float jitterAmount, ref RenderingData renderingData)
        {
            unjitteredProjectionMatrix = Camera.nonJitteredProjectionMatrix;
            const int kMaxSampleCount = 8;
            if (++taaFrameIndex >= kMaxSampleCount)
                taaFrameIndex = 0;
            GeometryUtility.CalculateFrustumPlanes(Camera, frustumPlanes);

            indexRead = indexWrite;
            indexWrite = (++indexWrite) % 2;

            float pixelWidth = renderingData.cameraData.pixelWidth;
            float pixelHeight = renderingData.cameraData.pixelHeight;

            // The variance between 0 and the actual halton sequence values reveals noticeable
            // instability in Unity's shadow maps, so we avoid index 0.
            float jitterX = HaltonSequence.Get((taaFrameIndex & 1023) + 1, 2) - 0.5f;
            float jitterY = HaltonSequence.Get((taaFrameIndex & 1023) + 1, 3) - 0.5f;

            jitterX *= jitterAmount;
            jitterY *= jitterAmount;

            taaJitterStrength = new Vector4(jitterX, jitterY, jitterX / pixelWidth, jitterY / pixelHeight);

            Matrix4x4 proj;

            if (Camera.orthographic)
            {
                float vertical = Camera.orthographicSize;
                float horizontal = vertical * Camera.aspect;

                var offset = taaJitterStrength;
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

            jitteredProjectionMatrix = proj;
        }


        public Matrix4x4 GetJitteredProjectionMatrix()
        {
#if UNITY_2021_2_OR_NEWER
            if (UnityEngine.FrameDebugger.enabled)
            {
                taaJitterStrength = Vector4.zero;
                return unjitteredProjectionMatrix;
            }
#endif
            return jitteredProjectionMatrix;
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

    #endregion

    [SerializeField] private Settings settings = new Settings();

    TAACameraSettingsPass cameraSettingsPass;
    TemporalAntiAliasingPass temporalAntiAliasingPass;

    internal static Dictionary<Camera, TemporalAntiAliasingPassData> cameraDataDict = new Dictionary<Camera, TemporalAntiAliasingPassData>();

    /// <inheritdoc/>
    public override void Create()
    {
        name = "Temporal Anti-Aliasing";

        cameraSettingsPass = new TAACameraSettingsPass();
        temporalAntiAliasingPass = new TemporalAntiAliasingPass();

        cameraSettingsPass.renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
        temporalAntiAliasingPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    }


    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!renderingData.cameraData.isSceneViewCamera)
        {
            var camera = renderingData.cameraData.camera;
            bool found = cameraDataDict.TryGetValue(camera, out TemporalAntiAliasingPassData data);
            if (!found)
            {
                data = new TemporalAntiAliasingPassData(camera);
                cameraDataDict.Add(camera, data);
            }

            cameraSettingsPass.Setup(settings);
            temporalAntiAliasingPass.Setup(settings);
            temporalAntiAliasingPass.ConfigureInput(ScriptableRenderPassInput.Motion);

            renderer.EnqueuePass(cameraSettingsPass);
            renderer.EnqueuePass(temporalAntiAliasingPass);
        }
    }




    [GenerateHLSL]
    internal enum StencilUsage
    {
        Clear = 0,

        // Note: first bit is free and can still be used by both phases.

        // --- Following bits are used before transparent rendering ---

        RequiresDeferredLighting = (1 << 1),
        SubsurfaceScattering = (1 << 2),     //  SSS, Split Lighting
        TraceReflectionRay = (1 << 3),     //  SSR or RTR - slot is reuse in transparent
        Decals = (1 << 4),     //  Use to tag when an Opaque Decal is render into DBuffer
        ObjectMotionVector = (1 << 5),     //  Animated object (for motion blur, SSR, SSAO, TAA)

        // --- Stencil  is cleared after opaque rendering has finished ---

        // --- Following bits are used exclusively for what happens after opaque ---
        ExcludeFromTAA = (1 << 1),    // Disable Temporal Antialiasing for certain objects
        DistortionVectors = (1 << 2),    // Distortion pass - reset after distortion pass, shared with SMAA
        SMAA = (1 << 2),    // Subpixel Morphological Antialiasing
        // Reserved TraceReflectionRay = (1 << 3) for transparent SSR or RTR
        AfterOpaqueReservedBits = 0x38,        // Reserved for future usage

        // --- Following are user bits, we don't touch them inside HDRP and is up to the user to handle them ---
        UserBit0 = (1 << 6),
        UserBit1 = (1 << 7),

        H
    }
}


