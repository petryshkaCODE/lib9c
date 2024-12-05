﻿#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Libplanet.Action;
using Nekoyume.Action;
using Nekoyume.Model.Item;
using Nekoyume.Model.EnumType;
using Nekoyume.Model.State;
using Nekoyume.TableData;

namespace Nekoyume.Helper
{
    using GradeDict = Dictionary<int, Dictionary<ItemSubType, int>>;

    /// <summary>
    /// Represents the result of the synthesis.
    /// </summary>
    public struct SynthesizeResult
    {
        public ItemBase ItemBase;
        public bool IsSuccess;
        public int RecipeId;
        public int SubRecipeId;

        public bool IsEquipment => ItemBase.ItemType == ItemType.Equipment;
    }

    /// <summary>
    /// A class that simulates the synthesis of items.
    /// <see cref="InputData"/>
    /// </summary>
    public static class SynthesizeSimulator
    {
        private static readonly ItemType[] ValidItemType =
        {
            ItemType.Costume,
            ItemType.Equipment,
        };

        private static readonly ItemSubType[] ValidItemSubType =
        {
            ItemSubType.FullCostume,
            ItemSubType.Title,
            ItemSubType.Grimoire,
            ItemSubType.Aura,
        };

        /// <summary>
        /// Simulate the synthesis of items.
        /// </summary>
        public struct InputData
        {
            /// <summary>
            /// The sheet that contains the synthesis information.
            /// <seealso cref="Nekoyume.TableData.SynthesizeSheet"/>
            /// </summary>
            public SynthesizeSheet SynthesizeSheet;
            /// <summary>
            /// The sheet that contains the synthesis weight information.
            /// <seealso cref="Nekoyume.TableData.SynthesizeWeightSheet"/>
            /// </summary>
            public SynthesizeWeightSheet SynthesizeWeightSheet;
            /// <summary>
            /// The sheet that contains the costume item information.
            /// <seealso cref="Nekoyume.TableData.CostumeItemSheet"/>
            /// </summary>
            public CostumeItemSheet CostumeItemSheet;
            /// <summary>
            /// The sheet that contains the equipment item information.
            /// <seealso cref="Nekoyume.TableData.EquipmentItemSheet"/>
            /// </summary>
            public EquipmentItemSheet EquipmentItemSheet;
            /// <summary>
            /// The sheet that contains the equipment item recipe information.
            /// <seealso cref="Nekoyume.TableData.EquipmentItemRecipeSheet"/>
            /// </summary>
            public EquipmentItemRecipeSheet EquipmentItemRecipeSheet;
            /// <summary>
            /// The sheet that contains the equipment item sub recipe information.
            /// <seealso cref="Nekoyume.TableData.EquipmentItemSubRecipeSheetV2"/>
            /// </summary>
            public EquipmentItemSubRecipeSheetV2 EquipmentItemSubRecipeSheetV2;
            /// <summary>
            /// The sheet that contains the equipment item option information.
            /// <seealso cref="Nekoyume.TableData.EquipmentItemOptionSheet"/>
            /// </summary>
            public EquipmentItemOptionSheet EquipmentItemOptionSheet;
            /// <summary>
            /// The sheet that contains the skill information.
            /// <seealso cref="Nekoyume.TableData.SkillSheet"/>
            /// </summary>
            public SkillSheet SkillSheet;
            /// <summary>
            /// The block index when action is executed.
            /// </summary>
            public long BlockIndex;
            /// <summary>
            /// The random object to use.
            /// Caution: Must have the same seed as when the action is executed
            /// </summary>
            public IRandom RandomObject;
            /// <summary>
            /// The grade dictionary of the material items.
            /// </summary>
            public GradeDict GradeDict;
        }

        private struct EquipmentData
        {
            /// <summary>
            /// RecipeId of the equipment
            /// </summary>
            public int RecipeId;
            /// <summary>
            /// SubRecipeId of the equipment
            /// </summary>
            public int SubRecipeId;
        }

        /// <summary>
        /// Simulate the synthesis of items.
        /// </summary>
        /// <param name="inputData"><see cref="InputData"/> </param>
        /// <returns></returns>
        /// <exception cref="SheetRowNotFoundException"> </exception>
        /// <exception cref="NotEnoughMaterialException"></exception>
        public static List<SynthesizeResult> Simulate(InputData inputData)
        {
            var synthesizeResults = new List<SynthesizeResult>();

            var synthesizeSheet = inputData.SynthesizeSheet;
            var random = inputData.RandomObject;
            var gradeDict = inputData.GradeDict;

            // Calculate the number of items to be synthesized based on materials
            foreach (var gradeItem in gradeDict)
            {
                var gradeId = gradeItem.Key;
                var subTypeDict = gradeItem.Value;

                if (!synthesizeSheet.TryGetValue(gradeId, out var synthesizeRow))
                {
                    throw new SheetRowNotFoundException(
                        $"Aborted as the synthesize row for grade ({gradeId}) was failed to load in {nameof(SynthesizeSheet)}", gradeId
                    );
                }

                foreach (var subTypeItem in subTypeDict)
                {
                    var itemSubType = subTypeItem.Key;
                    var materialCount = subTypeItem.Value;

                    // TODO: subType별로 필요한 아이템 개수가 다를 수 있음
                    var requiredCount = synthesizeRow.RequiredCountDict[itemSubType].RequiredCount;
                    var succeedRate = synthesizeRow.RequiredCountDict[itemSubType].SucceedRate;
                    var synthesizeCount = materialCount / requiredCount;
                    var remainder = materialCount % requiredCount;

                    if (synthesizeCount <= 0 || remainder != 0)
                    {
                        throw new NotEnoughMaterialException(
                            $"Aborted as the number of materials for grade {gradeId} and subtype {itemSubType} is not enough."
                        );
                    }

                    // Calculate success for each synthesis
                    for (var i = 0; i < synthesizeCount; i++)
                    {
                        // random value range is 0 ~ 9999
                        // If the SucceedRate of the table is 0, use '<' for always to fail.
                        // and SucceedRate of the table is 10000, always success(because left value range is 0~9999)
                        var isSuccess = random.Next(SynthesizeSheet.SucceedRateMax) < succeedRate;

                        var grade = (Grade)gradeId;
                        // Decide the item to add to inventory based on SynthesizeWeightSheet
                        var synthesizedItem = GetSynthesizedItem(
                            grade,
                            isSuccess,
                            inputData.SynthesizeWeightSheet,
                            inputData.CostumeItemSheet,
                            inputData.EquipmentItemSheet,
                            inputData.EquipmentItemRecipeSheet,
                            inputData.EquipmentItemSubRecipeSheetV2,
                            inputData.EquipmentItemOptionSheet,
                            inputData.SkillSheet,
                            inputData.BlockIndex,
                            random,
                            itemSubType,
                            out var equipmentData);

                        if (isSuccess && grade == (Grade)synthesizedItem.Grade)
                        {
                            // If there are no items in the data that are one above the current grade, they cannot succeed.
                            isSuccess = false;
                        }

                        synthesizeResults.Add(new SynthesizeResult
                        {
                            ItemBase = synthesizedItem,
                            IsSuccess = isSuccess,
                            RecipeId = equipmentData.RecipeId,
                            SubRecipeId = equipmentData.SubRecipeId,
                        });
                    }
                }
            }

            return synthesizeResults;
        }

        /// <summary>
        /// Get the grade of the material items and return the dictionary of the grade and the number of items.
        /// </summary>
        /// <param name="materialIds">material item ids</param>
        /// <param name="avatarState">target avatar state</param>
        /// <param name="blockIndex">current block index</param>
        /// <param name="addressesHex">addresses in hex</param>
        /// <param name="materialEquipments">material equipment list</param>
        /// <param name="materialCostumes">material costume list</param>
        /// <returns></returns>
        /// <exception cref="InvalidMaterialException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static GradeDict GetGradeDict(List<Guid> materialIds, AvatarState avatarState, long blockIndex,
            string addressesHex, out List<Equipment> materialEquipments, out List<Costume> materialCostumes)
        {
            ItemSubType? cachedItemSubType = null;
            var gradeDict = new GradeDict();
            materialEquipments = new List<Equipment>();
            materialCostumes = new List<Costume>();

            foreach (var materialId in materialIds)
            {
                var materialEquipment = GetEquipmentFromId(materialId, avatarState, blockIndex, addressesHex, ref cachedItemSubType);
                var materialCostume = GetCostumeFromId(materialId, avatarState, addressesHex, ref cachedItemSubType);
                if (materialEquipment == null && materialCostume == null)
                {
                    throw new InvalidMaterialException(
                        $"{addressesHex} Aborted as the material item is not a valid item type."
                    );
                }

                if (materialEquipment != null)
                {
                    materialEquipments.Add(materialEquipment);
                    SetGradeDict(ref gradeDict, materialEquipment.Grade, materialEquipment.ItemSubType);
                }

                if (materialCostume != null)
                {
                    materialCostumes.Add(materialCostume);
                    SetGradeDict(ref gradeDict, materialCostume.Grade, materialCostume.ItemSubType);
                }
            }

            if (cachedItemSubType == null)
            {
                throw new InvalidOperationException("ItemSubType is not set.");
            }

            return gradeDict;
        }

        private static void SetGradeDict(ref GradeDict gradeDict, int grade, ItemSubType itemSubType)
        {
            if (gradeDict.ContainsKey(grade))
            {
                if (gradeDict[grade].ContainsKey(itemSubType))
                {
                    gradeDict[grade][itemSubType]++;
                }
                else
                {
                    gradeDict[grade][itemSubType] = 1;
                }
            }
            else
            {
                gradeDict[grade] = new Dictionary<ItemSubType, int>
                {
                    { itemSubType, 1 },
                };
            }
        }

        private static Equipment? GetEquipmentFromId(Guid materialId, AvatarState avatarState, long blockIndex, string addressesHex, ref ItemSubType? cachedItemSubType)
        {
            if (!avatarState.inventory.TryGetNonFungibleItem(materialId, out Equipment materialEquipment))
            {
                return null;
            }

            if (materialEquipment.RequiredBlockIndex > blockIndex)
            {
                throw new RequiredBlockIndexException(
                    $"{addressesHex} Aborted as the material ({materialId}) is not available yet;" +
                    $" it will be available at the block #{materialEquipment.RequiredBlockIndex}."
                );
            }

            // Validate item type
            if (!ValidItemType.Contains(materialEquipment.ItemType))
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a valid item type: {materialEquipment.ItemType}."
                );
            }

            if (!ValidItemSubType.Contains(materialEquipment.ItemSubType))
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a valid item sub type: {materialEquipment.ItemSubType}."
                );
            }

            cachedItemSubType ??= materialEquipment.ItemSubType;
            if (materialEquipment.ItemSubType != cachedItemSubType)
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a {cachedItemSubType}, but {materialEquipment.ItemSubType}."
                    );
            }

            return materialEquipment;
        }

        private static Costume? GetCostumeFromId(Guid materialId, AvatarState avatarState, string addressesHex, ref ItemSubType? cachedItemSubType)
        {
            if (!avatarState.inventory.TryGetNonFungibleItem(materialId, out Costume costumeItem))
            {
                return null;
            }

            // Validate item type
            if (!ValidItemType.Contains(costumeItem.ItemType))
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a valid item type: {costumeItem.ItemType}."
                );
            }

            if (!ValidItemSubType.Contains(costumeItem.ItemSubType))
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a valid item sub type: {costumeItem.ItemSubType}."
                );
            }

            cachedItemSubType ??= costumeItem.ItemSubType;
            if (costumeItem.ItemSubType != cachedItemSubType)
            {
                throw new InvalidMaterialException(
                    $"{addressesHex} Aborted as the material item is not a {cachedItemSubType}, but {costumeItem.ItemSubType}."
                    );
            }

            return costumeItem;
        }

        private static ItemBase GetSynthesizedItem(
            Grade grade,
            bool isSuccess,
            SynthesizeWeightSheet weightSheet,
            CostumeItemSheet costumeItemSheet,
            EquipmentItemSheet equipmentItemSheet,
            EquipmentItemRecipeSheet equipmentItemRecipeSheet,
            EquipmentItemSubRecipeSheetV2 equipmentItemSubRecipeSheetV2,
            EquipmentItemOptionSheet equipmentItemOptionSheet,
            SkillSheet skillSheet,
            long blockIndex,
            IRandom random,
            ItemSubType itemSubTypeValue,
            out EquipmentData equipmentData)
        {
            equipmentData = new EquipmentData();
            switch (itemSubTypeValue)
            {
                case ItemSubType.FullCostume:
                case ItemSubType.Title:
                    return GetRandomCostume(grade, isSuccess, itemSubTypeValue, weightSheet, costumeItemSheet, random);
                case ItemSubType.Aura:
                case ItemSubType.Grimoire:
                    return GetRandomEquipment(
                        grade,
                        isSuccess,
                        itemSubTypeValue,
                        weightSheet,
                        equipmentItemSheet,
                        equipmentItemRecipeSheet,
                        equipmentItemSubRecipeSheetV2,
                        equipmentItemOptionSheet,
                        skillSheet,
                        blockIndex,
                        random,
                        ref equipmentData);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

#region GetRandomItem

        private static ItemBase GetRandomCostume(Grade grade, bool isSuccess, ItemSubType itemSubType, SynthesizeWeightSheet weightSheet, CostumeItemSheet costumeItemSheet, IRandom random)
        {
            HashSet<int>? synthesizeResultPool = null;
            if (isSuccess)
            {
                synthesizeResultPool = GetSynthesizeResultPool(GetTargetGrade(grade), itemSubType, costumeItemSheet);
                if (synthesizeResultPool.Count == 0)
                {
                    synthesizeResultPool = GetSynthesizeResultPool(grade, itemSubType, costumeItemSheet);
                }
            }
            else
            {
                synthesizeResultPool = GetSynthesizeResultPool(grade, itemSubType, costumeItemSheet);
            }

            if (synthesizeResultPool.Count == 0)
            {
                throw new InvalidOperationException($"No available items to synthesize for grade {grade} and subtype {itemSubType}");
            }

            var randomValue = GetRandomValueForItem(grade, synthesizeResultPool, weightSheet, random, out var itemWeights);
            var cumulativeWeight = 0;
            foreach (var (itemId, weight) in itemWeights)
            {
                cumulativeWeight += weight;
                if (randomValue >= cumulativeWeight)
                {
                    continue;
                }

                if (!costumeItemSheet.TryGetValue(itemId, out var equipmentRow))
                {
                    throw new SheetRowNotFoundException(
                        $"Aborted as the equipment row ({itemId}) was failed to load in {nameof(EquipmentItemSheet)}", itemId
                    );
                }
                return ItemFactory.CreateItem(equipmentRow, random);
            }

            // Should not reach here
            throw new InvalidOperationException("Failed to select a synthesized item.");
        }

        private static ItemBase GetRandomEquipment(
            Grade grade,
            bool isSuccess,
            ItemSubType itemSubType,
            SynthesizeWeightSheet weightSheet,
            EquipmentItemSheet equipmentItemSheet,
            EquipmentItemRecipeSheet equipmentItemRecipeSheet,
            EquipmentItemSubRecipeSheetV2 equipmentItemSubRecipeSheetV2,
            EquipmentItemOptionSheet equipmentItemOptionSheet,
            SkillSheet skillSheet,
            long blockIndex,
            IRandom random,
            ref EquipmentData equipmentData)
        {
            HashSet<int>? synthesizeResultPool = null;
            if (isSuccess)
            {
                synthesizeResultPool = GetSynthesizeResultPool(GetTargetGrade(grade), itemSubType, equipmentItemSheet);
                if (synthesizeResultPool.Count == 0)
                {
                    synthesizeResultPool = GetSynthesizeResultPool(grade, itemSubType, equipmentItemSheet);
                }
            }
            else
            {
                synthesizeResultPool = GetSynthesizeResultPool(grade, itemSubType, equipmentItemSheet);
            }

            if (synthesizeResultPool.Count == 0)
            {
                throw new InvalidOperationException($"No available items to synthesize for grade {grade} and subtype {itemSubType}");
            }

            var randomValue = GetRandomValueForItem(grade, synthesizeResultPool, weightSheet, random, out var itemWeights);
            var cumulativeWeight = 0;
            foreach (var (itemId, weight) in itemWeights)
            {
                cumulativeWeight += weight;
                if (randomValue >= cumulativeWeight)
                {
                    continue;
                }

                if (!equipmentItemSheet.TryGetValue(itemId, out var equipmentRow))
                {
                    throw new SheetRowNotFoundException(
                        $"Aborted as the equipment row ({itemId}) was failed to load in {nameof(EquipmentItemSheet)}", itemId
                    );
                }

                // find first recipe (see AuraSummon.SimulateSummon)
                var recipeRow = equipmentItemRecipeSheet.Values.FirstOrDefault(r => r.ResultEquipmentId == itemId);
                if (recipeRow == null)
                {
                    throw new SheetRowNotFoundException(
                        $"Aborted as the recipe row for equipment ({itemId}) was failed to load in {nameof(EquipmentItemRecipeSheet)}", itemId
                    );
                }
                equipmentData.RecipeId = recipeRow.Id;

                // Validate subRecipeId
                if (recipeRow.SubRecipeIds.Count == 0)
                {
                    throw new InvalidRecipeIdException(
                        $"Recipe of item id({itemId}) does not have any subRecipe.");
                }

                // find first sub recipe (see AuraSummon.SimulateSummon)
                var subRecipeId = recipeRow.SubRecipeIds[0];
                if (!equipmentItemSubRecipeSheetV2.TryGetValue(subRecipeId, out var subRecipeRow))
                {
                    throw new SheetRowNotFoundException(
                        nameof(EquipmentItemSubRecipeSheetV2),
                        subRecipeId
                    );
                }
                equipmentData.SubRecipeId = subRecipeId;

                var equipment = (Equipment)ItemFactory.CreateItemUsable(
                    equipmentRow,
                    random.GenerateRandomGuid(),
                    blockIndex
                );

                AuraSummon.AddAndUnlockOption(
                    equipment,
                    random,
                    subRecipeRow,
                    equipmentItemOptionSheet,
                    skillSheet
                );

                return equipment;
            }

            // Should not reach here
            throw new InvalidOperationException("Failed to select a synthesized item.");
        }

        private static int GetRandomValueForItem(Grade grade, HashSet<int> synthesizeResultPool, SynthesizeWeightSheet synthesizeWeightSheet,
                                          IRandom random, out List<(int ItemId, int Weight)> itemWeights)
        {
            var totalWeight = 0;
            itemWeights = new List<(int ItemId, int Weight)>();
            foreach (var itemId in synthesizeResultPool)
            {
                var weight = GetWeight(grade, itemId, synthesizeWeightSheet);
                itemWeights.Add((itemId, weight));
                totalWeight += weight;
            }

            // Random selection based on weight
            return random.Next(totalWeight + 1);
        }

#endregion GetRandomItem

#region Helper

        /// <summary>
        /// get expected result item pool (Costume)
        /// </summary>
        /// <param name="sourceGrade">grade of material items</param>
        /// <param name="subType">subtype of material items</param>
        /// <param name="sheet">costume item sheet to use</param>
        /// <returns>expected result item pool of costume</returns>
        public static HashSet<int> GetSynthesizeResultPool(Grade sourceGrade, ItemSubType subType, CostumeItemSheet sheet)
        {
            return sheet.Values
                        .Where(r => r.ItemSubType == subType)
                        .Where(r => (Grade)r.Grade == sourceGrade)
                        .Select(r => r.Id)
                        .ToHashSet();
        }

        /// <summary>
        /// get expected result item pool (Equipment)
        /// </summary>
        /// <param name="sourceGrade">grade of material items</param>
        /// <param name="subType">subtype of material items</param>
        /// <param name="sheet">equipment item sheet to use</param>
        /// <returns>expected result item pool of equipment</returns>
        public static HashSet<int> GetSynthesizeResultPool(Grade sourceGrade, ItemSubType subType, EquipmentItemSheet sheet)
        {
            return sheet.Values
                        .Where(r => r.ItemSubType == subType)
                        .Where(r => (Grade)r.Grade == sourceGrade)
                        .Select(r => r.Id)
                        .ToHashSet();
        }

        /// <summary>
        /// Returns a list of items that may come out as a result of that synthesis.
        /// </summary>
        /// <param name="sourceGrades">grades of material items</param>
        /// <param name="subType">excepted FullCostume,Title</param>
        /// <param name="sheet">CostumeItemSheet to use</param>
        /// <returns>list of items key(int)</returns>
        public static List<int> GetSynthesizeResultPool(List<Grade> sourceGrades, ItemSubType subType, CostumeItemSheet sheet)
        {
            return sheet
                   .Values
                   .Where(r => r.ItemSubType == subType)
                   .Where(r => sourceGrades.Any(grade => (Grade)r.Grade == GetUpgradeGrade(grade, subType, sheet)))
                   .Select(r => r.Id)
                   .ToList();
        }

        /// <summary>
        /// Returns a list of items that may come out as a result of that synthesis.
        /// </summary>
        /// <param name="sourceGrades">grades of material items</param>
        /// <param name="subType">excepted Grimoire,Aura</param>
        /// <param name="sheet">EquipmentItemSheet to use</param>
        /// <returns>list of items key(int)</returns>
        public static List<int> GetSynthesizeResultPool(List<Grade> sourceGrades, ItemSubType subType, EquipmentItemSheet sheet)
        {
            return sheet
                   .Values
                   .Where(r => r.ItemSubType == subType)
                   .Where(r => sourceGrades.Any(grade => (Grade)r.Grade == GetUpgradeGrade(grade, subType, sheet)))
                   .Select(r => r.Id)
                   .ToList();
        }

        /// <summary>
        /// Returns the grade of the item that can be obtained by synthesizing the item. (Costume)
        /// </summary>
        /// <param name="grade">source grade</param>
        /// <param name="subType">subtype of material items</param>
        /// <param name="sheet">costume item sheet to use</param>
        /// <returns>grade of the item that can be obtained by synthesizing the item</returns>
        public static Grade GetUpgradeGrade(Grade grade ,ItemSubType subType, CostumeItemSheet sheet)
        {
            var targetGrade = GetTargetGrade(grade);
            var hasGrade = sheet.Values
                .Any(r => r.ItemSubType == subType && r.Grade == (int)targetGrade);

            return hasGrade ? targetGrade : grade;
        }

        /// <summary>
        /// Returns the grade of the item that can be obtained by synthesizing the item. (Equipment)
        /// </summary>
        /// <param name="grade">source grade</param>
        /// <param name="subType">subtype of material items</param>
        /// <param name="sheet">equipment item sheet to use</param>
        /// <returns>grade of the item that can be obtained by synthesizing the item</returns>
        public static Grade GetUpgradeGrade(Grade grade ,ItemSubType subType, EquipmentItemSheet sheet)
        {
            var targetGrade = GetTargetGrade(grade);
            var hasGrade = sheet.Values
                .Any(r => r.ItemSubType == subType && r.Grade == (int)targetGrade);

            return hasGrade ? targetGrade : grade;
        }

        /// <summary>
        /// Returns the weight of the item that can be obtained by synthesizing the item.
        /// </summary>
        /// <param name="grade">source grade</param>
        /// <param name="itemId">item id of material item</param>
        /// <param name="sheet">SynthesizeWeightSheet to use</param>
        /// <returns>weight of the item that can be obtained by synthesizing the item</returns>
        public static int GetWeight(Grade grade, int itemId, SynthesizeWeightSheet sheet)
        {
            var defaultWeight = SynthesizeWeightSheet.DefaultWeight;
            var gradeRow = sheet.Values.FirstOrDefault(r => r.GradeId == (int)grade);
            return gradeRow == null ? defaultWeight : gradeRow.WeightDict.GetValueOrDefault(itemId, defaultWeight);
        }

        // TODO: move to ItemExtensions
        /// <summary>
        /// Get the item id from the item.
        /// </summary>
        /// <param name="itemBase">item to get id</param>
        /// <returns>item id</returns>
        /// <exception cref="ArgumentException"></exception>
        public static List<Guid> GetItemGuid(ItemBase itemBase) => itemBase switch
        {
            Costume costume => new List<Guid> { costume.ItemId, },
            ItemUsable itemUsable => new List<Guid> { itemUsable.ItemId, },
            _ => throw new ArgumentException($"Unexpected item type: {itemBase.GetType()}", nameof(itemBase)),
        };

        /// <summary>
        /// Get the item id from the item.
        /// </summary>
        /// <param name="itemBases">items to get id</param>
        /// <returns>item id list</returns>
        /// <exception cref="ArgumentException"></exception>
        public static List<Guid> GetItemGuids(IEnumerable<ItemBase> itemBases) => itemBases.Select(
            i =>
            {
                return i switch
                {
                    Costume costume => costume.ItemId,
                    ItemUsable itemUsable => itemUsable.ItemId,
                    _ => throw new ArgumentException($"Unexpected item type: {i.GetType()}", nameof(i)),
                };
            }).ToList();

        private static Grade GetTargetGrade(Grade grade) => grade switch
        {
            Grade.Normal => Grade.Rare,
            Grade.Rare => Grade.Epic,
            Grade.Epic => Grade.Unique,
            Grade.Unique => Grade.Legendary,
            Grade.Legendary => Grade.Divinity,
            Grade.Divinity => Grade.Divinity,
            _ => throw new ArgumentOutOfRangeException(nameof(grade), grade, null),
        };

        /// <summary>
        /// Get the target grade of the item.
        /// max grade is Divinity
        /// </summary>
        /// <param name="gradeId">grade id of the item</param>
        /// <returns>target grade id</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static int GetTargetGrade(int gradeId)
        {
            return gradeId switch
            {
                1 => 2, // Grade.Normal => Grade.Rare
                2 => 3, // Grade.Rare => Grade.Epic
                3 => 4, // Grade.Epic => Grade.Unique
                4 => 5, // Grade.Unique => Grade.Legendary
                5 => 6, // Grade.Legendary => Grade.Divinity
                6 => 6, // Grade.Divinity => Grade.Divinity (Max)
                _ => throw new ArgumentOutOfRangeException(nameof(gradeId), gradeId, null),
            };
        }
#endregion Helper
    }
}
