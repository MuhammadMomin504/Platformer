using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FallingDigit : MonoBehaviour
{

    private Rigidbody rb = default;
    private bool shouldInceaseVelocity = false;
    public ParticleSystem[] particleSystems = default;


    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyUp(KeyCode.L))
        {
            rb.isKinematic = false;
            shouldInceaseVelocity = true;
            rb.velocity = Vector3.down * 5f;
        }

        if (shouldInceaseVelocity)
        {
            Vector3 currentVelocity = rb.velocity;
            currentVelocity.y -= 0.5f;
            rb.velocity = currentVelocity;
        }
        
        
    }

    private void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.layer == Constants.Layers.Ground)
        {
            CameraController.instance.TriggerShake();

            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Play();
            }
            
        }
    }
}
