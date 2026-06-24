using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Base class cho tất cả các game object trong game.
/// Các class con (Enemy, Player, Item, etc.) sẽ kế thừa từ class này
/// để tự động có các thuộc tính HP, MP, Name và ID.
/// </summary>
public class BaseObject : MonoBehaviour
{
    [SerializeField] protected int maxHp;        // Maximum Health Points
    [SerializeField] protected int hp;           // Current Health Points
    [SerializeField] protected int maxMp;        // Maximum Mana Points
    [SerializeField] protected int mp;           // Current Mana Points
    [SerializeField] protected string objectName; // Tên của object
    [SerializeField] protected int objectId;      // ID của object

    protected SpriteRenderer spriteRenderer;
    protected Collider2D objectCollider;
    protected TMP_Text nameTextComponent;

    [Header("Hover Settings")]
    [SerializeField] protected Color outlineColor = new Color(1f, 1f, 1f, 0.8f);
    [SerializeField] [Range(0f, 5f)] protected float outlineSize = 1f;

    protected Material originalMaterial;
    protected Material outlineMaterial;

    public SpriteRenderer SpriteRenderer => spriteRenderer;
    public Collider2D ObjectCollider => objectCollider;
    public TMP_Text NameTextComponent => nameTextComponent;

    protected virtual void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (spriteRenderer == null)
        {
            Debug.LogError($"[BaseObject] SpriteRenderer is missing on {gameObject.name} or any of its children!");
        }
        else
        {
            originalMaterial = spriteRenderer.sharedMaterial;
            Shader outlineShader = Shader.Find("Custom/SpriteOutline");
            if (outlineShader != null)
            {
                outlineMaterial = new Material(outlineShader);
                outlineMaterial.SetColor("_OutlineColor", outlineColor);
                outlineMaterial.SetFloat("_OutlineSize", outlineSize);
            }
            else
            {
                Debug.LogWarning("[BaseObject] Custom/SpriteOutline shader not found!");
            }
        }

        objectCollider = GetComponent<Collider2D>();
        if (objectCollider == null)
        {
            objectCollider = GetComponentInChildren<Collider2D>();
        }

        if (objectCollider == null)
        {
            // Try to add a child object with BoxCollider2D to support hover and satisfy BaseObject requirements
            GameObject colliderHolder = new GameObject("Collider2D_Holder");
            colliderHolder.transform.SetParent(transform, false);
            BoxCollider2D boxCol = colliderHolder.AddComponent<BoxCollider2D>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                boxCol.size = spriteRenderer.sprite.bounds.size;
            }
            objectCollider = boxCol;
            Debug.Log($"[BaseObject] Automatically created 2D Collider child holder for {gameObject.name} with size {boxCol.size}");
        }

        objectCollider.isTrigger = true;

        nameTextComponent = GetComponentInChildren<TMP_Text>();
        UpdateNameText();
    }

    // Properties với modifier virtual để các class con có thể override nếu cần
    public virtual int MaxHP
    {
        get => maxHp;
        set => maxHp = value;
    }
    
    public virtual int HP 
    { 
        get => hp; 
        set => hp = Mathf.Clamp(value, 0, maxHp); // Giới hạn HP từ 0 đến maxHp
    }
    
    public virtual int MaxMP
    {
        get => maxMp;
        set => maxMp = value;
    }
    
    public virtual int MP 
    { 
        get => mp; 
        set => mp = Mathf.Clamp(value, 0, maxMp); // Giới hạn MP từ 0 đến maxMp
    }
    
    public virtual string ObjectName 
    { 
        get => objectName; 
        set 
        {
            objectName = value; 
            UpdateNameText();
        }
    }

    protected virtual void UpdateNameText()
    {
        if (nameTextComponent != null)
        {
            nameTextComponent.text = string.IsNullOrEmpty(objectName) ? gameObject.name : objectName;
        }
    }
    
    public virtual int ObjectId 
    { 
        get => objectId; 
        set => objectId = value; 
    }
    public virtual void TakeDamage(int damage)
    {
        hp -= damage;
        if (hp <= 0)
        {
            Die();
        }
    }
    public virtual void Die()
    {
        gameObject.SetActive(false);
    }
    public virtual void Revive()
    {
        hp = maxHp; // Hồi full HP khi revive
        mp = maxMp; // Hồi full MP khi revive
        gameObject.SetActive(true);
    }
    
    public virtual void Heal(int amount)
    {
        HP += amount; // Sử dụng property HP để tự động clamp
    }
    
    public virtual void RestoreMP(int amount)
    {
        MP += amount; // Sử dụng property MP để tự động clamp
    }
    public virtual void UseMP(int amount)
    {
        MP -= amount; // Sử dụng property MP để tự động clamp
    }
    public virtual void OnSelected()
    {  
    }
    
    public virtual void SetHover(bool isHovered)
    {
        Debug.Log($"[BaseObject] SetHover({isHovered}) called on {gameObject.name}. SpriteRenderer: {(spriteRenderer != null ? "OK" : "Null")}, OutlineMaterial: {(outlineMaterial != null ? "OK" : "Null")}");

        if (spriteRenderer == null || outlineMaterial == null) return;

        if (isHovered)
        {
            outlineMaterial.SetColor("_OutlineColor", outlineColor);
            outlineMaterial.SetFloat("_OutlineSize", outlineSize);
            spriteRenderer.material = outlineMaterial;
            Debug.Log($"[BaseObject] Applied outline material to {gameObject.name}");
        }
        else
        {
            spriteRenderer.material = originalMaterial;
            Debug.Log($"[BaseObject] Restored original material on {gameObject.name}");
        }
    }

    protected virtual void OnDestroy()
    {
        if (outlineMaterial != null)
        {
            Destroy(outlineMaterial);
        }
    }
}
