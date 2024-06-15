using UnityEngine;
using UnityEngine.InputSystem;
using FishNet;
using FishNet.Object;


[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : NetworkBehaviour
{
    [Header("Camera")] public Transform playerRoot;
    public Transform playerCam;

    public float cameraSensitivity;
    private float rotX;
    private float rotY;

    [Header("Movement")] public CharacterController controller;
    public float speed;
    public float jumpHeight;
    public float gravity;
    public bool isGrounded;
    private Vector3 velocity;

    //Input System
    [Header("Input")] public InputAction move;
    public InputAction jump;
    public InputAction mouseX;
    public InputAction mouseY;

    //Block
    public GameObject blockPrefab;
    public float maxPlaceDistance = 10f;
    public float maxRemoveDistance = 10f;
    public float gridSize = 0.75f;

    //Highlighting
    public Material highlightMaterial;
    private GameObject highlightedBlock;


    [Header("Benchmark Config")]
    public bool enable_RPC_benchmark = false;

    public int x = 0;
    public int z = 0;

    private Vector3 targetCoordinates ;
    private bool is_placed = false;

    public float tick_interval_time = 0.5f; 
    private float next_Tick = 0;        





    void OnEnable()
    {
        move.Enable();
        jump.Enable();
        mouseX.Enable();
        mouseY.Enable();
        
        jump.performed += ctx => Jump();
    }

    void OnDisable()
    {
        move.Disable();
        jump.Disable();
        mouseX.Dispose();
        mouseY.Disable();
        
        jump.performed -= ctx => Jump();
    }
    

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!base.IsOwner)
        {
            // Ensure that the camera sees other players' bodies
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer.CompareTag("Player"))
                {
                    renderer.enabled = true;
                }
            }
            return;
        }
        #if !UNITY_EDITOR
        string[] args = System.Environment.GetCommandLineArgs();
        if (args.Length > 3){
            for(int i = 0; i < args.Length-3;i++)
            {
                if (args[i] == "-rpc_benchmark"){
                    enable_RPC_benchmark = true;
                    if (!int.TryParse(args[i+1],  out x) || !int.TryParse(args[i+2], out z)){
                        Debug.LogError("Unable to parse string.");    
                    }
                    if(!float.TryParse(args[i+3],  out tick_interval_time)){
                        Debug.LogError("Unable to parse string.");    
                    }
                }

            }
        }
        #endif
        

        // Disable rendering of the local player's body
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            if (renderer.CompareTag("Player"))
            {
                renderer.enabled = false;
            }
        }
    }
    void Start()
    {
        if(enable_RPC_benchmark){
            Debug.LogError("Starting Benchmark: terainmodification_benchmark: x:"+x+" z:"+z+"tick_interval_time "+ tick_interval_time);
            targetCoordinates = new Vector3(x, 7, z);
            next_Tick = Time.time + tick_interval_time;
        }
        Cursor.lockState = CursorLockMode.Locked;

        controller = GetComponent<CharacterController>();

    }


    void Update()
    {
        // Log player activity
        
        if (!base.IsOwner)
        {
            playerCam.GetComponent<Camera>().enabled = false;
            return;
        }
        
        if(enable_RPC_benchmark){
            benchmark_step();
        }
        
        HighlightBlock();
        HandleBlocks();
        HandleCamera();
        HandleMovement();
    }
     void benchmark_step(){
        if ( next_Tick <= Time.time)
        {
            if(is_placed)
            {
                remove_at_cord_RPC(targetCoordinates);
                Debug.Log("removed block");
                is_placed = false;
            }
            else
            {
                place_at_cord_RPC(targetCoordinates);
                Debug.Log("added block");
                is_placed = true;
            }
            next_Tick = Time.time + tick_interval_time;
        }
        
    }

    [ServerRpc]
    void remove_at_cord_RPC(Vector3 targetCoordinates)
    {
        Vector3 test = RoundToNearestGrid(targetCoordinates) + new Vector3(0,2,0);
        Ray ray = new Ray(test, Vector3.down); // Assuming the ray points downward
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit))
        { 
            Destroy(hit.collider.gameObject);
            InstanceFinder.ServerManager.Despawn(hit.collider.gameObject);
        }
    }

    [ServerRpc]
    void place_at_cord_RPC(Vector3 targetCoordinates)
    {
        Vector3 gridPosition = RoundToNearestGrid(targetCoordinates);
        GameObject newBlock = Instantiate(blockPrefab, gridPosition, Quaternion.identity);
        InstanceFinder.ServerManager.Spawn(newBlock);
    }

    void HandleCamera()
    {
        Vector2 mouseInput = new Vector2(mouseX.ReadValue<float>() * cameraSensitivity,
            mouseY.ReadValue<float>() * cameraSensitivity);
        rotX -= mouseInput.y;
        rotX = Mathf.Clamp(rotX, -90, 90);
        rotY += mouseInput.x;

        playerRoot.rotation = Quaternion.Euler(0f, rotY, 0f);
        playerCam.localRotation = Quaternion.Euler(rotX, 0f, 0f); 
    }

    void HandleBlocks()
    {
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            CmdPlaceBlock(playerCam.position, playerCam.forward);
        }
        else if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            CmdRemoveBlock(playerCam.position, playerCam.forward);
        }

        controller.Move(velocity * Time.deltaTime);
    }
    
    void HandleMovement()
    {
        Vector2 moveInput = move.ReadValue<Vector2>();
        Vector3 moveVelocity = playerRoot.forward * moveInput.y + playerRoot.right * moveInput.x;
        moveVelocity *= speed;

        if (controller.isGrounded)
        {
            velocity.y = -0.1f; // Reset velocity when grounded
            if (jump.triggered)
            {
                velocity.y = Mathf.Sqrt(2f * jumpHeight * gravity); // Jump
            }
        }
        else
        {
            velocity.y -= gravity * Time.deltaTime; // Apply gravity when jumping or falling
        }

        controller.Move((moveVelocity + velocity) * Time.deltaTime);
    }

    void Jump()
    {
        if (isGrounded)
        {
            velocity.y = Mathf.Sqrt(2f * jumpHeight * gravity);
        }
    }

    // Placing and removing blocks
    [ServerRpc]
    void CmdPlaceBlock(Vector3 cameraPosition, Vector3 cameraForward)
    {
        RaycastHit hit;
        int layerMask = ~(1 << LayerMask.NameToLayer("Player")); 

        if (Physics.Raycast(cameraPosition, cameraForward, out hit, maxPlaceDistance, layerMask))
        {
            if (hit.collider.CompareTag("Block") || hit.distance <= maxPlaceDistance)
            {
                Vector3 gridPosition = RoundToNearestGrid(hit.point + hit.normal * 0.5f);
                GameObject newBlock = Instantiate(blockPrefab, gridPosition, Quaternion.identity);
                InstanceFinder.ServerManager.Spawn(newBlock);
            }
        }
    }

    [ServerRpc]
    void CmdRemoveBlock(Vector3 cameraPosition, Vector3 cameraForward)
    {
        RaycastHit hit;
        int layerMask = ~(1 << LayerMask.NameToLayer("Player")); 

        if (Physics.Raycast(cameraPosition, cameraForward, out hit, maxPlaceDistance, layerMask))
        {
            if (hit.collider.CompareTag("Block") || hit.distance <= maxPlaceDistance) 
            {
                Destroy(hit.collider.gameObject);
                InstanceFinder.ServerManager.Despawn(hit.collider.gameObject);
            }
        }
    }

    bool IsPlayerInSpace(Vector3 position)
    {
        Collider[] playerColliders = playerRoot.GetComponentsInChildren<Collider>();

        // Adjust the height based on the player's size
        float playerHeight = 1.8f;

        foreach (Collider playerCollider in playerColliders)
        {
            Collider[] colliders = Physics.OverlapBox(position + new Vector3(0f, playerHeight / 2f, 0f), new Vector3(0.375f, playerHeight / 2f, 0.375f));

            foreach (Collider collider in colliders)
            {
                if (collider.CompareTag("Block"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    Vector3 RoundToNearestGrid(Vector3 position)
    {
        float x = Mathf.Round(position.x / gridSize) * gridSize;
        float y = Mathf.Round(position.y / gridSize) * gridSize;
        float z = Mathf.Round(position.z / gridSize) * gridSize;
        return new Vector3(x, y, z);
    }

    bool IsBlockAtPosition(Vector3 position)
    {
        Collider[] colliders = Physics.OverlapSphere(position, 0.35f);
        foreach (Collider collider in colliders)
        {
            if (collider.CompareTag("Block"))
            {
                return true;
            }
        }

        return false;
    }

    void HighlightBlock()
    {
        RaycastHit hit;
        int layerMask = ~(1 << LayerMask.NameToLayer("Player")); // Ignore the player layer

        Vector3 raycastDirection = playerCam.forward;
        Vector3 raycastDirectionDownward = Vector3.down;

        if (Physics.Raycast(playerCam.position, raycastDirection, out hit, maxPlaceDistance, layerMask) ||
            Physics.Raycast(playerCam.position, raycastDirectionDownward, out hit, maxPlaceDistance, layerMask))
        {
            GameObject hitBlock = hit.collider.gameObject;
            if (hitBlock.CompareTag("Block"))
            {
                Renderer blockRenderer = hitBlock.GetComponent<Renderer>();
                Material[] materials = blockRenderer.materials;

                if (materials.Length < 2)
                {
                    if (highlightedBlock != null && highlightedBlock != hitBlock)
                    {
                        RemoveHighlight(highlightedBlock);
                    }

                    if (highlightedBlock != hitBlock)
                    {
                        ApplyHighlight(hitBlock);
                        highlightedBlock = hitBlock;
                    }
                }
            }
        }
        else
        {
            if (highlightedBlock != null)
            {
                RemoveHighlight(highlightedBlock);
                highlightedBlock = null;
            }
        }
    }


    void ApplyHighlight(GameObject block)
    {
        Renderer blockRenderer = block.GetComponent<Renderer>();
        Material[] materials = blockRenderer.materials;
        Material[] newMaterials = new Material[materials.Length + 1];
        for (int i = 0; i < materials.Length; i++)
        {
            newMaterials[i] = materials[i];
        }

        newMaterials[materials.Length] = highlightMaterial;
        blockRenderer.materials = newMaterials;
    }

    void RemoveHighlight(GameObject block)
    {
        Renderer blockRenderer = block.GetComponent<Renderer>();
        Material[] materials = blockRenderer.materials;

        if (materials.Length > 1)
        {
            Material[] newMaterials = new Material[materials.Length - 1];
            for (int i = 0; i < newMaterials.Length; i++)
            {
                newMaterials[i] = materials[i];
            }

            blockRenderer.materials = newMaterials;
        }
    }
}