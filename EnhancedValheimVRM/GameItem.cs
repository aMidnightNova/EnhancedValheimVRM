using System;
using System.Collections;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EnhancedValheimVRM
{
    public static class GameItem
    {
        private static ObjectDB _database;
        private static readonly Dictionary<string, bool> SpecialRigs = new Dictionary<string, bool>();
        private static readonly Dictionary<string, string> Classes = new Dictionary<string, string>();

        // Settings-file class of an item prefab (see VrmSettings.ItemClasses), or null.
        public static string ClassOf(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || ObjectDB.instance == null) return null;
            if (_database != ObjectDB.instance)
            {
                _database = ObjectDB.instance;
                SpecialRigs.Clear();
                Classes.Clear();
            }

            if (Classes.TryGetValue(itemName, out var known)) return known;
            var prefab = _database.GetItemPrefab(itemName.GetStableHashCode());
            var item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            string result = null;
            if (item != null)
            {
                var type = item.m_itemData.m_shared.m_itemType.ToString();
                var skill = item.m_itemData.m_shared.m_skillType.ToString();
                if (type == "Shield")
                    result = "Shield";
                else if (type == "Torch")
                    result = "Torch";
                else if (skill == "Crossbows")
                    result = "Crossbow";
                else if (skill == "Bows" || type == "Bow")
                    result = "Bow";
                else if (skill == "Swords")
                    result = "Sword";
                else if (skill == "Knives")
                    result = "Knife";
                else if (skill == "Clubs")
                    result = "Club";
                else if (skill == "Axes")
                    result = "Axe";
                else if (skill == "Spears")
                    result = "Spear";
                else if (skill == "Polearms")
                    result = "Polearm";
                else if (skill == "Pickaxes")
                    result = "Pickaxe";
                else if (skill == "Unarmed")
                    result = "Fist";
                else if (skill == "BloodMagic" || skill == "ElementalMagic")
                    result = "Staff";
                else if (type == "Tool") result = "Tool";
            }

            Classes[itemName] = result;
            return result;
        }

        public static bool IsSpecialCase(string itemName)
        {
            if (string.IsNullOrEmpty(itemName) || ObjectDB.instance == null) return false;
            if (_database != ObjectDB.instance)
            {
                _database = ObjectDB.instance;
                SpecialRigs.Clear();
            }

            if (SpecialRigs.TryGetValue(itemName, out var special)) return special;
            var prefab = _database.GetItemPrefab(itemName.GetStableHashCode());
            if (prefab == null) return false;
            var item = prefab.GetComponent<ItemDrop>();
            var type = item != null ? item.m_itemData.m_shared.m_itemType.ToString() : "";
            special = (type == "OneHandedWeapon" || type == "TwoHandedWeapon" || type == "TwoHandedWeaponLeft" ||
                    type == "Bow" || type == "Tool" || type == "Torch" || type == "Shield") &&
                HasOwnHandRig(prefab.transform.Find("attach_skin"));
            SpecialRigs[itemName] = special;
            return special;
        }

        // Temporary attachment testing command; keep only our spawned drops for cleanup.
        private static bool _spawningWeapons;
        private static int _spawnGeneration;
        private const string TestWeaponTag = "EnhancedValheimVRM.DevWeapon";

        private static string TestOwner(Player player)
        {
            return player.GetPlayerID().ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        }

        private static ItemDrop[] TestDrops(Player player)
        {
            var owner = TestOwner(player);
            return UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(drop => drop != null && drop.m_itemData?.m_customData != null &&
                    drop.m_itemData.m_customData.TryGetValue(TestWeaponTag, out var tag) &&
                    tag.StartsWith(owner, StringComparison.Ordinal))
                .ToArray();
        }

        internal static string SpawnWeapons(bool twoHandedOnly)
        {
            var player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() == 0 || ObjectDB.instance == null || ItemSets.instance == null)
                return "Enter a world first.";
            if (Console.instance == null || !Console.instance.IsCheatsEnabled())
                return "Enable devcommands in the console first.";
            if (_spawningWeapons || TestDrops(player).Length != 0)
                return "Use /vrm dev clearweapons before spawning another set.";
            _spawningWeapons = true;
            _spawnGeneration++;
            CoroutineHelper.Instance.StartCoroutine(SpawnWeaponsAsync(player, twoHandedOnly));
            return twoHandedOnly
                ? "Spawning regional two-handed weapon piles (including bows, crossbows and staves)."
                : "Spawning regional weapon/shield piles in front of you; one pile per region.";
        }

        private static readonly string[] TestRegions =
        {
            "Meadows", "BlackForest", "Swamp", "Mountain", "Plains", "Mistlands", "Ashlands", "DeepNorth", "Ocean"
        };

        private static int TestRegion(string setName)
        {
            var name = (setName ?? "").Replace(" ", "").Replace("_", "");
            // Includes the game's Swamps/Mountains names and regional variants such as PlainsBoss.
            for (var index = 0; index < TestRegions.Length; index++)
            {
                if (name.StartsWith(TestRegions[index], StringComparison.OrdinalIgnoreCase)) return index;
            }

            return -1; // Generic/debug presets are not regional loadouts.
        }

        private static bool IsTestWeapon(ItemDrop drop)
        {
            if (drop == null || drop.m_itemData == null) return false;
            var data = drop.m_itemData.m_shared;
            // Preset membership identifies player equipment; no NPC-name/icon guessing.
            return (drop.m_itemData.IsWeapon() || data.m_itemType == ItemDrop.ItemData.ItemType.Shield) &&
                data.m_skillType != Skills.SkillType.Pickaxes && data.m_skillType != Skills.SkillType.Fishing;
        }

        private static IEnumerator SpawnWeaponsAsync(Player player, bool twoHandedOnly)
        {
            var database = ObjectDB.instance;
            var sets = ItemSets.instance;
            var generation = _spawnGeneration;
            var tag = TestOwner(player) + Guid.NewGuid().ToString("N");
            var forward = player.transform.forward;
            forward.y = 0;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward), origin = player.transform.position + forward * 5;
            int count = 0, pile = 0;
            try
            {
                // Read the same definitions as itemset; never invoke it (it also changes skills/inventory).
                var regions = sets.m_sets.Where(set => TestRegion(set.m_name) >= 0)
                    .GroupBy(set => TestRegion(set.m_name))
                    .OrderBy(group => group.Key);
                foreach (var region in regions)
                {
                    var weapons = region.SelectMany(set => set.m_items)
                        .Select(entry => entry.m_item)
                        .Where(drop => IsTestWeapon(drop) && (!twoHandedOnly || drop.m_itemData.IsTwoHanded()))
                        .GroupBy(drop => drop.gameObject.name, StringComparer.Ordinal)
                        .Select(group => group.First())
                        .OrderBy(drop => drop.gameObject.name, StringComparer.Ordinal)
                        .ToArray();
                    if (weapons.Length == 0) continue;
                    var center = origin + right * (pile % 3 - 1) * 8 + forward * (pile / 3) * 8;
                    for (var index = 0; index < weapons.Length; index++)
                    {
                        if (generation != _spawnGeneration || !_spawningWeapons || player == null ||
                            player != Player.m_localPlayer ||
                            database != ObjectDB.instance || sets != ItemSets.instance)
                            yield break;
                        // All of a region's equipment stays in a small pile; only regional piles are spaced out.
                        var angle = index * 2.399963f;
                        var radius = 0.8f * Mathf.Sqrt((index + 0.5f) / weapons.Length);
                        var position = center + right * (Mathf.Cos(angle) * radius) +
                            forward * (Mathf.Sin(angle) * radius);
                        if (Physics.Raycast(position + Vector3.up * 20,
                                Vector3.down,
                                out var hit,
                                60,
                                LayerMask.GetMask("terrain", "Default", "static_solid")))
                            position.y = hit.point.y;
                        position.y += 0.5f;
                        var item = weapons[index].m_itemData.Clone();
                        item.m_dropPrefab = weapons[index].gameObject;
                        item.m_quality = 1;
                        item.m_variant = 0;
                        // ItemData.Clone and Save/Load preserve this across inventory pickup and later drops.
                        item.m_customData[TestWeaponTag] = tag;
                        ItemDrop.DropItem(item, 1, position, Quaternion.identity);
                        count++;
                        yield return null;
                    }

                    pile++;
                    Logger.Log("Weapon pile " + pile + ": " + TestRegions[region.Key] + ", " + weapons.Length +
                        " weapons/shields.");
                }

                Logger.Log("Spawned " + count + " preset weapons/shields in " + pile +
                    " regional piles. /vrm dev clearweapons removes uncollected drops.");
                if (pile == 0) Logger.LogWarning("No regional weapon itemsets were present in the loaded game.");
            }
            finally
            {
                if (generation == _spawnGeneration) _spawningWeapons = false;
            }
        }

        internal static string ClearTestWeapons()
        {
            var player = Player.m_localPlayer;
            if (player == null || player.GetPlayerID() == 0) return "Enter a world first.";
            _spawningWeapons = false;
            _spawnGeneration++;
            var drops = TestDrops(player);
            foreach (var drop in drops)
            {
                var item = drop.gameObject;
                var view = item.GetComponent<ZNetView>();
                if (view != null && view.IsValid())
                {
                    view.ClaimOwnership();
                    ZNetScene.instance.Destroy(item);
                }
                else
                    UnityEngine.Object.Destroy(item);
            }

            return "Removed " + drops.Length +
                " tagged test weapon drops, including re-dropped items. Inventory items are kept.";
        }

        internal static void AuditWeapons()
        {
            var database = ObjectDB.instance;
            if (database == null) return;
            CoroutineHelper.Instance.StartCoroutine(AuditWeaponsAsync(database));
        }

        private static IEnumerator AuditWeaponsAsync(ObjectDB database)
        {
            var types = new HashSet<string>
            {
                "OneHandedWeapon",
                "TwoHandedWeapon",
                "TwoHandedWeaponLeft",
                "Bow",
                "Tool",
                "Torch",
                "Shield"
            };
            var rows = new StringBuilder("Prefab,ItemType,AttachOverride,Skill,OwnHandRig,AttachmentVariants\n");
            // Index the live collection rather than retaining an enumerator across frames.
            for (var index = 0; database != null && index < database.m_items.Count; index++)
            {
                yield return null;
                if (database == null || database != ObjectDB.instance || index >= database.m_items.Count) yield break;
                var prefab = database.m_items[index];
                if (prefab == null) continue;
                var item = prefab.GetComponent<ItemDrop>();
                if (item == null || !types.Contains(item.m_itemData.m_shared.m_itemType.ToString())) continue;
                var data = item.m_itemData.m_shared;
                var special = IsSpecialCase(prefab.name);
                var variants = new List<string>();
                foreach (Transform child in prefab.transform)
                {
                    if (child.name.StartsWith("attach")) variants.Add(child.name);
                }

                rows.Append('"')
                    .Append(prefab.name.Replace("\"", "\"\""))
                    .Append("\",")
                    .Append(data.m_itemType)
                    .Append(',')
                    .Append(data.m_attachOverride)
                    .Append(',')
                    .Append(data.m_skillType)
                    .Append(',')
                    .Append(special)
                    .Append(',')
                    .Append(string.Join("|", variants))
                    .Append('\n');
            }

            var output = rows.ToString();
            var write = Task.Run(() =>
                File.WriteAllText(Path.Combine(Constants.Vrm.Dir, "weapon_attachment_audit.csv"), output));
            while (!write.IsCompleted) yield return null;
            if (write.IsFaulted)
                Logger.LogWarning("Cannot write weapon audit: " + write.Exception.GetBaseException().Message);
        }

        internal static bool HasOwnHandRig(Transform attachment)
        {
            if (attachment == null) return false;
            var left = BoneLookup.Find(attachment, HumanBodyBones.LeftHand);
            var right = BoneLookup.Find(attachment, HumanBodyBones.RightHand);
            if (left == null && right == null) return false;
            // Two-handed item type alone is not enough: bows and ordinary greatswords
            // follow one socket. Only a mesh skinned to its OWN hand rig needs retargeting.
            return (left != null && left.GetComponentInChildren<MeshRenderer>(true) != null) ||
                (right != null && right.GetComponentInChildren<MeshRenderer>(true) != null) ||
                attachment.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Any(renderer =>
                        renderer.sharedMesh != null && renderer.bones.Any(bone => bone != null &&
                            ((left != null && (bone == left || bone.IsChildOf(left))) ||
                                (right != null && (bone == right || bone.IsChildOf(right))))));
        }
    }
}
