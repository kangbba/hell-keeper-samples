using UnityEngine;

/// <summary>
/// Authoring data for one buff type.
/// Stored at Resources/HeroBuffs/BuffData_[BuffName].asset.
/// </summary>
[CreateAssetMenu(fileName = "BuffData_New", menuName = "SlotDefense/Hero Buff Data", order = 1)]
public class HeroBuffData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Buff type this asset describes")]
    public BuffType buffType;

    [Header("Presentation")]
    [Tooltip("Icon shown on the buff displayer")]
    public Sprite buffIcon;
}
