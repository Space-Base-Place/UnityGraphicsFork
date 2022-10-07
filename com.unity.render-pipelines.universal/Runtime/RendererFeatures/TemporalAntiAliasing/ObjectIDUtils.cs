using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public static class ObjectIDUtils
{
    private static readonly List<Renderer> renderers = new();
    private static readonly List<Material> materials = new();
    public static int _ObjectID = Shader.PropertyToID("_ObjectID");

    public static void SetObjectIDByInstanceID(GameObject gameObject)
    {
        var objectID = Mathf.Abs(gameObject.GetInstanceID()) % 65535;
        var normalizedID = objectID / 65535f;

        gameObject.GetComponentsInChildren(true, renderers);

        foreach (var renderer in renderers)
        {
            renderer.GetMaterials(materials);
            foreach (var materal in materials)
            {
                materal.SetFloat(_ObjectID, normalizedID);
            }
        }
    }

    public static float GetObjectID(object obj)
    {
        var hash = obj.GetHashCode();
        var objectID = Mathf.Abs(hash) % 65535;
        var normalizedID = objectID / 65535f;
        return normalizedID;
    }
}
