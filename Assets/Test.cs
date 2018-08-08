using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Chaos;

public class Test : MonoBehaviour
{
    public SkinnedMeshRenderer[] skins;
    public int Frame = 10;
	// Use this for initialization
	void Start () {
	    if (0 == skins.Length)
	    {
	        skins = GetComponentsInChildren<SkinnedMeshRenderer>();
	    }
    }
	
	// Update is called once per frame
	void Update () {
	    if (Frame-- == 0)
        {
            SkinnedMeshCombination.Combine(transform, skins);
        }
	}
}
