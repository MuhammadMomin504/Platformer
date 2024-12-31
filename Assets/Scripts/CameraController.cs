using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    public static CameraController instance;
    public Transform cameraTransform;
    public float shakeDuration = 0.5f;
    public float shakeMagnitude = 0.2f;
    public float dampingSpeed = 1.0f;

    private Vector3 initialPosition;
    private float shakeTime;
    
    private void Awake()
    {
        if(!instance)
            instance = this;
    }
    
    // Start is called before the first frame update
    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        initialPosition = cameraTransform.localPosition;
    }

    // Update is called once per frame
    void Update()
    {
        if (shakeTime > 0)
        {
            float xOffset = Mathf.PerlinNoise(Time.time * 20, 0) * 2 - 1; // Smooth random values
            float yOffset = Mathf.PerlinNoise(0, Time.time * 20) * 2 - 1;

            cameraTransform.localPosition = initialPosition + new Vector3(xOffset, yOffset, 0) * shakeMagnitude;

            shakeTime -= Time.deltaTime * dampingSpeed;

            if (shakeTime <= 0)
            {
                shakeTime = 0;
                cameraTransform.localPosition = initialPosition;
            }
        }
    }
    
    public void TriggerShake()
    {
        shakeTime = shakeDuration;
    }
    
    
}
