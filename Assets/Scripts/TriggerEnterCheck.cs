using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TriggerEnterCheck : MonoBehaviour
{
    private void OnTriggerStay(Collider other)
    {
        if (other.gameObject.layer == Constants.Layers.Player)
        {
            if(GameplayManager.instance)
                GameplayManager.instance.EndColliderTrigger(true);
            
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.gameObject.layer == Constants.Layers.Player)
        {
            if(GameplayManager.instance)
                GameplayManager.instance.EndColliderTrigger(false);
            
        }
    }
}
