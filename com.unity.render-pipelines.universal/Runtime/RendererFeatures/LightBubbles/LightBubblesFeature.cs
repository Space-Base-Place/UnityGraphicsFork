using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class LightBubblesFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Color Color;
        [Range(0, 1)] public float EdgeSharpness = 0.3f;
    }

    public Settings settings = new Settings();


    class CustomRenderPass : ScriptableRenderPass
    {
        Material material;
        const int StencilPass = 0;
        const int BubblePass = 1; 
        const int BlendPass = 2;

        private RenderTargetIdentifier renderSource;
        private RenderTargetIdentifier renderTarget;
        private RenderTargetIdentifier depthTarget;
        private RenderTargetIdentifier bubbleBuffer;
        int temporaryRTId = Shader.PropertyToID("_TempRT");
        int bufferRTId = Shader.PropertyToID("_BubbleBuffer");

        private Settings settings;
        MaterialPropertyBlock mpb;

        Mesh SphereMesh 
        { 
            get 
            {
                if (sphereMesh == null)
                    sphereMesh = CreateSphereMesh();
                return sphereMesh;
            } 
        }
        Mesh sphereMesh;
        
        public void Setup(Settings settings)
        {
            this.settings = settings;

            EnsureMaterial(true);

            mpb = new MaterialPropertyBlock();
        }

        private void EnsureMaterial(bool forceRecreate = false)
        {
            if (material == null || forceRecreate)
            {
                material = new Material(Shader.Find("Hidden/LightBubbles"));
                material.SetColor("_Color", settings.Color);
            }
        }


        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            EnsureMaterial();

            renderTarget = renderingData.cameraData.renderer.cameraColorTarget;
            depthTarget = renderingData.cameraData.renderer.cameraDepthTarget;

            cmd.GetTemporaryRT(temporaryRTId, renderingData.cameraData.cameraTargetDescriptor);
            renderSource = new RenderTargetIdentifier(temporaryRTId);
            cmd.GetTemporaryRT(bufferRTId, renderingData.cameraData.cameraTargetDescriptor);
            bubbleBuffer = new RenderTargetIdentifier(bufferRTId);

            //you're supposed to do this instead of cmd.SetRenderTarget, but it doesnt work
            //ConfigureTarget(bubbleBuffer);
            //ConfigureClear(ClearFlag.None, Color.black);
        }


        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get("LightBubbles");

            cmd.Blit(renderTarget, renderSource);

            cmd.SetRenderTarget(bubbleBuffer, depthTarget);
            cmd.ClearRenderTarget(false, true, Color.black);

            foreach (var light in renderingData.lightData.visibleLights)
            {
                if (light.lightType != LightType.Point)
                    continue;

                Vector3 posWS = light.localToWorldMatrix.GetColumn(3);

                Matrix4x4 transformMatrix = new Matrix4x4(
                    new Vector4(light.range, 0.0f, 0.0f, 0.0f),
                    new Vector4(0.0f, light.range, 0.0f, 0.0f),
                    new Vector4(0.0f, 0.0f, light.range, 0.0f),
                    new Vector4(posWS.x, posWS.y, posWS.z, 1.0f)
                );

                mpb.SetFloat("_LightInstanceIntensity", light.light.intensity);
                mpb.SetFloat("_EdgeSharpness", settings.EdgeSharpness);
                mpb.SetVector("_CurrentLightColor", light.finalColor);

                cmd.DrawMesh(SphereMesh, transformMatrix, material, 0, StencilPass, mpb);
                cmd.DrawMesh(SphereMesh, transformMatrix, material, 0, BubblePass, mpb);
            }
            
            cmd.SetGlobalTexture("_BubbleBufferTex", bubbleBuffer);
            Blit(cmd, renderSource, renderTarget, material, BlendPass);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            CommandBufferPool.Release(cmd);
        }

        // Cleanup any allocated resources that were created during the execution of this render pass.
        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            cmd.ReleaseTemporaryRT(temporaryRTId);
            cmd.ReleaseTemporaryRT(bufferRTId);
        }
    }

    CustomRenderPass m_ScriptablePass;

    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new CustomRenderPass();

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        m_ScriptablePass.Setup(settings);
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview)
            return;

        renderer.EnqueuePass(m_ScriptablePass);
    }


    static Mesh CreateSphereMesh()
    {
        return Icosahedron.Generate(2);
        // This icosaedron has been been slightly inflated to fit an unit sphere.
        // This is the same geometry as built-in deferred.

/*        Vector3[] positions =
        {
                new Vector3(0.000f,  0.000f, -1.070f), new Vector3(0.174f, -0.535f, -0.910f),
                new Vector3(-0.455f, -0.331f, -0.910f), new Vector3(0.562f,  0.000f, -0.910f),
                new Vector3(-0.455f,  0.331f, -0.910f), new Vector3(0.174f,  0.535f, -0.910f),
                new Vector3(-0.281f, -0.865f, -0.562f), new Vector3(0.736f, -0.535f, -0.562f),
                new Vector3(0.296f, -0.910f, -0.468f), new Vector3(-0.910f,  0.000f, -0.562f),
                new Vector3(-0.774f, -0.562f, -0.478f), new Vector3(0.000f, -1.070f,  0.000f),
                new Vector3(-0.629f, -0.865f,  0.000f), new Vector3(0.629f, -0.865f,  0.000f),
                new Vector3(-1.017f, -0.331f,  0.000f), new Vector3(0.957f,  0.000f, -0.478f),
                new Vector3(0.736f,  0.535f, -0.562f), new Vector3(1.017f, -0.331f,  0.000f),
                new Vector3(1.017f,  0.331f,  0.000f), new Vector3(-0.296f, -0.910f,  0.478f),
                new Vector3(0.281f, -0.865f,  0.562f), new Vector3(0.774f, -0.562f,  0.478f),
                new Vector3(-0.736f, -0.535f,  0.562f), new Vector3(0.910f,  0.000f,  0.562f),
                new Vector3(0.455f, -0.331f,  0.910f), new Vector3(-0.174f, -0.535f,  0.910f),
                new Vector3(0.629f,  0.865f,  0.000f), new Vector3(0.774f,  0.562f,  0.478f),
                new Vector3(0.455f,  0.331f,  0.910f), new Vector3(0.000f,  0.000f,  1.070f),
                new Vector3(-0.562f,  0.000f,  0.910f), new Vector3(-0.957f,  0.000f,  0.478f),
                new Vector3(0.281f,  0.865f,  0.562f), new Vector3(-0.174f,  0.535f,  0.910f),
                new Vector3(0.296f,  0.910f, -0.478f), new Vector3(-1.017f,  0.331f,  0.000f),
                new Vector3(-0.736f,  0.535f,  0.562f), new Vector3(-0.296f,  0.910f,  0.478f),
                new Vector3(0.000f,  1.070f,  0.000f), new Vector3(-0.281f,  0.865f, -0.562f),
                new Vector3(-0.774f,  0.562f, -0.478f), new Vector3(-0.629f,  0.865f,  0.000f),
            };

        int[] indices =
        {
                0,  1,  2,  0,  3,  1,  2,  4,  0,  0,  5,  3,  0,  4,  5,  1,  6,  2,
                3,  7,  1,  1,  8,  6,  1,  7,  8,  9,  4,  2,  2,  6, 10, 10,  9,  2,
                8, 11,  6,  6, 12, 10, 11, 12,  6,  7, 13,  8,  8, 13, 11, 10, 14,  9,
                10, 12, 14,  3, 15,  7,  5, 16,  3,  3, 16, 15, 15, 17,  7, 17, 13,  7,
                16, 18, 15, 15, 18, 17, 11, 19, 12, 13, 20, 11, 11, 20, 19, 17, 21, 13,
                13, 21, 20, 12, 19, 22, 12, 22, 14, 17, 23, 21, 18, 23, 17, 21, 24, 20,
                23, 24, 21, 20, 25, 19, 19, 25, 22, 24, 25, 20, 26, 18, 16, 18, 27, 23,
                26, 27, 18, 28, 24, 23, 27, 28, 23, 24, 29, 25, 28, 29, 24, 25, 30, 22,
                25, 29, 30, 14, 22, 31, 22, 30, 31, 32, 28, 27, 26, 32, 27, 33, 29, 28,
                30, 29, 33, 33, 28, 32, 34, 26, 16,  5, 34, 16, 14, 31, 35, 14, 35,  9,
                31, 30, 36, 30, 33, 36, 35, 31, 36, 37, 33, 32, 36, 33, 37, 38, 32, 26,
                34, 38, 26, 38, 37, 32,  5, 39, 34, 39, 38, 34,  4, 39,  5,  9, 40,  4,
                9, 35, 40,  4, 40, 39, 35, 36, 41, 41, 36, 37, 41, 37, 38, 40, 35, 41,
                40, 41, 39, 41, 38, 39,
            };


        Mesh mesh = new Mesh();
        mesh.indexFormat = IndexFormat.UInt16;
        mesh.vertices = positions;
        mesh.normals = positions;
        mesh.triangles = indices;

        return mesh;*/
    }

    private static class Icosahedron
    {
        public static Mesh Generate(int subdivisions)
        {
            return SubdivideMesh(GenerateIcosahedron(), subdivisions, true);
        }

        private static Mesh GenerateIcosahedron()
        {
            float t = (1.0f + Mathf.Sqrt(5.0f)) / 2.0f;

            // Initial Vertices and Triangles of Icosahedron
            Vector3[] vertices =
            {
            new Vector3(-1, t, 0).normalized,
            new Vector3(1, t, 0).normalized,
            new Vector3(-1, -t, 0).normalized,
            new Vector3(1, -t, 0).normalized,
            new Vector3(0, -1, t).normalized,
            new Vector3(0, 1, t).normalized,
            new Vector3(0, -1, -t).normalized,
            new Vector3(0, 1, -t).normalized,
            new Vector3(t, 0, -1).normalized,
            new Vector3(t, 0, 1).normalized,
            new Vector3(-t, 0, -1).normalized,
            new Vector3(-t, 0, 1).normalized
        };
            int[] triangles =
            {
            0, 11, 5,
            0, 1, 7,
            0, 5, 1,
            0, 7, 10,
            0, 10, 11,
            1, 5, 9,
            5, 11, 4,
            11, 10, 2,
            10, 7, 6,
            7, 1, 8,
            3, 9, 4,
            3, 4, 2,
            3, 2, 6,
            3, 6, 8,
            3, 8, 9,
            4, 9, 5,
            2, 4, 11,
            6, 2, 10,
            8, 6, 7,
            9, 8, 1
        };

            Mesh mesh = new Mesh
            {
                vertices = vertices,
                triangles = triangles,
                normals = vertices
            };
            return mesh;
        }

        private static Mesh SubdivideMesh(Mesh mesh, int divisions, bool normalize = true)
        {
            List<Vector3> vertices = new List<Vector3>(mesh.vertices);
            int[] triangles = mesh.triangles;

            bool hasUVs = true;
            List<Vector2> uvs = new List<Vector2>(mesh.uv);
            if (uvs.Count == 0)
                hasUVs = false;

            // Divide faces
            var midPointCache = new Dictionary<int, int>();

            for (int d = 0; d < divisions; d++)
            {
                var newTriangles = new List<int>();
                for (int f = 0, t = 0; f < triangles.Length / 3; f++)
                {
                    // Get current triangle vertices
                    int a = triangles[t++];
                    int b = triangles[t++];
                    int c = triangles[t++];

                    // Find vertice at centre of each edge
                    int ab = GetMidPointIndex(midPointCache, a, b, vertices, uvs, hasUVs);
                    int bc = GetMidPointIndex(midPointCache, b, c, vertices, uvs, hasUVs);
                    int ca = GetMidPointIndex(midPointCache, c, a, vertices, uvs, hasUVs);

                    // Create new Triangles
                    newTriangles.Add(a); newTriangles.Add(ab); newTriangles.Add(ca); //triangle 1
                    newTriangles.Add(b); newTriangles.Add(bc); newTriangles.Add(ab); //triangle 2
                    newTriangles.Add(c); newTriangles.Add(ca); newTriangles.Add(bc); //triangle 3
                    newTriangles.Add(ab); newTriangles.Add(bc); newTriangles.Add(ca);//triangle 4
                }
                triangles = newTriangles.ToArray();
            }

            Mesh newMesh = new Mesh();


            // Create Normals
            if (normalize)
            {
                Vector3[] vertArray = vertices.ToArray();
                for (int i = 0; i < vertArray.Length; i++)
                    vertArray[i] = vertArray[i].normalized;
                newMesh.vertices = vertArray;
                newMesh.normals = vertArray;
            }
            else
            {
                newMesh.vertices = vertices.ToArray();
                newMesh.RecalculateNormals();
            }

            newMesh.triangles = triangles;
            if (hasUVs) newMesh.uv = uvs.ToArray();

            return newMesh;
        }

        private static int GetMidPointIndex(Dictionary<int, int> cache, int indexA, int indexB,
        List<Vector3> vertices, List<Vector2> uvs, bool hasUVs)
        {
            // Checks if vertice has already been made and creates it if it hasn't
            int smallerIndex = Mathf.Min(indexA, indexB);
            int greaterIndex = Mathf.Max(indexA, indexB);
            int key = (smallerIndex << 16) + greaterIndex;

            if (cache.TryGetValue(key, out int index))
                return index;
            else
            {
                index = vertices.Count;

                Vector3 midPoint = Vector3.Lerp(vertices[indexA], vertices[indexB], 0.5f).normalized;
                vertices.Add(midPoint);

                if (hasUVs)
                {
                    Vector2 midUV = Vector2.Lerp(uvs[indexA], uvs[indexB], 0.5f);
                    uvs.Add(midUV);
                }

                cache.Add(key, index);
                return index;
            }
        }
    }
}


