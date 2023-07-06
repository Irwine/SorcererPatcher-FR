using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Noggog;

namespace SorcererPatcher
{
    public class Settings
    {
        [SettingName("Patch against entire load order")]
        public bool PatchFullLoadOrder = true;
        [SettingName("Patch against a single mod (e.g.: Sorcerer.esp)")]
        public string PatchSingleMod = "";
    }

    public class Program
    {
        private static Lazy<Settings> _settings = null!;

        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings("settings", "settings.json", out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "SorcererRecipeGenerator.esp")
                .Run(args);
        }

        private static readonly ModKey Sorcerer = ModKey.FromNameAndExtension("Sorcerer.esp");
        private static readonly ModKey Mysticism = ModKey.FromNameAndExtension("MysticismMagic.esp");
        private static ModKey _modToPatch;

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (_settings.Value is { PatchFullLoadOrder: false, PatchSingleMod: "" })
                throw new Exception("ERROR: Please provide the full name of a mod to patch (e.g.: Sorcerer.esp");

            if (!state.LoadOrder.ContainsKey(Sorcerer))
                throw new Exception("ERROR: Sorcerer.esp not found in load order");

            if (!state.LoadOrder.ContainsKey(Mysticism))
                throw new Exception("ERROR: MysticismMagic.esp not found in load order");

            if (_settings.Value.PatchSingleMod != "")
                _modToPatch = ModKey.FromNameAndExtension(_settings.Value.PatchSingleMod);

            var scrollWorkbenchKywd = state.LinkCache.Resolve<IKeywordGetter>("MAG_TableScrollEnchanter").ToNullableLink();
            var staffWorkbenchKywd = state.LinkCache.Resolve<IKeywordGetter>("MAG_TableStaffEnchanter").ToNullableLink();
            var vanillaStaffWorkbenchKywd = state.LinkCache.Resolve<IKeywordGetter>("DLC2StaffEnchanter").ToNullableLink();
            var scrollResearchKywd = state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollResearchNotes").ToNullableLink();
            var soulGemCommon = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemCommonFilled").ToNullableLink();
            var soulGemGreater = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemGreaterFilled").ToNullableLink();
            var soulGemGrand = state.LinkCache.Resolve<ISoulGemGetter>("SoulGemGrandFilled").ToNullableLink();
            var pickUpSound = state.LinkCache.Resolve<ISoundDescriptorGetter>("ITMNoteUp").ToNullableLink();
            var inventoryArt = state.LinkCache.Resolve<IStaticGetter>("MAG_ResearchItemScroll").ToNullableLink();
            var ink = state.LinkCache.Resolve<IMiscItemGetter>("MAG_EnchantedInk").ToNullableLink();
            var paper = state.LinkCache.Resolve<IMiscItemGetter>("MAG_ScrollPaper").ToNullableLink();
            var concEffect = state.LinkCache.Resolve<IMagicEffectGetter>("MAG_StaffEnchConcAimed").ToNullableLink();
            var staffKywd = state.LinkCache.Resolve<IKeywordGetter>("WeapTypeStaff").ToNullableLink();
            var scrollAlterationKywd =
                state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollTypeAlteration").ToNullableLink();
            var scrollConjurationKywd =
                state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollTypeConjuration").ToNullableLink();
            var scrollDestructionKywd =
                state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollTypeDestruction").ToNullableLink();
            var scrollIllusionywd =
                state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollTypeIllusion").ToNullableLink();
            var scrollRestorationKywd =
                state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollTypeRestoration").ToNullableLink();
            var conc = new Effect()
            {
                BaseEffect = concEffect,
                Data = new EffectData()
                {
                    Magnitude = 0.0f,
                    Duration = 0,
                    Area = 0
                }
            };

            var scrollCollection = _settings.Value.PatchFullLoadOrder
                ? state.LoadOrder.PriorityOrder.Scroll().WinningOverrides()
                : state.LoadOrder.TryGetValue(_modToPatch)!.Mod!.Scrolls;

            // Scrolls
            foreach (var scroll in scrollCollection)
            {
                var sName = scroll.Name!.ToString()!;
                var edid = scroll.EditorID!;

                if (edid.Contains("MAG_") || sName.Contains("Shalidor") || sName.Contains("J'zargo") || sName.Contains("Spider")) continue;

                Console.WriteLine($"Processing scroll: {scroll.Name}");

                // Determine recipe based on minimum skill level of costliest magic effect
                var max = 0.0f;
                uint costliestEffectLevel = 0;
                ActorValue costliestEffectSkill = new();

                // Find minimum skill level of magic effect with the highest base cost
                foreach (var effect in scroll.Effects)
                {
                    var record = state.LinkCache.Resolve<IMagicEffectGetter>(effect.BaseEffect.FormKey);
                    if (!(record.BaseCost > max)) continue;
                    max = record.BaseCost;
                    costliestEffectLevel = record.MinimumSkillLevel;
                    costliestEffectSkill = record.MagicSkill;
                }

                // Rectify scroll value
                var patched = state.PatchMod.Scrolls.GetOrAddAsOverride(scroll);
                var prevValue = patched.Value;
                patched.Value = costliestEffectLevel switch
                {
                    < 25 => 15,
                    >= 25 and < 50 => 30,
                    >= 50 and < 75 => 55,
                    >= 75 and < 100 => 100,
                    >= 100 => 160
                };

                // Add scroll type keyword
                switch (costliestEffectSkill)
                {
                    case ActorValue.Alteration:
                        patched.Keywords!.Add(scrollAlterationKywd);
                        break;
                    case ActorValue.Conjuration:
                        patched.Keywords!.Add(scrollConjurationKywd);
                        break;
                    case ActorValue.Destruction:
                        patched.Keywords!.Add(scrollDestructionKywd);
                        break;
                    case ActorValue.Illusion:
                        patched.Keywords!.Add(scrollIllusionywd);
                        break;
                    case ActorValue.Restoration:
                        patched.Keywords!.Add(scrollRestorationKywd);
                        break;
                }

                // Remove scroll from patch if record is unchanged
                if (patched.Equals(scroll))
                    state.PatchMod.Remove(scroll);

                var recipes = new List<(int, int, ushort)> // (scroll paper, enchanted ink, # of scrolls created)
                {
                    (2, 8, 2), // Master
                    (2, 5, 2), // Expert
                    (3, 4, 3), // Adept
                    (4, 3, 4), // Apprentice
                    (5, 2, 5) // Novice
                };

                var recipeToUse = costliestEffectLevel switch
                {
                    < 25 => recipes[4],
                    >= 25 and < 50 => recipes[3],
                    >= 50 and < 75 => recipes[2],
                    >= 75 and < 100 => recipes[1],
                    >= 100 => recipes[0]
                };

                var book = state.PatchMod.Books.AddNew();
                var perk = state.PatchMod.Perks.AddNew();
                var recipe = state.PatchMod.ConstructibleObjects.AddNew();
                var breakdownRecipe = state.PatchMod.ConstructibleObjects.AddNew();

                var name = scroll.Name!.ToString()!.Replace("Scroll of the ", "").Replace("Scroll of ", "");
                var nameStripped = name.Replace(" ", "");

                // Book logic
                book.EditorID = "MAG_ResearchNotes" + nameStripped;
                book.Name = "Research Notes: " + name;
                book.Weight = 0;
                book.Value = costliestEffectLevel switch
                {
                    < 25 => 100,
                    >= 25 and < 50 => 200,
                    >= 50 and < 75 => 300,
                    >= 75 and < 100 => 500,
                    >= 100 => 800
                };
                book.PickUpSound = pickUpSound;
                book.BookText = book.Name;
                book.Description = scroll.Name!.ToString()!.Contains("of the") switch
                {
                    true => $"Allows you to craft Scrolls of the " + name + ".",
                    false => $"Allows you to craft Scrolls of " + name + "."
                };
                book.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>
                {
                    scrollResearchKywd
                };
                book.InventoryArt = inventoryArt;
                book.Model = new Model
                {
                    File = "Clutter\\Common\\Scroll05.nif",
                };
                ScriptProperty attachedBook = new ScriptObjectProperty
                {
                    Name = "AttachedBook",
                    Object = book.ToNullableLink()
                };
                ScriptProperty craftingPerk = new ScriptObjectProperty
                {
                    Name = "CraftingPerk",
                    Object = perk.ToNullableLink()
                };
                book.VirtualMachineAdapter = new VirtualMachineAdapter
                {
                    Scripts = new ExtendedList<ScriptEntry>
                    {
                        new()
                        {
                            Name = "MAG_ResearchItem_Script",
                            Properties = new ExtendedList<ScriptProperty>
                            {
                                attachedBook, craftingPerk
                            }
                        }
                    }
                };
                Console.WriteLine($"    Generated research notes");

                // Perk logic
                perk.EditorID = "MAG_ResearchPerk" + nameStripped;
                perk.Name = name + " Research Perk";
                perk.Playable = true;
                perk.Hidden = true;
                perk.Level = 0;
                perk.NumRanks = 1;
                Console.WriteLine($"    Generated perk");

                // Recipe logic
                recipe.EditorID = "MAG_RecipeScroll" + nameStripped;
                recipe.CreatedObject = scroll.ToNullableLink();
                recipe.CreatedObjectCount = recipeToUse.Item3;
                recipe.WorkbenchKeyword = scrollWorkbenchKywd;
                var hasPerkCondData = new HasPerkConditionData();
                hasPerkCondData.Perk.Link.SetTo(perk);
                Condition hasPerk = new ConditionFloat
                {
                    CompareOperator = CompareOperator.EqualTo,
                    ComparisonValue = 1.0f,
                    Data = hasPerkCondData
                };
                recipe.Items = new ExtendedList<ContainerEntry>
                {
                    new()
                    {
                        Item = new ContainerItem
                        {
                            Item = ink,
                            Count = recipeToUse.Item2
                        }
                    },
                    new()
                    {
                        Item = new ContainerItem
                        {
                            Item = paper,
                            Count = recipeToUse.Item1
                        }
                    }
                };
                recipe.Conditions.Add(hasPerk);
                Console.WriteLine($"    Generated recipe");

                // Breakdown recipe logic
                breakdownRecipe.EditorID = "MAG_BreakdownRecipeScroll" + nameStripped;
                breakdownRecipe.CreatedObject = book.ToNullableLink();
                breakdownRecipe.CreatedObjectCount = 1;
                breakdownRecipe.WorkbenchKeyword = scrollWorkbenchKywd;
                var hasScrollsCondData = new GetItemCountConditionData();
                hasScrollsCondData.ItemOrList.Link.SetTo(scroll);
                Condition hasScrolls = new ConditionFloat
                {
                    CompareOperator = CompareOperator.GreaterThanOrEqualTo,
                    ComparisonValue = 1.0f,
                    Data = hasScrollsCondData
                };
                Condition noPerk = new ConditionFloat
                {
                    CompareOperator = CompareOperator.EqualTo,
                    ComparisonValue = 0.0f,
                    Data = hasPerkCondData
                };
                breakdownRecipe.Items = new ExtendedList<ContainerEntry>
                {
                    new()
                    {
                        Item = new ContainerItem
                        {
                            Item = scroll.ToLink(),
                            Count = 1
                        }
                    }
                };
                breakdownRecipe.Conditions.Add(noPerk);
                breakdownRecipe.Conditions.Add(hasScrolls);
                Console.WriteLine($"    Generated breakdown recipe");
            }

            var staffEnchCollection = _settings.Value.PatchFullLoadOrder
                ? state.LoadOrder.PriorityOrder.ObjectEffect().WinningOverrides()
                : state.LoadOrder.TryGetValue(_modToPatch)!.Mod!.ObjectEffects;

            // Staff enchantments
            foreach (var ench in staffEnchCollection)
            {
                if (!ench.EditorID!.Contains("Staff") || ench.EditorID.Contains("MAG_")) continue;

                Console.WriteLine($"Processing staff enchantment: {ench.Name}");

                var patched = state.PatchMod.ObjectEffects.GetOrAddAsOverride(ench);
                var max = 0.0f;
                uint costliestEffectLevel = 0;

                foreach (var effect in ench.Effects)
                {
                    var record = state.LinkCache.Resolve<IMagicEffectGetter>(effect.BaseEffect.FormKey);
                    if (!(record.BaseCost > max)) continue;
                    max = record.BaseCost;
                    costliestEffectLevel = record.MinimumSkillLevel;
                }

                patched.EnchantmentAmount = costliestEffectLevel switch
                {
                    < 25 => 20,
                    >= 25 and < 50 => 30,
                    >= 50 and < 75 => 60,
                    >= 75 and < 100 => 120,
                    >= 100 => 250
                };

                patched.EnchantmentCost = Convert.ToUInt32(patched.EnchantmentAmount);

                if (patched is {CastType: CastType.Concentration, TargetType: TargetType.Aimed})
                    patched.Effects.Add(conc);

                Console.WriteLine($"Finished processing {patched.Name}");
            }

            var staffCollection = _settings.Value.PatchFullLoadOrder
                ? state.LoadOrder.PriorityOrder.Weapon().WinningOverrides()
                : state.LoadOrder.TryGetValue(_modToPatch)!.Mod!.Weapons;

            // Staves
            foreach (var staff in staffCollection)
            {
                if (!staff.HasKeyword(staffKywd) || staff.EditorID!.Contains("MAG_")) continue;

                var patched = state.PatchMod.Weapons.GetOrAddAsOverride(staff);

                state.LinkCache.TryResolve<IObjectEffectGetter>(staff.ObjectEffect.FormKey, out var ench);
                var max = 0.0f;
                uint costliestEffectLevel = 0;

                if (ench is null) continue;

                Console.WriteLine($"Processing staff: {staff.Name}");

                foreach (var effect in ench.Effects)
                {
                    var record = state.LinkCache.Resolve<IMagicEffectGetter>(effect.BaseEffect.FormKey);
                    if (!(record.BaseCost > max)) continue;
                    max = record.BaseCost;
                    costliestEffectLevel = record.MinimumSkillLevel;
                }

                patched.EnchantmentAmount = costliestEffectLevel switch
                {
                    < 25 => 500,
                    >= 25 and < 50 => 750,
                    >= 50 and < 75 => 1500,
                    >= 75 and < 100 => 3000,
                    >= 100 => 5000
                };

                if (patched.Equals(staff))
                    state.PatchMod.Remove(patched);

                Console.WriteLine($"Finished processing staff: {staff.Name}");
            }

            var staffRecipeCollection = _settings.Value.PatchFullLoadOrder
                ? state.LoadOrder.PriorityOrder.ConstructibleObject().WinningOverrides()
                : state.LoadOrder.TryGetValue(_modToPatch)!.Mod!.ConstructibleObjects;

            // Staff recipes
            foreach (var staffRecipe in staffRecipeCollection)
            {
                var edid = staffRecipe.EditorID!;
                if (edid.Contains("MAG_") || !staffRecipe.WorkbenchKeyword.Equals(vanillaStaffWorkbenchKywd)) continue;

                if (state.LinkCache.TryResolve<IWeaponGetter>(staffRecipe.CreatedObject.FormKey, out var staff))
                {
                    Console.WriteLine($"Processing staff: {staff.Name}");

                    var newRecipe = state.PatchMod.ConstructibleObjects.GetOrAddAsOverride(staffRecipe);

                    newRecipe.EditorID += "Alt";
                    newRecipe.WorkbenchKeyword = staffWorkbenchKywd;
                    newRecipe.Items!.RemoveAt(0);

                    var ench = state.LinkCache.Resolve<IObjectEffectGetter>(staff.ObjectEffect.FormKey);
                    var max = 0.0f;
                    uint costliestEffectLevel = 0;

                    foreach (var effect in ench.Effects)
                    {
                        var record = state.LinkCache.Resolve<IMagicEffectGetter>(effect.BaseEffect.FormKey);
                        if (!(record.BaseCost > max)) continue;
                        max = record.BaseCost;
                        costliestEffectLevel = record.MinimumSkillLevel;
                    }

                    var recipes = new List<(IFormLink<ISoulGemGetter>, int)>
                    {
                        (soulGemCommon, 1),
                        (soulGemGreater, 1),
                        (soulGemGrand, 1),
                        (soulGemGrand, 2),
                        (soulGemGrand, 3),
                    };

                    var recipeToUse = costliestEffectLevel switch
                    {
                        < 25 => recipes[0],
                        >= 25 and < 50 => recipes[1],
                        >= 50 and < 75 => recipes[2],
                        >= 75 and < 100 => recipes[3],
                        >= 100 => recipes[4]
                    };

                    newRecipe.Items.Add(new ContainerEntry
                    {
                        Item = new ContainerItem
                        {
                            Item = recipeToUse.Item1,
                            Count = recipeToUse.Item2
                        }
                    });
                }
                else
                {
                    Console.WriteLine($"ERROR: Failed to process recipe {staffRecipe.EditorID}");
                }
            }
        }
    }
}
