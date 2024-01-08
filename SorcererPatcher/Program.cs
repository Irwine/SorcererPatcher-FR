#region

using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Noggog;

#endregion

namespace SorcererPatcher;

public class Settings
{
    [SettingName("Patcher l'ordre de chargement au complet")]
    public bool PatchFullLoadOrder = true;

    [SettingName("Ne patcher qu'un seul mod (e.g.: Sorcerer.esp)")]
    public string PatchSingleMod = "";
}

public class Program
{
    private static Lazy<Settings> _settings = null!;

    private static readonly ModKey Sorcerer = ModKey.FromNameAndExtension("Sorcerer.esp");
    private static readonly ModKey Mysticism = ModKey.FromNameAndExtension("MysticismMagic.esp");
    private static ModKey _modToPatch;

    public static async Task<int> Main(string[] args)
    {
        return await SynthesisPipeline.Instance
            .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
            .SetAutogeneratedSettings("settings", "settings.json", out _settings)
            .SetTypicalOpen(GameRelease.SkyrimSE, "SorcererPatcher.esp")
            .Run(args);
    }

    public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
    {
        if (_settings.Value is { PatchFullLoadOrder: false, PatchSingleMod: "" })
            throw new Exception("ERROR: Please provide the full name of a mod to patch (e.g.: Sorcerer.esp");

        if (!state.LoadOrder.ContainsKey(Sorcerer))
            throw new Exception("ERROR: Sorcerer.esp not found in load order");

        if (!state.LoadOrder.ContainsKey(Mysticism))
            throw new Exception("ERROR: MysticismMagic.esp not found in load order");

        if (_settings.Value.PatchSingleMod != "")
        {
            Console.WriteLine($"Patching single mod: {_settings.Value.PatchSingleMod}");
            _modToPatch = ModKey.FromNameAndExtension(_settings.Value.PatchSingleMod);
        }

        var scrollWorkbenchKywd =
            state.LinkCache.Resolve<IKeywordGetter>("MAG_TableScrollEnchanter").ToNullableLink();
        var staffWorkbenchKywd =
            state.LinkCache.Resolve<IKeywordGetter>("MAG_TableStaffEnchanter").ToNullableLink();
        var vanillaStaffWorkbenchKywd =
            state.LinkCache.Resolve<IKeywordGetter>("DLC2StaffEnchanter").ToNullableLink();
        var scrollResearchKywd =
            state.LinkCache.Resolve<IKeywordGetter>("MAG_ScrollResearchNotes").ToNullableLink();
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
        var magicSkills = new HashSet<ActorValue>
        {
            ActorValue.Alteration, ActorValue.Conjuration, ActorValue.Destruction, ActorValue.Illusion,
            ActorValue.Restoration
        };
        var conc = new Effect
        {
            BaseEffect = concEffect,
            Data = new EffectData
            {
                Magnitude = 0.0f,
                Duration = 0,
                Area = 0
            }
        };
        var heartStone = state.LinkCache.Resolve<IMiscItemGetter>("DLC2HeartStone").ToNullableLink();

        HashSet<string> uniqueStaves = new()
        {
            "Bâton de prêtre-dragon",
            "Oeil de Melka",
            "Bâton d'ensorcellement de Gadnor",
            "Bâton d'Halldir",
            "Bâton d'Hevnoraak",
            "Rose sanghine",
            "Crâne de la Corruption",
            "Baguette d'arachnologie",
            "Bâton de prépotence",
            "Bâton de la Mégère courroucée",
            "Bâton de Jyrik Gauldurson",
            "Bâton de Magnus",
            "Bâton de Tandil",
            "Wabbajack",
            "Bâton en aethérium",
            "Bâton de Ruunvald",
            "Bâton de Miraak",
            "Bâton d'Hasedoki",
            "Arme de la Lune",
            "Arme du Soleil",
            "Bâton de Shéogorath",
            "Bâton des vers"
        };

        HashSet<string> brumaUniqueStaves = new()
        {
            "CYRGonterFarmMS01SheepStaff",
            "CYRWoodenStaffOfAwesomeConflagration",
            "CYRRodOfPotency",
            "CYRBladeOfPrepotence",
            "CYRStaffOfTitanSummoning",
            "CYRSceptreOfFrostyEntombment"
        };

        HashSet<FormKey> staffEnchantmentsToSkip = new();

        // Scrolls
        var scrollCollection = _settings.Value.PatchFullLoadOrder
            ? state.LoadOrder.PriorityOrder.Scroll().WinningOverrides()
            : state.LoadOrder.TryGetValue(_modToPatch)?.Mod?.Scrolls;

        var scrollCount = 0;

        Console.WriteLine();

        if (scrollCollection != null)
            foreach (var scroll in scrollCollection)
            {
                var sName = scroll.Name?.ToString();
                var edid = scroll.EditorID;

                if (sName != null && edid != null &&
                    (edid.Contains("MAG_") || sName.Contains("Shalidor") || sName.Contains("J'zargo") ||
                     sName.Contains("Araignée"))) continue;

                Console.WriteLine(
                    $"Processing scroll: {scroll.Name} (0x{scroll.FormKey.ID:X}: {scroll.FormKey.ModKey.FileName})");

                // Determine recipe based on minimum skill level of costliest magic effect
                var max = 0.0f;
                uint costliestEffectLevel = 0;
                ActorValue costliestEffectSkill = new();

                // Find minimum skill level of magic effect with the highest base cost
                foreach (var effect in scroll.Effects)
                {
                    state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var record);
                    if (record is null) continue;
                    if (!(record.BaseCost > max)) continue;
                    max = record.BaseCost;
                    costliestEffectLevel = record.MinimumSkillLevel;
                    if (magicSkills.Contains(record.MagicSkill))
                        costliestEffectSkill = record.MagicSkill;
                }

                // Rectify scroll value
                var patched = state.PatchMod.Scrolls.GetOrAddAsOverride(scroll);
                patched.Value = costliestEffectLevel switch
                {
                    < 25 => 15,
                    >= 25 and < 50 => 30,
                    >= 50 and < 75 => 55,
                    >= 75 and < 100 => 100,
                    >= 100 => 160
                };

                patched.Keywords ??= new ExtendedList<IFormLinkGetter<IKeywordGetter>>();

                switch (costliestEffectSkill)
                {
                    case ActorValue.Alteration:
                        patched.Keywords.Add(scrollAlterationKywd);
                        break;
                    case ActorValue.Conjuration:
                        patched.Keywords.Add(scrollConjurationKywd);
                        break;
                    case ActorValue.Destruction:
                        patched.Keywords.Add(scrollDestructionKywd);
                        break;
                    case ActorValue.Illusion:
                        patched.Keywords.Add(scrollIllusionywd);
                        break;
                    case ActorValue.Restoration:
                        patched.Keywords.Add(scrollRestorationKywd);
                        break;
                }

                // Remove scroll from patch if record is unchanged
                if (patched.Value == scroll.Value)
                    state.PatchMod.Remove(patched);

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

                var notes = state.PatchMod.Books.AddNew();
                var perk = state.PatchMod.Perks.AddNew();
                var recipe = state.PatchMod.ConstructibleObjects.AddNew();
                var breakdownRecipe = state.PatchMod.ConstructibleObjects.AddNew();

                var name = sName?.Replace("Scroll of the ", "").Replace("Scroll of ", "");
                var nameStripped = name?.Replace(" ", "");

                // Book logic
                notes.EditorID = "MAG_ResearchNotes" + nameStripped;
                notes.Name = "Notes de recherches - " + name;
                notes.Weight = 0;
                notes.Value = costliestEffectLevel switch
                {
                    < 25 => 100,
                    >= 25 and < 50 => 200,
                    >= 50 and < 75 => 300,
                    >= 75 and < 100 => 500,
                    >= 100 => 800
                };
                notes.PickUpSound = pickUpSound;
                notes.BookText = notes.Name;
                notes.Description = (name != null && name.Contains("of the")) switch
                {
                    true => $"Vous permet de fabriquer : {name}.",
                    false => $"Vous permet de fabriquer : {name}."
                };
                notes.Keywords = new ExtendedList<IFormLinkGetter<IKeywordGetter>>
                {
                    scrollResearchKywd
                };
                notes.InventoryArt = inventoryArt;
                notes.Model = new Model
                {
                    File = @"Clutter\Common\Scroll05.nif"
                };
                ScriptProperty attachedBook = new ScriptObjectProperty
                {
                    Name = "AttachedBook",
                    Object = notes.ToNullableLink()
                };
                ScriptProperty craftingPerk = new ScriptObjectProperty
                {
                    Name = "CraftingPerk",
                    Object = perk.ToNullableLink()
                };
                notes.VirtualMachineAdapter = new VirtualMachineAdapter
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
                Console.WriteLine($"\tGenerated research notes for {scroll.Name} (0x{scroll.FormKey.ID:X})");

                // Perk logic
                perk.EditorID = "MAG_ResearchPerk" + nameStripped;
                perk.Name = name + " Research Perk";
                perk.Playable = true;
                perk.Hidden = true;
                perk.Level = 0;
                perk.NumRanks = 1;
                Console.WriteLine($"\tGenerated perk for {scroll.Name} (0x{scroll.FormKey.ID:X})");

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
                Console.WriteLine($"\tGenerated recipe for {scroll.Name} (0x{scroll.FormKey.ID:X})");

                // Breakdown recipe logic
                breakdownRecipe.EditorID = "MAG_BreakdownRecipeScroll" + nameStripped;
                breakdownRecipe.CreatedObject = notes.ToNullableLink();
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
                Console.WriteLine($"\tGenerated breakdown recipe for {scroll.Name} (0x{scroll.FormKey.ID:X})");

                scrollCount++;
            }

        // Staves
        var staffCollection = _settings.Value.PatchFullLoadOrder
            ? state.LoadOrder.PriorityOrder.Weapon().WinningOverrides()
            : state.LoadOrder.TryGetValue(_modToPatch)?.Mod?.Weapons;

        var staffCount = 0;

        if (staffCollection != null)
            foreach (var staff in staffCollection)
            {
                if (staff.EditorID is not null && (!staff.HasKeyword(staffKywd) || staff.EditorID.Contains("MAG_") ||
                                                   staff.EditorID.Contains("Template"))) continue;

                state.LinkCache.TryResolve<IObjectEffectGetter>(staff.ObjectEffect.FormKey, out var ench);

                if (ench is null)
                {
                    Console.WriteLine(
                        $"WARNING: {staff.Name} (0x{staff.FormKey.ID:X}) does not have an enchantment, skipping...");
                    continue;
                }

                if ((staff.FormKey.ModKey.FileName.ToString().Equals("BSHeartland.esm")
                     && staff.EditorID is not null
                     && brumaUniqueStaves.Contains(staff.EditorID))
                    || (staff.Name?.String != null && uniqueStaves.Contains(staff.Name.String)))
                {
                    staffEnchantmentsToSkip.Add(ench.FormKey);
                    Console.WriteLine($"Skipping unique staff {staff.Name} (0x{staff.FormKey.ID:X})");
                    continue;
                }

                var patched = state.PatchMod.Weapons.GetOrAddAsOverride(staff);

                var max = 0.0f;
                uint costliestEffectLevel = 0;

                Console.WriteLine(
                    $"Processing staff: {staff.Name} (0x{staff.FormKey.ID:X}: {staff.FormKey.ModKey.FileName})");

                foreach (var effect in ench.Effects)
                {
                    state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var record);
                    if (record is null) continue;
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

                if (patched.EnchantmentAmount == staff.EnchantmentAmount)
                    state.PatchMod.Remove(patched);

                Console.WriteLine(
                    $"\tFinished processing {staff.Name} (0x{staff.FormKey.ID:X}: {staff.FormKey.ModKey.FileName})");

                staffCount++;
            }

        // Staff enchantments
        var staffEnchCollection = _settings.Value.PatchFullLoadOrder
            ? state.LoadOrder.PriorityOrder.ObjectEffect().WinningOverrides()
            : state.LoadOrder.TryGetValue(_modToPatch)?.Mod?.ObjectEffects;

        var staffEnchCount = 0;

        Console.WriteLine();

        if (staffEnchCollection != null)
            foreach (var ench in staffEnchCollection)
            {
                if (ench.EditorID != null &&
                    (!ench.EditorID.Contains("Staff") || ench.EditorID.Contains("MAG_"))) continue;

                if (staffEnchantmentsToSkip.Contains(ench.FormKey))
                {
                    Console.WriteLine($"Skipping unique staff enchantment {ench.Name} (0x{ench.FormKey.ID:X})");
                    continue;
                }

                Console.WriteLine(
                    $"Processing staff enchantment: {ench.Name} (0x{ench.FormKey.ID:X}: {ench.FormKey.ModKey.FileName})");

                var patched = state.PatchMod.ObjectEffects.GetOrAddAsOverride(ench);
                var max = 0.0f;
                uint costliestEffectLevel = 0;

                foreach (var effect in ench.Effects)
                {
                    state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var record);
                    if (record is null) continue;
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

                if (patched is { CastType: CastType.Concentration, TargetType: TargetType.Aimed })
                    patched.Effects.Add(conc);

                if (patched.EnchantmentAmount == ench.EnchantmentAmount)
                    state.PatchMod.Remove(patched);

                Console.WriteLine(
                    $"\tFinished processing {ench.Name} (0x{ench.FormKey.ID:X}: {ench.FormKey.ModKey.FileName})");

                staffEnchCount++;
            }

        // Staff recipes
        var staffRecipeCollection = _settings.Value.PatchFullLoadOrder
            ? state.LoadOrder.PriorityOrder.ConstructibleObject().WinningOverrides()
            : state.LoadOrder.TryGetValue(_modToPatch)?.Mod?.ConstructibleObjects;

        var staffRecipeCount = 0;
        var errors = new List<IConstructibleObjectGetter>();

        Console.WriteLine();

        if (staffRecipeCollection != null)
        {
            foreach (var staffRecipe in staffRecipeCollection)
            {
                var edid = staffRecipe.EditorID;
                if (edid != null && (edid.Contains("MAG_") ||
                                     !staffRecipe.WorkbenchKeyword.Equals(vanillaStaffWorkbenchKywd))) continue;

                if (state.LinkCache.TryResolve<IWeaponGetter>(staffRecipe.CreatedObject.FormKey, out var staff))
                {
                    if ((staff.FormKey.ModKey.FileName.ToString().Equals("BSHeartland.esm")
                         && staff.EditorID is not null
                         && brumaUniqueStaves.Contains(staff.EditorID))
                        || (staff.Name?.String != null && uniqueStaves.Contains(staff.Name.String)))
                    {
                        Console.WriteLine($"Skipping recipe for unique staff {staff.Name} (0x{staff.FormKey.ID:X})");
                        continue;
                    }

                    Console.WriteLine(
                        $"Processing recipe for {staff.Name} (0x{staffRecipe.FormKey.ID:X}: {staffRecipe.FormKey.ModKey.FileName})");

                    state.LinkCache.TryResolve<IObjectEffectGetter>(staff.ObjectEffect.FormKey, out var ench);
                    var max = 0.0f;
                    uint costliestEffectLevel = 0;

                    if (ench != null)
                    {
                        foreach (var effect in ench.Effects)
                        {
                            state.LinkCache.TryResolve<IMagicEffectGetter>(effect.BaseEffect.FormKey, out var record);
                            if (record is null) continue;
                            if (!(record.BaseCost > max)) continue;
                            max = record.BaseCost;
                            costliestEffectLevel = record.MinimumSkillLevel;
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            $"WARNING: {staff.Name} (0x{staffRecipe.FormKey.ID:X}) does not have an enchantment, skipping...");
                        continue;
                    }

                    var newRecipe = state.PatchMod.ConstructibleObjects.AddNew();
                    newRecipe.EditorID = staffRecipe.EditorID;
                    newRecipe.Items = new ExtendedList<ContainerEntry>();
                    if (staffRecipe.Items != null)
                        foreach (var item in staffRecipe.Items)
                            newRecipe.Items.Add(item.DeepCopy());
                    foreach (var cond in staffRecipe.Conditions)
                        newRecipe.Conditions.Add(cond.DeepCopy());
                    newRecipe.CreatedObject = new FormLinkNullable<IConstructibleGetter>();
                    newRecipe.CreatedObject.SetTo(staffRecipe.CreatedObject);
                    newRecipe.CreatedObjectCount = staffRecipe.CreatedObjectCount;

                    newRecipe.EditorID += "Alt";
                    newRecipe.WorkbenchKeyword = staffWorkbenchKywd;
                    newRecipe.Items.RemoveAll(item => item.Item.Item.FormKey.Equals(heartStone.FormKey));

                    var recipes = new List<(IFormLink<ISoulGemGetter>, int)>
                    {
                        (soulGemCommon, 1),
                        (soulGemGreater, 1),
                        (soulGemGrand, 1),
                        (soulGemGrand, 2),
                        (soulGemGrand, 3)
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

                    Console.WriteLine(
                        $"\tFinished processing recipe for {staff.Name} (0x{staffRecipe.FormKey.ID:X}: {staffRecipe.FormKey.ModKey.FileName})");

                    staffRecipeCount++;
                }
                else
                {
                    Console.WriteLine(
                        $"ERROR: Failed to process recipe for {staffRecipe.EditorID} (0x{staffRecipe.FormKey.ID:X})");
                    errors.Add(staffRecipe);
                }
            }

            if (errors.Count > 0)
            {
                Console.WriteLine($"Failed to process {errors.Count} staff recipes: ");
                foreach (var error in errors)
                    Console.WriteLine($"\t{error.EditorID} (0x{error.FormKey.ID:X})");
            }
        }

        Console.WriteLine();

        if (_settings.Value.PatchFullLoadOrder)
            // Soul gems
            foreach (var soulgem in state.LoadOrder.PriorityOrder.SoulGem().WinningOverrides())
            {
                Console.WriteLine(
                    $"Processing {soulgem.EditorID} (0x{soulgem.FormKey.ID:X}: {soulgem.FormKey.ModKey.FileName})");
                var patched = state.PatchMod.SoulGems.GetOrAddAsOverride(soulgem);

                patched.Value = soulgem.EditorID switch
                {
                    "SoulGemCommon" => 45,
                    "SoulGemCommonFilled" => 135,
                    "SoulGemGreater" => 75,
                    "SoulGemGreaterFilled" => 265,
                    "SoulGemGrand" => 160,
                    "SoulGemGrandFilled" => 400,
                    "SoulGemBlack" => 240,
                    "SoulGemBlackFilled" => 600,
                    _ => patched.Value
                };

                if (patched.Value == soulgem.Value)
                    state.PatchMod.Remove(patched);

                Console.WriteLine(
                    $"\tFinished processing {soulgem.EditorID} (0x{soulgem.FormKey.ID:X}: {soulgem.FormKey.ModKey.FileName})");
            }

        Console.WriteLine();
        Console.WriteLine($"{scrollCount} parchemins patchés");
        Console.WriteLine($"{staffCount} bâtons et {staffEnchCount} enchantements patchés");
        Console.WriteLine($"{staffRecipeCount} recettes de bâtons patchées");

        var recordCount = state.PatchMod.EnumerateMajorRecords().Count();

        Console.WriteLine($"{recordCount} entrées patchés");
    }
}
