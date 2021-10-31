using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Tooltip("Generates a 16-bit pseudo-unique object ID for use with TAA")]
public class ObjectID : MonoBehaviour
{
    private static int index = 32000;

    public int objectID { get; private set; }
    public float normalizedID { get; private set; }
    public Vector4 colorID { get; private set; }


    private void Start()
    {
        objectID = Random.Range(0, 65535);// GetNextIndex();
        normalizedID = objectID / 65535f;
        colorID = new Vector4(0,0,0, normalizedID);

        UpdateMaterials();
    }

    public void UpdateMaterials()
    {
        var renderers = transform.GetComponentsInChildren<Renderer>(true);

        List<Material> materials = new List<Material>();
        foreach (var renderer in renderers)
        {
            renderer.GetMaterials(materials);
            foreach (var material in materials)
            {
                material.SetFloat("_ObjectID", normalizedID);
            }
        }
    }

    private int GetNextIndex()
    {
        index = index++ % 65535;
        return index;
    }

    public void UpdateMeshUV7()
    {
        List<Vector4> IDuvs = new List<Vector4>();

        var meshFilters = transform.GetComponentsInChildren<MeshFilter>(true);

        foreach (var meshFilter in meshFilters)
        {
            IDuvs.Clear();
            var mesh = meshFilter.mesh;
            int count = mesh.vertexCount;
            for (int i = 0; i < count; i++)
                IDuvs.Add(colorID);

            mesh.SetUVs(7, IDuvs);
        }

        var skinnedMeshRenderers = transform.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
        {
            IDuvs.Clear();
            var mesh = Instantiate(skinnedMeshRenderer.sharedMesh);
            int count = mesh.vertexCount;
            for (int i = 0; i < count; i++)
                IDuvs.Add(colorID);

            mesh.SetUVs(7, IDuvs);
            skinnedMeshRenderer.sharedMesh = mesh;
        }
    }
}
