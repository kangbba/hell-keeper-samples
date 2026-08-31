using UnityEngine;

[CreateAssetMenu(menuName = "Augments/Augment Data")]
public class AugmentData : ScriptableObject
{
    [Header("Info")]
    public string id;
    public string title;
    public Sprite icon;
    [TextArea] public string description;

    [Header("Values")]
    public float main = 1.0f;        // Primary magnitude: a multiplier or a flat increase.
    public float duration = 0f;      // Seconds, for augments that expire.
    public float possibility = 0f;   // Proc chance, for augments that roll.

    public void SetMeta(
        string id,
        Sprite icon,
        string title,
        string description,
        float main,
        float duration = 0f,
        float possibility = 0f)
    {
        this.id = id;
        this.icon = icon;
        this.title = title;
        this.description = description;
        this.main = main;
        this.duration = duration;
        this.possibility = possibility;
    }
}
