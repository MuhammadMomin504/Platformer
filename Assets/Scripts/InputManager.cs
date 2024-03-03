using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    public static bool left = false;
    public static bool right = false;
    public static bool backward = false;
    public static bool forward = false;
    public static bool mouseClicked = false;
    
    
    private bool leftButton = false;
    private bool rightButton = false;
    private bool backwardButton = false;
    private bool forwardButton = false;
    private bool mouseButton = false;


    private float leftFloat = 0f;
    private float rightFloat = 0f;
    private float backwardFloat = 0f;
    private float forwardFloat = 0f;
    private float mouseClickedFloat = 0f;
    
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space))
        {
            mouseButton = true;
            mouseClickedFloat = 0.1f;
        }
        
        if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) )
        {
            rightButton = true;
        }
        if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) )
        {
            leftButton = true;
        }
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) )
        {
            forwardButton = true;
        }
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S) )
        {
            backwardButton = true;
        }

        if (Input.GetKeyUp(KeyCode.RightArrow) || Input.GetKeyUp(KeyCode.D))
        {
            rightButton = false;
        }
        if (Input.GetKeyUp(KeyCode.LeftArrow) || Input.GetKeyUp(KeyCode.A))
        {
            leftButton = false;
        }
        if (Input.GetKeyUp(KeyCode.UpArrow) || Input.GetKeyUp(KeyCode.W))
        {
            forwardButton = false;
        }
        if (Input.GetKeyUp(KeyCode.DownArrow) || Input.GetKeyUp(KeyCode.S))
        {
            backwardButton = false;
        }
        
        if (Input.GetMouseButtonUp(0) || Input.GetKeyUp(KeyCode.Space))
        {
            mouseButton = false;
        }
        
        if (leftButton)
            leftFloat = 1.0f;
        if (rightButton)
            rightFloat = 1.0f;
        if (backwardButton)
            backwardFloat = 1.0f;
        if (forwardButton)
            forwardFloat = 1.0f;
        if (mouseButton)
            mouseButton = false;
        
        left = leftFloat > 0.05f;
        right = rightFloat > 0.05f;
        backward = backwardFloat > 0.05f;
        forward = forwardFloat > 0.05f;
        mouseClicked = mouseClickedFloat > 0.05f;
    }
    
    void LateUpdate()
    {
        //Reset values to zero
        leftFloat = Mathf.MoveTowards(leftFloat, 0.0f, Time.deltaTime * 25.0f);
        rightFloat = Mathf.MoveTowards(rightFloat, 0.0f, Time.deltaTime * 25.0f);
        backwardFloat = Mathf.MoveTowards(backwardFloat, 0.0f, Time.deltaTime * 25.0f);
        forwardFloat = Mathf.MoveTowards(forwardFloat, 0.0f, Time.deltaTime * 25.0f);
        mouseClickedFloat = Mathf.MoveTowards(mouseClickedFloat, 0.0f, Time.deltaTime * 25.0f);

        if (!Input.anyKey)
        {
            leftButton = false;
            rightButton = false;
            forwardButton = false;
            backwardButton = false;
            mouseButton = false;
        }
       
    }
}
