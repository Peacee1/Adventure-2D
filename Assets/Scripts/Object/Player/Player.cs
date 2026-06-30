using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class Player : BaseObject
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Dash Settings")]
    [SerializeField] private float dashSpeed = 15f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCooldown = 1f;

    private Rigidbody2D rb;
    private Vector2 moveInput;
    private bool isDashing = false;
    private bool isAttacking = false;
    private float lastDashTime = -999f;
    private Vector3 dashDirection;
    private float lastLogTime = -999f;
    private Vector2 rbVelocity;

    [Header("Attack Settings")]
    [SerializeField] private float attackDuration = 0.4f;

    [Header("Rotation Settings")]
    [SerializeField] private float rotationSpeed = 360f; // degrees per second
    private GameObject colliderObject;
    private GameObject dashTrailObject;
    private Animator animatorComponent;

    protected override void Awake()
    {
        base.Awake();

        // Safely destroy the 3D CharacterController to avoid component warning and logic conflict
        CharacterController oldController = GetComponent<CharacterController>();
        if (oldController != null)
        {
            Destroy(oldController);
        }

        // Initialize Rigidbody2D for top-down 2D movement
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
        }

        if (rb != null)
        {
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
        else
        {
            Debug.LogError("[Player] Failed to get or add Rigidbody2D component!");
        }

        // Automatically create a non-trigger BoxCollider2D on the root for solid physical wall collisions
        BoxCollider2D physicalCollider = gameObject.AddComponent<BoxCollider2D>();
        physicalCollider.isTrigger = false;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            physicalCollider.size = spriteRenderer.sprite.bounds.size;
        }

        // Set default player stats
        if (maxHp <= 0) maxHp = 100;
        if (hp <= 0) hp = maxHp;
        if (maxMp <= 0) maxMp = 50;
        if (mp <= 0) mp = maxMp;

        // Ensure Player sprite renders in front of ground tilemap (sorting layer/order)
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = 5;
        }

        // Dynamically ensure CameraFollow is on Main Camera
        EnsureCameraFollow();
    }

    private void EnsureCameraFollow()
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            if (mainCam.GetComponent<Freeland.Gameplay.CameraFollow>() == null)
            {
                mainCam.gameObject.AddComponent<Freeland.Gameplay.CameraFollow>();
                Debug.Log("[Player] Dynamically attached CameraFollow to Main Camera.");
            }
        }
    }

    private void Start()
    {
        FindBoxColliderObject();
        FindDashTrailObject();
        if (colliderObject != null)
        {
            colliderObject.transform.localRotation = Quaternion.identity;
            animatorComponent = colliderObject.GetComponent<Animator>();
        }
    }

    private void FindDashTrailObject()
    {
        // 1. Try to find the child 0 of Square GameObject directly
        Transform squareTransform = transform.Find("Square");
        if (squareTransform != null)
        {
            if (squareTransform.childCount > 0)
            {
                dashTrailObject = squareTransform.GetChild(0).gameObject;
                Debug.Log($"[Player] Found dash trail: {dashTrailObject.name} as child of Square.");
            }
        }

        // 2. Fall back to child 0 of colliderObject if not found yet
        if (dashTrailObject == null && colliderObject != null && colliderObject.transform.childCount > 0)
        {
            dashTrailObject = colliderObject.transform.GetChild(0).gameObject;
            Debug.Log($"[Player] Found dash trail: {dashTrailObject.name} as child of colliderObject.");
        }

        // 3. Set the trail object to inactive initially
        if (dashTrailObject != null)
        {
            dashTrailObject.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[Player] No dash trail object found (child 0 of Square or child 0 of colliderObject)!");
        }
    }

    private void FindBoxColliderObject()
    {
        // 1. Search in children first (excluding this GameObject)
        BoxCollider2D[] allBox2D = GetComponentsInChildren<BoxCollider2D>();
        foreach (var col in allBox2D)
        {
            if (col.gameObject != gameObject)
            {
                colliderObject = col.gameObject;
                Debug.Log($"[Player] Found BoxCollider2D on child GameObject: {colliderObject.name}");
                return;
            }
        }

        BoxCollider[] allBox3D = GetComponentsInChildren<BoxCollider>();
        foreach (var col in allBox3D)
        {
            if (col.gameObject != gameObject)
            {
                colliderObject = col.gameObject;
                Debug.Log($"[Player] Found BoxCollider on child GameObject: {colliderObject.name}");
                return;
            }
        }

        // 2. If not found in children, search on this GameObject itself
        BoxCollider2D selfBox2D = GetComponent<BoxCollider2D>();
        if (selfBox2D != null)
        {
            colliderObject = selfBox2D.gameObject;
            Debug.Log($"[Player] Found BoxCollider2D on root GameObject: {colliderObject.name}");
            return;
        }

        BoxCollider selfBox3D = GetComponent<BoxCollider>();
        if (selfBox3D != null)
        {
            colliderObject = selfBox3D.gameObject;
            Debug.Log($"[Player] Found BoxCollider on root GameObject: {colliderObject.name}");
            return;
        }
        
        Debug.LogWarning("[Player] No BoxCollider or BoxCollider2D found in children or root GameObject!");
    }

    private void Update()
    {
        HandleInput();

        if (isDashing)
        {
            rbVelocity = (Vector2)dashDirection * dashSpeed;
        }
        else if (isAttacking)
        {
            // Khóa di chuyển trong lúc attack
            rbVelocity = Vector2.zero;
        }
        else
        {
            rbVelocity = moveInput.normalized * moveSpeed;
        }

        // Update animator speed based on movement status instead of rotating the collider
        if (colliderObject != null)
        {
            if (animatorComponent == null)
            {
                animatorComponent = colliderObject.GetComponent<Animator>();
            }

            if (animatorComponent != null)
            {
                bool isMoving = rbVelocity.sqrMagnitude > 0.01f;
                animatorComponent.SetFloat("speed", isMoving ? 1f : 0f);
            }
        }

        // Rotate character based on left/right movement direction
        if (rbVelocity.x < -0.01f)
        {
            transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            if (nameTextComponent != null)
            {
                nameTextComponent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
        }
        else if (rbVelocity.x > 0.01f)
        {
            transform.localRotation = Quaternion.identity;
            if (nameTextComponent != null)
            {
                nameTextComponent.transform.localRotation = Quaternion.identity;
            }
        }

        // Log diagnostics every 2 seconds to debug visibility
        if (Time.time > lastLogTime + 2f)
        {
            lastLogTime = Time.time;
            Camera cam = Camera.main;
            string camInfo = cam != null ? $"Pos: {cam.transform.position}, Ortho: {cam.orthographic}, OrthoSize: {cam.orthographicSize}, CullingMask: {cam.cullingMask}" : "Null";
            string srInfo = spriteRenderer != null ? $"Enabled: {spriteRenderer.enabled}, Order: {spriteRenderer.sortingOrder}, Layer: {spriteRenderer.sortingLayerName}, Mat: {(spriteRenderer.sharedMaterial != null ? spriteRenderer.sharedMaterial.name : "Null")}, Sprite: {(spriteRenderer.sprite != null ? spriteRenderer.sprite.name : "Null")}" : "Null";
            Debug.Log($"[PlayerDebug] Player Pos: {transform.position}, Scale: {transform.localScale}, Active: {gameObject.activeInHierarchy}, SpriteRenderer: {srInfo} | Camera: {camInfo}");
        }
    }

    private void FixedUpdate()
    {
        if (rb != null)
        {
            rb.linearVelocity = rbVelocity;
        }
    }

    private void HandleInput()
    {
        // 1. Read WASD movement using the new Input System Keyboard polling
        moveInput = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) moveInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) moveInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) moveInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) moveInput.x += 1f;
        }

        // 2. Dash via Right Click using the new Input System Mouse polling
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame && !isAttacking)
        {
            TryDash();
        }

        // 3. Attack via Left Click using the new Input System Mouse polling
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            Attack();
        }
    }

    private void TryDash()
    {
        if (isDashing || Time.time < lastDashTime + dashCooldown) return;

        // Determine dash direction (use current move input, default to Vector3.right if stationary)
        Vector3 inputDir = new Vector3(moveInput.x, moveInput.y, 0f).normalized;
        if (inputDir == Vector3.zero)
        {
            dashDirection = Vector3.right;
        }
        else
        {
            dashDirection = inputDir;
        }

        StartCoroutine(PerformDash());
    }

    private IEnumerator PerformDash()
    {
        isDashing = true;
        lastDashTime = Time.time;

        if (dashTrailObject != null)
        {
            dashTrailObject.SetActive(true);
        }

        yield return new WaitForSeconds(dashDuration);

        isDashing = false;

        if (dashTrailObject != null)
        {
            dashTrailObject.SetActive(false);
        }
    }

    public virtual void Attack()
    {
        if (isAttacking) return;

        if (animatorComponent != null)
        {
            animatorComponent.SetTrigger("attack");
            Debug.Log("[Player] Attack triggered - animator trigger 'attack' set.");
        }
        else
        {
            Debug.LogWarning("[Player] Attack triggered but animatorComponent is null!");
        }

        StartCoroutine(AttackLockRoutine());
    }

    private IEnumerator AttackLockRoutine()
    {
        isAttacking = true;
        yield return new WaitForSeconds(attackDuration);
        isAttacking = false;
    }
}
