# Luge Rush — Art (Kızaklar & Pist Kiti)

Tüm modeller Blender Python betikleriyle üretilir (tekrarlanabilir). Bu belge kızakları, pist kitini ve ortak yardımcı kütüphane `lr_common.py`'yi anlatır. Animasyon notları için `ANIMATION.md`'ye bakın.

## Betikleri çalıştırma
```
set BL="C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe"
%BL% -b -P Art/Scripts/sleds_build.py              # 4 kızak (FBX + .blend + render)
%BL% -b -P Art/Scripts/sleds_build.py -- Sport     # tek kızak
%BL% -b -P Art/Scripts/track_kit.py                # pist kiti (FBX + Track_Kit.blend + render)
%BL% -b -P Art/Scripts/track_kit.py -- --no-render
python Art/Scripts/make_palette.py                 # paleti yeniden üret (satır 14 = pist kiti renkleri)
```
| Çıktı | Yer |
|---|---|
| Kızak FBX | `Game/Assets/_LugeRush/Art/Models/Sleds/SM_Sled_{Classic,Sport,Tandem,Mini}.fbx` |
| Pist FBX | `Game/Assets/_LugeRush/Art/Models/Track/SM_Track_*.fbx` |
| Kaynak | `Art/Blender/Sleds_<Tip>.blend`, `Art/Blender/Track_Kit.blend` (modüller ızgara halinde) |
| Önizleme | `Art/Renders/sled_<tip>.png` (4 renk), `sled_<tip>_hero.png` (yakın plan), `sled_<tip>_with_rider(_back).png` (SK_Rider Sit_Idle ile oturma kontrolü), `sled_<tip>_fp.png` (1. şahıs), `track_kit.png`, `track_finish.png`, `track_canopy.png`, `track_lift.png`, `track_lift_lane.png`, `track_lift_wheel.png`, `track_props.png` |

Unity projesi yoksa (`Game/ProjectSettings/ProjectVersion.txt` yok) FBX'ler `Art/Export/<Kategori>/` altına yazılır. `.meta` dosyası üretilmez.

## Eksenler ve pivot
- 1 birim = 1 m. Blender'da ön = **-Y** → Unity **+Z**. Blender +Z = Unity +Y. Blender +X = Unity −X (sol taraf; `.L` adları Unity'de de sol).
- FBX ayarları ART_GUIDE'dakilerle aynı (`lr_common.FBX_SETTINGS`); Unity'de nesnelerin rotasyonu sıfır gelir.
- **Bilinen Blender 5.2 hatası:** `bake_space_transform=True` ile, dışa aktarılan en üst nesnenin **2+ seviye altındaki** nesnelerin yerel dönüşümü bozuk yazılıyor. Bu yüzden kızaklarda düzenleyici kök boşluk (`SM_Sled_*`) FBX'e yazılmaz (`export_fbx(..., drop_root=True)`); Unity kök GameObject'i dosya adıyla zaten oluşturur, hiyerarşi aynı kalır. Hiyerarşiyi en fazla 1 seviye derin tutun — `export_fbx` derin nesneler için uyarı basar.

## Renk yöntemi (kızak gövdesi)
- Tek doku: `LR_Palette.png`. Her yüzün UV'si bir palet hücresinin merkezine yazılır (`MeshBuilder` → yüz katmanı `lr_pal` → `to_object()` UV'leri yazar).
- Kızakta **`Body`** alt nesnesi yalnızca **`tint_white / tint_light / tint_mid / tint_dark`** hücrelerini kullanır ve **`LR_Palette_Tint`** materyaline sahiptir. Unity'de bu materyalin `_BaseColor`'ı kızak rengiyle çarpılır:
  turuncu `#F2792B`, kırmızı `#D93A3A`, sarı `#F2C230`, turkuaz `#3BB8C4`. Gri tonlar sayesinde gövdede açık/orta/koyu varyasyon korunur (üst yüzler ve kenar dudakları açık, iç kısım koyu).
- Diğer tüm parçalar (`Trim`, `Chassis`, `Handlebar`, `Wheel_*`) sabit renkli **`LR_Palette`** materyalini kullanır (çıkartmalar, vidalar, tampon, numara paneli renk değiştirmez).
- Başlangıç kapısı lambaları **`LR_Palette_Emissive`** kullanır (aynı atlas hem BaseMap hem Emission map).

## Kızak hiyerarşisi ve boşluklar
```
SM_Sled_<Tip>            (Unity'nin dosya adından oluşturduğu kök; pivot = zemin temas merkezi)
  Body                   tint gövde (LR_Palette_Tint)
  Trim                   çıkartma şeritleri, vidalar, yan kauçuk şerit, ön/arka tampon, ayak basamakları, numara paneli, reflektörler
  Chassis                gövde altında görünen koyu gri etek (skirt) + şasi rayları, akslar, çatallar, fren pabucu
  Wheels_Mid             (yalnız Tandem) orta destek tekerlekleri, dönmez
  Handlebar              pivot = kolon tabanı, dönme ekseni = yerel yukarı (Unity Y). Direksiyon = Y etrafında döndür.
    Grip.L, Grip.R       el IK hedefleri (gidonla birlikte döner); dönme ekseni üzerinde
  Wheel_FL/FR/RL/RR      pivot = aks merkezi, aks = Unity X ekseni (X etrafında döndür)
  Seat                   sürücü kalça eklemi (SK_Rider Hips orijini), yerden 0.25 m
  Camera_FP              1. şahıs göz noktası (Sit_Idle'da Head + göz ofseti) = Seat + (0, 0.68 yukarı, 0.08 ileri)
  Seat2                  (Tandem) arka yolcu
  Grip2.L, Grip2.R       (Tandem) arka yolcunun sabit gidonu (Trim içinde), Seat2'ye göre aynı ofsetler
```
**Sürücü sözleşmesi (SK_Rider / ANIMATION.md ile uyumlu, Unity ekseni, Seat'e göre):** `Grip.L/R` = (∓0.20, +0.20, +0.55); gidon ekseni dikey, merkez (0, 0.20, 0.55), `Handlebar` pivotu bu eksenin altında konsol üstünde; ayak duvarı/basamakları Seat'in 0.88 m önünde (taban ~0.84). Tandem: `Seat2` = `Seat` + 1.16 m geride; yolcunun ayakları ön koltuk kabuğunun arkasına dayanır.

Form: alçak gövde (yan duvar üstü 0.24–0.33 m), gövdeye kalıplanmış koltuk kabuğu (yükseltilmiş, yatık sırtlık + yan destekler), oturak + uyluk destekleri, bacaklar arasında orta konsol (gidon kolonu buradan çıkar), daralan U-burun + yatay tampon, yanlarda kauçuk şerit, altta görünen koyu etek ve tekerlekler.

| Kızak | Boyut (U×G×Y, m; tampon ve sırtlık dahil) | Tris |
|---|---|---|
| SM_Sled_Classic (Orman) | 1.53 × 0.82 × 0.67 | 2106 |
| SM_Sled_Sport (Ok) | 1.75 × 0.94 (yan podlar) × 0.61 | 2478 |
| SM_Sled_Tandem (İkiz) | 2.70 × 0.82 × 0.67 | 2924 |
| SM_Sled_Mini (Fıstık) | 1.43 × 0.92 × 0.67 | 2206 |

## Pist kiti — modül kuralları
- Uç uca eklenen modüller: **pivot başlangıç ucunda (0,0,0), uzunluk Unity +Z boyunca**. x = 0 modülün orta çizgisi.
- Kenar modüllerinin "dış" tarafı (direk braketleri, file payandaları) **Unity +X**. Sol kenarda kullanırken Y etrafında 180° döndürüp modül uzunluğu kadar ileriden başlatın (veya X ölçeğini −1 yapın).
- Tabelalar, kapılar, bitiş takı ve işaret direkleri **Unity −Z'ye bakar** (yaklaşan sürücüye dönük).

| Modül | Uzunluk / ölçü | Tris | Not |
|---|---|---|---|
| SM_Track_Curb_2m | 2 m, 0.30 m genişlik, 0.12 m yükseklik | 104 | sarı-siyah 0.5 m bloklar, pist tarafı Unity −X |
| SM_Track_Rail_4m | 4 m (direkler 0 ve 2 m'de) | 160 | 2 boru: 0.50 / 0.88 m |
| SM_Track_Rail_End | 1 m | 304 | boruları zemine kıvrılan uç parça; dizinin sonuna |
| SM_Track_Net_4m | 4 m (sınır tam 0–4 m), 1.9 m yükseklik | 144 | elmas file (çift yüzlü), arka payandalar; direkler 2 m aralıkla, ilki başlangıç ucuyla hizalı |
| SM_Track_Sign_CurveLeft / CurveRight / Slow / Warning / Stop | direk 2.1 m | 104–456 | sarı tabelalar; "YAVAŞ", "DUR" yazıları geometri |
| SM_Track_MarkerPost_01..05, _100m/_200m/_300m | 1.12 m | 188–724 | numara/mesafe direği (iki yüzde rakam) |
| SM_Track_StartGate | iç açıklık 5 m, 3.6 m | 2954 | önde "BAŞLANGIÇ", arkada "LUGE RUSH"; çocuklar `Lamp_Red_1..3`, `Lamp_Green` (sürücüye göre soldan sağa) |
| SM_Track_StartLine | 4 × 0.4 m | 80 | damalı çizgi, yoldan 5 mm yukarıda |
| SM_Track_GridSlot | 0.9 × 0.6 m | 6 | beyaz U işareti, pivot = slot merkezi |
| SM_Track_FinishArch | iç açıklık 5.6 m, 4.2 m | 3968 | yeşil kemer, iki yüzde "LUGE RUSH", damalı bayrak direkleri |
| SM_Track_FinishLine | 5 × 0.6 m | 120 | damalı bitiş çizgisi |
| SM_Track_FinishCanopy_4m | 4 m, 6 m genişlik | 744 | çelik çerçeve + oluklu çatı + desensiz kırmızı flama dizileri |
| SM_Track_Lift_StationBottom | 6 m | 1032 | geri dönüş makarası (kablo hizası), karşı ağırlık kulesi, kumanda kutusu |
| SM_Track_Lift_StationTop | 6 m | 1728 | tahrik makarası (kablo hizası), motor evi, operatör kabini, "ÇIKIŞ" tabelası |
| SM_Track_Lift_CablePost | — | 220 | kabloyu 0.25 m'de tutan makaralı alçak direk (4–8 m'de bir) |
| SM_Track_Lift_Cable_4m | 4 m | 8 | kablo segmenti, yükseklik 0.255 m |
| SM_Track_Lift_Hook | — | 160 | pivot = kablo kavrama merkezi; kanca ucu pivotun ~0.18 m üstünde |
| SM_Track_Lift_RampRail_4m | 4 m | 136 | lift şeridi alçak korkuluğu (sarı boru + turuncu tekme levhası) |
| SM_Track_Tunnel_4m | 4 m, 5.2 m iç genişlik, 2.8 m | 940 | ahşap çerçeve + kırmızı oluklu metal çatı |
| SM_Track_LightPole | 5 m | 284 | kol Unity −X'e (piste) uzanır; lamba camı `lamp_glass` |
| SM_Track_TrashBin | 1 m | 192 | |
| SM_Track_SledStack | ~2.7 m | 2548 | palette iç içe 4 kızak + 2 park etmiş kızak; `SM_Sled_*` gövde üreticisi (`spec_decor`), gerçek renk hücreleri (satır 0) |

### Lift hizası
- Kablo hattı şerit merkezinden **Unity +0.75 m** (gidiş yönünün sağı; `TrackSceneBuilder.CableLateral`). `Lift_Cable_4m`, `CablePost` ve `Hook` kendi pivotlarında x = 0'dır; bu ofsetle yerleştirilir.
- İstasyon modüllerinin pivotu şerit merkezindedir; makara düzlemi modül içinde zaten +0.75 m'dedir, ek ofset verilmez.
- Makara yarıçapı 0.6 m, merkezi 0.855 m. Kablo makaranın altına 0.255 m'de teğet girer; istasyonun içindeki kablo parçası modüllerin bittiği yere (istasyon başından 3 m) kadar uzanır. Dönüş kablosu makaranın üstünden yer makarasına iner.
- **Kızak koridoru:** |x| < 0.60 m ve 1.10 m altında istasyonlarda hiç geometri yoktur (en geniş kızak 0.47 m yarı genişlik). Beton plaka, korkuluk, aks ve zemin çizgileri bu koridorun dışındadır.
- `track_kit.py -- --only A,B` yalnız adı verilen FBX'leri yazar (FBX zaman damgası taşıdığı için diğerleri değişmez).

## lr_common.py (ortak API — diğer sanatçılar da kullanabilir)
- `reset_scene()`, `empty(name, loc, parent)`, `set_parent(child, parent)`, `hierarchy(root)`, `tri_count(objs)`, `save_blend(path)`
- `palette()`, `cell_index(name)`, `cell_uv(index)`, `tone(name, step)` / `lighter()` / `darker()` (rampalar: `_light/_base/_dark/_deep`, `tint_*`, bazı çiftler)
- `palette_material(name="LR_Palette", tint=None, emissive=False)`
- `MeshBuilder(seed)`: `box(center, size, cell, bevel, rot, taper)`, `cylinder(p0, p1, r, cell, segments, radius2, cap_cell)`, `sweep(points, r, cell, sides, closed, scale)`, `loft(rings, cell)`, `surface(rows, cell, double_sided, cell_fn)`, `tri2(a, b, c, cell)`, `poly(points, cell, thickness)`, `prism(outline2d, depth, cell, matrix)`, `text(body, cell, size, depth, matrix, font_path, resolution)`, `sphere(...)`, `paint(faces, cell|dict|callable)`, `shade(faces, base)` (normal yönüne göre ton varyasyonu), `bevel_faces(...)` (pah yüzleri bir ton açık = kenar vurgusu), `jitter(...)`, `mirror_x(...)`, `to_object(name, origin, material, parent)`
- `recolor_object(obj, {old: new})`, `export_fbx(objs, path, anim=False, drop_root=False)`, `unity_model_dir(category)`
- `render_preview(objs, path, size, bg, view, lens, margin, aspect, target, cam_loc)` (Eevee, Standard görünüm dönüşümü, düz arka plan), `duplicate_hierarchy(root, offset)`, `cleanup(objs)`

## Palet satır 14 (Tasarımcı)
`steel_blue_light/base/dark`, `hazard_orange(_dark)`, `bin_green_light/base/dark`, `roof_red(_dark)`, `glass_dark`, `emit_amber`, `concrete_dark`, `galv_light`.
