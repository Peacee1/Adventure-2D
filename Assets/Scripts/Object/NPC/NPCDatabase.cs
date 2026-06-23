using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class NPCConfig
{
    public int id;
    public string name;
    public int maxHp;
    public int maxMp;
    public string avatarPath;

    /// <summary>
    /// Nạp Sprite từ avatarPath. Hỗ trợ cả sprite đơn hoặc sub-sprite cắt từ Sprite Sheet (Multiple Sprite).
    /// Ví dụ đường dẫn: "Tiny Swords (Free Pack)/Units/Blue Units/Monk/Idle/Idle_0"
    /// </summary>
    public Sprite GetAvatarSprite()
    {
        if (string.IsNullOrEmpty(avatarPath)) return null;

        // Tìm dấu gạch chéo cuối cùng để phân tách đường dẫn file ảnh và tên sub-sprite
        int lastSlash = avatarPath.LastIndexOf('/');
        if (lastSlash == -1)
        {
            // Trường hợp sprite đơn bình thường
            return TrimSprite(Resources.Load<Sprite>(avatarPath));
        }

        string sheetPath = avatarPath.Substring(0, lastSlash);
        string spriteName = avatarPath.Substring(lastSlash + 1);

        // Loại bỏ phần mở rộng ".png" nếu người dùng điền vào đường dẫn
        if (sheetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            sheetPath = sheetPath.Substring(0, sheetPath.Length - 4);
        }

        // Tải toàn bộ sprites từ Sprite Sheet
        Sprite[] sprites = Resources.LoadAll<Sprite>(sheetPath);
        foreach (Sprite sprite in sprites)
        {
            if (sprite.name.Equals(spriteName, StringComparison.OrdinalIgnoreCase))
            {
                return TrimSprite(sprite);
            }
        }

        // Nếu không tìm thấy trong Sprite Sheet, thử tải trực tiếp cả đường dẫn
        return TrimSprite(Resources.Load<Sprite>(avatarPath));
    }

    /// <summary>
    /// Tự động cắt bỏ các khoảng pixel trong suốt (transparent border) bao quanh Sprite 
    /// để hình ảnh hiển thị to và rõ nét nhất trên UI.
    /// Yêu cầu: Texture của Sprite phải được bật "Read/Write Enabled" trong Import Settings.
    /// </summary>
    private Sprite TrimSprite(Sprite sprite)
    {
        if (sprite == null) return null;

        try
        {
            Texture2D texture = sprite.texture;
            Rect rect = sprite.rect;
            int x = (int)rect.x;
            int y = (int)rect.y;
            int width = (int)rect.width;
            int height = (int)rect.height;

            // Đọc danh sách pixels của Sprite
            Color[] pixels = texture.GetPixels(x, y, width, height);

            int minX = width;
            int maxX = 0;
            int minY = height;
            int maxY = 0;

            // Tìm khung giới hạn của phần hình ảnh có màu (alpha > 0.05f)
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    if (pixels[dy * width + dx].a > 0.05f)
                    {
                        if (dx < minX) minX = dx;
                        if (dx > maxX) maxX = dx;
                        if (dy < minY) minY = dy;
                        if (dy > maxY) maxY = dy;
                    }
                }
            }

            // Nếu Sprite trống hoặc hoàn toàn trong suốt
            if (minX > maxX || minY > maxY)
            {
                return sprite;
            }

            int croppedWidth = maxX - minX + 1;
            int croppedHeight = maxY - minY + 1;

            // Tạo một Sprite mới với khung hình chữ nhật đã được cắt gọn
            Rect croppedRect = new Rect(x + minX, y + minY, croppedWidth, croppedHeight);
            Vector2 pivot = new Vector2(0.5f, 0.5f); // Tâm ở giữa
            return Sprite.Create(texture, croppedRect, pivot, sprite.pixelsPerUnit);
        }
        catch (Exception ex)
        {
            // Trả về sprite gốc nếu gặp lỗi cấu hình Read/Write của texture
            Debug.LogWarning($"[NPCDatabase] Không thể tự động cắt sprite '{sprite.name}': {ex.Message}. Hãy tích chọn 'Read/Write Enabled' trong phần cài đặt Import Settings của file ảnh.");
            return sprite;
        }
    }
}

[CreateAssetMenu(fileName = "NPCDatabase", menuName = "Database/NPC Database")]
public class NPCDatabase : ScriptableObject
{
    [SerializeField] private List<NPCConfig> npcs = new List<NPCConfig>();

    private Dictionary<int, NPCConfig> npcLookup;
    private static NPCDatabase instance;

    public static NPCDatabase Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<NPCDatabase>("NPCDatabase");
                if (instance != null)
                {
                    instance.InitializeLookup();
                }
                else
                {
                    Debug.LogError("[NPCDatabase] NPCDatabase asset not found in Resources! Make sure to create NPCDatabase.asset under a Resources folder.");
                }
            }
            return instance;
        }
    }

    private void OnEnable()
    {
        InitializeLookup();
    }

    public void InitializeLookup()
    {
        npcLookup = new Dictionary<int, NPCConfig>();
        if (npcs == null) return;

        foreach (var npc in npcs)
        {
            if (npc != null && !npcLookup.ContainsKey(npc.id))
            {
                npcLookup.Add(npc.id, npc);
            }
        }
    }

    public NPCConfig GetNPCConfig(int id)
    {
        if (npcLookup == null)
        {
            InitializeLookup();
        }

        if (npcLookup.TryGetValue(id, out var npc))
        {
            return npc;
        }

        Debug.LogWarning($"[NPCDatabase] NPC with ID {id} not found in database.");
        return null;
    }
}
