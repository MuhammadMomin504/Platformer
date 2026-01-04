using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    #region Exposed Variables...

    [SerializeField] private float movementSpeed = 5f;
    [SerializeField] private Transform playerMesh = default;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float jumpHeight = 5f;
    [SerializeField] private float gravityModifier = 1.5f;
    #endregion


    #region Private Variables...
    private AnimationController myAnimationController = default;
    private bool leftInput = false;
    private bool rightInput = false;
    private bool jumpInput = false;
    private Vector3 wantedPosition = default;
    private float currentMovementSpeed = 0f;
    private RaycastHit hit;
    private RaycastHit groundHit;
    private bool isPushing = false;
    private bool shouldInFrontRotation = false;
    private Vector3 startPosition = default;
    private bool isJumping = false;
    private Vector3 visualOffsetWhenPushing = new Vector3(0.5f, 0f, 0f);
    private bool isOnBox = false;
    private bool inAir = false;
    private float jumpVelocity = 0f;
    //Vector3 verticalVelocity;
    private float currentY = 0f;
    Vector3 verticalMove = new Vector3(0f, 0f, 0f);
    private float lastJumpTime = 0f;
    
    
    #endregion

    #region Getters...
    
    public float CurrentMovementSpeed => currentMovementSpeed;
    
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
        if(shouldInFrontRotation)
            transform.GetChild(0).transform.rotation = Quaternion.Euler(0f, Mathf.MoveTowards(transform.GetChild(0).transform.localEulerAngles.y, 180f, Time.deltaTime * 100f), 0f);
        
        if (GameplayManager.instance)
        {
            if(GameplayManager.instance.GameState != GameplayManager.GameStates.Play)
                return;
        }
        
        leftInput = InputManager.left;
        rightInput = InputManager.right;
        jumpInput = InputManager.jump;
        
        if (rightInput)
        {
            wantedPosition.x = 1f;
            FlipDirection(true);

        }
        if (leftInput)
        {
            wantedPosition.x = -1f;
            FlipDirection(false);

        }
        
        if (!isPushing)
        {
            playerMesh.localPosition = Vector3.Lerp(playerMesh.localPosition, Vector3.zero, Time.deltaTime * 10f);;
        }
        
        SetAnimation();
        
        //myAnimationController.transform.localRotation = Quaternion.Euler(Vector3.zero);
    }

    private void FixedUpdate()
    {
        if (GameplayManager.instance)
        {
            if(GameplayManager.instance.GameState != GameplayManager.GameStates.Play)
                return;
        }
        
        ThrowRayCast();
        
        int groundLayer = 1 << Constants.Layers.Ground;
        int layer = 1 << Constants.Layers.Default | 1 << Constants.Layers.Pushable;
        
        Vector3 origin = transform.position + new Vector3(0f, 0.1f, 0f);
        Vector3 direction = Vector3.down; // X direction (right)
        float rayDistance = 0.5f;
        
        
        //Debug.DrawRay(origin, direction * rayDistance, Color.red);
        //
        if (Physics.Raycast(origin, direction, out RaycastHit hit, rayDistance, layer))
        {
            float Ydistance = MathF.Abs(transform.position.y - hit.point.y);
            if (Ydistance < 0.2f)
            {
                isOnBox = true;
                inAir = false;
                isJumping = false;
            }
            
        }
        else
            isOnBox = false;
        
        if (Physics.Raycast(origin, direction, out RaycastHit groundHit, 0.5f, groundLayer))
        {

            if (jumpVelocity < 0)
            {
                jumpVelocity = 0f;
                inAir = false;
                isOnBox = false;
                isJumping = false;
            }
            
        }
        else
        {
            if(!isOnBox)
                inAir = true;
        }
        
        if (!inAir )
        {
            jumpVelocity = 0f;
        }

        if (jumpInput)
        {
            if (!isJumping && !inAir)
            {
                if (lastJumpTime > 0.1f)
                {
                    jumpVelocity = Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight * gravityModifier);
                    isJumping = true;
                    inAir = true;
                    wantedPosition.y = jumpVelocity;
                    startPosition = transform.position;
                    verticalMove = Vector3.zero;
                    isOnBox = false;
                }
                
            }
        }
        
        if (inAir || isJumping)
        {
            jumpVelocity += Physics.gravity.y * gravityModifier * Time.deltaTime;
            jumpVelocity = Mathf.Clamp(jumpVelocity, jumpVelocity, 12.5f);
            lastJumpTime = 0f;
            currentY =  Vector3.up.y * jumpVelocity * Time.deltaTime + (0.5f * Physics.gravity.y * Time.deltaTime * Time.deltaTime);
            verticalMove.y = currentY;
        }
        else
        {
            //verticalVelocity = Vector3.zero;
            verticalMove = Vector3.zero;
            jumpVelocity = 0f;
            lastJumpTime += Time.deltaTime;
        }
        
        if (wantedPosition != Vector3.zero)
        {
            currentMovementSpeed = Mathf.MoveTowards(currentMovementSpeed, movementSpeed, Time.deltaTime * 30f);
        }
        else
        {
            currentMovementSpeed = 0f;
        }
        
        if (!rightInput && !leftInput)
        {
            wantedPosition = Vector3.zero;
        }
        
        Vector3 horizontalDir = new Vector3(wantedPosition.x, 0f, 0f);
        Vector3 horizontalMove = horizontalDir * currentMovementSpeed * Time.deltaTime;
        
        transform.position += horizontalMove + (verticalMove);
    }
    
    private void ThrowRayCast()
    {
        int layer = 1 << Constants.Layers.Pushable;
        
        float offsetX = wantedPosition.x > 0 ? 0.1f : -0.1f;

        // Vector3 origin = transform.position + new Vector3(0f, 1f, 0f);
        Vector3 origin = transform.position + new Vector3(0.0f, 0.2f, 0f);
        Vector3 direction = Vector3.right * wantedPosition.x; // X direction (right)
        float rayDistance = 0.4f;

        //Debug.DrawRay(origin, direction * rayDistance, Color.red);

        // Perform the raycast
        if (Physics.Raycast(origin, direction, out RaycastHit hit, rayDistance, layer))
        {
            Vector3 targetVisualOffset = visualOffsetWhenPushing;
            targetVisualOffset.x *= -wantedPosition.x;
            playerMesh.localPosition = Vector3.Lerp(playerMesh.localPosition, targetVisualOffset, Time.deltaTime * 10f);;
            
            isPushing = true;
            //Debug.DrawRay(origin, direction * rayDistance, Color.green);
            
            //Check if the hit object has a Rigidbody
            Rigidbody rb = hit.collider.GetComponent<Rigidbody>();
            if (rb != null)
            {
                // Apply a push force along the X-axis
                rb.AddForce(Vector3.right * (wantedPosition.x * 12f), ForceMode.Force);
            }
        }
        else
        {
            isPushing = false;
        }
    }

    private void SetAnimation()
    {
        myAnimationController.DisableAllBools();
        
        if(currentMovementSpeed > 0f)
            myAnimationController.PlayWalk(true);
        else
        {
            myAnimationController.PlayIdle(true);
        }
        
        if(isPushing && currentMovementSpeed > 0f)
            myAnimationController.PlayPush(true);
    }

    private void FlipDirection(bool isTurnRight)
    {
        if(isTurnRight)
            playerMesh.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        else
            playerMesh.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
    }
    
    public void PlayDanceAnimation()
    {
        myAnimationController.PlayDance(true);
        shouldInFrontRotation = true;
    }
    
    
}
