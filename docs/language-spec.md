# US Dil Belirtimi

Bu belge US programlama dilinin kaynak belirtimidir. Uygulama bu belgeye uyar. Belgede yazmayan bir sözdizimi geçerli değildir.

Durum: normative taslak (v0.1 hedefi).

Tagline: Türkçe düşün. Kodla.

## 1. Kimlik

- Dil adı: US
- Dosya uzantısı: `.us`
- Büyük/küçük harf duyarlıdır.
- Karakter kodlaması UTF-8'dir. Kaynak metin Unicode olarak işlenir. Türkçe karakterler identifier içinde geçerlidir.
- Noktalı virgül zorunlu değildir. Yazılırsa deyimi bitirir.
- Tip güvenlidir. Yerel değişken ve alan bildirimlerinde tip açıktır. Örtük değişken bildirimi yoktur.
- `null` literali yoktur. Boş değer literali `~` karakteridir.
- `new`, `return`, `case`, `default`, `using`, `import`, `require`, `include` anahtar kelimeleri yoktur.

Standart teknoloji ve protokol adları Türkçeleştirilmez ve olduğu gibi yazılır:

`HTTP`, `GET`, `POST`, `PUT`, `PATCH`, `DELETE`, `JSON`, `URL`, `SQL`, `REST`, `JWT`, `WebSocket`

## 2. Sözcükler

### 2.1 Identifier

Identifier, Unicode harfi (Türkçe harfler dahil) veya `_` ile başlar. Devamı harf, rakam veya `_` olabilir.

Örnekler: `öğrenci`, `çalışıyor`, `ürünFiyatı`, `İstanbul`, `Kullanıcı`.

`Kullanıcı` ile `kullanıcı` farklı identifier'lardır.

### 2.2 Anahtar kelimeler

```
eğer edeğilse değilse
dön ile adım tekrarla yap sonsuzdön çık devam
fonksiyon afonksiyon bekle
model sınıf oluştur bu temel
genel özel korumalı dahili
arayüz soyut statik sanal yerinegeç mühürlü
alan al olarak
seç seçenek
ref çıktı
dene yakala sonunda fırlat
başında
olay
eşişlem
doğru yanlış
doğrula kural
dönüşüm
bağla
servis kapsamlı tekil geçici
kontrolcü rota
test beklenen
işlem onayla gerial
veritabanı tablo birlikte
```

`_başında` ayrı bir belirteçtir. Identifier `_başında` ile bu belirteç aynı yazımı paylaşır; sözcük çözümleyici bunu anahtar belirteç olarak üretir.

Bağlamsal adlar (her yerde anahtar kelime değildir):

- Model kısıtı konumunda: `anahtar`, `zorunlu`, `uzunluk`, `benzersiz`, `aralık`, `varsayılan`
- Süre birimi konumunda: `milisaniye`, `saniye`, `dakika`, `saat`, `gün`, `hafta`
- Yapılandırma bağlama konumunda: `Ayar`

`yaz`, `syaz`, `sor`, `oku`, `temizle`, `bugün`, `şimdi`, `json`, `dosya`, `klasör`, `yol`, `log`, `http` standart kütüphane adlarıdır; anahtar kelime değildir.

### 2.3 Literaller

- Metin: `"..."`. Enterpolasyon: `"Merhaba {ad}"`. Kaçış: `\"`, `\\`, `\n`, `\r`, `\t`.
- Karakter: `'ç'`
- Tamsayı: `38`, `0`, ondalık taban. Başlangıçta ikilik/onaltılık zorunlu değildir.
- Gerçek sayı: `1.5`
- Mantıksal: `doğru`, `yanlış`
- Boş: `~`
- Süre: `100.milisaniye`, `5.saniye`, `30.dakika`, `2.saat`, `7.gün`, `3.hafta`

Süre biçimi sözdizimsel olarak sayı, nokta ve birim identifier'ıdır. Anlam çözümlemesi bilinen birimlerde bunu süre değerine çevirir. Bilinmeyen birim normal üye erişimidir.

### 2.4 Yorumlar

```
// satır yorumu

/*
  blok yorumu
*/
```

### 2.5 İşleçler ve noktalama

```
= == != < <= > >=
+ - * / %
&& || !
++ --
+= -= *= /=
=> !=>
@ ~ ? ?. ?? -> ^
{ } ( ) [ ] < >
. , : ;
```

Uzun eşleşme geçerlidir. `=>` iki ayrı belirteç değildir. `!=>` , `!=` artı `>` değildir. `?.` ve `??` ayrıdır. `_başında`, `_` artı identifier değildir.

## 3. Deyim sonu

Noktalı virgül deyimi bitirir.

Noktalı virgül yoksa, satır sonu şu durumda deyimi bitirir: sonraki belirteç mevcut deyimi sürdüren bir infix işleç, `.`, `?.`, `??`, `,` veya açılmamış bir parantez/köşeli/süslü parantez devamı değildir.

`edeğilse` ve `değilse` ait oldukları `eğer` deyiminin parçasıdır. `tekrarla`, ait olduğu `yap` deyiminin parçasıdır. `sonunda` ve `yakala`, ait oldukları korumalı bloğun parçasıdır.

Süslü parantez kendi başına bir blok deyimidir.

## 4. Tipler

### 4.1 İlkel tipler

| Yazım | Anlam |
| --- | --- |
| `metin` | Metin |
| `karakter` | Tek karakter |
| `tamsayı` | İşaretli tamsayı (32 bit) |
| `utamsayı` | İşaretsiz tamsayı (32 bit) |
| `ondalık` | 64 bit kayan nokta |
| `gondalık` | 32 bit kayan nokta |
| `hondalık` | Yüksek duyarlıklı ondalık |
| `mantıksal` | Mantıksal değer |
| `nesne` | Kök referans tipi |
| `dinamik` | Çalışma zamanında çözülen tip |
| `tarih` | Takvim günü |
| `saat` | Gün içi saat |
| `tarihsaat` | Tarih ve saat |
| `süre` | Süre |

`dinamik` tip denetimini zayıflatır. Standart kodda tercih edilmez. Üye erişimi çalışma zamanında çözülür; başarısızlık çalışma zamanı hatasıdır.

### 4.2 Nullable

`metin?`, `Kullanıcı?` değer tipi veya referans tipi için boş değer alabilirliği belirtir.

`~` yalnızca nullable konuma atanabilir. Nullable olmayan konuma `~` atamak tanı hatasıdır.

### 4.3 Jenerik tipler

```
liste<Kullanıcı>
sözlük<metin, tamsayı>
dizi<tamsayı>
küme<metin>
sliste<tamsayı>
Sonuç<T>
```

Standart koleksiyon tipleri: `dizi`, `liste`, `sözlük`, `küme`, `sliste`.

### 4.4 Dönüşümler

- Aynı tip kimlik dönüşümüdür.
- `tamsayı` → `ondalık` / `gondalık` / `hondalık` genişletmedir.
- `utamsayı` → `ondalık` genişletmedir.
- `tamsayı` ↔ `utamsayı` örtük değildir.
- Daraltma örtük değildir.
- `T` → `T?` örtüktür.
- `T?` → `T` örtük değildir.
- Sayısal metin dönüşümü örtük değildir.

## 5. Değişkenler

```
ad: metin = "İbrahim"
yaş: tamsayı = 38
aktif: mantıksal = doğru
```

Tip zorunludur. `ad = "İbrahim"` bildirim değildir.

İlk değer isteğe bağlıdır. İlk değer yoksa değişken kesinlikle atanmadan okunamaz.

Toplu bildirim:

```
ad, soyad, şehir: metin = "İbrahim", "Dağ", "İstanbul"
yaş, sınıf: tamsayı = 38, 12
aktif, yönetici: mantıksal = doğru, yanlış
telefon, eposta: metin? = ~, ~
```

İlk değer varsa değer sayısı ad sayısıyla eşit olmalıdır. Eşit değilse tanı hatası verilir.

Aynı kapsamda yinelenen ad hatadır. İç kapsamda dış adı gölgelemek hatadır.

```
ad: metin = "Ali"

eğer doğru
{
    ad: metin = "Veli"
}
```

Bu program tanı hatası üretir.

## 6. İfadeler

Öncelik, sıkıdan gevşeğe:

1. Üye, çağrı, indis, `?.`, sonek `++` `--`
2. Tekil `+` `-` `!`, önek `++` `--`, `@` oluşturma
3. `*` `/` `%`
4. `+` `-`
5. `<` `<=` `>` `>=`
6. `==` `!=`
7. `&&`
8. `||`
9. `??`
10. İfade-eğer
11. Atama `=` `+=` `-=` `*=` `/=` (sağa birleşir)

`^` sorgu öğesi işleçidir ve ancak sorgu lambda konumunda geçerlidir. Normal değişken değildir.

Null güvenli erişim: `kullanıcı?.ad`

Null birleştirme: `kullanıcı?.ad ?? "Misafir"`

Atama bir ifadedir ve atanan değeri üretir.

## 7. Kontrol akışı

```
eğer yaş >= 18
{
    syaz "Yetişkin"
}
edeğilse yaş >= 13
{
    syaz "Genç"
}
değilse
{
    syaz "Çocuk"
}
```

`edeğilse` koşul ister. `değilse` koşul almaz. `edeğilse`, `değilse`'den önce gelir. Birden çok `edeğilse` olabilir. En çok bir `değilse` olabilir.

İfade-eğer üçlü işleç değildir:

```
durum: metin =
    eğer yaş >= 18 => "Yetişkin" !=> "Çocuk"
```

`=>` doğru dalı, `!=>` yanlış dalıdır. İki dalın tipi ortak bir tipe dönüşebilir olmalıdır.

## 8. Döngüler

Koleksiyon:

```
dön kullanıcılar -> kullanıcı
{
    syaz kullanıcı.ad
}
```

`kullanıcı` döngü değişkenidir. Tip, koleksiyonun öğe tipidir ve yazılmaz. Bu değişken döngü gövdesi kapsamındadır. Dış kapsamda aynı adı gölgelemek yine hatadır; derleyici döngü değişkenini dış adla çakışırsa tanı hatası verir. `dön` `^` oluşturmaz. Döngü değişkeni ile sorgu öğesi ayrıdır.

Aralık, her iki uç dahildir:

```
dön 1 ile 10 -> i
{
    syaz i
}
```

Adım yazılmazsa yön aralıktan çıkar. `A < B` ise adım `+1`, `A > B` ise adım `-1`, `A == B` ise gövde yalnız `A` için bir kez çalışır.

```
dön 5 ile 1 -> i
```

değerleri `5, 4, 3, 2, 1` olur.

Adım büyüklük olarak yazılır; yönü aralık belirler. Kullanıcı negatif adım yazmaz.

```
dön 0 ile 100 adım 10 -> i
dön 1 ile 5 adım 2 -> i
dön 5 ile 1 adım 2 -> i
```

İlk örnek `0, 10, ..., 100` üretir. İkinci örnek `1, 3, 5` üretir. Üçüncü örnek `5, 3, 1` üretir. Adım bitişe tam denk gelmezse bitiş zorlanmaz: `dön 1 ile 6 adım 2 -> i` değerleri `1, 3, 5` olur.

`adım 0` geçersizdir ve anlam tanısıdır. Aralık uçları ve adım tamsayı olmalıdır. Adım, sabit olarak sıfır veya negatif görülebiliyorsa bağlayıcı tanı verir. Adım çalışma anında sıfır veya negatif çıkarsa çalışma zamanı hatası verilir ve döngü sürmez.

`tekrarla` koşulu her girişten önce sınanır:

```
tekrarla sayı < 10
{
    sayı++
}
```

`yap` gövdeyi bir kez çalıştırır, sonra `tekrarla` koşulunu sınar:

```
yap
{
    sayı++
}
tekrarla sayı < 10
```

Sonsuz döngü:

```
sonsuzdön
{
}
```

`çık` en iç döngüyü bitirir. `devam` en iç döngünün sonraki turuna gider. Döngü dışında kullanımları tanı hatasıdır.

## 9. Fonksiyonlar

```
fonksiyon topla(a: tamsayı, b: tamsayı): tamsayı
{
    => a + b
}
```

Tek satır:

```
fonksiyon kare(x: tamsayı): tamsayı => x * x
```

Dönüş tipi yoksa fonksiyon değer döndürmez:

```
fonksiyon selamla(ad: metin)
{
    syaz "Merhaba {ad}"
}
```

Değer döndürmeyen fonksiyonda `=>` kullanılamaz. Değer döndüren fonksiyonun her normal çıkış yolu bir `=>` içermelidir.

`=>` sonuç ifadesidir. Blok içinde deyim olarak yazılır ve fonksiyonu o değerle bitirir.

Parametre biçimleri:

- değer: `a: tamsayı`
- referans: `ref sayı: tamsayı`
- çıktı: `çıktı kalan: tamsayı`
- varsayılan değer: `mesaj: metin = "Merhaba"`

Varsayılanlı parametre, varsayılansız parametreden sonra gelmelidir. Çağrıda sonda kalan varsayılanlı parametreler yazılmayabilir. Adlandırılmış argüman yoktur.

`ref` çağrıda değişken ister ve fonksiyon girişinde atanmış olmalıdır. `çıktı` çağrıda değişkene yazılır ve fonksiyonun her normal çıkışında atanmış olmalıdır. Çağrı yerinde `ref` ve `çıktı` tekrar yazılır:

```
artır(ref sayı)
böl(a, b, çıktı kalan)
```

## 10. Eşzamansızlık

```
afonksiyon kullanıcıGetir(id: tamsayı): Kullanıcı?
{
    kullanıcı: Kullanıcı? = bekle db.kullanıcıBul(id)
    => kullanıcı
}
```

`afonksiyon` eşzamansız fonksiyondur. `bekle` yalnız `afonksiyon` gövdesinde, eşzamansız lambda içinde veya üst düzey eşzamansız giriş noktasında geçerlidir.

`Task<T>` kullanıcı sözdiziminde görünmez. Eşzamansız fonksiyonun yazılan dönüş tipi, beklenen değerin tipidir. Dönüş tipi yazılmayan `afonksiyon` değer döndürmez.

`bekle` ifadesinin tipi, beklenen işlemin sonuç tipidir.

`bekle` bağlayıcıda üst düzeyde, `afonksiyon` içinde, `test`, `beklenenHata` ve `eşişlem` bağlamında geçerlidir. Normal `fonksiyon` içinde `US0835` verir. Yorumlayıcı bu kararı yeniden vermez.

`bekle`, `afonksiyon` çağrısının sonucunu bekler. Süre ifadesi zamanlayıcı üzerinden bekler ve değer döndürmez. Gövde, ilk gerçek bekleme noktasına kadar çalışır. Tamamlanmış işlem de aynı yoldan beklenir. US tipi `Task` değildir.

## 11. Lambda

```
(x: tamsayı) => x * 2
(a: tamsayı, b: tamsayı) => a + b

(x: tamsayı) =>
{
    sonuç: tamsayı = x * 2
    => sonuç
}
```

Lambda parametre tipleri açıktır. Gövde tek ifade veya blok olabilir.

## 12. Model

Model veri taşır. Davranış için sınıf kullanılır.

```
model Kullanıcı
{
    id: tamsayı
    ad, soyad: metin
    yaş: tamsayı
    aktif: mantıksal
}
```

Model üyeleri varsayılan olarak geneldir. Erişim belirteci yazılırsa o üye için geçerlidir.

Kısıtlar:

```
model Kullanıcı
{
    id: tamsayı : anahtar
    ad: metin : zorunlu, uzunluk(100)
    eposta: metin : zorunlu, benzersiz
    yaş: tamsayı : aralık(18, 120)
    aktif: mantıksal : varsayılan(doğru)
}
```

Başlangıç kısıt kümesi:

| Kısıt | Anlam |
| --- | --- |
| `anahtar` | Kimlik alanı |
| `zorunlu` | Boş veya `~` olamaz |
| `uzunluk(n)` | Metin uzunluğu en çok n |
| `benzersiz` | Kayıt kümesinde tekil |
| `aralık(min, max)` | Sayısal kapalı aralık |
| `varsayılan(değer)` | Bildirimde değer yoksa kullanılan değer |

Kısıt listesi genişletilebilir. Çekirdek, kısıt adını ve argümanlarını sembol olarak saklar. Bilinmeyen kısıt, çekirdek doğrulamada tanı hatasıdır. Sağlayıcılar kendi kısıtlarını ayrıca kaydedebilir.

Model örneği `@` ile oluşturulur. `new` yoktur.

Nesne başlatıcı:

```
kullanıcı: Kullanıcı = @Kullanıcı
{
    ad = "İbrahim"
    soyad = "Dağ"
    yaş = 38
}
```

Kurucu çağrısı:

```
kullanıcı: Kullanıcı = @Kullanıcı("İbrahim", "Dağ", 38)
hesap: HesapMakinesi = @HesapMakinesi()
```

Kopyalama orijinali değiştirmez:

```
güncel: Kullanıcı = kullanıcı ile
{
    ad = "Ahmet"
    aktif = yanlış
}
```

`ile` bu konumda bağlamsal anahtar kelimedir. Döngü aralığındaki `ile` ile karışmaması için `ile` blok yalnızca bir nesne ifadesinden sonra gelir.

`ile` yalnız model kopyalar. Sınıf kopyası bağlama hatasıdır (`US0838`). Kopya yeni örnektir; kaynak değişmez. Üye değerleri sığ kopyalanır: iç içe nesne yeniden üretilmez, aynı örneği paylaşır. Eksik üye kaynaktaki değeri taşır. Yazılan üye kopyada değişir.

## 13. Sınıf

Sınıf davranış ve iş kuralı taşır. Üyeler varsayılan olarak özeldir.

```
sınıf KullanıcıServisi
{
    fonksiyon doğrula(id: tamsayı): mantıksal
    {
        => id > 0
    }
}
```

Erişim: `genel`, `özel`, `korumalı`, `dahili`.

Kurucu adı `oluştur`dur:

```
sınıf Kullanıcı
{
    ad: metin

    genel oluştur(ad: metin)
    {
        bu.ad = ad
    }
}
```

`bu` geçerli örnektir. `temel` taban örnektir. `temel.üye()` sanal gönderim yapmaz; taban gövdesini çağırır.

Sınıfta hiç `oluştur` yoksa parametresiz gizli kurucu vardır. `@HesapMakinesi()` bu kurucuyu kullanır. En az bir `oluştur` yazıldıysa gizli parametresiz kurucu üretilmez. Taban sınıfın parametresiz kurucusu (gizli ya da açık) örnek oluşturulurken, türetilmiş kurucudan önce çalışır. Taban yalnız parametre isteyen `oluştur` tanımlıyorsa ve `temel(...)` sözdizimi yoksa bu bağlama hatasıdır (`US0837`). Yeni `temel(...)` sözdizimi yoktur.

Örnek oluşturma sırası: örnek ayrılır, taban kısmı hazırlanır, bildirilen alanların başlangıç değeri yazılır, sonra kurucu gövdesi çalışır. Kalıtımda sıra tabandan türetilmişe doğrudur. Alanlar ilkel sıfır değeriyle başlar; nullable alan `~` olur. Modelde `varsayılan(...)` alan atlanırsa kullanılır. Nullable model alanı verilmezse `~` olur. Nullable olmayan ya da `zorunlu` model alanı verilmezse çalışma zamanı hatasıdır (`US1512`).

Statik üyede `bu` kullanılamaz.

## 14. OOP

```
arayüz Uçabilir
{
    fonksiyon uç()
}

sınıf Köpek : Hayvan
{
}

sınıf Kuş : Hayvan, Uçabilir
{
}
```

Değiştiriciler: `soyut`, `statik`, `sanal`, `yerinegeç`, `mühürlü`.

```
genel yerinegeç fonksiyon ses()
{
    temel.ses()
}
```

Kalıtım kuralları:

- En çok bir sınıf tabanı.
- Birden çok arayüz.
- `mühürlü` sınıf devralınamaz. `mühürlü` üye yerine geçilemez.
- `yerinegeç`, `sanal`, `soyut` veya arayüz üyesinin yerine geçer.
- Soyut sınıf örneği `@` ile oluşturulamaz.
- Arayüz örneği oluşturulamaz.
- Sınıf, devraldığı arayüzlerin üyelerini uygulamalıdır.

## 15. Alan ve alma

```
alan Ticaret.Api : genel
```

Dosya kapsamlı alan tercih edilir. `: genel`, o dosyadaki üst düzey bildirimlerin varsayılan görünürlüğünü genel yapar. Sınıf üyelerini genel yapmaz. Model alanlarının varsayılanı zaten geneldir ve bu madde onu değiştirmez.

`: genel` yazılmazsa üst düzey bildirimlerin varsayılan görünürlüğü dahili olur.

```
al US.Web
al Ticaret.Modeller
al Ticaret.Modeller olarak Modeller
```

Tek alma anahtar kelimesi `al`dır. Joker alma yoktur. `al Ticaret.*` geçersizdir.

`al` paket indirmez. Paket çözümlemesi ile kaynak alma farklıdır.

## 16. Jenerikler

```
model Sonuç<T>
{
    başarılı: mantıksal
    veri: T?
}

fonksiyon ilk<T>(öğeler: liste<T>): T?
{
    => öğeler.ilkYadaBoş()
}

sınıf Depo<T: Varlık>
{
}

sınıf Depo<T: Varlık, Kaydedilebilir>
{
}
```

Kısıt listesindeki adlar sınıf veya arayüz olmalıdır. Çağrıda tip argümanı çıkarılabiliyorsa yazılmayabilir. Çıkarım başarısızsa tanı hatası verilir; örtük `dinamik`'e düşülmez.

## 17. Koleksiyonlar ve sorgu

Literal:

```
isimler: liste<metin> = ["Ali", "Ayşe"]
```

Boş literal `[]` hedef tipten çözülür. Hedef tip yoksa tanı hatasıdır.

İndis sıfır tabanlıdır. `sayılar[0]` ilk öğedir. Aralık dışı erişim veya atama `US1517` üretir. İndis ataması aynı örneği değiştirir. `liste`, `dizi` ve `sliste` indislenir. `küme` ve `sözlük` indislenmez.

`liste<T>` sıralı, değiştirilebilir ve indisli dizidir. `adet` öğe sayısıdır ve `tamsayı` döndürür. `ekle` aynı örneğe ekler ve o örneği döndürür. `kaldır` ilk eşit öğeyi çıkarır ve `mantıksal` döner. `içerir` eşitlik sorgusudur.

`dizi<T>` sabit uzunluktadır. Uzunluğu `uzunluk` verir. `ekle` ve `kaldır` `US1514` üretir. İndis ataması serbesttir. `dizi.adet` ve `liste.uzunluk` tanı hatasıdır (`US0854`).

`küme<T>` yinelenen öğe tutmaz. Var olan öğenin yeniden eklenmesi örneği değiştirmez. Ekleme sırası dil sözü değildir.

`sözlük<K,V>` anahtar ve değer tutar. `{ anahtar: değer }` literali yoktur. Boş `[]` hedef `sözlük` tipiyle geçerlidir. Dolu köşeli literal `US0809` verir. Kayıt `ekle(anahtar, değer)` ile yazılır. Yinelenen anahtar `US1518` üretir.

`sliste<T>` `liste` ile aynı üyeleri olan, ekleme sırasını koruyan ayrı bir sıralı koleksiyondur. Başka bir söz verilmez.

Sorgu üyeleri kaynağı değiştirmez ve yeni koleksiyon üretir: `filtrele`, `sırala`, `tsırala`, `tersÇevir`, `benzersiz`, `atla`, `sınırla`. Değiştiren üyeler `ekle`, `kaldır` ve indis atamasıdır. `tersÇevir` yeni koleksiyon döndürür.

Sorgu değerlendirmesi heveslidir. Her üye, sonucu hemen maddileştirir. `filtrele` her öğe için yüklemi bir kez çalıştırır ve sırayı korur. `&&` ve `||` kısa devresi yüklem içinde de geçerlidir.

`sırala` artan, `tsırala` azalan sıralar. `t` ters sıralama anlamına gelir. Eşit anahtarlar kaynak sırasını korur. Anahtar seçici öğe başına bir kez çalışır. `metin` karşılaştırması sıra sayısıyladır (ordinal) ve makinenin kültürüne bağlı değildir. `büyük` ve `küçük` bu kuraldan ayrıdır ve Türkçe kültürünü kullanır. Sayı ve `karakter` sıralanabilir. Sıralanamayan anahtar `US0853` verir. Nesne metne çevrilerek sıralanmaz.

`ilk` ilk öğeyi veya ilk eşleşeni verir. Eşleşme yoksa `US1515` üretilir. `ilkYadaBoş` ve `bul` sonuç tipi `T?` dir ve bulamazsa `~` döner. `bul` yüklem ister. Yüklemli `ilkYadaBoş` ile `bul` aynı sonucu üretir. `varMı` ilk doğruda durur; boş kaynak `yanlış`tır. `tümü` yüklemdir, `mantıksal` döner ve boş kaynakta `doğru`dur. İlk yanlışta durur.

`adet` özellik olarak sayıdır. `adet(yüklem)` eşleşenleri sayar. `toplam` ve `ortalama` `ondalık` döner. Boş toplam `0`dır. Boş ortalama, `min` ve `maks` `US1516` üretir. `min` ve `maks` öğenin kendisini döndürür, `~` döndürmez. `benzersiz` ilk görüleni tutar. `atla` ve `sınırla` sırayı korur. Negatif sabit `US0849` tanı hatasıdır. Çalışma anında negatif değer de `US0849` üretir.

Koleksiyon olmayan alıcıda sorgu adı `US0852` verir. Program çalışmaz.

`dön` veya sorgu sırasında aynı koleksiyon değişirse `US1519` üretilir. Anlık görüntü gezintisi yoktur.

`^` sorgu anındaki öğedir. Genel değişken değildir. İç sorgu kendi öğesini kullanır ve bitince dış öğe geri gelir. Döngü değişkeni `^` değildir.

Sorgu:

`filtrele`, `sırala`, `tsırala`, `ilk`, `ilkYadaBoş`, `varMı`, `tümü`, `adet`, `toplam`, `ortalama`, `min`, `maks`, `benzersiz`, `atla`, `sınırla`.

`^` geçerli koleksiyon öğesidir.

```
db.kullanıcılar.ilkYadaBoş(^.id == id)

kullanıcılar
    .filtrele(^.aktif && ^.yaş >= minYaş)
    .tsırala(^.puan)
```

İç içe sorguda `^` en iç sorgu öğesini gösterir. Dış öğe, sorgu lambdasının açık parametresiyle adlandırılır. `^` kapsam dışındaki bir değişkene çözülmez.

`sırala` artan, `tsırala` azalan sıralar.

## 18. seç ve seçenek

`seç` deyiminde `case` ve `default` yoktur. `_` joker koldur.

```
seç gün
{
    1 => syaz "Pazartesi"
    2 => syaz "Salı"
    _ => syaz "Geçersiz"
}
```

Kollar sırayla denenir. İlk eşleşen kol çalışır. Eşleşme yoksa ve `_` yoksa çalışma zamanında hata fırlatılır. Değer döndüren `seç` ifadesi daha sonraki bir sürümün konusudur. Bu sürümde `seç` deyimdir.

```
seçenek SiparişDurumu
{
    Bekliyor
    Onaylandı
    İptal
}
```

Seçenek üyeleri adlarıyla `SiparişDurumu.Bekliyor` biçiminde kullanılır. Aynı seçenekte yinelenen ad hatadır.

## 19. Hata yönetimi

```
dene
{
    işlemYap()
}
yakala hata
{
    syaz hata.mesaj
}
sonunda
{
    temizle()
}
```

Tipli yakalama:

```
yakala hata: VeritabanıHatası
{
}
```

Fırlatma:

```
fırlat @Hata("Kullanıcı bulunamadı")
```

`yakala` en az bir tane olmadan `dene` yazılamaz. `sonunda` isteğe bağlıdır. `yakala` kolları yazım sırasıyla denenir. İlk uyan kol çalışır.

Tipsiz `yakala` kullanıcı hatasını ve çalışma zamanı arızasını yakalar. Tipli `yakala` yalnız `Hata` veya ondan türeyen kullanıcı hatasını, bağlanmış tipin türetme zinciriyle eşleştirir. Ad karşılaştırması yapılmaz. Sıfıra bölme tipsiz yakalamaya düşer. `yakala hata: Hata` onu yakalamaz.

`sonunda` normal bitiş, fırlatma, yakalanan hata, yakalanmayan hata, `=>`, `çık` ve `devam` sonrası çalışır. `sonunda` yeni hata fırlatırsa o hata birincildir. Bekleyen gövde veya `_başında` hatası tanı metnine eklenmez.

`fırlat` bir `Hata` örneği üretir. Yakalanmadan çıkarsa tanı `US1525` olur ve ileti `mesaj` alanıdır. Çalışma zamanı arızası kendi `US15xx` kodunu korur. Dönüş, `çık` ve `devam` hata değildir. `ref`, `çıktı` ve nesne değişikliği sonraki hata yüzünden geri alınmaz.

### 19.1 başında

`başında` hazırlık aşamasıdır ve `dene` ile aynı korumalı bölgededir.

```
başında
{
    bağlantı.aç()
}

dene
{
    veriGetir()
}
yakala hata
{
}
sonunda
{
    bağlantı.kapat()
}
```

`başında` hata verirse `dene` çalışmaz, eşleşen `yakala` çalışır, `sonunda` çalışır.

### 19.2 _başında

```
_başında
{
    bağlantı = db.bağlan()
}

dene
{
    veriGetir()
}
yakala hata
{
}
sonunda
{
    bağlantı?.kapat()
}
```

`_başında` hata verirse:

- `dene` çalışmaz
- `yakala` çalışmaz
- `sonunda` çalışır
- `sonunda` bittikten sonra aynı hata çağırana iletilir

Hata yutulmaz. `sonunda` kendi hatasını fırlatırsa, `_başında` hatası yerine `sonunda` hatası iletilir. `sonunda` hatası, bekleyen `_başında` hatasını tanı olarak saklamaz; çalışma zamanında `sonunda` hatası birincildir.

## 20. Olaylar

```
genel olay kullanıcıEklendi: Kullanıcı
```

Abonelik `+=`, ayrılma `-=` ile yapılır. Tetikleme:

```
kullanıcıEklendi.tetikle(kullanıcı)
```

Olay tipi, dinleyiciye geçen değerin tipidir. `tetikle` o tipte bir argüman ister. Uyumsuz işleyici `US0803` verir. `US0847` bu kuralın kodu değildir.

Abonelikler örneğe aittir. `+=` listeye ekler. Aynı işleyici birden çok kez eklenebilir. `-=` aynı fonksiyon ve aynı alıcıdan oluşan ilk kaydı çıkarır. Kayıt yoksa `US1520` çıkar. `US1514` sabit dizi kuralıdır.

`tetikle` o andaki listeyi abonelik sırasıyla çağırır. Çağrı sırasında eklenen veya çıkarılan dinleyici bu turu bozmaz; yeni dinleyici sonraki turda çalışır. Bir dinleyici hata verirse kalanlar çalışır. Birden çok hata `US1521` ile birleşir. Eşzamansız işleyici `US1524` ile reddedilir.

## 21. eşişlem

```
eşişlem
{
    kullanıcılar: liste<Kullanıcı> =
        bekle kullanıcılarıGetir()

    ürünler: liste<Ürün> =
        bekle ürünleriGetir()
}
```

Bloktaki birbirine bağlı olmayan `bekle` işleri birlikte başlar. Hepsi tamamlanınca blok tamamlanır. Bu, işlemci paralelliği değildir.

Hata kuralı:

- Tüm başlayan işler gözlemlenir. İlk hatada diğerleri sessizce bırakılmaz.
- Birden çok hata varsa hepsi tek bir birleşik hatada saklanır. Birleşik hata, bittiği gözlemlenen hataların tamamını içerir.
- `eşişlem` bir `dene` içindeyse birleşik hata normal `yakala` kurallarıyla yakalanır.
- İşlerden biri hata verirse başarılı olanların sonuçları bloğun değişkenlerine yazılmaz. Blok ya tam biter ya da hata ile çıkar.
- Birden çok hata kaynak sırasıyla `US1521` kodlu tek birleşik hatada saklanır. İlk hata diğerini iptal etmez.
- Çıktı sırası dil sözü değildir. `syaz` kilit sırasıyla yazılır.
- Sorgu ve dönüşüm gövdesi eşzamansız değildir. `bekle` bu gövdelere eklenmez.

## 22. Tarih ve saat

Tipler: `tarih`, `saat`, `tarihsaat`, `süre`. Bunlar metin değildir. Her biri tek bir çalışma zamanı değeridir.

`tarihsaat` duvar saatidir. Saat dilimi ve UTC ofseti saklanmaz. `şimdi` konağın yerel duvar saatini verir. `bugün` aynı saatin tarihidir. İkisi de doğrudan sistem saatini çağırmaz. Konak saati testte sabitlenebilir. İki okuma aynı sabit saatte aynı değeri verir.

```
bitiş: tarihsaat = şimdi + 30.dakika
bekle 2.saniye
```

Süre birimleri sayı alıcısında `süre` üretir: `milisaniye`, `saniye`, `dakika`, `saat`, `gün`, `hafta`. `bekle` ve tarih aritmetiği aynı süre değerini kullanır. Taşma `US1523` verir. Negatif süre çıkarma ile elde edilir. Eksi işaretli süre literali yoktur.

İşlemler:

- `tarihsaat + süre` ve `tarihsaat - süre` sonuç tipi `tarihsaat`dır.
- `tarihsaat - tarihsaat` sonuç tipi `süre`dir.
- `süre + süre` ve `süre - süre` sonuç tipi `süre`dir.
- `süre + tamsayı` uyumsuzdur ve `US0807` verir.

Aynı zaman tipinde `<`, `<=`, `>`, `>=`, `==` ve `!=` değerin kendisini karşılaştırır. Karşılaştırma metne çevrilerek yapılmaz.

Enterpolasyon biçimi kültürden bağımsızdır:

- `tarih`: `yyyy-MM-dd`
- `saat`: `HH:mm:ss`
- `tarihsaat`: `yyyy-MM-dd HH:mm:ss`
- `süre`: milisaniye sayısı ve ` ms`

`bekle` bir süre alırsa o süre kadar bekler ve değer döndürmez.

## 23. Metin

Enterpolasyon ifadesi metin literalinin içinde `{ ... }` bölgesidir. `{{` ve `}}` kaçışı tek süslü parantez üretir.

Standart üyeler: `kırp`, `büyük`, `küçük`, `içerir`, `başlar`, `biter`, `değiştir`, `böl`, `birleştir`.

`büyük` ve `küçük` açık Türkçe kültürü kullanır. Konak kültürü sonucu değiştirmez. `"istanbul".büyük()` sonucu `İSTANBUL`, `"İSTANBUL".küçük()` sonucu `istanbul`dır.

`içerir`, `başlar`, `biter` ve `değiştir` sıra sayısıyla ve büyük/küçük harf duyarlıdır. Bu, `metin` eşitliğiyle aynıdır. Sıralama kuralı bu üyelere yayılmaz.

`böl` ayıracı `liste<metin>` döndürür ve boş parçaları korur. Boş ayıraç `US1536` verir. `birleştir` ayıraç metninin üyesidir ve `liste<metin>` alır: `",".birleştir(["a", "b", "c"])`.

## 24. Girdi ve çıktı

| Ad | Anlam |
| --- | --- |
| `yaz` | Satır sonu olmadan yazar |
| `syaz` | Satır sonu ile yazar |
| `sor` | İstem yazar ve satır okur |
| `oku` | Satır okur |
| `temizle` | Çıktı yüzeyini temizler |

Üst düzey kapsamda bu adlar standart kütüphane tarafından bağlanır. Aynı kapsamdaki yeniden bildirim `US0804` verir.

`sor` istemi satır sonu olmadan yazar, sonra bir satır döndürür. `oku` bir satır döndürür. Girdi bittiğinde `US1526` verilir. `temizle` parantezsiz yazılabilir ve çıktı yüzeyini temizler. Girdi ve çıktı konak üzerinden gider. Test gerçek konsol beklemez.

## 25. Dönüşüm

```
dönüşüm Kullanıcı => KullanıcıDto
{
    id = ^.id
    ad = ^.ad
    soyad = ^.soyad
}
```

```
dto: KullanıcıDto = kullanıcı.dönüştür<KullanıcıDto>()
```

Açık dönüşüm varsa o kullanılır. Eş adlı alanları sessizce kopyalayan örtük eşleme yoktur. Dönüşüm yoksa `US0851` verilir ve program çalışmaz. Aynı kaynak ve hedef çifti yinelenirse `US0850` verilir. Koleksiyon üzerinde `dönüştür<T>()` yoktur.

`^` dönüşüm kaynağıdır. Gövde içindeki iç sorgu kendi öğesini kullanır ve bitince kaynak geri gelir. Hedef örnek, yazılan alanlar bağlı alan sembolleriyle doldurulur. Yazılmayan alan model kuralına uyar: nullable alan `~` olur, zorunlu veya varsayılansız alan `US1512` üretir.

## 26. JSON

```
json.oku(...)
json.yaz(...)
```

```
metinJson: metin = json.yaz(k)
k: Kullanıcı = json.oku<Kullanıcı>(metinJson)
```

`json.oku` JSON metni okur. Dosya yolu okumaz. Dosya `dosya.oku` ile okunur. Hedef tipi soldaki değişkenden veya açık tip argümanından alınır. Hedef tip yoksa `US0855` verilir.

Özellik adı model alan adıdır. `yaş` JSON'da `"yaş"` kalır. Adlar kendiliğinden başka bir yazıma çevrilmez. Unicode adlar geçerlidir.

Bilinmeyen alan `US1527` verir. Eksik ve varsayılansız zorunlu alan `US1528` verir. `varsayılan` varsa eksik alan o değerle dolar. `~` ile JSON `null` eşlenir. Nullable olmayan alana `null` gelirse `US1541` verilir. Bozuk JSON `US1535` verir. Döngüsel nesne `US1529` verir. İç içe model ve `liste<T>` aynı kuralla okunur ve yazılır.

JSON sayısı metinden, hedef US tipine göre okunur. `tamsayı` tam sayı, `utamsayı` işaretsiz tam sayı, `ondalık` 64 bit kayan nokta, `gondalık` 32 bit kayan nokta, `hondalık` yüksek duyarlıklı ondalıktır. Sayı önce kayan noktaya çevrilip sonra hedefe yazılmaz.

Yazım sıkışıktır ve alan sırasını korur. JSON ve dosya API'leri eşzamanlıdır.

## 27. Yapılandırma

Varsayılan dosya adı `uygulamaAyar.json`dır. Çalışma zamanı bu dosyayı başlangıçta en çok bir kez okur.

```
{
  "MongoDb": {
    "Sunucu": "localhost",
    "Port": 27017,
    "Veritabani": "Ticaret"
  }
}
```

```
model MongoDbAyar : Ayar("MongoDb")
{
    sunucu: metin
    port: tamsayı
    veritabani: metin
}
```

`Ayar("...")` kalıtım değildir. Yapılandırma bölümü meta verisidir. Model başka bir modeli bu maddeyle devralmış sayılmaz.

```
bağla ayar: MongoDbAyar
```

Dosya en çok bir kez okunur ve metin önbelleğe alınır. Dosya yoksa `US1532` verilir. Bölüm yoksa `US1533` verilir. `bağla ayar` bu önbellekten modeli üretir ve hücreye yazar. Ortam değişkeni bu dosyanın üzerine yazmaz.

JSON alan adları ile model alan adları büyük/küçük harf duyarsız eşlenmez. `Sunucu` ile `sunucu` aynı değildir. Yanlış ad `US1527` verir. Eksik zorunlu alan ve varsayılan, JSON okuma kuralıyla aynıdır. Örnek dosyadaki büyük harfli adlar dosya şeklini gösterir. Bağlama adları model alan adlarıyla yazılır. Uygulama örneği:

```
{
  "MongoDb": {
    "sunucu": "localhost",
    "port": 27017,
    "veritabani": "Ticaret"
  }
}
```

## 28. Dosya sistemi

```
dosya.oku
dosya.yaz
dosya.sonaEkle
dosya.sil
dosya.varMı

klasör.oluştur
klasör.sil
klasör.varMı
klasör.dosyalar

yol.birleştir
```

Metin dosyası UTF-8 okunur ve yazılır. `dosya.yaz` var olan dosyanın üzerine yazar ve üst klasörü kendiliğinden açmaz. Üst klasör yoksa `US1539` verilir. `dosya.sonaEkle` üst klasör varsa olmayan dosyayı oluşturur. `dosya.oku` ve `dosya.sil` olmayan dosyada `US1537` verir. `varMı` `mantıksal` döndürür ve hata fırlatmaz.

`klasör.dosyalar` yalnız doğrudan çocukların adlarını döndürür. Sıra, sıra sayısıyladır ve konak sırasına bırakılmaz. Boş olmayan klasörü silmek `US1540` verir.

`yol.birleştir` US yol metninde `/` kullanır. `..` çözüldükten sonra yol çalışma kökünün dışına çıkarsa `US1530` verilir. Sembolik bağ `US1531` ile reddedilir. Kök, uygulama çalışma dizinidir. Yorumlayıcı dosyayı doğrudan işletim sisteminden okumaz. Test bellek dosya sistemini kullanır.

## 29. Veritabanı

```
veritabanı UygulamaDb
{
    tablo kullanıcılar: Kullanıcı
    tablo ürünler: Ürün
}
```

```
bağla db: UygulamaDb
```

```
bekle db.kullanıcılar
    .filtrele(^.aktif)
    .sırala(^.ad)
    .tümü()
```

İlişki yükleme:

```
db.siparişler
    .birlikte(^.kullanıcı)
    .birlikte(^.ürünler)
    .ilkYadaBoş(^.id == id)
```

İç içe `birlikte` bu sürümün çekirdeğinde yoktur. Sözdizimi ayrıştırılırsa ve iç içe üye yolu `US0863` verir. Olmayan üye `US0862` verir. Düz üye yolu desteklenir.

`bağla db` kaydı `db` değişkenine yazar. Veritabanı oturumu program ve test çalıştırıcısı içinde bellek sağlayıcısıdır. Bilinmeyen tablo `US1547` verir. `ekle` anahtar alanına kimlik yazar. Olmayan kayıtta `güncelle` ve `sil` `US1548` verir. Doğrulama yazmayı `US1551` ile keser.

Veritabanı sorgusu bağlanmış ağaçtır. Metin SQL üretilmez. `^` satır alanına bağlanır. Yerel değerler parametre gibi tutulur; kullanıcı metni SQL'e eklenmez. Kullanıcı fonksiyonu veya yan etkili ifade süzgeçte `US0865` verir. Sonuç `tümü()` ile `liste` olarak gelir.

Kullanıcı komutu:

```
us veritabanı senkronize
```

`SyncDatabase.Run(root, approve)` API si şema farkını üretir. Kullanıcı göç dosyası yazmaz. Sistem içeride şema geçmişini, uygulanan değişiklikleri, güvenli farkı ve geri alma meta verisini tutar. Yıkıcı fark (`DropColumn`, `NarrowColumn`) onaysız `US1550` verir.

Yıkıcı şema değişikliği kullanıcı onayı olmadan uygulanmaz. Onay yoksa komut değişiklik yapmadan durur ve tanı basar.

Sağlayıcı yoksa sahte sağlayıcı ile mimari sınanır. Gerçek sunucu bağlantısı `BLOCKED_EXTERNAL_INTEGRATION` olarak kalabilir.

## 30. İşlem

```
işlem
{
    bekle db.siparişler.ekle(sipariş)

    eğer !stokYeterli
    {
        gerial
        => yanlış
    }

    bekle db.stok.güncelle(stok)

    onayla
}
```

`onayla` ve `gerial` yalnız `işlem` gövdesinde geçerlidir.

Kural, tek ve belirleyicidir:

- `gerial` işlemi geri alır ve işlem kapsamını kapatır.
- `onayla` işlemi onaylar ve işlem kapsamını kapatır.
- Gövde hata ile çıkarsa ve henüz kapanmadıysa geri alınır.
- Gövde normal biter ve `onayla` veya `gerial` çalışmadıysa kendiliğinden onaylanır.
- Kapanmış bir işlemde yeniden `onayla` veya `gerial` tanı hatasıdır (`US0836`). Çalışma zamanında da hatadır (`US1549`).
- İç içe `işlem` bu sürümde yoktur ve `US0864` verir.

## 31. Doğrulama

Model kısıtları doğrulamaya katılır.

```
doğrula kullanıcı
{
    kural kullanıcı.yaş >= 18
        => "18 yaşından küçük olamaz."

    kural kullanıcı.eposta.içerir("@")
        => "Geçerli e-posta giriniz."
}
```

`kural` koşulu `mantıksal` olmalıdır. Sağdaki metin, koşul yanlışsa üretilen tanıttır. Koşul tipi yanlışsa `US0845` verilir.

`doğrula` hata fırlatmaz. Sonuç `liste<metin>`dir. Geçerli model boş liste döndürür. `zorunlu`, `uzunluk` ve `aralık` bu listede ileti üretir. `zorunlu` boş metni ve `~` değerini boş sayar. Aralık uçları dahildir. `varsayılan` kurulumda uygulanmış olur. `benzersiz`, bellek içi küme verilmeden atlanır ve bu atlama hata değildir. Küme veritabanı sağlayıcısıyla zorunlu olur.

## 32. Bağımlılık bağlama

```
bağla servis: KullanıcıServisi
bağla db: UygulamaDb
bağla ayar: MongoDbAyar
```

`bağla servis` ve `bağla db` kapsayıcıdan çözer. Çözülen örnek, tür adı `servis` veya `db` olan değişkene yazılır. Bu çözümleme `@` değildir ve kurucu çağırmaz. `bağla ayar` yapılandırma bölümünden model örneği üretir; servis kaydı aramaz. `bağla log` derlenmez ve `US0804` verir.

Örnek oluşturma `@Tip(...)` biçimindedir. Alma `al` biçimindedir. Üçü birbirinin yerine geçmez. `@` kapsayıcı sayacını artırmaz. Kayıtsız tip çalışma zamanında `US1544` verir. Bağlayıcı, konağın sonradan ekleyebileceği kaydı önceden reddetmez.

Aynı tip için iki kayıt `US0859` verir. Son kayıt kazanmaz.

## 33. Servis ömrü

```
servis KullanıcıServisi : kapsamlı
{
}
```

| Yazım | Anlam |
| --- | --- |
| `kapsamlı` | Kapsam başına bir örnek |
| `tekil` | Süreç başına bir örnek |
| `geçici` | Her çözümlemede yeni örnek |

`servis` gövdesi sınıf gövdesiyle aynı kuralları kullanır. Varsayılan üye görünürlüğü özeldir. Ömür yazılmazsa veya üç addan biri değilse `US0858` verilir. Varsayılan ömür yoktur.

İki noktanın sağında ömür durur. Arayüz adı yazılmaz. Kayıt, somut servis tipinin kendisidir. Uygulama tipi de odur. Ad benzerliğiyle arayüz aranmaz. `bağla servis: ArayüzAdı` yalnız o tip kayıtlıysa çözülür.

Tek `oluştur` kullanılır. `oluştur` yoksa örtük parametresiz kurucu vardır. Birden fazla `oluştur` `US0860` verir. Çalışma zamanı kurucu seçmez.

`tekil` bir `kapsamlı` servise bağımlıysa `US0861` verilir. `tekil`, kurulusu sırasında `geçici` alabilir; o örnek tekille birlikte yaşar. `geçici`, aynı kapsamda aynı `kapsamlı` örneği alır. `kapsamlı`, her kapsamda aynı `tekil` örneği alır.

Döngü `A → B → A` biçiminde `US1546` verir ve yığın taşırmaz. Dolaylı döngü de aynı tanıdadır. Kimlik tip sembolüdür.

Kuruluş hata verirse örnek önbelleğe yazılmaz. Sonraki çözümleme yeniden dener. Kapanmış kapsamdan çözümleme ve kapanmış kapsamlı örneğin metodu `US1545` verir. `tekil` önbelleği kapsayıcıdadır. `kapsamlı` önbelleği kapsamdadır. `geçici` önbelleğe girmez.

Aynı `tekil` veya aynı kapsamdaki aynı `kapsamlı` servis birlikte çözülürse tek örnek oluşur. Çözümleme yığını çağrıya özeldir. Kullanıcıya açık `dispose` sözdizimi yoktur. Kapsam kapanınca sahip olduğu kapsamlı örnekler bırakılır. Her yerel test yeni kapsayıcı açar; `tekil` testler arasında paylaşılmaz.

Servis metodu olağan metot çağrısıdır. `afonksiyon` `bekle` ile çalışır. Kurucu eşzamanlıdır.

## 34. Günlük

```
log.iz(...)
log.bilgi(...)
log.uyarı(...)
log.hata(...)
log.kritik(...)
```

`log` üst düzey kapsamda hazırdır. Açık `bağla` gerekmez. Her çağrı bir kayıt üretir. Kayıt düzey ve metin taşır. Düzey sırası `iz`, `bilgi`, `uyarı`, `hata`, `kritik`tir. Süzgeç yoktur. İleti, çağrıdan önce hesaplanmış metindir. Günlük bu metni yeniden ayrıştırmaz. Zaman damgası dil sözleşmesi değildir.

## 35. Web

```
kontrolcü KullanıcıKontrolcü : "/kullanicilar"
{
    bağla servis: KullanıcıServisi

    rota GET "/{id}"
    afonksiyon bul(id: tamsayı): Kullanıcı?
    {
        => bekle servis.bul(id)
    }

    rota POST "/"
    afonksiyon ekle(kullanıcı: Kullanıcı): Kullanıcı
    {
        => bekle servis.ekle(kullanıcı)
    }
}
```

Rota, hemen ardından gelen fonksiyona aittir. Rota ile fonksiyon ayrı bildirimlerdir. Rota bir fonksiyonun üst verisidir.

HTTP yardımcıları: `tamam`, `oluşturuldu`, `içerikYok`, `hatalıİstek`, `yetkisiz`, `yasak`, `bulunamadı`, `çakışma`.

HTTP istemcisi:

```
kullanıcı: Kullanıcı = bekle http.GET(url).json()

sonuç: Kullanıcı = bekle http.POST(url, kullanıcı).json()

http
    .başlık("Authorization", "Bearer {token}")
    .GET(url)
```

Fiiller: `GET`, `POST`, `PUT`, `PATCH`, `DELETE`.

## 36. Test

Ayrı bir `atest` anahtar kelimesi yoktur.

```
test "iki sayı doğru toplanır"
{
    sonuç: tamsayı = topla(2, 3)
    beklenen sonuç == 5
}
```

`test` bir bildirimdir. Program yüklenince bulunur ve normal yürütmede kendiliğinden çalışmaz. Adı metin literali olmalıdır. Değilse `US0856` verilir. Aynı ad `US0804` verir. Boş gövde başarılıdır. Gövde içindeki `=>` testi bitirir. İlk yanlış `beklenen` testi durdurur. Sonraki savlar çalışmaz.

`beklenen` ardından gelen ifadenin `mantıksal` ve doğru olmasını ister. Mantıksal değilse `US0857` verilir. Yanlışsa test başarısız olur ve tanı `US1542` kodunu, test adını ve ifadenin kaynak metnini taşır. İfadenin kendisi değerlendirilemezse bu başarısızlık değil, test hatasıdır.

Hata beklentisi `beklenenHata` bloğudur. Bu bir anahtar sözcük değildir; adın tamamı `beklenenHata` ise ve ardından blok geliyorsa test deyimidir. Kullanıcı hatasını ve yakalanabilir `US15xx` arızasını kabul eder. İkisi de yoksa test `US1543` ile başarısız olur. .NET iç istisnası beklenen hata sayılmaz. `sonunda` ve `_başında` iletimi bu sınırın içinde de geçerlidir.

```
beklenenHata
{
    kullanıcıGetir(-1)
}
```

Test gövdesinde `bekle` varsa o test eşzamansız çalışır. Ayrı `atest` yoktur.

Her test yeni bir yürütme durumu alır. Üst düzey deyimler her testten önce yeniden çalışır ve bir kezlik ortak kurulum değildir. Çıktı, girdi, saat, dosya, yapılandırma ve günlük test bazında ayrılır. Varsayılan test saati sabittir. Testler kaynak sırasında, sırayla çalışır. Derleme hatası varsa hiçbir test çalışmaz. Bir testin hatası sonrakini atlamaz.

Komut: `us test`

## 37. Proje

Varsayılan giriş dosyası `ana.us`dir.

Tek dosya çalıştırılabilir. Üst düzey deyimler geçerlidir. `Main` veya sarmalayıcı sınıf gerekmez.

Üst düzey `bekle` geçerlidir. Dosyada üst düzey `bekle` varsa giriş noktası eşzamansızdır.

Proje bildirimi kaynak dilinden ayrıdır. Dosya adı `us.proje`dir.

```
proje "Ticaret"
sürüm "1.0.0"
hedef "uygulama"
başlangıç "ana.us"

paket "US.Json" sürüm "1.0.0"
```

Hedefler: `uygulama`, `kütüphane`, `web`.

`us.proje` ayrıştırıcısı US deyim ayrıştırıcısından ayrı olabilir.

Komutlar:

```
us yeni konsol Merhaba
us yeni kütüphane Matematik
us yeni web TicaretApi
us çalıştır
us çalıştır ana.us
us derle
us test
us yayınla
us paket ekle PaketAdi
us veritabanı senkronize
```

Tanılar Türkçe ve anlaşılırdır.

Paket kurulumu `us paket ekle` ile yapılır. `al` indirme yapmaz.

## 38. Tanılar

Her tanı şunları taşır:

- kararlı kod
- şiddet: bilgi, uyarı, hata
- ileti
- kaynak dosya
- satır
- sütun
- aralık
- isteğe bağlı öneri

İleti Türkçe'dir. Kod `US` ve dört basamaktır. Örnek: `US1021`.

Örnek biçim:

```
Hata US1021

'yaş' değişkeni 'tamsayı' olmalıdır,
ancak 'metin' değer verildi.

12 | yaş: tamsayı = "38"
                    ^^^^

Öneri:
yaş: tamsayı = 38
```

Satır ve sütun 1 tabanlıdır. Aralık, UTF-16 kod birimi ofsetidir. Sütun, satır içindeki UTF-16 kod birimi konumudur. Bu, kayıp karakter üretmeden Türkçe metni adresler.

## 39. Giriş noktası

`ana.us` proje girişidir. `us çalıştır` bu dosyayı, `us.proje` başka bir başlangıç yazmadıkça çalıştırır.

Üst düzey deyimler bir sentetik fonksiyon gövdesi gibi çözülür. Bu sentetik fonksiyonun adı kullanıcı kapsamında görünmez.

## 40. Belirsizlik kararları

Aşağıdaki kararlar bu belirtimin parçasıdır:

1. `_başında` hatası yutulmaz. `sonunda` çalıştıktan sonra çağırana iletilir.
2. `işlem` normal ve eksiksiz biterse kendiliğinden onaylanır. `onayla` erken veya son onayıdır. `gerial` geri almadır. Hata, kapanmamış işlemi geri alır.
3. Noktalı virgül isteğe bağlıdır. Satır sonu, deyim sürdürülemiyorsa deyimi bitirir.
4. Yerel değişkende ilk değer isteğe bağlıdır. Okumadan önce atama zorunludur.
5. Gölgeleme hatadır.
6. Süre birimleri nokta sonrası bağlamsal adlardır. `milisaniye` bu kümededir.
7. `Ayar("...")` kalıtım değildir.
8. Örtük alan eşlemesi yoktur.
9. JSON bağlama katıdır.
10. Joker `al` yoktur.
11. İndis sıfır tabanlıdır.
12. `dizi` sabit uzunluktadır. `ekle` ve `kaldır` çalışmaz.
13. `kaldır` yalnız ilk eşleşmeyi çıkarır.
14. `tersÇevir` ve sorgu üyeleri yeni koleksiyon üretir.
15. `ilk` boş kaynakta hata verir. `ilkYadaBoş` ve `bul` `~` döner.
16. `tümü` yüklemdir ve boş kaynakta doğrudur.
17. Boş `ortalama`, `min` ve `maks` hata verir. Boş `toplam` sıfırdır.
18. Sorgu hevesli maddileşir.
19. `metin` sıralaması sıra sayısıyladır.
20. Gezinti sırasında koleksiyon değişirse çalışma durur.
21. `sliste` ekleme sırasını koruyan sıralı koleksiyondur. `küme` sıra sözü vermez. `sözlük` literali yoktur.
22. Tipsiz `yakala` kullanıcı hatasını ve `US15xx` arızasını yakalar. Tipli `yakala` yalnız kullanıcı hatasını türetme zinciriyle eşleştirir.
23. `sonunda` içindeki yeni hata bekleyen hatanın önüne geçer. Bekleyen hata tanı metnine eklenmez.
24. Kullanıcı hatası `Hata` örneğidir ve yakalanmazsa `US1525` olur. Çalışma zamanı arızası, kontrol sinyali ve yorumlayıcı hatası ayrı kavramlardır.
25. `afonksiyon` ve `bekle` US eşzamansızlığıdır. Kaynak `Task` görmez. Süre beklemesi zamanlayıcıya gider.
26. `RunAsync` yürütme girişidir. `Run` uyumluluk sarmalayıcıdır ve yakalanmış eşitleme bağlamında kullanılmaz.
27. `eşişlem` bağımsız deyimleri birlikte başlatır. Çağrı yığını, `^` yığını ve kontrol akışı çocukta ayrıdır. Yığın nesneleri ve çıktı paylaşılır.
28. Olayda `+=` ekler, `-=` ilk eşleşen fonksiyon ve alıcıyı çıkarır. Eksik çıkarma `US1520` verir. `tetikle` anlık listeyi abonelik sırasıyla çağırır. Dinleyici hataları birleşir. Eşzamansız işleyici yoktur.
29. `tarihsaat` duvar saatidir. Saat dilimi saklanmaz. `bugün` ve `şimdi` konak saatinden okunur.
30. `büyük` ve `küçük` Türkçe kültür kullanır. Metin arama, değiştirme ve bölme sıra sayısıyla ve büyük/küçük harf duyarlıdır.
31. JSON katıdır. Bilinmeyen alan, eksik zorunlu alan, bozuk metin ve nullable olmayan `null` hatadır. Sayı hedef tipe metinden okunur.
32. Dosya UTF-8'dir. Olmayan dosyayı silmek hatadır. Klasör listesi sıra sayısıyladır. Yol kökü terk edilemez.
33. `Ayar("...")` yapılandırma bölümüdür. `uygulamaAyar.json` en çok bir kez okunur.
34. `doğrula` ileti listesi döndürür ve fırlatmaz.
35. `log` düzey ve metin kaydeder. Üst düzeyde hazırdır.
36. `test` bildirimdir ve normal programda kendiliğinden çalışmaz. İlk yanlış `beklenen` testi durdurur. `beklenenHata` kullanıcı hatasını ve `US15xx` arızasını kabul eder. Her test üst düzeyi yeniden başlatır.
37. `bağla`, `@` ve `al` ayrı yollardır. Servis ömrü yazılmak zorundadır. Kayıt somut servis tipidir. Aynı tipin iki kaydı `US0859` verir. Birden fazla `oluştur` `US0860` verir. `tekil`in `kapsamlı` bağımlılığı `US0861` verir. Döngü `US1546`, kayıtsız tip `US1544`, kapanmış kapsam `US1545` verir. Başarısız kuruluş önbelleğe yazılmaz.
38. Veritabanı bellek sağlayıcısıyla çalışır. Sorgu bağlanmış ağaçtır ve SQL metni üretmez. `birlikte` düz üye ister. İç içe ilişki `US0863`, olmayan üye `US0862`, iç içe işlem `US0864`, yan etkili süzgeç `US0865` verir. Bilinmeyen tablo `US1547`, olmayan kayıt `US1548`, kapanmış işlem `US1549`, yıkıcı şema `US1550`, doğrulama engeli `US1551` verir. `işlem` normal bitince onaylanır.
39. Web, `kontrolcü` ve `rota` bildirimlerinden bağlanmış rota tablosu kurar. Fiiller `GET` `POST` `PUT` `PATCH` `DELETE`dır. Bilinmeyen fiil `US0866`, yinelenen yapısal rota `US0868` verir. Yol büyük/küçük harfe duyarlıdır; sondaki `/` kök dışında düşürülür. İstek başına taze DI kapsamı açılır. Gövde bağlama M9 JSON kurallarını kullanır. Dönüş `~` veya birimsiz sonuç `204`tür. Eşleşmeyen yol `404`, yöntem uyumsuzluğu `405`tır. HTTP istemcisi `IHttpTransport` arkasındadır; kayıtsız yol `US1534`, boş başlık `US0867`, hatalı rota değeri `US1552`, desteklenmeyen içerik tipi `US1553` verir.
