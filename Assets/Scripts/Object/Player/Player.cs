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
    private float lastDashTime = -999f;
    private Vector3 dashDirection;
    private float lastLogTime = -999f;
    private Vector2 rbVelocity;

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
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

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

    private void Update()
    {
        HandleInput();

        if (isDashing)
        {
            rbVelocity = (Vector2)dashDirection * dashSpeed;
        }
        else
        {
            rbVelocity = moveInput.normalized * moveSpeed;
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
        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
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

        yield return new WaitForSeconds(dashDuration);

        isDashing = false;
    }

    public virtual void Attack()
    {
        Debug.Log("[Player] Attack triggered (method is currently empty).");
        // Hàm attack tạm thời bỏ trống theo yêu cầu
    }
}
