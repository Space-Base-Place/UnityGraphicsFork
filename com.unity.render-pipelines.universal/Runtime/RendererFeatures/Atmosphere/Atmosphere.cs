using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class Atmosphere : MonoBehaviour
{
    public static List<Atmosphere> allAtmospheres = new List<Atmosphere>();

    [InlineEditor] public AtmosphereData Data;

    private void Update()
    {
        if (Data != null)
            Data.UpdatePlanetCentre(transform.position);
    }


    private void OnEnable()
    {
        allAtmospheres.Add(this);
    }

    private void OnDisable()
    {
        allAtmospheres.Remove(this);
    }
}
