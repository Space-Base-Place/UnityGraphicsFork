using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SelectionOutlines
{
    public enum Layer
    {
        Selected = 0,
        Highlighted = 1,
    }

    public static void Add(GameObject gameObject, Layer layer)
    {
        SelectionOutlinesFeature.Add(gameObject, layer);
    }

    public static void Remove(GameObject gameObject, Layer layer)
    {
        SelectionOutlinesFeature.Remove(gameObject, layer);
    }
}

public class SelectionOutlinesFeature : ScriptableRendererFeature
{
    [Serializable]
    public class Settings
    {
        public Config selected;
        public Config highlighted;

        [Serializable]
        public class Config
        {
            [ColorUsage(true, true)] public Color color = Color.white;
            [Min(0)] public float scale = 1;
            public RenderPassEvent RenderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            public int renderPassOffset;
        }

        public Config GetConfig(int i)
        {
            return i switch
            {
                0 => selected,
                1 => highlighted,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }

    public Settings settings = new Settings();


    private static SelectionOutlinesPass[] passes;


    /// <inheritdoc/>
    public override void Create()
    {
        int count = Enum.GetValues(typeof(SelectionOutlines.Layer)).Length;
        passes = new SelectionOutlinesPass[count];

        for (int i = 0; i < count; i++)
        {
            passes[i] = new();
            Settings.Config config = settings.GetConfig(i);
            passes[i].Setup(config);
            passes[i].renderPassEvent = config.RenderPassEvent + config.renderPassOffset;
        }
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview)
            return;

        for (int i = 0; i < passes.Length; i++)
        {
            if (passes[i].objects.Count > 0)
                renderer.EnqueuePass(passes[i]);
        }
    }

    internal static void Add(GameObject gameObject, SelectionOutlines.Layer layer)
    {
        if (passes == null)
            return;

        var pass = passes[(int)layer];

        if (pass == null)
            return;

        if (!pass.objects.Contains(gameObject))
            pass.objects.Add(gameObject);
    }

    internal static void Remove(GameObject gameObject, SelectionOutlines.Layer layer)
    {
        if (passes == null)
            return;

        var pass = passes[(int)layer];

        if (pass == null)
            return;

        if (pass.objects.Contains(gameObject))
            pass.objects.Remove(gameObject);
    }




    class SelectionOutlinesPass : ScriptableRenderPass
    {
        private Material material;
        MaterialPropertyBlock mpb;

        private RenderTargetIdentifier renderTarget;
        private RenderTargetHandle tempRenderTarget;
        private RenderTargetHandle canvas;


        internal List<GameObject> objects = new();
        private static readonly List<Renderer> renderers = new();
        private static readonly Queue<Transform> transformQ = new();

        public void Setup(Settings.Config settings)
        {
            var shader = Shader.Find("Hidden/Outlines");

            if (shader == null)
            {
                Debug.LogError("Shader is null");
                return;
            }

            material = new Material(shader);
            mpb = new();
            mpb.SetColor(OutlinesIDs._Color, settings.color);
            mpb.SetFloat(OutlinesIDs._Scale, settings.scale);

            tempRenderTarget.Init("_TempRenderTarget");
            canvas.Init("_Canvas");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;

            cmd.GetTemporaryRT(tempRenderTarget.id, descriptor);
            cmd.GetTemporaryRT(canvas.id, descriptor);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (material == null)
            {
                return;
            }


            CommandBuffer cmd = CommandBufferPool.Get("Selection Outlines");

            cmd.SetRenderTarget(canvas.Identifier());
            cmd.ClearRenderTarget(true, true, Color.black);

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null)
                    continue;
                float id = (i + 1f) / objects.Count;
                cmd.SetGlobalFloat(OutlinesIDs._FlatColor, id);

                DrawRenderers(cmd, obj);
            }

            cmd.SetRenderTarget(tempRenderTarget.Identifier());
            cmd.SetGlobalTexture(OutlinesIDs._MainTex, renderTarget);
            cmd.SetGlobalTexture(OutlinesIDs._CanvasTex, canvas.Identifier());
            cmd.DrawMesh(RenderingUtils.fullscreenMesh, Matrix4x4.identity, material, 0, OutlinesIDs.SimpleSobelPassIndex, mpb);
            Blit(cmd, tempRenderTarget.Identifier(), renderTarget);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static List<Material> materials = new();

        private void DrawRenderers(CommandBuffer cmd, GameObject obj)
        {
            transformQ.Enqueue(obj.transform);

            while (transformQ.Count > 0)
            {
                var t = transformQ.Dequeue();
                if (!t.gameObject.activeInHierarchy)
                    continue;

                t.GetComponents(renderers);
                for (int i = 0; i < renderers.Count; i++)
                {
                    int subMeshCount = 1;
                    if (renderers[i] is MeshRenderer mr)
                    {
                        if (mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                            subMeshCount = mf.sharedMesh.subMeshCount;
                    }
                    else if (renderers[i] is SkinnedMeshRenderer smr)
                    {
                        subMeshCount = smr.sharedMesh.subMeshCount;
                    }
                    renderers[i].GetMaterials(materials);
                    for (int subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
                    {
                        Material m = materials.Count == subMeshCount ? materials[subMeshIndex] : materials[0];
                        int pass = m.FindPass("Forward");
                        cmd.DrawRenderer(renderers[i], m, subMeshIndex, pass);
                    }
                }

                for (int childIndex = 0; childIndex < t.childCount; childIndex++)
                {
                    transformQ.Enqueue(t.GetChild(childIndex));
                }
            }
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(tempRenderTarget.id);
            cmd.ReleaseTemporaryRT(canvas.id);
        }


    }

}


