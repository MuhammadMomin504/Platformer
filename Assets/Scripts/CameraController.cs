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
    
    
    
    public float rotationSpeed = 0.2f; // Speed of the camera rotation

    private float rotationX = 0f;
    private float rotationY = 0f;
    
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
