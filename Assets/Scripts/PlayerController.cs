using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Exposed Variables...
    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private Transform playerMesh = default;
    #endregion


    #region Private Variables...
    private AnimationController myAnimationController = default;
    private bool leftInput = false;
    private bool rightInput = false;
    private Vector3 myWantedPosition = default;
    private float currentMovementSpeed = 0f;
    
    #endregion

    #region Getters...
    
    #endregion

    private void Awake()
    {
        myAnimationController = GetComponent<AnimationController>();
    }

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        leftInput = InputManager.left;
        rightInput = InputManager.right;
        
        if (rightInput)
        {
            myWantedPosition.x = 1f;
            FlipDirection(true);

        }
        if (leftInput)
        {
            myWantedPosition.x = -1f;
            FlipDirection(false);

        }
        
        if (myWantedPosition != Vector3.zero)
        {
            currentMovementSpeed = Mathf.MoveTowards(currentMovementSpeed, movementSpeed, Time.deltaTime * 30f);
        }
        else
        {
            currentMovementSpeed = 0f;
        }
        
        if (!rightInput && !leftInput)
        {
            myWantedPosition = Vector3.zero;
        }
        
        transform.position = Vector3.MoveTowards(transform.position, transform.position + myWantedPosition, Time.deltaTime * currentMovementSpeed);
        SetAnimation();

        //myAnimationController.transform.localRotation = Quaternion.Euler(Vector3.zero);
    }

    private void SetAnimation()
    {
        myAnimationController.DisableAllBools();
        if (Input.GetKeyUp(KeyCode.P))
        {
            myAnimationController.PlayDance(true);
        }
        else if(currentMovementSpeed > 0f)
            myAnimationController.PlayWalk(true);
        else
        {
            myAnimationController.PlayIdle(true);
        }
    }

    private void FlipDirection(bool isTurnRight)
    {
        if(isTurnRight)
            playerMesh.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        else
            playerMesh.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
    }
    
    
}
