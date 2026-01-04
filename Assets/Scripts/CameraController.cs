using System;
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

    [Header("Camera Follow Settings")]
    public float followSpeed = 5f; // Speed at which camera follows the player

    private Vector3 initialPosition;
    private float shakeTime;
    
    
    
    public float rotationSpeed = 0.2f; // Speed of the camera rotation

    private float rotationX = 0f;
    private float rotationY = 0f;
    private Vector3 currentPosition = default;
    
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

        // Initialize current position to camera's starting position
        currentPosition = transform.position;
    }

    private void FixedUpdate()
    {
        if (GameplayManager.instance && GameplayManager.instance.PlayerTransform != null)
        {
            // Calculate target position (follow player on X axis, keep Y and Z fixed)
            Vector3 targetPosition = GameplayManager.instance.PlayerTransform.position;
            targetPosition.z = currentPosition.z; // Keep current Z position
            targetPosition.y = currentPosition.y; // Keep current Y position

            // Smoothly interpolate to target position
            currentPosition = Vector3.Lerp(currentPosition, targetPosition, followSpeed * Time.deltaTime);

            // Apply the smooth position to the camera
            transform.position = currentPosition;
        }
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
        
        
        
        // if (Input.GetMouseButton(0))
        // {
        //     float mouseX = Input.GetAxis("Mouse X");
        //     float mouseY = Input.GetAxis("Mouse Y");
        //
        //     rotationX += mouseX * rotationSpeed;
        //     rotationY -= mouseY * rotationSpeed;
        //     rotationY = Mathf.Clamp(rotationY, -90f, 90f); // Clamp the vertical rotation to avoid flipping
        // }
        //
        // // Check for touch input
        // if (Input.touchCount > 0)
        // {
        //     Touch touch = Input.GetTouch(0);
        //     if (touch.phase == TouchPhase.Moved)
        //     {
        //         rotationX += touch.deltaPosition.x * rotationSpeed * Time.deltaTime;
        //         rotationY -= touch.deltaPosition.y * rotationSpeed * Time.deltaTime;
        //         rotationY = Mathf.Clamp(rotationY, -90f, 90f); // Clamp the vertical rotation to avoid flipping
        //     }
        // }
        //
        // // Apply rotation to the camera
        // transform.eulerAngles = new Vector3(rotationY, rotationX, 0f);
    }
    
    public void TriggerShake()
    {
        shakeTime = shakeDuration;
    }
    
    
}
