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
    [SerializeField] protected int atkPhysical;    // ATK Vật Lý
    [SerializeField] protected int atkMagic;       // ATK Phép
    [SerializeField] protected int defPhysical;    // DEF Vật Lý
    [SerializeField] protected int defMagic;       // DEF Phép

    [Header("Regeneration (/giây)")]
    [SerializeField] protected float hpRegen = 0f; // HP hồi mỗi giây
    [SerializeField] protected float mpRegen = 0f; // MP hồi mỗi giây

    [Header("Combat")]
    [SerializeField] protected float attackRange = 1.5f; // Tầm đánh (units)
    [SerializeField] protected float attackSpeed = 1f;   // Giây để hoàn thành 1 đòn (thấp = nhanh hơn)
    [SerializeField] protected float lifeSteal   = 0f;   // Hút máu (0–1, ví dụ 0.1 = 10%)

    [SerializeField] protected string objectName; // Tên của object
    [SerializeField] protected int objectId;      // ID của object

    [Header("UI Offsets Config")]
    [Tooltip("Height of the name tag text above the character pivot.")]
    [SerializeField] protected float nameYOffset = 4f;

    [Tooltip("Height of the health bar above the character pivot.")]
    [SerializeField] protected float hpBarYOffset = 5f;

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

        // 1. Default to "null" if name is empty or not set
        if (string.IsNullOrEmpty(objectName))
        {
            objectName = "null";
        }

        // 2. Dynamically instantiate the Name Tag text above the character
        GameObject nameTagGo = new GameObject("NameTag", typeof(RectTransform), typeof(TMPro.TextMeshPro));
        nameTagGo.transform.SetParent(transform, false);
        nameTagGo.transform.localPosition = new Vector3(0f, nameYOffset, 0f);

        // 3. Configure the TextMeshPro name tag
        nameTextComponent = nameTagGo.GetComponent<TMPro.TextMeshPro>();
        nameTextComponent.fontSize = 6.75f; // Configured size
        nameTextComponent.alignment = TextAlignmentOptions.Center;
        nameTextComponent.color = Color.white;
        nameTextComponent.outlineWidth = 0.2f;
        nameTextComponent.outlineColor = Color.black;

        // Load custom font from Resources
        var fontAsset = Resources.Load<TMPro.TMP_FontAsset>("Font/antiquity-print SDF");
        if (fontAsset != null)
            nameTextComponent.font = fontAsset;
        else
            Debug.LogWarning("[BaseObject] Could not load Font/antiquity-print SDF for name tag.");

        // Set sorting layer/order so name tag renders in front of character sprite
        var textRenderer = nameTagGo.GetComponent<MeshRenderer>();
        if (spriteRenderer != null && textRenderer != null)
        {
            textRenderer.sortingLayerID = spriteRenderer.sortingLayerID;
            textRenderer.sortingOrder   = spriteRenderer.sortingOrder + 10;
        }

        // Keep name tag oriented correctly (no mirroring/flipping)
        nameTagGo.AddComponent<NameTagController>();

        UpdateNameText();

        // Load and instantiate the HPBarCanvas prefab from Resources
        GameObject hpBarPrefab = Resources.Load<GameObject>("Prefab/HPBarCanvas");
        if (hpBarPrefab != null)
        {
            GameObject hpBarInstance = Instantiate(hpBarPrefab, transform);
            hpBarInstance.name = "HPBarCanvas_Instance";
            hpBarInstance.transform.localPosition = new Vector3(0f, hpBarYOffset, 0f); // Use configurable height offset

            // Inject this BaseObject into the HealthBarUI component attached to the prefab
            HealthBarUI hpBarUI = hpBarInstance.GetComponent<HealthBarUI>();
            if (hpBarUI != null)
            {
                hpBarUI.SetTarget(this);
            }

            // Prevent HP bar flipping/mirroring and lock rotation in world space
            hpBarInstance.AddComponent<NameTagController>();
            hpBarInstance.AddComponent<LockRotation>();
        }
        else
        {
            Debug.LogWarning("[BaseObject] Could not load Prefab/HPBarCanvas from Resources.");
        }

        StartCoroutine(RegenCoroutine());
    }

    /// <summary>
    /// Coroutine tự động hồi HP và MP mỗi giây khi còn sống.
    /// </summary>
    private IEnumerator RegenCoroutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            if (hp <= 0) continue; // Không hồi khi đã chết

            if (hpRegen > 0f && hp < maxHp)
            {
                hp = Mathf.Min(maxHp, Mathf.RoundToInt(hp + hpRegen));
            }

            if (mpRegen > 0f && mp < maxMp)
            {
                mp = Mathf.Min(maxMp, Mathf.RoundToInt(mp + mpRegen));
            }
        }
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
    
    public virtual int ATKPhysical
    {
        get => atkPhysical;
        set => atkPhysical = Mathf.Max(0, value);
    }

    public virtual int ATKMagic
    {
        get => atkMagic;
        set => atkMagic = Mathf.Max(0, value);
    }

    public virtual int DEFPhysical
    {
        get => defPhysical;
        set => defPhysical = Mathf.Max(0, value);
    }

    public virtual int DEFMagic
    {
        get => defMagic;
        set => defMagic = Mathf.Max(0, value);
    }

    public virtual int ObjectId 
    { 
        get => objectId; 
        set => objectId = value; 
    }

    public virtual float HPRegen
    {
        get => hpRegen;
        set => hpRegen = Mathf.Max(0f, value);
    }

    public virtual float MPRegen
    {
        get => mpRegen;
        set => mpRegen = Mathf.Max(0f, value);
    }

    /// <summary>Tầm đánh – khoảng cách tối đa để đòn đánh chạm mục tiêu.</summary>
    public virtual float AttackRange
    {
        get => attackRange;
        set => attackRange = Mathf.Max(0.1f, value);
    }

    /// <summary>Tốc độ đánh – giây cần để hoàn thành 1 đòn (thấp hơn = nhanh hơn).</summary>
    public virtual float AttackSpeed
    {
        get => attackSpeed;
        set => attackSpeed = Mathf.Max(0.05f, value);
    }

    /// <summary>Hút máu – tỉ lệ sát thương chuyển thành HP (0 = không hút, 1 = hút 100%).</summary>
    public virtual float LifeSteal
    {
        get => lifeSteal;
        set => lifeSteal = Mathf.Clamp01(value);
    }

    /// <summary>
    /// Tốc độ di chuyển – override trong subclass nếu cần.
    /// BuffSystem đọc/ghi qua property này.
    /// </summary>
    public virtual float MoveSpeed
    {
        get => 0f;
        set { }  // Override trong Player để ghi vào moveSpeed
    }

    /// <summary>
    /// Hệ số nhân hiệu quả hồi máu – override trong subclass.
    /// BuffSystem đọc/ghi qua property này.
    /// </summary>
    public virtual float HealingPower
    {
        get => 0f;
        set { }  // Override trong Player để ghi vào healingPower
    }

    /// <summary>
    /// Nhận sát thương vật lý. Công thức: Dame nhận = Dame - DEF Vật Lý * 1.5 (tối thiểu 1).
    /// </summary>
    public virtual void TakePhysicalDamage(int rawDamage)
    {
        int actualDamage = Mathf.Max(1, rawDamage - Mathf.RoundToInt(defPhysical * 1.5f));
        Debug.Log($"[{gameObject.name}] Nhận dame VẬT LÝ: {rawDamage} - DEF({defPhysical})*1.5 = {actualDamage}");
        hp -= actualDamage;
        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Nhận sát thương phép. Công thức: Dame nhận = Dame - DEF Phép * 1.5 (tối thiểu 1).
    /// </summary>
    public virtual void TakeMagicDamage(int rawDamage)
    {
        int actualDamage = Mathf.Max(1, rawDamage - Mathf.RoundToInt(defMagic * 1.5f));
        Debug.Log($"[{gameObject.name}] Nhận dame PHÉP: {rawDamage} - DEF({defMagic})*1.5 = {actualDamage}");
        hp -= actualDamage;
        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Nhận sát thương theo loại (DamageType). Tự động chọn DEF phù hợp.
    /// </summary>
    public virtual void TakeDamage(int rawDamage, DamageType damageType)
    {
        if (damageType == DamageType.Physical)
            TakePhysicalDamage(rawDamage);
        else
            TakeMagicDamage(rawDamage);
    }

    /// <summary>
    /// Nhận sát thương thuần (không áp dụng DEF) – dùng cho True Damage, rơi xuống hố, v.v.
    /// </summary>
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
        int before = hp;
        HP += amount; // Sử dụng property HP để tự động clamp
        int healed = hp - before;

        // TODO: Phát event khi có hệ thống UI / healing number
        // OnHealed?.Invoke(healed);
    }

    /// <summary>
    /// Hook gọi khi đối tượng NÀY gây sát thương cho target.
    /// Dùng để kích hoạt LifeSteal, Advancement effects, v.v.
    ///
    /// TODO: Gọi hàm này từ Attack system sau khi tính damage thực tế.
    /// Ví dụ: attacker.OnDealtDamage(actualDamage, target);
    /// </summary>
    /// <param name="actualDamage">Sát thương thực tế đã trừ DEF (không phải raw).</param>
    /// <param name="target">Mục tiêu nhận dame.</param>
    public virtual void OnDealtDamage(int actualDamage, BaseObject target)
    {
        // Hút máu (LifeSteal) – chạy ngay khi có chỉ số
        if (lifeSteal > 0f)
        {
            int lifeStealHeal = Mathf.RoundToInt(actualDamage * lifeSteal);
            Heal(lifeStealHeal);
            // TODO: Hiện số hồi máu floating text
            // FloatingTextManager.Show(gameObject, $"+{lifeStealHeal}", Color.green);
        }

        // TODO: Gọi advancement system's OnAttackHit nếu object là Player
        // GetComponent<AdvancementSystem>()?.OnAttackHit(target, actualDamage);
        // GetComponent<ArcherAdvancementSystem>()?.OnAttackHit(target, actualDamage);
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
