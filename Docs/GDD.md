# Luge Rush — Oyun Tasarım Belgesi (GDD)

> Çalışma adı: **Luge Rush**. Platform: **PC (Windows)**, klavye + gamepad. Motor: **Unity 6000.3.19f1 (URP)**. Sanat: **Blender 5.2**, low-poly.

## 1. Referans (videodan çıkarılanlar)
Gerçek hayatta bir **luge (yaz kızağı)** pisti, sürücü gözünden çekilmiş:
- **Araçlar:** Motorsuz, yerçekimiyle yokuş aşağı giden tek kişilik plastik kızaklar. Alçak, küvet şeklinde gövde; öne uzanan ayak boşluğu; ortada **gidon** (sağa/sola çevir = dön, kendine çek = fren). Küçük tekerlekler gövdenin altında gizli.
- **Renkler:** Turuncu (oyuncu), kırmızı, sarı, turkuaz/mavi. Sürücüler renkli **kasklı** (sarı, mavi, yeşil, siyah).
- **Pist:** 3–4 m genişliğinde koyu asfalt/beton şerit, yer yer ıslak parlaklık. Kenarlarda **sarı-siyah çizgili** yükseltilmiş bordür, dış virajlarda **metal korkuluk** ve **file/ağ** bariyerler. Keskin, eğimli (banked) virajlar, uzun düzlükler.
- **Çevre:** Yoğun **tropik orman** — bambu, muz/palmiye yaprakları, yüksek ağaçlar, sarmaşıklar; sarı **uyarı tabelaları**; ağaçların arasından süzülen güneş.
- **Bitiş:** Damalı bayraklı büyük **bitiş takı** ("LUGE" yazılı yeşil kemer), ardından **kırmızı flama/bayrak** asılı kapalı bitiş alanı.
- **Dinamik:** Kızaklar arka arkaya dizilir, sollama yapılır, yakın temas olur.

> Not: Videodaki işletmenin adı/logosu/ulusal bayrakları **kullanılmaz**; her şey özgün tasarımdır (bitiş takında "LUGE RUSH" yazar, flamalar desensiz/özgün).

## 2. Oyun özeti
Arcade hissiyatlı, yerçekimiyle çalışan kızak yarışı. Oyuncu tek başına botlara karşı veya oda kurup arkadaşlarıyla (internet, oda kodu) yarışır. 3 harita, oyuncunun seçtiği tur sayısı (1–10, varsayılan 3), 4 kızak tipi.

## 3. Temel oynanış
| Mekanik | Açıklama |
|---|---|
| **Yerçekimi** | Hız, eğimden gelir. Motor yok. Yuvarlanma direnci + hava direnci sınırlar. |
| **Direksiyon** | Gidon; hız arttıkça dönüş yarıçapı büyür. |
| **Fren** | Gidonu çekmek → yavaşlama. Viraja girişte kısa fren önemlidir. |
| **Drift** | Hızlıyken fren + dönüş → arka kayar, **drift metresi** dolar (mini/süper boost seviyeleri). Bırakınca boost. |
| **Rüzgar çekme (slipstream)** | Önündeki kızağın 2–10 m arkasında, ±1.5 m hizada → hava direnci azalır, metre dolunca kısa boost. |
| **Başlangıç itişi** | Geri sayım sırasında "GO" anında itiş tuşuna doğru zamanlamayla basmak → başlangıç boostu (erken basarsan küçük ceza). |
| **Çarpışma** | Kızaklar fiziksel olarak çarpışır; bordür/korkuluğa sürtünme hız kaybettirir. Hafif kızaklar daha kolay savrulur. |
| **Pistten çıkma / takılma** | Pist dışına düşen veya 3 sn takılan kızak son geçilen kontrol noktasına **reset**lenir (R tuşu ile manuel de). |
| **Lift** | Her turun sonunda pist, kızağı tepeye çeken **kablolu lifte** girer (~6 sn). Liftte kontrol yok, sıralama korunur, kızaklar arası mesafe sıkışmaz (sabit hız). Kamera hafif sinematik açıya geçer. |

### Kontroller
| Eylem | Klavye | Gamepad |
|---|---|---|
| Dön | A/D, ←/→ | Sol çubuk |
| Fren | S, ↓, Space | Sol tetik (LT) |
| Drift | Shift (fren+dön ile de tetiklenir) | RB |
| İtiş (başlangıç) | W, ↑ | A / Çarpı |
| Kamera (1./3. şahıs) | C | Y / Üçgen |
| Geriye bak | Q | RS tık |
| Reset | R | Back/Select |
| Duraklat | Esc | Start |

## 4. Kızak tipleri
Videodaki tarza sadık, her biri 4 renk seçeneği (turuncu, kırmızı, sarı, turkuaz). Değerler `SledStats` ScriptableObject'lerindedir, oyun içinde 1–5 bar olarak gösterilir.

| Tip | Ad | Görünüm | Kütle | Azami hız | Tutuş | Dönüş | Drift | Not |
|---|---|---|---|---|---|---|---|---|
| Klasik | **Orman** | Videodaki küvet tipi, yuvarlak hatlar | 3 | 3 | 3 | 3 | 3 | Dengeli, yeni başlayan |
| Spor | **Ok** | Alçak, uzun burun, yan kanatçıklar, yarış numarası | 2 | 3 | 5 | 4 | 3 | Virajlarda en iyi |
| Tandem | **İkiz** | İki kişilik uzun kızak (arka koltukta yolcu) | 5 | 5 | 2 | 2 | 2 | Düzlükte en hızlı, ağır |
| Mini | **Fıstık** | Kısa, tombul, büyük tampon | 1 | 2 | 4 | 5 | 5 | Çevik, kolay savrulur |

## 5. Haritalar (3 adet, hepsi kapalı devre + lift)
| # | Ad | Tema | Öne çıkanlar | Işık |
|---|---|---|---|---|
| 1 | **Yeşil Vadi** | Tropik orman (videoya en yakın) | Bambu koruları, dev yapraklar, ıslak asfalt parıltısı, file bariyerler, ahşap köprü, bitiş takı + flamalı kapalı bitiş alanı | Öğle, ağaç arası güneş huzmeleri, hafif sis |
| 2 | **Çam Zirvesi** | Alp dağı | Çam ormanı, granit kayalar, şelale ve dere, uçurum kenarı panoramik viraj, kaya tüneli, ahşap dağ evi | Sabah, berrak gökyüzü, uzak karlı zirveler |
| 3 | **Kızıl Kanyon** | Çöl kanyonu | Kızıl kaya kuleleri (mesa), kaktüs, kuru çalı, kanyon içi dar geçit, doğal kaya kemeri, terk edilmiş maden rayları | Gün batımı, turuncu-mor gökyüzü, uzun gölgeler |

Her pist: ~1.2–1.8 km iniş + ~150 m lift. Tur süresi hedefi 60–90 sn. Her pistte en az: 1 uzun düzlük (slipstream), 1 saç tokası viraj, 1 S-viraj, 1 eğimli (banked) geniş viraj, 1 kısa tünel/kapalı bölüm. Kısayol ilk sürüm kapsamından çıkarıldı (tek hatlı AI ve reset sistemiyle uyumsuz).

## 6. Modlar ve akış
```
Ana Menü ─┬─ Tek Oyuncu ─→ Kızak + renk seç → Harita seç → Tur (1–10) + Bot sayısı (0–7) + Zorluk → Yarış
          ├─ Çok Oyunculu ─┬─ Oda Kur  → Oda kodu gösterilir; host harita/tur/bot ayarlar → Hazır → Yarış
          │                └─ Odaya Katıl (kod gir) / Hızlı katıl (açık oda listesi)
          ├─ Ayarlar (grafik, ses, kontroller, dil TR/EN)
          └─ Çıkış
Yarış → Sonuç ekranı (sıra, toplam süre, en iyi tur) → Tekrar / Menü
```
- Oda kapasitesi: **8 kızak** (oyuncular + botlar). Host boş yerleri botla doldurabilir.
- Bot zorlukları: Kolay / Normal / Zor (yarış çizgisi doğruluğu, fren zamanlaması, drift kullanımı, lastik bant (rubber-band) miktarı).

## 7. HUD
Sıra (1/8), tur (2/3), tur süresi + en iyi tur, hız (km/s), drift/boost metresi, mini harita (pist çizgisi + noktalar), geri sayım, "SON TUR" bildirimi, sollama bildirimleri.

## 8. Kamera
- **1. şahıs** (videodaki gibi): Kamera kask içinde; sürücünün kolları, gidon, bacakları ve kızak burnu görünür. Hızla FOV artar, hafif titreşim.
- **3. şahıs**: Arkadan-yukarıdan takip, viraja göre yumuşak sapma.
- Lift sırasında: yan sinematik açı. Bitişte: yörünge kamerası.

## 9. Ses (sonraki faz)
Tekerlek uğultusu (hıza bağlı), fren sürtünmesi, drift cızırtısı, korkuluğa çarpma, lift zinciri tıkırtısı, orman/dağ/çöl ambiyansı, menü müziği + yarış müziği.

## 10. Kapsam dışı (şimdilik)
Mobil, kariyer modu, kozmetik mağaza, kalıcı hesap/ilerleme.
