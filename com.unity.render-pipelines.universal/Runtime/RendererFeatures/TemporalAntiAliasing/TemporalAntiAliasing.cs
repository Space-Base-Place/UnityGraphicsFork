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
        [Range(0, 1)] public float taaObjectIdRejection;
        public bool taaAntiRinging;

        public LayerMask objectIdLayers;
    }

    class TAACameraSettingsPass : ScriptableRenderPass
    {
        Settings settings;
        bool firstPass;

        public void Setup(Settings settings, bool firstPass)
        {
            this.settings = settings;
            this.firstPass = firstPass;
            profilingSampler = new ProfilingSampler("TAA Camera Jitter");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ref var cameraData = ref renderingData.cameraData;
            var camera = cameraData.camera;

            if (firstPass)
            {
                var foundData = cameraDataDict.TryGetValue(camera, out currentData);

                if (!foundData) // Don't render if no data
                {
                    currentData = null;
                    return;
                }
                
                // Ensure Buffers
                var descriptor = new RenderTextureDescriptor(camera.scaledPixelWidth, camera.scaledPixelHeight, RenderTextureFormat.DefaultHDR, 16);
                var objectIDDescriptor = new RenderTextureDescriptor(camera.scaledPixelWidth, camera.scaledPixelHeight, RenderTextureFormat.R16, 0);

                currentData.resetPostProcessingHistory = currentData.EnsureBuffers(ref descriptor, ref objectIDDescriptor);

                // Update the jitter state
                currentData.UpdateState(settings.taaJitterAmount, ref renderingData);
            }

            if (currentData == null) // Don't render if no data
                return;

            // Jitter camera
            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {

                var viewMatrix = camera.worldToCameraMatrix;
                var projMatrix = currentData.GetJitteredProjectionMatrix();

                RenderingUtils.SetViewAndProjectionMatrices(cmd, viewMatrix, GL.GetGPUProjectionMatrix(projMatrix, true), true);
            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

    }

    class ObjectIDPass : ScriptableRenderPass
    {
        Material material;
        LayerMask layerMask;


        public void Setup(Settings settings)
        {
            profilingSampler = new ProfilingSampler("ObjectID");

            layerMask = settings.objectIdLayers;

            var shader = Shader.Find("Hidden/ObjectID");

            if (shader == null)
                return;

            material = new Material(shader);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                Debug.LogError("Material is null");
                return;
            }

            var camera = renderingData.cameraData.camera;

            if (currentData == null) // Don't render if no data
                return;

            var cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, profilingSampler))
            {
                var objectIDDescriptor = new RenderTextureDescriptor(camera.scaledPixelWidth, camera.scaledPixelHeight, RenderTextureFormat.R16, 0);
                cmd.GetTemporaryRT(ShaderIDs._CurrentObjectIDTexture, objectIDDescriptor);

                cmd.SetRenderTarget(ShaderIDs._CurrentObjectIDTexture, renderingData.cameraData.renderer.cameraDepthTarget);
                cmd.ClearRenderTarget(false, true, Color.clear);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var sortingSettings = new SortingSettings(camera);
                var drawingSettings = CreateDrawingSettings(new ShaderTagId("UniversalForward"), ref renderingData, renderingData.cameraData.defaultOpaqueSortFlags);
                drawingSettings.overrideMaterial = material;
                drawingSettings.overrideMaterialPassIndex = 0;
                

                var filteringSettings = new FilteringSettings(RenderQueueRange.all, layerMask);

                context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref filteringSettings);

            }

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(ShaderIDs._CurrentObjectIDTexture);
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

        private RenderTargetHandle renderSource;
        private RenderTargetIdentifier renderTarget;


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

            renderSource.Init("_TempRT");
            cmd.GetTemporaryRT(renderSource.id, renderingData.cameraData.cameraTargetDescriptor);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (temporalAAMaterial == null || temporalAAMaterial.shader == null)
                return; // dont render if not initialised

            var camera = renderingData.cameraData.camera;

            if (currentData == null) // Don't render if no data
                return;

            PrepareTAAPassData(settings, currentData, ref renderingData);


            const int taaPass = 0;
            const int excludeTaaPass = 1;
            const int taauPass = 2;
            const int copyHistoryPass = 3;

            var cmd = CommandBufferPool.Get("Temporal Anti-Aliasing");

            cmd.Blit(renderTarget, renderSource.Identifier());
            cmd.SetGlobalTexture(ShaderIDs._InputTexture, renderSource.Identifier());

            if (currentData.resetPostProcessingHistory)
            {
                var historyMpb = MaterialPropertyBlockPool.Get();
                historyMpb.SetVector(ShaderIDs._TaaScales, currentData.taaScales);

                DrawFullScreen(cmd, currentData.temporalAAMaterial, currentData.prevHistory, historyMpb, copyHistoryPass);
                DrawFullScreen(cmd, currentData.temporalAAMaterial, currentData.nextHistory, historyMpb, copyHistoryPass);

                MaterialPropertyBlockPool.Release(historyMpb);
            }

            var mpb = MaterialPropertyBlockPool.Get();
            mpb.SetInt(ShaderIDs._StencilMask, (int)StencilUsage.ExcludeFromTAA);
            mpb.SetInt(ShaderIDs._StencilRef, (int)StencilUsage.ExcludeFromTAA);
            mpb.SetTexture(ShaderIDs._InputHistoryTexture, currentData.prevHistory);
            if (currentData.prevMVLen != null && currentData.motionVectorRejection)
            {
                mpb.SetTexture(ShaderIDs._InputVelocityMagnitudeHistory, currentData.prevMVLen);
            }

            var taaHistorySize = currentData.previousScreenSize;

            var taaFrameInfo = new Vector4(settings.taaSharpenStrength, 0, 0, 1);
            mpb.SetVector(ShaderIDs._TaaFrameInfo, taaFrameInfo);
            mpb.SetVector(ShaderIDs._TaaJitterStrength, currentData.taaJitterStrength);

            mpb.SetVector(ShaderIDs._TaaPostParameters, currentData.taaParameters);
            mpb.SetVector(ShaderIDs._TaaHistorySize, taaHistorySize);
            mpb.SetVector(ShaderIDs._TaaFilterWeights, currentData.taaFilterWeights);

            // Object ID
            var taaObjectIdParams = new Vector4(currentData.cameraVelocity, settings.taaObjectIdRejection, 0, 0);
            mpb.SetVector(ShaderIDs._TaaObjectIDParameters, taaObjectIdParams);
            cmd.SetGlobalTexture(ShaderIDs._CurrentObjectIDTexture, ShaderIDs._CurrentObjectIDTexture);
            mpb.SetTexture(ShaderIDs._PreviousObjectIDTexture, currentData.prevObjectID);
            cmd.SetRandomWriteTarget(3, currentData.nextObjectID);

            CoreUtils.SetRenderTarget(cmd, renderTarget);

            cmd.SetRandomWriteTarget(1, currentData.nextHistory);
            if (currentData.nextMVLen != null && currentData.motionVectorRejection)
            {
                cmd.SetRandomWriteTarget(2, currentData.nextMVLen);
            }

            cmd.DrawProcedural(Matrix4x4.identity, currentData.temporalAAMaterial, taaPass, MeshTopology.Triangles, 3, 1, mpb);
            //cmd.DrawProcedural(Matrix4x4.identity, data.temporalAAMaterial, excludeTaaPass, MeshTopology.Triangles, 3, 1, mpb);

            cmd.ClearRandomWriteTargets();
            MaterialPropertyBlockPool.Release(mpb);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);


            // Camera must be reset before the next frame or we introduce drift
            camera.ResetProjectionMatrix();
        }

        private void PrepareTAAPassData(Settings settings, TemporalAntiAliasingPassData passData, ref RenderingData renderingData)
        {
            float minAntiflicker = 0.0f;
            float maxAntiflicker = 3.5f;
            float motionRejectionMultiplier = Mathf.Lerp(0.0f, 250.0f, settings.taaMotionVectorRejection * settings.taaMotionVectorRejection * settings.taaMotionVectorRejection);

            // The anti flicker becomes much more aggressive on higher values
            float temporalContrastForMaxAntiFlicker = 0.7f - Mathf.Lerp(0.0f, 0.3f, Mathf.SmoothStep(0.5f, 1.0f, settings.taaAntiFlicker));

            float antiFlickerLerpFactor = settings.taaAntiFlicker;
            float historySharpening = settings.taaHistorySharpening;

            if (renderingData.cameraData.isSceneViewCamera)
            {
                // Force settings for scene view.
                historySharpening = 0.25f;
                antiFlickerLerpFactor = 0.7f;
            }
            float antiFlicker = Mathf.Lerp(minAntiflicker, maxAntiflicker, antiFlickerLerpFactor);

            passData.taaParameters = new Vector4(historySharpening, antiFlicker, motionRejectionMultiplier, temporalContrastForMaxAntiFlicker);

            /*            this more accurate version of the filter could probably
             *            be upgraded seperately from the rest of the logic:
             *            
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

            // we always clear history when size changes so this is redundant?
            int width = renderingData.cameraData.pixelWidth;
            int height = renderingData.cameraData.pixelHeight;
            passData.previousScreenSize = new Vector4(width, height, 1.0f / width, 1.0f / height);

            passData.taaScales = Vector4.one;
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(renderSource.id);
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

    #endregion


    #region RENDERER FEATURE

    [SerializeField] private Settings settings = new Settings();

    TAACameraSettingsPass cameraSettingsPass;
    ObjectIDPass objectIDPass;
    TemporalAntiAliasingPass temporalAntiAliasingPass;

    internal static Dictionary<Camera, TemporalAntiAliasingPassData> cameraDataDict = new Dictionary<Camera, TemporalAntiAliasingPassData>();
    internal static TemporalAntiAliasingPassData currentData;

    /// <inheritdoc/>
    public override void Create()
    {
        name = "Temporal Anti-Aliasing";

        cameraSettingsPass = new TAACameraSettingsPass();
        cameraSettingsPass.renderPassEvent = RenderPassEvent.BeforeRenderingGbuffer - 1;

        // TODO: remove this pass and include object IDs in GBuffer pass
        //objectIDPass = new ObjectIDPass();
        //objectIDPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

        temporalAntiAliasingPass = new TemporalAntiAliasingPass();
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

            cameraSettingsPass.Setup(settings, true);
            renderer.EnqueuePass(cameraSettingsPass);

            //objectIDPass.Setup(settings);
            //renderer.EnqueuePass(objectIDPass);

            temporalAntiAliasingPass.Setup(settings);
            temporalAntiAliasingPass.ConfigureInput(ScriptableRenderPassInput.Motion);
            renderer.EnqueuePass(temporalAntiAliasingPass);

        }
    }

    #endregion

    #region SUPPORT CLASSES

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
        public Camera camera;
        public Vector4 taaJitterStrength;
        public Matrix4x4 unjitteredProjectionMatrix;
        public float cameraVelocity;

        public RenderTexture prevHistory => historyBuffer[indexRead];
        public RenderTexture nextHistory => historyBuffer[indexWrite];
        public RenderTexture prevMVLen => velocityBuffer[indexRead];
        public RenderTexture nextMVLen => velocityBuffer[indexWrite];
        public RenderTexture nextObjectID => objectIDBuffer[indexRead];
        public RenderTexture prevObjectID => objectIDBuffer[indexWrite];

        // Private fields
        private Matrix4x4 jitteredProjectionMatrix;
        private int taaFrameIndex;
        private int indexRead;
        private int indexWrite;
        private RenderTexture[] historyBuffer;
        private RenderTexture[] velocityBuffer;
        private RenderTexture[] objectIDBuffer;
        private Plane[] frustumPlanes = new Plane[6];
        private Vector3 prevCameraWP;

        public TemporalAntiAliasingPassData(Camera camera)
        {
            this.camera = camera;
        }

        public void UpdateState(float jitterAmount, ref RenderingData renderingData)
        {
            unjitteredProjectionMatrix = camera.nonJitteredProjectionMatrix;
            const int kMaxSampleCount = 8;
            if (++taaFrameIndex >= kMaxSampleCount)
                taaFrameIndex = 0;
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);

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

            if (camera.orthographic)
            {
                float vertical = camera.orthographicSize;
                float horizontal = vertical * camera.aspect;

                var offset = taaJitterStrength;
                offset.x *= horizontal / (0.5f * pixelWidth);
                offset.y *= vertical / (0.5f * pixelHeight);

                float left = offset.x - horizontal;
                float right = offset.x + horizontal;
                float top = offset.y + vertical;
                float bottom = offset.y - vertical;

                proj = Matrix4x4.Ortho(left, right, bottom, top, camera.nearClipPlane, camera.farClipPlane);
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

            var currentCameraWP = camera.transform.position;
            cameraVelocity = (currentCameraWP - prevCameraWP).magnitude;
            prevCameraWP = currentCameraWP;
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
        public bool EnsureBuffers(ref RenderTextureDescriptor colorDescriptor, ref RenderTextureDescriptor objectIDDescriptor)
        {
            bool sizeChanged = false;
            sizeChanged |= EnsureBuffer(ref historyBuffer, ref colorDescriptor);
            sizeChanged |= EnsureBuffer(ref velocityBuffer, ref colorDescriptor);
            sizeChanged |= EnsureBuffer(ref objectIDBuffer, ref objectIDDescriptor);
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
                //RenderTexture.ReleaseTemporary(rt);
                rt = null;
            }
            if (rt == null)
            {
                rt = new RenderTexture(width, height, depthBits, format, 0);
                //rt = RenderTexture.GetTemporary(width, height, depthBits, format, RenderTextureReadWrite.Default, antiAliasing);
                rt.antiAliasing = antiAliasing;
                rt.filterMode = filterMode;
                rt.wrapMode = TextureWrapMode.Clamp;
                rt.enableRandomWrite = true;
                rt.Create();
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

    internal static class ShaderIDs
    {
        public static readonly int _InputTexture = Shader.PropertyToID("_InputTexture");
        public static readonly int _DepthTexture = Shader.PropertyToID("_DepthTexture");
        public static readonly int _InputHistoryTexture = Shader.PropertyToID("_InputHistoryTexture");
        public static readonly int _OutputHistoryTexture = Shader.PropertyToID("_OutputHistoryTexture");
        public static readonly int _CurrentObjectIDTexture = Shader.PropertyToID("_CurrentObjectIDTexture");
        public static readonly int _PreviousObjectIDTexture = Shader.PropertyToID("_PreviousObjectIDTexture");
        public static readonly int _OutputObjectIDTexture = Shader.PropertyToID("_OutputObjectIDTexture");
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
        public static readonly int _TaaObjectIDParameters = Shader.PropertyToID("_TaaObjectIDParameters");

        public static readonly int _StencilMask = Shader.PropertyToID("_StencilMask");
        public static readonly int _StencilRef = Shader.PropertyToID("_StencilRef");
    }

    #endregion
}


