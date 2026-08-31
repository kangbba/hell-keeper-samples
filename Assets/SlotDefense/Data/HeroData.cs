using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// Basic heroes drop from the slot directly; advanced heroes only exist by merging.
/// </summary>
public enum HeroGrade
{
    Basic,
    Advanced
}

/// <summary>
/// One ingredient of an unlock recipe: a hero type at a star level, times a count.
/// </summary>
[Serializable]
public class UnlockRequirement
{
    public HeroType heroType;
    public int starLevel;
    [Tooltip("How many of this ingredient are required")]
    public int count = 1;
}

/// <summary>
/// Wrapper around the requirement list. Kept as its own serializable type so the
/// recipe can be shown or hidden in the inspector as a single unit.
/// </summary>
[Serializable]
public class UnlockRecipe
{
    [Tooltip("Ingredients required to unlock this hero, e.g. 2-star Fire plus 3-star Ice")]
    public List<UnlockRequirement> requirements = new List<UnlockRequirement>();
}

/// <summary>
/// The half of a hero definition designers set by hand: identity, presentation and the
/// merge recipe. Numeric balance comes from the spreadsheet pipeline instead, because it
/// is tuned in bulk and compared across heroes.
/// </summary>
[CreateAssetMenu(fileName = "HeroData_New", menuName = "SlotDefense/Hero Data", order = 0)]
public class HeroData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Elemental type, also the key used by the balance tables")]
    public HeroType heroType;
    
    [Tooltip("Default targeting behaviour")]
    public TargetingMode targetingMode;

    [Tooltip("Display name, Korean")]
    public string heroName_KO;

    [Tooltip("Description, Korean")]
    [TextArea(3, 5)]
    public string description_KO;

    [Header("Presentation")]
    [Tooltip("Accent colour used across the hero UI")]
    public Color heroColor = Color.white;

    [Tooltip("Icon")]
    public Sprite heroIcon;

    [Header("Grade")]
    [Tooltip("Basic or advanced")]
    public HeroGrade heroGrade = HeroGrade.Basic;
    
    public bool IsHeroGradeAdvanced => heroGrade == HeroGrade.Advanced;

    [Header("Unlock recipe, advanced heroes only")]
    public UnlockRecipe unlockRecipe = new UnlockRecipe();

    [Tooltip("Image explaining the recipe in the UI")]
    public Sprite unlockRecipeSprite;
}
