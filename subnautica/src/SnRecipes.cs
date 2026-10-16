using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using UnityEngine;

namespace SubnautiCraft
{
    /// <summary>
    /// Subnautica's whole fabricator in Minecraft's crafting table. The mod ships the recipes made only of materials
    /// with their own Minecraft items (copper wire, glass...); every other recipe in the fabricator's tree (the mobile
    /// vehicle bay, the knife, the habitat builder, filtered water...) is sent from here, with its results and
    /// ingredients as Minecraft knows them ("Subnautica items" by kind), and Minecraft adds it to Steve's world. The
    /// blueprint rules are the same: it crafts once the diver knows it. Their icons are sent too.
    /// </summary>
    internal static class SnRecipes
    {
        private const int GridSize = 9;

        public static IEnumerator Send(HostLink link)
        {
            CraftTree tree = CraftTree.GetTree(CraftTree.Type.Fabricator);
            if (tree == null)
            {
                yield break;
            }

            var techs = new List<TechType>();
            using (IEnumerator<TreeNode> e = tree.nodes.Traverse(false))
            {
                while (e.MoveNext())
                {
                    if (e.Current is CraftNode node && node.action == TreeAction.Craft && node.techType0 != TechType.None && !techs.Contains(node.techType0))
                    {
                        techs.Add(node.techType0);
                    }
                }
            }

            var sb = new StringBuilder("{\"t\":\"recipes\",\"r\":[");
            var generic = new HashSet<TechType>();
            var tooBig = new List<TechType>();
            int count = 0;
            foreach (TechType tech in techs)
            {
                if (!link.Connected)
                {
                    yield break;
                }

                ReadOnlyCollection<Ingredient> ingredients = TechData.GetIngredients(tech);
                if (ingredients == null || ingredients.Count == 0)
                {
                    continue;
                }

                int total = 0;
                bool ownItems = SnItemTable.Items.ContainsKey(tech);
                foreach (Ingredient i in ingredients)
                {
                    total += i.amount;
                    ownItems &= SnItemTable.Items.ContainsKey(i.techType);
                }

                if (ownItems)
                {
                    continue; // the mod's own recipes have it
                }

                if (total > GridSize)
                {
                    tooBig.Add(tech);
                    continue;
                }

                string eat = "";
                if (!SnItemTable.Items.ContainsKey(tech))
                {
                    generic.Add(tech);
                    CoroutineTask<GameObject> task = CraftData.GetPrefabForTechTypeAsync(tech, false);
                    yield return task;
                    GameObject prefab = task.GetResult();
                    eat = SnItems.EatJson(tech, prefab != null ? prefab.GetComponent<Eatable>() : null) + SteveTools.ChargeJson(prefab, true);
                }

                sb.Append(count++ == 0 ? "" : ",")
                    .Append("{\"out\":{").Append(SnItems.McItem(tech)).Append(eat).Append("},\"n\":").Append(Mathf.Max(1, TechData.GetCraftAmount(tech)))
                    .Append(",\"in\":[");
                for (int k = 0; k < ingredients.Count; k++)
                {
                    Ingredient i = ingredients[k];
                    if (!SnItemTable.Items.ContainsKey(i.techType))
                    {
                        generic.Add(i.techType);
                    }

                    sb.Append(k == 0 ? "{" : ",{").Append(SnItems.McItem(i.techType)).Append(",\"n\":").Append(i.amount).Append('}');
                }

                sb.Append("]}");
            }

            // their pictures first, so the recipe book shows them
            int sent = 0;
            foreach (TechType t in generic)
            {
                if (!link.Connected)
                {
                    yield break;
                }

                if (SnItems.SendIcon(t, link) && ++sent % 4 == 0)
                {
                    yield return null;
                }
            }

            if (sent > 0)
            {
                link.Send("{\"t\":\"icons\"}");
            }

            link.Send(sb.Append("]}").ToString());
            Plugin.Log.LogInfo($"fabricator: {count} more recipes sent to Minecraft ({sent} icons)"
                + (tooBig.Count > 0 ? $"; too many ingredients for a crafting grid: {string.Join(", ", tooBig)}" : ""));
        }
    }
}
