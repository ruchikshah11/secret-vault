namespace SecretVault.Models;

public enum CardBrand { Unknown, Visa, Mastercard, Amex, RuPay, Discover, Jcb, Diners, Maestro, UnionPay }

// Card network detection from the number (IIN/BIN ranges). Shared by the desktop and mobile apps.
public static class CardNetworks
{
    public static readonly CardBrand[] Selectable =
    {
        CardBrand.Visa, CardBrand.Mastercard, CardBrand.Amex, CardBrand.RuPay, CardBrand.Discover,
        CardBrand.Jcb, CardBrand.Diners, CardBrand.Maestro, CardBrand.UnionPay
    };

    public static CardBrand Resolve(PaymentCard card) =>
        Enum.TryParse<CardBrand>(card.Brand, ignoreCase: true, out var chosen) && chosen != CardBrand.Unknown
            ? chosen
            : Detect(card.CardNumber);

    public static CardBrand Detect(string number)
    {
        var d = new string(number.Where(char.IsDigit).ToArray());
        if (d.Length == 0) return CardBrand.Unknown;

        int Prefix(int len) => d.Length >= len ? int.Parse(d[..len]) : -1;
        var p2 = Prefix(2);
        var p3 = Prefix(3);
        var p4 = Prefix(4);
        var p6 = Prefix(6);

        // Order matters: more specific ranges first.
        if (p2 is 34 or 37) return CardBrand.Amex;
        if (p4 is >= 3528 and <= 3589) return CardBrand.Jcb;
        if (p3 is >= 300 and <= 305 || p2 is 36 or 38 or 39) return CardBrand.Diners;
        if (p6 is >= 508500 and <= 508999 or >= 606985 and <= 607984 or >= 608001 and <= 608500 or >= 652150 and <= 653149
            || p2 is 81 or 82) return CardBrand.RuPay;
        if (p4 == 6011 || p2 == 65 || p3 is >= 644 and <= 649) return CardBrand.Discover;
        if (p2 == 62) return CardBrand.UnionPay;
        if (p2 is >= 51 and <= 55 || p4 is >= 2221 and <= 2720) return CardBrand.Mastercard;
        if (d[0] == '4') return CardBrand.Visa;
        if (p2 is 50 or (>= 56 and <= 69)) return CardBrand.Maestro;
        if (p2 == 60) return CardBrand.RuPay;
        return CardBrand.Unknown;
    }

    public static string DisplayName(CardBrand brand) => brand switch
    {
        CardBrand.Amex => "American Express",
        CardBrand.Jcb => "JCB",
        CardBrand.Diners => "Diners Club",
        CardBrand.UnionPay => "UnionPay",
        CardBrand.RuPay => "RuPay",
        CardBrand.Unknown => "Card",
        _ => brand.ToString()
    };

    public static string ShortName(CardBrand brand) => brand switch
    {
        CardBrand.Amex => "Amex",
        CardBrand.Diners => "Diners",
        _ => DisplayName(brand)
    };
}
