# SK_Rider — Karakter, Rig ve Animasyon

Üretim tamamen betikle yapılır, tekrarlanabilir:
```
blender.exe -b -P Art/Scripts/rider_build.py     # model + rig + klipler -> Art/Blender/Rider_Main.blend + SK_Rider.fbx
blender.exe -b -P Art/Scripts/rider_render.py    # önizlemeler -> Art/Renders/rider_*.png  (args: lineup rig anim fp)
```
Betikler: `rider_common.py` (palet/mesh/export/render yardımcıları), `rider_build.py` (iskelet, gövde/kafa/kask
varyantları, ağırlıklar), `rider_anim.py` (poz çözücü: omurga FK + kol/bacak analitik IK, klip tanımları),
`rider_render.py`.

## Çıktı
`Game/Assets/_LugeRush/Art/Models/Characters/SK_Rider.fbx` — tek iskelet `Rider`, 10 skinned mesh, 13 take.

| Mesh | Tris | Not |
|---|---|---|
| `Body_A1_TeeJeans` / `Body_A2_TeeJeans` | 1980 | beyaz / lacivert tişört + kot (kemer, kıvrık paça) |
| `Body_B1_WindbreakerTights` / `Body_B2_...` | 1992 | kırmızı / turkuaz rüzgarlık (fermuar, şerit, dik yaka) + tayt (yan şerit) |
| `Body_C1_HoodieShorts` / `Body_C2_...` | 2192 | hardal / gri sweatshirt (kapüşon, cep, ip) + şort + beyaz çorap |
| `Head_A` / `Head_B` / `Head_C` | 344 | kahve saç (odaklı kaş) / siyah saç + sakal / sarı saç (neşeli kaş) |
| `Helmet` | 320 | kabuk, vizör siperi, vizör çizgisi, havalandırma, çene kayışı + toka |

Aynı anda **bir** Body + **bir** Head + Helmet aktif → en fazla **2856 tris** (bütçe 4k).
Diğer Body/Head objeleri prefab'da kapatılmalı (FBX'te hepsi açık gelir).

### Renk / malzeme (tek palet doku, 3 malzeme yuvası)
| Malzeme | Kullanım | Unity'de varyasyon |
|---|---|---|
| `LR_Palette` | giysi, ayakkabı, göz, saç, kayış | — |
| `LR_Skin` | cilt yüzleri (UV = `skin_1` / `skin_1_shade` hücreleri) | `_BaseMap_ST.z = k/16`, k = 0..5 → `skin_1..skin_6` (gölge tonu satır 15'te aynı sütunda) |
| `LR_Helmet` | kask kabuğu (UV = `helmet_yellow` / `helmet_yellow_dark`) | `_BaseMap_ST.z = k/16`, k = 0..7 → sarı, mavi, yeşil, siyah, beyaz, pembe, kırmızı, mor |

Öneri: `RiderAppearance` bileşeni MaterialPropertyBlock ile `_BaseMap_ST = (1,1,k/16,0)` atasın. Palet dokusu Point filtre, mipmap kapalı olmalı.
6 gövde × 3 kafa × 6 cilt × 8 kask = 864 kombinasyon.
Palet: satır 15 eklendi (`skin_N_shade`, `eye_dark`, `hair_brown`, tişört gölgeleri, `hoodie_mustard*`, `khaki*`).

## İskelet (30 kemik, leaf bone yok)
Rest pozu: **T-pose**, ayakta, ayaklar orijinde, yüz −Y (Unity +Z), boy ~1.75 m (+kask 1.79).
```
Hips
├─ Spine ─ Chest ─ UpperChest ─┬─ Neck ─ Head
│                              ├─ Shoulder.L ─ UpperArm.L ─ LowerArm.L ─ Hand.L ─┬─ Fingers1.L ─ Fingers2.L
│                              │                                                 └─ Thumb1.L ─ Thumb2.L
│                              └─ Shoulder.R ... (aynı)
├─ UpperLeg.L ─ LowerLeg.L ─ Foot.L ─ Toes.L
└─ UpperLeg.R ─ ...
```
Humanoid eşleme: hepsi otomatik isimle eşleşmeli. Parmaklar: `Thumb1/2` → Thumb Proximal/Intermediate,
`Fingers1/2` → **Index** Proximal/Intermediate (4 parmak tek "kepçe"; Middle/Ring/Little boş).
`.L/.R` ekini Unity tanır; tanımazsa Avatar Configure'da elle eşleyin.

## Klipler (30 fps, FBX take adları birebir)
Bütün klipler **oturur pozda**; Hips kemiği armatür orijininde (= kızağın `Seat` boşluğu).

| Klip | Kare | Süre | Loop | Açıklama |
|---|---|---|---|---|
| `Sit_Idle` | 0–90 | 3.0 s | ✔ | nefes, hafif sallanma, baş hareketi |
| `Steer_Left` / `Steer_Right` | 0–90 | 3.0 s | ✔ | tam sapma pozu: gidon ±22°, gövde virajın içine; aynı nefes döngüsü (blend için senkron) |
| `Brake` | 0–90 | 3.0 s | ✔ | gidon göğse çekilir (+0.13 m geri, +0.07 m yukarı), geriye yaslanma |
| `Drift_Left` / `Drift_Right` | 0–90 | 3.0 s | ✔ | gidon ±30°, agresif yatma, dış bacak gerili/dışa açık, titreme |
| `Push_Start` | 0–30 | 1.0 s | ✘ | gidonu bırak → yanlardan yere uzan → geriye it → tekrar kavra |
| `Bump` | 0–12 | 0.4 s | ✘ | çarpma sarsıntısı (geri-ileri kamçı), eller gidonda |
| `Lift_Relax` | 0–180 | 6.0 s | ✔ | eller dizlerde, geriye yaslanmış, sola/yukarı/sağa bakar |
| `Look_Back` | 0–60 | 2.0 s | ✘ | sol omuz üstünden ~120° geri bakış; 15–45 arası tutma (ayrı "hold" klibi olarak bölünebilir) |
| `Win_Cheer` | 0–60 | 2.0 s | ✔ | iki yumruk havada pompalama |
| `Lose_Shrug` | 0–60 | 2.0 s | ✘ | avuçlar yukarı omuz silkme → çökük poz (son kare tutulur) |
| `Wave` | 0–60 | 2.0 s | ✔ | sağ el sallama, sol el gidonda |

Döngüler: son kare ilk kareyle birebir aynı, eklenen sinüsler tam periyot.

## Kızak sözleşmesi varsayımları (Araç Tasarımcısı ile doğrulanmalı)
`Seat`'e göre (Unity ekseni, Seat = kalça eklemi; kalça yüzeyi ~0.10 m aşağıda):
- `Grip.L/R` = (±0.20, +0.20, +0.55) — yani yerden 0.45 m (Seat yerden ~0.25 m)
- Gidon dönüş ekseni dikey, merkez (0, 0.20, 0.55); fren = gidon geriye/yukarı çekilir
- Ayaklar ~0.76 m önde, Seat hizasında (kızak burnu iç yüzü)
- Yer: Seat'in 0.25 m altı (Push_Start eller buraya doğru uzanır)

Seat yüksekliği/gidon konumu farklıysa `rider_anim.py` başındaki `GRIP_PIVOT`, `GRIP_HALF`, `GROUND_Z`
değiştirilip `rider_build.py` yeniden çalıştırılır; Unity'de IK zaten kesinleştirir.

## Unity import ayarları
- Model: Scale 1, **Normals: Import**, Read/Write kapalı, Blend Shapes yok.
- Rig: **Humanoid**, Avatar Definition: Create From This Model (T-pose; otomatik eşleme).
- Animation (her klip):
  - **Loop Time + Loop Pose:** Sit_Idle, Steer_Left, Steer_Right, Brake, Drift_Left, Drift_Right, Lift_Relax, Win_Cheer, Wave.
  - Kapalı: Push_Start, Bump, Look_Back, Lose_Shrug.
  - Root Transform Rotation / Position (Y) / Position (XZ): **Bake Into Pose**, Based Upon: **Original** (hepsi).
  - Animator: **Apply Root Motion kapalı**. Rider prefab'ı `Seat` altında, local identity.
  - Anim. compression: Keyframe Reduction (varsayılan) yeterli.
- Not: Humanoid retarget hips yüksekliğini avatar ölçeğine göre normalize eder; oturma yüksekliği kayarsa
  rider kökünü Y'de ayarlayın ya da Rig'i **Generic** yapın (aynı FBX ile birebir çalışır).

## Animator Controller önerisi
Parametreler (ARCHITECTURE.md): `Steer`, `Brake`, `Speed01`, `Drift`, `OnLift`, `Push`, `Bump`, `Win`, `Lose`.

**Base Layer**
- `Ride` (Blend Tree 1D, param `Steer`): −1 `Steer_Left`, 0 `Sit_Idle`, +1 `Steer_Right`.
  *(Dikkat: `Steer` > 0 sağa dönüşse eşik yönleri bu şekilde; oyun işaret kuralı tersse eşikleri çevirin.)*
- `Ride` → `Drift` (Blend Tree 1D `Steer`: −1 `Drift_Left`, +1 `Drift_Right`) : `Drift == true`, 0.15 s geçiş; geri `Drift == false`, 0.25 s.
- `Ride` → `Lift` (`Lift_Relax`): `OnLift == true`, 0.5 s; geri `OnLift == false`, 0.4 s.
- Any State → `Push_Start` (trigger `Push`) → exit time ile `Ride`.
- Any State → `Win_Cheer` (`Win`), Any State → `Lose_Shrug` (`Lose`), bitiş state'leri.
- Lobi için ayrı controller veya state: `Wave` (döngü), arada `Sit_Idle`.

**Brake Layer** (override, avatar mask: gövde + kollar): `Brake` klibi, katman ağırlığı = `Brake` parametresi (script'ten `SetLayerWeight`) — ya da Base'deki `Ride` yerine 2D blend (Steer × Brake).
**Hit Layer** (additive, tam vücut): `Bump` trigger → `Bump` (additive için Unity'de klibi "Additive Reference Pose = Sit_Idle f0" ile kullanın), 0.05 s giriş.
**Look Layer** (override, mask: Spine→Head): `Look_Back` (ileride arka bakış tuşu için).

**IK:** `OnAnimatorIK` ile `AvatarIKGoal.LeftHand/RightHand` → `Grip.L/R` (ağırlık 1; Push_Start/Lift/Win/Lose/Wave sırasında 0'a indirin).
Gidon dönüşü (`Handlebar` objesi) `Steer` ile ±22°, drift'te ±30° dönerse eller klip pozuyla örtüşür.

## 1. şahıs kamera
- Kamera `Head` kemiğinin altında; göz noktası rest pozda Head ekleminden 0.115 m yukarı, 0.085 m ileri (karakter uzayında; kafa içinde).
- `Head_*` ve `Helmet` renderer'ları: Shadow Casting = **Shadows Only**.
- Near clip ≤ 0.03. Gövde yakası kameranın hemen altında; kolları, elleri, bacakları ve kızak burnunu gördüğü `rider_fp_view.png` ile kontrol edildi.
- Kafa animasyonu kamerayı da döndürür (drift'te ~7° yatma, ~20° sapma; Look_Back'te ~120°). Oyun hissine göre
  kamerayı Head'in yalnız konumuna bağlayıp rotasyonu kızaktan almak/yumuşatmak önerilir.

## Renderlar (`Art/Renders/`)
`rider_lineup_front.png`, `rider_lineup_34.png`, `rider_lineup_heads.png`, `rider_rig_pose.png`,
`rider_anim_<Klip>.png` (13 adet), `rider_anim_overview.png`, `rider_fp_view.png`.
Renderdaki turuncu kızak yalnız render için yer tutucudur (export edilmez).
