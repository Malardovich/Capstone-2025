using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Simplemente copia un transform y sus hijos en otro objeto igual.
// Opcionalmente genera un padre libre para mover/rotar arbitrariamente.
public class TransformCopier : MonoBehaviour
{
    public Transform source; // El origen. Probablemente el transform con el componente Avatar.
    public Transform destination; // El destino. Duplica el origen, desactiva el Avatar y asigna este nuevo transform aquí.

    private Dictionary<Transform, Transform> transforms = new Dictionary<Transform, Transform>();

    private void Start()
    {
        if (source.name == destination.name)
        {
            destination.name += "(dst)";
        }

        Transform[] all = source.GetComponentsInChildren<Transform>();
        Transform[] alld = destination.GetComponentsInChildren<Transform>();
        foreach (Transform t in all)
        {
            if (t.GetComponent<SkinnedMeshRenderer>() != null) continue;
            Transform match = null;
            foreach(Transform t1 in alld)
            {
                if (t.name == t1.name)
                {
                    match = t1;
                    break;
                }
            }
            transforms.Add(t, match);
        }
    }

    private void LateUpdate()
    {
        foreach(KeyValuePair<Transform, Transform> k in transforms)
        {
            if (k.Value == null) continue;
            k.Value.localPosition = k.Key.localPosition;
            k.Value.localRotation = k.Key.localRotation;
            k.Value.localScale = k.Key.localScale;
        }
    }
}