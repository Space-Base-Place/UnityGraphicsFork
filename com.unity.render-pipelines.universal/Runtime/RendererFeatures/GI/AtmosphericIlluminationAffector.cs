using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class AtmosphericIlluminationAffector : MonoBehaviour
{
    public static List<AtmosphericIlluminationAffector> allAffectors = new List<AtmosphericIlluminationAffector>();

    public Color color;

    private void OnEnable()
    {
        allAffectors.Add(this);
    }

    private void OnDisable()
    {
        allAffectors.Remove(this);
    }
}
