using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;

namespace ItemCount
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        TextMeshProUGUI _text;
        TextMeshProUGUI Text
        {
            get
            {
                if (_text == null)
                {
                    _text = Object.Instantiate(GameplayDataSettings.UIStyle.TemplateTextUGUI);
                }
                return _text;
            }
        }

        void Awake()
        {
            Debug.Log("ItemCount Loaded!!!");
        }

        void OnDestroy()
        {
            if (_text != null)
                Object.Destroy(_text);
        }

        void OnEnable()
        {
            ItemHoveringUI.onSetupItem += OnSetupItemHoveringUI;
        }

        void OnDisable()
        {
            ItemHoveringUI.onSetupItem -= OnSetupItemHoveringUI;
        }

        void OnSetupItemHoveringUI(ItemHoveringUI uiInstance, Item item)
        {
            if (item == null)
            {
                if (_text != null)
                    _text.gameObject.SetActive(false);
                return;
            }

            int total = GetTotalOwnedCount(item.TypeID);

            Text.gameObject.SetActive(true);
            Text.transform.SetParent(uiInstance.LayoutParent);
            Text.transform.localScale = Vector3.one;
            Text.transform.SetAsLastSibling();
            Text.text = $"Owned: {total}";
            Text.fontSize = 18f;
        }

        static int GetTotalOwnedCount(int typeId)
        {
            int sum = 0;

            var charInv = LevelManager.Instance?.MainCharacter?.CharacterItem?.Inventory;
            if (charInv != null)
            {
                foreach (var it in charInv)
                {
                    sum += CountInTree(it, typeId);
                }
            }

            var storageInv = PlayerStorage.Inventory;
            if (storageInv != null)
            {
                foreach (var it in storageInv)
                {
                    sum += CountInTree(it, typeId);
                }
            }

            var petInv = LevelManager.Instance?.PetProxy?.Inventory;
            if (petInv != null)
            {
                foreach (var it in petInv)
                {
                    sum += CountInTree(it, typeId);
                }
            }

            return sum;
        }

        static int CountInTree(Item root, int typeId)
        {
            int total = 0;
            if (root == null)
                return 0;

            if (root.TypeID == typeId)
            {
                total += root.Stackable ? root.StackCount : 1;
            }

            if (root.Slots != null)
            {
                foreach (var slot in root.Slots)
                {
                    if (slot == null) continue;
                    var content = slot.Content;
                    if (content != null)
                    {
                        total += CountInTree(content, typeId);
                    }
                }
            }

            if (root.Inventory != null)
            {
                foreach (var child in root.Inventory)
                {
                    if (child != null)
                    {
                        total += CountInTree(child, typeId);
                    }
                }
            }

            return total;
        }
    }
}
