using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using System.IO;

namespace ItemCount
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        static System.Collections.Generic.Dictionary<int, int> _storageCache = new System.Collections.Generic.Dictionary<int, int>();
        static bool _storageCacheValid = false;
        static Inventory? _lastStorageRef = null;
        const string CacheFileName = "ItemCount.cfg";

        TextMeshProUGUI? _text;
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

        // Persistent cache helpers
        static string GetCachePath()
        {
            string modsDir = System.IO.Path.Combine(Application.dataPath, "Mods");
            string modDir = System.IO.Path.Combine(modsDir, "ItemCount");
            System.IO.Directory.CreateDirectory(modDir);
            return System.IO.Path.Combine(modDir, CacheFileName);
        }

        static void SaveStorageCacheToDisk()
        {
            try
            {
                var path = GetCachePath();
                using (var sw = new System.IO.StreamWriter(path, false))
                {
                    // Header explaining the file structure
                    sw.WriteLine("# ItemCount storage cache");
                    sw.WriteLine("# Format: <TypeID>=<Count>");
                    sw.WriteLine("# One entry per line. Counts reflect last known contents of PlayerStorage.");
                    foreach (var kv in _storageCache)
                    {
                        sw.WriteLine($"{kv.Key}={kv.Value}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ItemCount] Failed to save cache: {ex.Message}");
            }
        }

        static void LoadStorageCacheFromDisk()
        {
            try
            {
                var path = GetCachePath();
                if (!System.IO.File.Exists(path))
                {
                    Debug.Log("[ItemCount] No cache file found to load.");
                    return;
                }
                _storageCache.Clear();
                var lines = System.IO.File.ReadAllLines(path);
                foreach (var raw in lines)
                {
                    var line = raw?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    if (int.TryParse(line.Substring(0, eq).Trim(), out var id) && int.TryParse(line.Substring(eq + 1).Trim(), out var count))
                    {
                        if (count > 0)
                            _storageCache[id] = count;
                    }
                }
                _storageCacheValid = _storageCache.Count > 0;
                Debug.Log($"[ItemCount] Loaded cache. Valid={_storageCacheValid}, entries={_storageCache.Count}");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ItemCount] Failed to load cache: {ex.Message}");
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
            // Load any previously saved storage counts so they persist across map loads
            LoadStorageCacheFromDisk();
            // Proactively rebuild cache if storage is available now (e.g., inside base)
            var storageInv = PlayerStorage.Inventory;
            if (storageInv != null)
            {
                RebuildStorageCache(storageInv);
            }
            ItemHoveringUI.onSetupItem += OnSetupItemHoveringUI;
            ItemUtilities.OnPlayerItemOperation += OnPlayerItemOperation;
        }

        void OnDisable()
        {
            ItemHoveringUI.onSetupItem -= OnSetupItemHoveringUI;
            ItemUtilities.OnPlayerItemOperation -= OnPlayerItemOperation;
            // Persist latest cache on disable
            SaveStorageCacheToDisk();
        }

        void OnPlayerItemOperation()
        {
            // Only invalidate cache when storage is actually accessible (inside base)
            // Outside base, keep the last known cache so counts remain available
            if (PlayerStorage.Inventory != null)
            {
                _storageCacheValid = false;
            }
        }

        void OnSetupItemHoveringUI(ItemHoveringUI uiInstance, Item item)
        {
            if (item == null)
            {
                if (_text != null)
                    _text.gameObject.SetActive(false);
                return;
            }

            int playerCount = GetPlayerOnlyCount(item.TypeID);
            int storageCount = GetStorageOnlyCount(item.TypeID);
            int petCount = GetPetOnlyCount(item.TypeID);
            int total = playerCount + storageCount + petCount;

            Text.gameObject.SetActive(true);
            Text.transform.SetParent(uiInstance.LayoutParent);
            Text.transform.localScale = Vector3.one;
            Text.transform.SetAsLastSibling();
            Text.text = $"Owned:  {total} (Player: {playerCount}, Pet: {petCount}, Storage: {storageCount})";
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
                // If storage inventory exists but is empty (likely not loaded outside base), prefer cached values
                if (storageInv.IsEmpty())
                {
                    if (!_storageCacheValid)
                    {
                        LoadStorageCacheFromDisk();
                    }
                    if (_storageCacheValid && _storageCache.TryGetValue(typeId, out var cachedFromEmpty))
                    {
                        sum += cachedFromEmpty;
                    }
                }
                else
                {
                    // When storage is available and has content, compute live count and refresh cache for later use
                    EnsureStorageCache(storageInv);
                    foreach (var it in storageInv)
                    {
                        sum += CountInTree(it, typeId);
                    }
                }
            }
            else
            {
                // When storage is not available (e.g., outside base), use cached count if valid
                if (!_storageCacheValid)
                {
                    // Lazy reload from disk in case the cache was invalidated by gameplay actions after leaving base
                    LoadStorageCacheFromDisk();
                }
                if (_storageCacheValid && _storageCache.TryGetValue(typeId, out var cached))
                {
                    sum += cached;
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

        // Breakdown helpers that mirror the logic in GetTotalOwnedCount, per source
        static int GetPlayerOnlyCount(int typeId)
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
            return sum;
        }

        static int GetStorageOnlyCount(int typeId)
        {
            int sum = 0;
            var storageInv = PlayerStorage.Inventory;
            if (storageInv != null)
            {
                if (storageInv.IsEmpty())
                {
                    if (!_storageCacheValid)
                    {
                        LoadStorageCacheFromDisk();
                    }
                    if (_storageCacheValid && _storageCache.TryGetValue(typeId, out var cachedFromEmpty))
                    {
                        sum += cachedFromEmpty;
                    }
                }
                else
                {
                    EnsureStorageCache(storageInv);
                    foreach (var it in storageInv)
                    {
                        sum += CountInTree(it, typeId);
                    }
                }
            }
            else
            {
                if (!_storageCacheValid)
                {
                    LoadStorageCacheFromDisk();
                }
                if (_storageCacheValid && _storageCache.TryGetValue(typeId, out var cached))
                {
                    sum += cached;
                }
            }
            return sum;
        }

        static int GetPetOnlyCount(int typeId)
        {
            int sum = 0;
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

        static void EnsureStorageCache(Inventory storage)
        {
            if (storage == null)
                return;

            if (!_storageCacheValid || _lastStorageRef != storage)
            {
                RebuildStorageCache(storage);
            }
        }

        static void RebuildStorageCache(Inventory storage)
        {
            // Build into a temporary map first to avoid clobbering a good cache with an empty snapshot
            var temp = new System.Collections.Generic.Dictionary<int, int>();
            void Acc(Item node)
            {
                if (node == null) return;
                int add = node.Stackable ? node.StackCount : 1;
                if (add > 0)
                {
                    if (temp.TryGetValue(node.TypeID, out var cur)) temp[node.TypeID] = cur + add; else temp[node.TypeID] = add;
                }
                if (node.Slots != null)
                {
                    foreach (var slot in node.Slots)
                    {
                        if (slot?.Content != null) Acc(slot.Content);
                    }
                }
                if (node.Inventory != null)
                {
                    foreach (var child in node.Inventory)
                    {
                        if (child != null) Acc(child);
                    }
                }
            }

            foreach (var root in storage)
            {
                if (root == null) continue;
                Acc(root);
            }

            // If the snapshot is empty but we already have a non-empty cache, keep the existing cache
            if (temp.Count == 0 && _storageCacheValid && _storageCache.Count > 0)
            {
                Debug.Log("[ItemCount] Live storage snapshot empty; preserving existing non-empty cache.");
                return;
            }

            _storageCache = temp;
            _lastStorageRef = storage;
            _storageCacheValid = _storageCache.Count > 0;
            SaveStorageCacheToDisk();
        }

        static void AccumulateCounts(Item node)
        {
            if (node == null) return;

            int add = node.Stackable ? node.StackCount : 1;
            if (add > 0)
            {
                if (_storageCache.TryGetValue(node.TypeID, out var cur))
                    _storageCache[node.TypeID] = cur + add;
                else
                    _storageCache[node.TypeID] = add;
            }

            if (node.Slots != null)
            {
                foreach (var slot in node.Slots)
                {
                    if (slot?.Content != null)
                    {
                        AccumulateCounts(slot.Content);
                    }
                }
            }

            if (node.Inventory != null)
            {
                foreach (var child in node.Inventory)
                {
                    if (child != null)
                        AccumulateCounts(child);
                }
            }
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
