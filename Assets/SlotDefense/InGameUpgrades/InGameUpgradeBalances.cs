/// <summary>
/// All in-run upgrade balance in one place.
/// Price = upgradeLevel * PRICE_PER_LEVEL
/// Damage multiplier = 1.0 + DAMAGE_INCREASE_PER_LEVEL * (upgradeLevel - 1)
/// </summary>
public static class InGameUpgradeBalances
{
    public const int PRICE_PER_LEVEL = 10;                 // Element per upgrade level
    public const float DAMAGE_INCREASE_PER_LEVEL = 0.05f;  // +5% damage per level

    public static int GetInGameUpgradePrice(int upgradeLevel)
    {
        return upgradeLevel * PRICE_PER_LEVEL;
    }

    public static float GetInGameUpgradeDamageMultiplier(int upgradeLevel)
    {
        return 1f + DAMAGE_INCREASE_PER_LEVEL * (upgradeLevel - 1);
    }
}
