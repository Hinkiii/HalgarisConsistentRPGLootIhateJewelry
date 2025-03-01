﻿using System;
using System.Collections.Generic;
using System.Linq;
using SynthesisRPGLoot.DataModels;
using SynthesisRPGLoot.Settings.Enums;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using SynthesisRPGLoot.Generators;
using SynthesisRPGLoot.Settings;

// ReSharper disable UnusedAutoPropertyAccessor.Local

namespace SynthesisRPGLoot.Analyzers
{
    public abstract class GearAnalyzer<TType>
        where TType : class, IMajorRecordGetter

    {
        protected GearSettings GearSettings;
        protected ConfiguredNameGenerator ConfiguredNameGenerator;

        protected RarityAndVariationDistributionSettings RarityAndVariationDistributionSettings;

        protected List<RarityClass> RarityClasses;

        protected int VarietyCountPerRarity;
        protected IPatcherState<ISkyrimMod, ISkyrimModGetter> State { get; init; }

        protected Dictionary<int, ResolvedEnchantment[]> ByLevelIndexed;

        protected SortedList<string, ResolvedEnchantment[]>[] AllRpgEnchants { get; init; }

        protected Dictionary<string, FormKey>[] ChosenRpgEnchants { get; init; }
        protected Dictionary<FormKey, ResolvedEnchantment[]>[] ChosenRpgEnchantEffects { get; init; }

        protected HashSet<ILeveledItemGetter> AllLeveledLists { get; set; }
        protected HashSet<ResolvedListItem<TType>> AllListItems { get; set; }
        protected HashSet<ResolvedListItem<TType>> AllEnchantedItems { get; set; }
        protected HashSet<ResolvedListItem<TType>> AllUnenchantedItems { get; set; }

        private HashSet<ResolvedListItem<TType>> BaseItems { get; set; }

        protected Dictionary<FormKey, IObjectEffectGetter> AllObjectEffects { get; set; }

        protected ResolvedEnchantment[] AllEnchantments { get; set; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        protected HashSet<short> AllLevels { get; set; }

        protected (short Key, HashSet<ResolvedEnchantment>)[] ByLevel { get; set; }


        protected readonly Random Random = new(Program.Settings.GeneralSettings.RandomGenerationSeed);

        private readonly LeveledListFlagSettings _leveledListFlagSettings =
            Program.Settings.GeneralSettings.LeveledListFlagSettings;

        private readonly string _enchantmentSeparatorString =
            Program.Settings.NameGeneratorSettings.EnchantmentSeparator;

        private readonly string _lastEnchantmentSeparatorString =
            Program.Settings.NameGeneratorSettings.LastEnchantmentSeparator;

        protected string EditorIdPrefix;

        protected string ItemTypeDescriptor;

        protected Dictionary<string, ILeveledItemGetter> GeneratedLeveledItemsCache { get; init; }
        
        protected Dictionary<string, TType> GeneratedItemCache { get; init; }

        public void Analyze()
        {
            AnalyzeGear();
        }

        protected abstract void AnalyzeGear();

        public void PreGenerationCheck()
        {
            BaseItems = RarityAndVariationDistributionSettings.LeveledListBase switch
            {
                LeveledListBase.AllValidEnchantedItems => AllEnchantedItems,
                LeveledListBase.AllValidUnenchantedItems => AllUnenchantedItems,
                _ => BaseItems
            };
            
            var rarityWeightsSum = RarityClasses.Select(r =>
                    (int) r.RarityWeight)
                .ToArray()
                .Sum()+GearSettings.BaseItemChanceWeight;

            if (rarityWeightsSum > 255)
            {
                var factor = (float)255/rarityWeightsSum;
                rarityWeightsSum = (short) (rarityWeightsSum * factor);
                GearSettings.BaseItemChanceWeight = (short) (GearSettings.BaseItemChanceWeight * factor);
                RarityClasses.ForEach(r => r.RarityWeight = (short) (r.RarityWeight * factor));
                GearSettings.RarityClasses = RarityClasses;
            }

            var uniqueBaseItemCount = BaseItems.Select(item => item.Resolved.FormKey).Distinct().ToHashSet().Count;

            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine($"Number of Base Items : {uniqueBaseItemCount}");
            Console.WriteLine($"Number of Base LeveldListEntries: {BaseItems.Count}");
            Console.WriteLine($"Number of Classes per Item: {RarityClasses.Count}");
            Console.WriteLine($"Number of Variations per Rarity: {VarietyCountPerRarity}");
            Console.WriteLine(
                $"Number of Unique LeveledLists to Create : {uniqueBaseItemCount * (RarityClasses.Count + 1)}");
            Console.WriteLine(
                $"Number of unique new Items to Generate : {uniqueBaseItemCount * RarityClasses.Count * VarietyCountPerRarity}");
            Console.WriteLine($"Total number of RPG Loot Patcher \"touched\" & new Leveled List Entries: {
                BaseItems.Count
                * (GearSettings.BaseItemChanceWeight
                   + RarityClasses.Select(r =>
                           (int) r.RarityWeight)
                       .ToArray()
                       .Sum()
                   * VarietyCountPerRarity)}"
            );
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
            Console.WriteLine(
                "Rarity Chances in Percent: \n" +
                "(not perfect since they only impact the new additions relative to any existing loot chances)");
            
            Console.WriteLine($"BaseItem Chance: {(float)GearSettings.BaseItemChanceWeight/rarityWeightsSum:P2}");
            foreach (var rarityClass in RarityClasses)
            {
                
                Console.WriteLine($"{rarityClass.Label} Chance: {(float)rarityClass.RarityWeight/rarityWeightsSum:P2}");
            }
            Console.WriteLine(
                "------------------------------------------------------------------------------------------------------");
        }

        public void Generate()
        {
            BaseItems = RarityAndVariationDistributionSettings.LeveledListBase switch
            {
                LeveledListBase.AllValidEnchantedItems => AllEnchantedItems,
                LeveledListBase.AllValidUnenchantedItems => AllUnenchantedItems,
                _ => BaseItems
            };

            foreach (var ench in BaseItems)
            {
                var entries = State.PatchMod.LeveledItems
                    .GetOrAddAsOverride(ench.List).Entries?.Where(entry =>
                        entry.Data?.Reference.FormKey == ench.Resolved.FormKey);

                if (entries == null) continue;
                if (ench.Entry.Data == null) continue;
                if (ench.List?.Entries == null) continue;
                var levelForName = ench.Entry.Data.Level;
                var topLevelListEditorId = $"HAL_TOP_LList_{ench.Resolved.EditorID}_Level_{levelForName}";
                LeveledItem topLevelList;
                if (GeneratedLeveledItemsCache.TryGetValue(topLevelListEditorId, out var topLeveledListGetter))
                {
                    topLevelList = State.PatchMod.LeveledItems.GetOrAddAsOverride(topLeveledListGetter);
                }
                else
                {
                    topLevelList = State.PatchMod.LeveledItems.AddNewLocking(State.PatchMod.GetNextFormKey());
                    topLevelList.DeepCopyIn(ench.List);
                    topLevelList.Entries = [];
                    topLevelList.EditorID = topLevelListEditorId;
                    topLevelList.Flags = GetLeveledItemFlags();
                    
                    GeneratedLeveledItemsCache.Add(topLevelList.EditorID,topLevelList);

                    for (var i = 0; i < GearSettings.BaseItemChanceWeight; i++)
                    {
                        var oldEntryChanceAdjustmentCopy = ench.Entry.DeepCopy();
                        topLevelList.Entries.Add(oldEntryChanceAdjustmentCopy);
                    }

                    var rarityClassNumber = 0;


                    foreach (var rarityClass in RarityClasses)
                    {
                        var leveledItemEditorId =
                            $"HAL_SUB_LList_{rarityClass.Label}_{ench.Resolved.EditorID}_Level_{levelForName}";
                        LeveledItem leveledItem;
                        if (GeneratedLeveledItemsCache.TryGetValue(leveledItemEditorId,
                                out var leveledItemGetter))
                        {
                            leveledItem = State.PatchMod.LeveledItems.GetOrAddAsOverride(leveledItemGetter);
                        }
                        else
                        {
                            leveledItem = State.PatchMod.LeveledItems.AddNewLocking(State.PatchMod.GetNextFormKey());
                            leveledItem.DeepCopyIn(ench.List);
                            leveledItem.Entries = [];
                            leveledItem.EditorID = leveledItemEditorId;
                            leveledItem.Flags = GetLeveledItemFlags();
                            
                            GeneratedLeveledItemsCache[leveledItem.EditorID] = leveledItem;

                            for (var i = 0; i < VarietyCountPerRarity; i++)
                            {
                                var level = ench.Entry.Data.Level;
                                var forLevel = ByLevelIndexed[level];
                                if (forLevel.Length.Equals(0)) continue;

                                var itm = EnchantItem(ench, rarityClassNumber);
                                var entry = ench.Entry.DeepCopy();
                                entry.Data.Reference.SetTo(itm);
                                leveledItem.Entries.Add(entry);
                            }
                        }

                        for (var i = 0; i < rarityClass.RarityWeight; i++)
                        {
                            var newRarityEntry = ench.Entry.DeepCopy();
                            newRarityEntry.Data.Reference.SetTo(leveledItem);

                            topLevelList.Entries.Add(newRarityEntry);
                        }

                        rarityClassNumber++;
                    }
                }

                foreach (var entry in entries)
                {
                    entry.Data.Reference.SetTo(topLevelList);
                }
            }
        }

        protected abstract FormKey EnchantItem(ResolvedListItem<TType> item, int rarity);

        protected FormKey GenerateEnchantment(int rarity)
        {
            var array = AllRpgEnchants[rarity].ToArray();
            var allRpgEnchantmentsCount = AllRpgEnchants[rarity].Count;
            var effects = array.ElementAt(Random.Next(0,
                (0 < allRpgEnchantmentsCount - 1) ? allRpgEnchantmentsCount - 1 : array.Length - 1)).Value;

            if (ChosenRpgEnchants[rarity]
                .ContainsKey(RarityClasses[rarity].Label + " " + GetEnchantmentsStringForName(effects)))
            {
                return ChosenRpgEnchants[rarity]
                    .GetValueOrDefault(RarityClasses[rarity].Label + " " + GetEnchantmentsStringForName(effects));
            }

            var objectEffectEditorId = EditorIdPrefix + "ENCH_" + RarityClasses[rarity].Label.ToUpper() + "_" +
                                       GetEnchantmentsStringForName(effects, true);

            var newObjectEffectGetter = State.PatchMod.ObjectEffects.AddNewLocking(State.PatchMod.GetNextFormKey());
            newObjectEffectGetter.DeepCopyIn(effects.First().Enchantment);
            newObjectEffectGetter.EditorID = objectEffectEditorId;
            newObjectEffectGetter.Name = RarityClasses[rarity].Label + " " + GetEnchantmentsStringForName(effects);
            newObjectEffectGetter.Effects.Clear();
            newObjectEffectGetter.Effects.AddRange(effects.SelectMany(e => e.Enchantment.Effects)
                .Select(e => e.DeepCopy()));
            newObjectEffectGetter.WornRestrictions.SetTo(effects.First().Enchantment.WornRestrictions);

            ChosenRpgEnchants[rarity].Add(RarityClasses[rarity].Label + " " + GetEnchantmentsStringForName(effects),
                newObjectEffectGetter.FormKey);
            ChosenRpgEnchantEffects[rarity].Add(newObjectEffectGetter.FormKey, effects);
            
            return newObjectEffectGetter.FormKey;
        }

        protected string GetEnchantmentsStringForName(IEnumerable<ResolvedEnchantment> resolvedEnchantments,
            bool isEditorId = false)
        {
            if (isEditorId)
            {
                return string.Join("_", resolvedEnchantments
                    .Select(resolvedEnchantment => resolvedEnchantment.Enchantment.EditorID).ToArray());
            }

            return BeatifyLabel(string.Join(_enchantmentSeparatorString, resolvedEnchantments
                .Select(resolvedEnchantment => resolvedEnchantment.Enchantment.Name?.String).ToArray()));
        }

        private string BeatifyLabel(string labelString)
        {
            var lastSeparatorIndex = labelString.LastIndexOf(_enchantmentSeparatorString, StringComparison.Ordinal);
            if (lastSeparatorIndex == -1) return labelString;
            return labelString.Remove(lastSeparatorIndex, _enchantmentSeparatorString.Length)
                .Insert(lastSeparatorIndex, _lastEnchantmentSeparatorString);
        }

        private LeveledItem.Flag GetLeveledItemFlags()
        {
            var flag = LeveledItem.Flag.CalculateForEachItemInCount;
            if (_leveledListFlagSettings.CalculateFromAllLevelsLessThanOrEqualPlayer)
                flag |= LeveledItem.Flag.CalculateFromAllLevelsLessThanOrEqualPlayer;
            if (_leveledListFlagSettings.SpecialLoot)
                flag |= LeveledItem.Flag.SpecialLoot;
            return flag;
        }

        protected string LabelMaker(int rarity, string itemName,
            ResolvedEnchantment[] effects)
        {
            var rarityClass = RarityClasses[rarity];

            switch (rarityClass.GeneratedNameScheme)
            {
                case GeneratedNameScheme.DontUse:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{itemName} of {GetEnchantmentsStringForName(effects)}"
                        : $"{rarityClass.Label} {itemName} of {GetEnchantmentsStringForName(effects)}";
                }
                case GeneratedNameScheme.AsItemName:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{ConfiguredNameGenerator.Next()} of {GetEnchantmentsStringForName(effects)}"
                        : $"{rarityClass.Label} {ConfiguredNameGenerator.Next()} of {GetEnchantmentsStringForName(effects)}";
                }
                case GeneratedNameScheme.AsItemNameReplacingEnchantments:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{ConfiguredNameGenerator.Next()}"
                        : $"{rarityClass.Label} {ConfiguredNameGenerator.Next()}";
                }
                case GeneratedNameScheme.AsAppendedPreviousOwnerName:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{itemName} of {GetEnchantmentsStringForName(effects)} of {ConfiguredNameGenerator.Next()}"
                        : $"{rarityClass.Label} {itemName} of {GetEnchantmentsStringForName(effects)} " +
                          $"of {ConfiguredNameGenerator.Next()}";
                }
                case GeneratedNameScheme.AsAppendedPreviousOwnerNameReplacingEnchantments:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{itemName}"
                        : $"{rarityClass.Label} {itemName}";
                }
                case GeneratedNameScheme.AsPrefixedPreviousOwnerName:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{GetNameWithPossessiveS(ConfiguredNameGenerator.Next())} " +
                          $"{itemName} of {GetEnchantmentsStringForName(effects)}"
                        : $"{GetNameWithPossessiveS(ConfiguredNameGenerator.Next())} " +
                          $"{rarityClass.Label} of {GetEnchantmentsStringForName(effects)}";
                }
                case GeneratedNameScheme.AsPrefixedPreviousOwnerNameReplacingEnchantments:
                {
                    return rarityClass.HideRarityLabelInName
                        ? $"{GetNameWithPossessiveS(ConfiguredNameGenerator.Next())} " +
                          $"{itemName}"
                        : $"{GetNameWithPossessiveS(ConfiguredNameGenerator.Next())} " +
                          $"{rarityClass.Label} {itemName}";
                }
                default:
                    goto case GeneratedNameScheme.DontUse;
            }
        }

        protected string GetNameWithPossessiveS(string name)
        {
            return name.EndsWith('s') ? $"{name}'" : $"{name}'s";
        }

        // Forgot what I wanted to use this for but will keep it just in case I ever remember
        // It might have been planned for a random distribution of rarities mode
        private int RandomRarity()
        {
            var rar = 0;
            var total = RarityClasses.Sum(t => t.RarityWeight);

            var roll = Random.Next(0, total);
            while (roll >= RarityClasses[rar].RarityWeight && rar < RarityClasses.Count)
            {
                roll -= RarityClasses[rar].RarityWeight;
                rar++;
            }

            return rar;
        }
    }
}
