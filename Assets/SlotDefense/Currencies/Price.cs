using System;

/// <summary>
/// A cost as a value: currency type plus amount. Shop, gacha and upgrades all pay
/// through this one switch, so there is exactly one place that knows how each
/// currency is checked and spent.
/// </summary>
[Serializable]
public class Price
{
    public CurrencyType CurrencyType;
    public int Amount;

    public Price(CurrencyType currencyType, int amount)
    {
        CurrencyType = currencyType;
        Amount = amount;
    }

    public bool CanAfford()
    {
        return CurrencyType switch
        {
            CurrencyType.Coin => DataManager.Coin.Value >= Amount,
            CurrencyType.Diamond => DataManager.Diamond.Value >= Amount,
            CurrencyType.Element => DataManager.Element.Value >= Amount,
            _ => false
        };
    }

    public bool TryPay()
    {
        if (!CanAfford())
            return false;

        return CurrencyType switch
        {
            CurrencyType.Coin => DataManager.SpendCoin(Amount),
            CurrencyType.Diamond => DataManager.SpendDiamond(Amount),
            CurrencyType.Element => DataManager.SpendElement(Amount),
            _ => false
        };
    }

    public void Grant()
    {
        switch (CurrencyType)
        {
            case CurrencyType.Coin:
                DataManager.AddCoin(Amount);
                break;
            case CurrencyType.Diamond:
                DataManager.AddDiamond(Amount);
                break;
            case CurrencyType.Element:
                DataManager.AddElement(Amount);
                break;
        }
    }

    public override string ToString()
    {
        return $"{CurrencyType}: {Amount}";
    }
}
