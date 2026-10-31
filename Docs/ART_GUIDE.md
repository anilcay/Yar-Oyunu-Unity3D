# Luge Rush — Sanat Rehberi (Blender → Unity)

## Stil: "Kaliteli low-poly, tekdüze değil"
Low-poly ama **ucuz görünmeyen** bir stil. Hedef: düz gölgelendirme (flat shading) + zengin renk çeşitliliği + iyi siluet.

Tekdüzeliği kırma kuralları (zorunlu):
1. **Renk varyasyonu:** Aynı nesnenin yüzleri tek renk olmaz. Palet atlasında her malzeme için 3–5 ton (açık/orta/koyu/sıcak/soğuk) vardır; yüzler bu tonlara dağıtılır (yaprak kümelerinde üst yüzler açık, alt yüzler koyu).
2. **Varyant:** Her çevre nesnesinden **en az 3–5 varyant** (ağaç, kaya, çalı, bambu kümesi). Ölçek/rotasyon/ton farkı yerleştirmede ayrıca uygulanır.
3. **Siluet:** Nesneler simetrik ve "kutu" olmaz; hafif rastgele vertex sapması (jitter), eğik gövdeler, düzensiz kayalar.
4. **Detay katmanı:** Kızakta pah kırılmış (bevel) kenarlar, vida/somun, çıkartma (decal) şeritleri, yarış numarası; pist kenarında tabelalar, kablo, kazık, yosun lekeleri.
5. **Kenar vurgusu:** Önemli kenarlarda ince açık ton bevel → ışıkta "highlight" yakalar.

## Teknik kurallar
- **Tek palet doku:** `Art/Textures/LR_Palette.png` (256×256, 16×16 hücre = 256 renk yuvası, her hücre 16×16 px düz renk). UV'ler hücre ortasına küçültülür. Tüm modeller bu tek dokuyu paylaşır → tek materyal, az draw call. Palet yerleşimi `Art/Textures/LR_Palette.json` içinde belgelenir (hücre → isim/hex).
- Ek olarak ihtiyaç varsa ikinci palet: `LR_Palette_Emissive.png` (lambalar, tabelalar).
- Kızak gövde rengi değiştirilebilir olmalı: gövde rengi paletteki **"tint" hücresine** (beyaz-gri ton) maplenir ve Unity'de `_BaseColor` ile renklendirilir **veya** her renk için ayrı palet hücresi — Tasarımcı birini seçip `Art/README.md`'de belgeler.
- Shading: Blender'da **flat** (smooth değil), Unity import'ta `Normals: Import`.
- **Poligon bütçeleri:** Kızak ≤ 3k tris, sürücü ≤ 4k tris, büyük ağaç ≤ 1.5k, kaya ≤ 300, çalı ≤ 200, pist modülü (korkuluk 4 m) ≤ 200. LOD gerekirse `_LOD0/_LOD1`.
- **Ölçek:** 1 Blender birimi = 1 m. Gerçekçi ölçek: kızak ~1.4 m uzunluk, 0.75 m genişlik, 0.35 m yükseklik; sürücü 1.75 m.
- **Pivot:** Zemin temas noktası merkezi. Kızak ileri = Unity +Z.
- **İsimlendirme:** `SM_<Kategori>_<Ad>_<Varyant>` (static mesh), `SK_` (skinned), örn. `SM_Sled_Classic`, `SM_Env_Forest_Bamboo_03`, `SK_Rider`.
- **Kızak boşlukları (empty):** `Seat` (sürücü oturma), `Grip.L`, `Grip.R` (gidon tutma), `Camera_FP` (1. şahıs göz hizası önerisi), `Wheel_FL/FR/RL/RR` (dönen tekerlek objeleri ayrı mesh), `Handlebar` (ayrı mesh, direksiyonla döner), `Seat2` (yalnız Tandem).

## Blender iş akışı
- Modeller **Python betikleriyle üretilir** (tekrarlanabilir): `Art/Scripts/*.py`, çalıştırma:
  `"C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe" -b -P Art/Scripts/<betik>.py`
- Kaynak dosyalar: `Art/Blender/*.blend`. Önizleme renderları: `Art/Renders/*.png` (Eevee, 3/4 açı, 512–1024 px) — Yönetici bunları inceler.
- FBX dışa aktarım ayarları:
  `axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', bake_space_transform=True, use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False`
  Animasyonlu: `bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False`.
- Çıktı hedefi: `Game/Assets/_LugeRush/Art/Models/<Kategori>/`, doku: `Game/Assets/_LugeRush/Art/Textures/`.

## Renk paleti yönü
| Tema | Ana tonlar |
|---|---|
| Kızaklar | Turuncu #F2792B, Kırmızı #D93A3A, Sarı #F2C230, Turkuaz #3BB8C4; koyu gri şasi #2E3238; krom gidon #C9CDD2 |
| Pist | Asfalt #3A3D40 / #4A4E52 / ıslak #2B2F33; bordür sarı #F2C230 + siyah #1E1E1E; korkuluk metal #8C9196 |
| Orman | Yaprak #2F6B2A #3F8A35 #5DA83E #8CC152, gövde #5B4636, bambu #9BB84A |
| Alp | Çam #1F4D3A #2C6B4F, granit #7D8590 #A3AAB3, kar #EEF3F7, su #4FA3C7 |
| Kanyon | Kaya #B5502E #D9733F #8C3A24, kum #E3B47A, kaktüs #5E8C4A, gökyüzü gradyanı #F2994A→#7A4FA0 |
