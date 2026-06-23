using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Freeland.Gameplay
{
    public class ObjectInformationPanel : MonoBehaviour
    {
        private static ObjectInformationPanel instance;

        public static ObjectInformationPanel Instance
        {
            get
            {
                Debug.Log($"[ObjectInformationPanel] Instance getter called. Current static instance: {(instance != null ? instance.name : "null")}");
                if (instance == null)
                {
                    // 1. Try to find the component in the current scene (including inactive ones)
                    instance = FindFirstObjectByType<ObjectInformationPanel>(FindObjectsInactive.Include);
                    Debug.Log($"[ObjectInformationPanel] FindFirstObjectByType in scene returned: {(instance != null ? instance.name : "null")}");

                    if (instance == null)
                    {
                        // 2. Load the prefab from Resources/Prefab/ObjectInfomation
                        GameObject prefab = Resources.Load<GameObject>("Prefab/ObjectInfomation");
                        if (prefab != null)
                        {
                            Debug.Log($"[ObjectInformationPanel] Loading prefab. Instantiating...");
                            GameObject obj = Instantiate(prefab);
                            if (instance == null)
                            {
                                instance = obj.GetComponent<ObjectInformationPanel>();
                                if (instance == null)
                                {
                                    instance = obj.GetComponentInChildren<ObjectInformationPanel>(true);
                                }
                            }
                            
                            Debug.Log($"[ObjectInformationPanel] Instantiated {obj.name}. Static instance is now: {(instance != null ? instance.name : "null")}");

                            if (instance == null)
                            {
                                Debug.LogError("[ObjectInformationPanel] Prefab loaded from Resources/Prefab/ObjectInfomation, but it does not contain the ObjectInformationPanel component on the root or in children!");
                            }
                        }
                        else
                        {
                            Debug.LogError("[ObjectInformationPanel] Prefab not found in Resources/Prefab/ObjectInfomation!");
                        }
                    }
                }
                return instance;
            }
        }

        [Header("UI References")]
        [SerializeField] private GameObject container;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text mpText;
        [SerializeField] private Image avatarImage;

        private bool isShowing = false;

        private void Awake()
        {
            Debug.Log($"[ObjectInformationPanel] Awake called on {gameObject.name}. Current static instance: {(instance != null ? instance.name : "null")}");
            if (instance == null)
            {
                instance = this;
                Debug.Log($"[ObjectInformationPanel] Awake set static instance to this ({gameObject.name})");
            }
            else if (instance != this)
            {
                Debug.LogWarning($"[ObjectInformationPanel] Awake detected duplicate on {gameObject.name}. Destroying duplicate UI root. Current active instance: {instance.name}");
                if (transform.parent != null && transform.parent.name.Contains("(Clone)"))
                {
                    Destroy(transform.parent.gameObject);
                }
                else
                {
                    Destroy(gameObject);
                }
                return;
            }

            // Hide by default only if not actively showing
            if (!isShowing)
            {
                Hide();
            }
        }

        public void Show(BaseObject obj)
        {
            if (obj == null)
            {
                Hide();
                return;
            }

            // Set basic info (Name, HP, MP) BEFORE enabling active status
            if (nameText != null)
            {
                nameText.text = string.IsNullOrEmpty(obj.ObjectName) ? obj.gameObject.name : obj.ObjectName;
            }

            if (hpText != null)
            {
                hpText.text = $"HP: {obj.HP} / {obj.MaxHP}";
            }

            if (mpText != null)
            {
                mpText.text = $"MP: {obj.MP} / {obj.MaxMP}";
            }

            // Set avatar from the object's SpriteRenderer sprite
            if (avatarImage != null)
            {
                Sprite avatarSprite = null;
                if (obj.SpriteRenderer != null)
                {
                    avatarSprite = obj.SpriteRenderer.sprite;
                }

                if (avatarSprite != null)
                {
                    avatarImage.sprite = avatarSprite;
                    avatarImage.gameObject.SetActive(true);
                }
                else
                {
                    avatarImage.gameObject.SetActive(false);
                }
            }

            // Enable active status after all parameters are populated
            isShowing = true;
            gameObject.SetActive(true);

            if (container != null)
            {
                container.SetActive(true);
            }
        }

        public void Hide()
        {
            isShowing = false;
            gameObject.SetActive(false);
            if (container != null)
            {
                container.SetActive(false);
            }
        }
    }
}
