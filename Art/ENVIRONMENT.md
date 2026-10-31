# Luge Rush — Çevre Kiti (Environment)

Sahibi: Çevre Tasarımcısı. Tüm varlıklar Blender Python betikleriyle üretilir (tekrarlanabilir, sabit seed).

## Üretim / yeniden üretim
```
"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/env_kit_common.py
"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/env_forest.py
"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/env_alpine.py
"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/env_canyon.py
"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/env_verify_fbx.py   # FBX geri okuma testi
```
| Betik | İçerik |
|---|---|
| `Art/Scripts/env_common.py` | Ortak yardımcılar: palet-UV builder (`B`), tüp/ico/hull/yaprak/çam katmanı primitifleri, FBX export, Eevee render, kompozisyon sahnesi (`TrackScene`). `lr_common.py` kullanılmadı (çalışma başında yoktu); bağımsız. |
| `Art/Scripts/env_kit_common.py` | Ortak kit + biyomlarca paylaşılan kurucular (kaya, şev, çim, uçurum duvarı `cliff_wall`, uzak dağ kartı). |
| `Art/Scripts/env_forest.py`, `env_alpine.py`, `env_canyon.py` | Biyom kitleri + ızgara ve kompozisyon renderları. |
| `Art/Scripts/env_verify_fbx.py` | Tüm FBX'leri yeniden import eder; boyut, tris, pivot ve konumu export anındaki istatistikle karşılaştırır. |

Çıktılar:
- FBX (122 adet): `Game/Assets/_LugeRush/Art/Models/Environment/{Common,Forest,Alpine,Canyon}/SM_Env_<Biyom>_<Ad>_<NN>.fbx`
- Kaynak: `Art/Blender/Env_Common.blend`, `Env_Forest.blend`, `Env_Alpine.blend`, `Env_Canyon.blend`
- Tris/boyut istatistiği: `Art/Export/Environment/stats_*.json`
- Renderlar: `Art/Renders/env_*.png` (aşağıda)

## Teknik kurallar (uygulanan)
- Tek materyal / tek doku: `LR_Palette.png` (Unity'de Filter Mode = **Point**, Mip Maps kapalı veya Point; sıkıştırma: None/High Quality). UV'ler her yüz için hücre ortasına küçük bir üçgen olarak yerleşir.
- Flat shading (her yüz ayrı normal) → Unity import: `Normals: Import`, `Tangents: None` yeterli (normal map yok).
- **Hiyerarşi yok:** her FBX tek bir mesh nesnesidir (alt nesne / boş nesne yok). `bake_space_transform` derin hiyerarşi hatası bu kiti etkilemiyor; `env_verify_fbx.py` ile 122 FBX'in tamamı geri okunup doğrulandı (boyut, tris, konum = 0). VineCurtain_01/02 bilinçli olarak yerden yüksektir (pivot dalın altındaki zeminde, sarmaşık uçları zeminden ~1 m yukarıda) — test bunu "zmin" uyarısı olarak raporlar, hata değildir.
- Export ayarları ART_GUIDE ile birebir: `axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False`.
- 1 birim = 1 m. Pivot = zemin temas noktası (nesne merkezinin altı). Duvar/uçurum modüllerinde pivot **ön yüzün alt-orta noktası**dır.
- Yön: Blender'da "ön" yüzü olan parçaların (uçurum, duvar, şelale, dağ kartları) önü **Blender −Y**'ye bakar → Unity'de +Z (ART_GUIDE'daki kızak ileri yönüyle aynı dönüşüm). Unity'de ilk import'ta bir duvarın önünün +Z'ye baktığını gözle doğrulayın.
- Yaprak/çim kartları çift taraflıdır (arka yüz ayrı, koyu tonlu geometri) → URP Lit materyali **Back face culling açık (Render Face: Front)** kalabilir; "Both" gereksiz.
- Su (şelale, dere) aynı opak palet materyalini kullanır (stilize düz renk). İstenirse `water_*` hücrelerine sahip yüzler için ayrı yarı saydam materyal sonradan eklenebilir (açık konu).

## Palet kullanımı
Satır 6–11 (çevre) + **satır 13 (yeni eklendi, `make_palette.py` ROWS[13])**:
`foam_white, mist_blue, mtn_far_light/base/dark, mtn_forest_far, mtn_forest_haze, mesa_far_light/base/dark, flower_pink, flower_purple, flower_blue, flower_white, leaf_dry, leaf_young`.
Ayrıca okuma amaçlı başka satırlardan hücre kullanılıyor (değiştirilmeden): `wood_*`, `rope` (satır 3; köprü, çit, platform), `rail_metal_*`, `asphalt_*`, `curb_*` (satır 2; maden rayı, önizleme pisti), `sign_red_dark` (dağ evi panjuru), `shadow_blue` (pencere camı), `tint_*` (yalnız render zemini, FBX'te yok). Bu hücrelerin rengi değişirse çevre kiti de etkilenir.

Tekdüzelik kırma: yüz renkleri normal yönüne göre (üst açık / alt koyu) + rastgele ton kayması; kayalarda düzlemsel kesimlerle büyük fasetler; ağaç gövdeleri eğik/bükük; yaprak kümeleri tek ana + uydu bloblar; kanyon kayalarında eğik (tilted) tortul bantları ve rastgele çıkıntı basamakları.

## Varlık listesi (tris = üçgen sayısı, boyut = X × Y × Z metre, Blender ekseni; Z = yükseklik)

### Common (29)
| Varlık | Tris | Boyut |
|---|---|---|
| Slope_01/02/03 (pist kenarı toprak şev, 8 m) | 166 / 166 / 188 | 6×8×2.8 / 5.6×8×4.3 / 5.3×8×2.0 |
| RockSmall_01–04 | 24–28 | ~0.6 m |
| RockMedium_01–04 | 46–60 | 1.6–2.2 m |
| RockLarge_01–04 (04 = ikiye yarılmış kaya) | 56–98 | 3.5–5.5 m |
| Grass_01–04 (kısa yoğun / uzun / tohum başlı / geniş yaprak) | 60–144 | 0.7–1.3 m |
| Gravel_01 (yaygı) / _02 (yığın) | 208 / 194 | 2.5 m |
| Fence_01 (sağlam) / _02 (kırık), 4 m | 96 | 4.5×0.4×1.5 |
| Stump_01/02, Log_01 | 134 | — |
| MountainCard_01 (mavi uzak sıra) / _02 (ormanlı tepeler) / _03 (keskin zirveler) | 128 | 320 m genişlik, 53–118 m |

Şev parçaları: pist kenarı (toe) x=0'da, yamaç +X yönüne yükselir, uzunluk Y boyunca 8 m. Sol kenarda 180° döndür. Uçlar birebir eşleşmez → 0.5–1 m üst üste bindirerek diz.

### Forest — Yeşil Vadi (29)
| Varlık | Tris | Boyut / not |
|---|---|---|
| TallTree_01 klasik 3 dallı | 558 | 21 m |
| TallTree_02 çatallı (Y), açık gri gövde | 414 | 23.5 m |
| TallTree_03 kavisli palmiye (yapraklar + kuru yaprak + hindistancevizi) | 540 | 16.4 m |
| TallTree_04 katmanlı yağmur ormanı ağacı, açık gövde | 498 | 23.8 m |
| TallTree_05 ince, sarmaşık sarılı, epifitli | 500 | 16.6 m |
| GiantTree_01–03 (payanda kökleri, şemsiye taç, sarkan lianlar) | 893 / 1018 / 770 | 19–21 m taç çapı, 20–27 m |
| Bamboo_01–04 (03 = altın sarısı bambu) | 1173–1367 | 8–12 m |
| BananaPlant_01 tek muz / _02 3 gövdeli muz kümesi / _03 genç muz / _04 fil kulağı (Alocasia) | 252 / 476 / 308 / 224 | 1.6–3.9 m |
| Fern_01/02 (yer eğreltisi) / _03 (ağaç eğreltisi, 1.8 m gövde) | 154 / 126 / 149 | — |
| VineCurtain_01/02 (dal + sarkan sarmaşık, 02 çiçekli) | 612 / 654 | 5.8 m / 4.4 m genişlik |
| FlowerBush_01 kırmızı / _02 sarı / _03 pembe-beyaz | 118–158 | ~1.5 m |
| MossRock_01–03 | 92 / 106 / 174 | 1.7 / 3.6 / 5.1 m |
| Footbridge_01 | 936 | açıklık 16 m (X boyunca), güverte 4.65 m, **kiriş altı serbest yükseklik ≈ 4.25 m**, kuleler x = ±5.2 m; diz destekleri dahil 4.25 m yükseklikte serbest genişlik ≈ 7.2 m (pist 6 m) |
| ViewPlatform_01 (2.8 m yükseltilmiş, sazdan çatılı, merdivenli) | 728 | 5.7×7.3×7.2 |

Not: BananaPlant_02 (476) ve _03 (308) "çalı ≤200" bütçesini aşar; bunları çalı değil orta boy bitki (≤500) olarak sınıflandırdım. Bambu kümeleri büyük ağaç bütçesinde (≤1.5k).

### Alpine — Çam Zirvesi (28)
| Varlık | Tris | Boyut / not |
|---|---|---|
| Pine_01 klasik / _02 ince uzun ladin / _03 kısa geniş çift tepeli / _04 rüzgâr yatırmış (bayrak taç) / _05 karlı | 125–191 | 10–19 m |
| FallenPine_01 (kök plakalı devrik çam) | 229 | 11 m |
| Cliff_01 duvar 10×8 / _02 yüksek duvar 8×14 (karlı) / _03 sarkan (overhang) / _04 blok kaya yığını | 200–274 | ön yüz −Y |
| Waterfall_01 (9 m tek düşüş + havuz + köpük + çerçeve kayalar) / _02 (3 basamaklı kaskad) | 385 / 591 | bir Cliff modülünün önüne yerleştir |
| Stream_01 düz / _02 45° dönüş (10 m, su seviyesi −0.22 m) | 308 / 309 | Y boyunca, uç uca eklenebilir |
| TunnelMouth_01 | 434 | **iç genişlik 7.2 m (≥6), merkez yükseklik 5.8 m, kenarda 3.8 m; ahşap portal: dikmeler arası 6.6 m, kiriş altı 4.9 m (≥4)**. Pist +Y boyunca içeri girer, derinlik 8 m |
| TunnelSegment_01 | 208 | aynı profil, 8 m, uç uca tekrar (tünelin içi) |
| Chalet_01 (taş temel, kütük duvar, taş plaka çatı, panjur, çiçek kutusu, balkon, odun yığını, baca) | 1224 | 8.5×6.8×7.5 |
| SnowPeaks_01/02 (karlı zirve arka plan kartları) | 128 | 420–480 m genişlik, 168–218 m |
| Flowers_01 mor/mavi başak / _02 papatya / _03 karışık | 138–158 | ~0.5 m |
| Slope_01–03, Grass_01–03 | 84–184 | Common'ın alp çayırı paleti |

### Canyon — Kızıl Kanyon (36)
| Varlık | Tris | Boyut / not |
|---|---|---|
| Mesa_01 hoodoo kule + şapka kaya / _02 geniş düz tepeli mesa / _03 ikiz kule / _04 aşınmış butte + kum eteği / _05 ince sütun + dengede kaya | 366–651 | 18–24 m yükseklik, 8–31 m genişlik |
| Wall_01 12×10 / _02 10×18 / _03 alçak sarkan / _04 oyuk (alt girinti) | 182–292 | ön yüz −Y, bantlar tortul katman sınırlarına hizalı |
| GorgeWall_01 (22 m) / _02 (18 m, güçlü sarkma) — dar geçit duvarları | 274 / 234 | karşılıklı yerleştir, aralık 9–12 m |
| Arch_01 | 650 | **serbest açıklık ≥ 9.4 m (≥8), kemer altı ≈ 9.5 m**; pist Y boyunca altından geçer |
| Saguaro_01–03 (kaburgalı gövde, 03 çiçekli) | 214–420 | 5.7–9.4 m |
| PricklyPear_01/02 (meyveli) | 190 / 184 | ~2 m |
| DryBush_01 dallı / _02 adaçayı öbeği / Tumbleweed_01 | 164–180 | ~1 m |
| MineRail_01 sağlam / _02 hasarlı, kuma gömülü (6 m, 0.6 m hat) | 192 / 208 | Y boyunca uç uca |
| MineCart_01 (cevherli) | 340 | 1.2×1.6×1.3 |
| WaterTower_01 | 634 | 10 m |
| MesaCard_01 (uzak mesa silueti kartı) | 128 | 460 m |
| RockSmall/Medium/Large_01–02, Slope_01–03, DryGrass_01–02 | 24–184 | Common'ın kanyon paleti |

Not: Saguaro_01 (300) ve _03 (420) "çalı" değil ağaç sınıfında değerlendirildi.

## Yerleştirme önerileri
Genel: her örnekte rastgele Y-rotasyonu (0–360°) + ±%15 ölçek + ±3° eğim (kayalar ±10°). Aynı varyant yan yana tekrarlanmasın. Ağaçların gövde tabanını 0.1–0.3 m zemine göm. Statik nesneler: `Static` (batching + GI) işaretle; büyük ağaçlar ve kayalarda yalnız gövde/kaya için basit collider (Capsule/Box/Mesh convex); yaprak, çim, çiçek → collider yok.

| Biyom | Bölge | Varlıklar | Yoğunluk | Ölçek |
|---|---|---|---|---|
| Orman | Pist kenarı 0–2 m | Grass, Fern_01/02, FlowerBush, küçük kaya | 1 öbek / 1.5 m² | 0.8–1.5 |
| | 1–8 m | Bamboo (kümeler halinde 2–4), BananaPlant, Fern_03, MossRock | 1 / 10 m² | 0.85–1.3 |
| | 3–30 m | TallTree (aralık ≥2.5 m) | 1 / 12 m² | 0.8–1.25 |
| | 7–22 m | GiantTree (aralık ≥8 m; taçları pist üstüne sarksın) | 1 / 150 m² | 0.9–1.15 |
| | Kenar | Slope (pist boyunca kesintisiz), VineCurtain (pist kenarı veya köprü altı), Footbridge (pist başına 1), ViewPlatform (virajda) | — | 1.0 |
| Alp | Yukarı yamaç | Cliff_01–03 sıra halinde (±10° döndür, 1–2 m bindir), Waterfall önlerinde | modül/9 m | 0.9–1.2 |
| | Her iki yan | Pine (yamaçta sık, aralık ≥3.5 m; vadi yanında seyrek ≥5 m), FallenPine, Cliff_04 | 1 / 25 m² | 0.7–1.25 |
| | Çayır | Flowers + Alpine Grass kümeler halinde | 1 / 2 m² | 0.8–1.5 |
| | Özel | TunnelMouth + TunnelSegment (tünel başına 1+n), Chalet (panoramik virajda vadi tarafı), Stream (çayırda uç uca), Fence | — | 1.0 |
| Kanyon | Pist kenarı | DryGrass, DryBush, Tumbleweed, küçük/orta kaya | 1 / 6 m² | 0.7–1.4 |
| | 1.5–14 m | Saguaro (aralık ≥3 m), PricklyPear | 1 / 40 m² | 0.8–1.3 |
| | 20–35 m | Wall_01–04 iki yanda (90° pisti gören), GorgeWall dar geçitte 4–6 m mesafede | modül/11 m | 1.0–1.15 |
| | Ufuk 200–600 m | Mesa_01–05 (ölçek 1.1–2.4) + MesaCard | — | — |
| | Özel | Arch (pist başına 1), MineRail + MineCart (pist yanı düzlükte), WaterTower | — | 1.0 |

Arka plan kartları: kamera yönüne bakacak şekilde 400–1000 m uzağa, tabanı ufkun 40–150 m altına yerleştir; sis ile birlikte kullan. Kartlar yalnızca önden görünür (tek taraflı).

## Işık / sis / gökyüzü (Unity URP önerileri)
Önizleme renderlarındaki ayarların URP karşılığı. Directional Light rotasyonu = (X: yükseklik açısı, Y: azimut). Sis: Lighting > Environment > Fog (Linear). Ambient: Gradient.

| Ayar | Orman — Yeşil Vadi (öğle) | Alp — Çam Zirvesi (sabah) | Kanyon — Kızıl Kanyon (gün batımı) |
|---|---|---|---|
| Güneş rotasyonu | (68°, 200°, 0) | (24°, 115°, 0) | (10°, 250°, 0) |
| Güneş rengi / yoğunluk | #FFF1D6 / 1.6 | #FFE6C2 / 1.8 | #FFB070 / 2.0 |
| Gölge | Soft, strength 0.85, mesafe 120 m, 4 cascade | Soft, 0.9, 150 m | Soft, 1.0, 150 m (uzun gölgeler) |
| Skybox (Gradient/Procedural) | tepe #BFD9E8, ufuk #DDE8D8 | tepe #4F95D6, ufuk #CFE3F2 | tepe #5A3F8E, ufuk #F0A060 (gradyan ufuk ağırlıklı) |
| Ambient (Sky/Equator/Ground) | #A9C9B0 / #7FA27A / #3A4A2E, yoğunluk 1.0 | #A8C8E8 / #B9C8B0 / #5A6A50 | #8A6A9E / #E09A70 / #6A3A30 |
| Sis rengi | #B9D6AE (yeşil hava) | #CFE0EE | #D58C86 (pembe-mor) |
| Sis başlangıç / bitiş (Linear) | 15 m / 160 m | 60 m / 900 m | 40 m / 600 m |
| Post-process (Volume) | Bloom 0.3 (thr 1.1), Color Adj. sat +10, hafif yeşil Shadows/Midtones; ıslak asfalt için pist materyalinde Smoothness 0.6 | Bloom 0.2, kontrast +10, beyaz dengesi temp −5 | Bloom 0.4, temp +15, tint +5 (macenta), vignette 0.25 |
| Ekstra | Ağaç arası güneş huzmesi: Volumetric yoksa ışık şaftı kartları (sonraki faz) | Şelalede köpük partikülü (sonraki faz) | Tumbleweed için rüzgâr animasyonu (gameplay) |

Mobil profil: sis mesafelerini %30 kısalt, gölge mesafesini 60–80 m'ye indir, GiantTree/Bamboo için LOD1 gerekebilir (bkz. açık konular).

## Renderlar
- Kompozisyon: `Art/Renders/env_forest_composition.png`, `env_alpine_composition.png`, `env_canyon_composition.png`
- Varyant ızgaraları: `env_common_grid.png`, `env_common_mountains.png`, `env_forest_grid_trees.png`, `env_forest_grid_plants.png`, `env_forest_grid_structures.png`, `env_alpine_grid_trees_rocks.png`, `env_alpine_grid_water_props.png`, `env_canyon_grid_rocks.png`, `env_canyon_grid_props.png`

Kompozisyon renderlarındaki pist şeridi ve arazi yalnızca önizleme içindir (FBX değil); gerçek pist Pist/Seviye ekibinindir.

## Açık konular
- LOD yok. En ağır parçalar Bamboo (≤1.37k), Chalet (1.22k), GiantTree (≤1.02k). Mobilde yoğun yerleşim için LOD1 (≈%40) veya impostor kartı önerilir.
- Su opak palet rengi; yarı saydam/akan su shader'ı (UV kaydırma) istenirse ayrı materyal + ayrı UV gerekir.
- Rüzgâr animasyonu (yaprak/bambu salınımı) için vertex color maskesi yok; istenirse eklenebilir.
- Şev parçaları birebir tile etmez (organik bindirme). Seviye tasarımcısı terrain kullanacaksa şevler yalnızca pist kenarı detay şeridi olarak kullanılmalı.
