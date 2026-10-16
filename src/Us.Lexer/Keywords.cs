namespace Us.Lexer;

public static class Keywords
{
    public static readonly IReadOnlyList<Keyword> All =
    [
        new("eğer", TokenKind.Eger),
        new("edeğilse", TokenKind.Edegilse),
        new("değilse", TokenKind.Degilse),
        new("dön", TokenKind.Don),
        new("ile", TokenKind.Ile),
        new("adım", TokenKind.Adim),
        new("tekrarla", TokenKind.Tekrarla),
        new("yap", TokenKind.Yap),
        new("sonsuzdön", TokenKind.Sonsuzdon),
        new("çık", TokenKind.Cik),
        new("devam", TokenKind.Devam),
        new("fonksiyon", TokenKind.Fonksiyon),
        new("afonksiyon", TokenKind.Afonksiyon),
        new("bekle", TokenKind.Bekle),
        new("model", TokenKind.Model),
        new("sınıf", TokenKind.Sinif),
        new("oluştur", TokenKind.Olustur),
        new("bu", TokenKind.Bu),
        new("temel", TokenKind.Temel),
        new("genel", TokenKind.Genel),
        new("özel", TokenKind.Ozel),
        new("korumalı", TokenKind.Korumali),
        new("dahili", TokenKind.Dahili),
        new("arayüz", TokenKind.Arayuz),
        new("soyut", TokenKind.Soyut),
        new("statik", TokenKind.Statik),
        new("sanal", TokenKind.Sanal),
        new("yerinegeç", TokenKind.Yerinegec),
        new("mühürlü", TokenKind.Muhurlu),
        new("alan", TokenKind.Alan),
        new("al", TokenKind.Al),
        new("olarak", TokenKind.Olarak),
        new("seç", TokenKind.Sec),
        new("seçenek", TokenKind.Secenek),
        new("ref", TokenKind.Ref),
        new("çıktı", TokenKind.Cikti),
        new("dene", TokenKind.Dene),
        new("yakala", TokenKind.Yakala),
        new("sonunda", TokenKind.Sonunda),
        new("fırlat", TokenKind.Firlat),
        new("başında", TokenKind.Basinda),
        new("olay", TokenKind.Olay),
        new("eşişlem", TokenKind.Eesislem),
        new("doğru", TokenKind.Dogru),
        new("yanlış", TokenKind.Yanlis),
        new("doğrula", TokenKind.Dogrula),
        new("kural", TokenKind.Kural),
        new("dönüşüm", TokenKind.Donusum),
        new("bağla", TokenKind.Bagla),
        new("servis", TokenKind.Servis),
        new("kapsamlı", TokenKind.Kapsamli),
        new("tekil", TokenKind.Tekil),
        new("geçici", TokenKind.Gecici),
        new("kontrolcü", TokenKind.Kontrolcu),
        new("rota", TokenKind.Rota),
        new("test", TokenKind.Test),
        new("beklenen", TokenKind.Beklenen),
        new("işlem", TokenKind.Islem),
        new("onayla", TokenKind.Onayla),
        new("gerial", TokenKind.Gerial),
        new("veritabanı", TokenKind.Veritabani),
        new("tablo", TokenKind.Tablo),
        new("birlikte", TokenKind.Birlikte),
    ];

    public static bool TryMatch(ReadOnlySpan<char> text, out TokenKind kind)
    {
        foreach (var keyword in All)
        {
            if (text.Equals(keyword.Text, StringComparison.Ordinal))
            {
                kind = keyword.Kind;
                return true;
            }
        }

        kind = default;
        return false;
    }

    public readonly record struct Keyword(string Text, TokenKind Kind);
}
