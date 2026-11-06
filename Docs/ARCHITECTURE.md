# Luge Rush — Teknik Mimari (Ekip Sözleşmesi)

Bu belge yazılımcı, seviye tasarımcısı ve sanat ekibi arasındaki **arayüz sözleşmesidir**. İsimleri değiştirmek gerekiyorsa önce Yöneticiye (ana sohbet) bildirin.

## Sürümler ve paketler
- Unity **6000.3.19f1**, URP (Universal 3D şablonu). Proje kökü: `Game/`.
- Paketler: `com.unity.inputsystem`, `com.unity.splines`, `com.unity.cinemachine` (3.x), `com.unity.netcode.gameobjects` (2.x), `com.unity.services.multiplayer` (Sessions: Lobby + Relay), `com.unity.ugui` (TextMeshPro dahil), `com.unity.render-pipelines.universal`.
- Active Input Handling: **Input System (New)**.

## Klasör yapısı (`Game/Assets/_LugeRush/`)
```
Art/Models/{Sleds,Characters,Environment/{Common,Forest,Alpine,Canyon},Track}/   ← Blender FBX çıktıları
Art/Textures/   Art/Materials/   Art/Animations/
Audio/
Prefabs/{Sleds,Characters,Environment,Track,UI,Network}/
Scenes/         Boot.unity, MainMenu.unity, Track_Forest.unity, Track_Alpine.unity, Track_Canyon.unity, Dev_Greybox.unity
ScriptableObjects/{Sleds,Tracks,AI}/
Scripts/{Core,Vehicle,Track,Race,AI,Camera,Input,Network,UI}/   ← runtime asmdef: LugeRush.Runtime
Editor/                                                          ← asmdef: LugeRush.Editor
Tests/
```
Namespace: `LugeRush.<Klasör>` (örn. `LugeRush.Vehicle`).

## Birimler ve eksenler
1 birim = 1 metre. Unity Y-yukarı, kızak ileri yönü **+Z**. Model pivotu: zemin temas düzleminin merkezi.

## Çekirdek arayüzler (değiştirmeden kullanın)
```csharp
namespace LugeRush.Vehicle {
  public struct SledInput {            // her FixedUpdate okunur
    public float Steer;   // -1..1
    public float Brake;   // 0..1
    public bool  Drift;   // basılı tutuluyor mu
    public bool  Push;    // başlangıç itişi / bu karede basıldı
    public bool  Reset;   // bu karede basıldı
  }
  public interface ISledInputSource { SledInput ReadInput(); }

  [CreateAssetMenu] public class SledStats : ScriptableObject {
    // Mass, MaxSpeed, GripLateral, SteerRate, SteerSpeedFalloff, BrakeForce,
    // RollingResistance, DragCoefficient, DriftGripMultiplier, DriftChargeRate,
    // BoostForce, BoostDuration, CollisionKnockbackScale ...
    // UI için 1..5 değerleri: UiSpeed, UiGrip, UiHandling, UiWeight, UiDrift
  }

  public class SledController : MonoBehaviour {   // Rigidbody tabanlı arcade fizik
    public SledStats Stats;
    public ISledInputSource InputSource { get; set; }
    public float SpeedKmh { get; }
    public float DriftCharge01 { get; }
    public bool  IsBoosting { get; }
    public bool  IsDrifting { get; }
    public bool  IsOnLift { get; }
    public bool  ControlsLocked { get; set; }    // geri sayım, lift, bitiş
    public SledInput LastInput { get; }          // animasyon için
    public void ResetTo(Vector3 pos, Quaternion rot);
    public event System.Action<Collision> Bumped;
  }
}

namespace LugeRush.Track {
  public class TrackPath : MonoBehaviour {        // SplineContainer sarmalayıcısı (kapalı döngü)
    public float Length { get; }
    public float GetNearestDistance(Vector3 worldPos);           // pist başından metre
    public Vector3 GetPosition(float distance);
    public Vector3 GetTangent(float distance);
    public Vector3 GetUp(float distance);                        // eğim (banking) dahil
    public float  GetWidth(float distance);
    public bool   IsInLift(float distance);
    public float  LiftStart { get; }  public float LiftEnd { get; }
    public float  StartLineDistance { get; }
  }
  // TrackMeshBuilder: spline'dan yol + sarı-siyah bordür + korkuluk/file yerleştirme (editor + runtime)
  // LiftSection: [LiftStart, LiftEnd] aralığında kızakları kablo ile sabit hızda çeker
  // StartGrid: 8 başlangıç slotu (2'li sıra)
}

namespace LugeRush.Race {
  public class RaceConfig { public string TrackId; public int Laps = 3; public int BotCount; public BotDifficulty Difficulty; }
  public class RaceParticipant : MonoBehaviour {  // her kızakta
    public string DisplayName; public bool IsBot; public bool IsLocalPlayer;
    public int Lap; public float TrackDistance; public int Position; public bool Finished;
    public float TotalTime; public float BestLapTime;
  }
  public class RaceManager : MonoBehaviour {
    public static RaceManager Instance;
    public RaceState State;  // Waiting, Countdown, Racing, Finished
    public event System.Action<int> CountdownTick;
    public event System.Action RaceStarted;
    public event System.Action<RaceParticipant,int> LapCompleted;
    public event System.Action<RaceParticipant> ParticipantFinished;
    public event System.Action RaceFinished;
    public IReadOnlyList<RaceParticipant> Standings { get; }
  }
}
```
- Kontrol noktaları: `TrackPath` mesafesinden türetilir (her ~50 m). Tur, tüm kontrol noktaları sırayla geçilip başlangıç çizgisi geçilince sayılır (ters gitme ile hile yok).
- Sıralama: `Lap * Length + progressDistance`.

## Animasyon sözleşmesi (Rider Animator)
Animator parametreleri — `SledAnimatorBridge` bileşeni bunları `SledController`'dan besler:
| Parametre | Tip | Aralık |
|---|---|---|
| `Steer` | float | -1..1 |
| `Brake` | float | 0..1 |
| `Speed01` | float | 0..1 (hız / azami hız) |
| `Drift` | bool | |
| `OnLift` | bool | |
| `Push` | trigger | başlangıç itişi |
| `Bump` | trigger | çarpışma sarsıntısı |
| `Win` / `Lose` | trigger | bitiş |

Klipler (FBX içindeki take adları): `Sit_Idle`, `Steer_Left`, `Steer_Right`, `Brake`, `Drift_Left`, `Drift_Right`, `Push_Start`, `Bump`, `Lift_Relax`, `Look_Back`, `Win_Cheer`, `Lose_Shrug`, `Wave` (menü/lobi).
Rider iskeleti Humanoid uyumludur; `Hand.L`/`Hand.R` gidon tutma noktasına IK hedefi olarak `Grip.L`/`Grip.R` boşlukları kızak modelinde bulunur.
1. şahıs görünümde aynı rider modeli kullanılır; `Head` ve kask meshi kamera için gizlenir (yalnız gölge).

**Kararlar (Yönetici):**
- **İşaret kuralı:** `Steer` > 0 = **sağa** (D tuşu / sol çubuk sağ). Blend tree: -1 = `Steer_Left`, 0 = `Sit_Idle`, +1 = `Steer_Right`. Drift aynı kuralla.
- **1. şahıs kamera:** Konum `Head` kemiğinden (göz hizası ofsetiyle) alınır; rotasyon kızak yönünü izler, kafa rotasyonunun yalnız ~%25'i yumuşatılarak eklenir (mide bulantısını önlemek için). `Look_Back` sırasında ayrı geri-bakış kamerası kullanılır.
- **Görünüm:** `RiderAppearance` bileşeni (LugeRush.Vehicle) gövde/kafa varyantını açar-kapar ve cilt/kask rengini malzeme `_BaseMap_ST.z = k/16` ofsetiyle seçer (bkz. `Art/ANIMATION.md`). Tüm varyantlar tek `SK_Rider.fbx` içinde, tek avatar.

## Çekirdek fazından onaylanan sapmalar/eklemeler (Yönetici)
- Kamera namespace'i **`LugeRush.CameraSystem`** (`LugeRush.Camera`, `UnityEngine.Camera`'yı gölgeler). Test asmdef'leri: `LugeRush.Tests.EditMode`, `LugeRush.Tests.PlayMode`.
- Ek tipler: `TrackFollower` (kızak başına pist mesafesi), `SledRecovery` (reset), `RaceBootstrap` (RaceSession → sahne), `SledCatalog`, `IRaceAuthority` + `LocalRaceAuthority`, `TrackSceneBuilder.BuildScene(def, path)` (haritalar bununla üretilir), `PlaceAlong` (modül dizme).
- `RaceParticipant.Lap` 1 tabanlı. Başlangıç çizgisi lift çıkışından 40 m sonra. Katmanlar: 8 Track, 9 Sled, 10 Barrier.
- **Lift:** hız 18 m/s kalır (görsel inandırıcılık). GDD'deki ~6 sn hedefi lift **uzunluğu** ile sağlanır: gerçek haritalarda lift ≤ 110 m.
- **HUD metni:** TextMeshPro kullanılır (TMP Essentials `Assets/TextMesh Pro` altına UI yazılımcısı tarafından kurulur); `DebugRaceHud` geçicidir.

## Ses (Yönetici)
- Namespace `LugeRush.Audio`. `GameAudio` kendi kendini kuran kalıcı nesnedir; `SledAudio` kızaklara çalışma anında eklenir, prefab değişikliği gerekmez. Tek ve çok oyunculu aynı yoldan gider.
- Yeniden üretim: önce `python Art/Scripts/audio_build.py`, sonra `LugeRush.Editor.AudioAssetBuilder.Build`. WAV'lar Git LFS ile depoda tutulur.

## Ağ fazı kararları (Yönetici)
- **NetworkSled:** `Prefabs/Network/NetworkSled.prefab` yalnız NetworkObject + owner-auth NetworkTransform + `NetworkSled` taşır. Her peer ağsız oyun kızağını `SledCatalog.GetPrefab(tip)` ile ayrı obje olarak kurar; sanat prefab'larına NetworkObject eklenmez.
- **Yetki:** `NetworkRaceAuthority` hostta; tek oyunculu mod da aynı yoldan (loopback yerel host) gider.
- **Kopma:** İstemci düşerse kızağı host botu olur. Host düşerse istemciler mesajla menüye döner.
- **İçerik:** `TrackCatalog` harita kimlikleri `forest`, `alpine`, `canyon`; sahneler `Track_Forest|Alpine|Canyon`. Sahne build'e girince harita menüde açılır.
- **Hile koruması yok:** Host, sahibin bildirdiği RaceDistance'a güvenir (arkadaş oyunu için kabul).

## Entegrasyon fazı kararları (Yönetici)
- **Rig:** `SK_Rider` Humanoid. `Prefabs/Characters/Rider.prefab` ve `AC_Rider` kullanılır; `RiderAppearance.Set(body, head, skin, helmet)`.
- **Kızak prefab'ı:** `SledCatalog.GetPrefab(type)`; kök Rigidbody + `SledController` + `SledVariant`, altında `SledVisual`. `Camera_FP` kökün ilk çocuğu kalır.
- **Denge:** `SlipstreamChargeTime` 3 s, `SlipstreamCooldown` 11.5 s, Tandem `MaxSpeed` 34. Solo AI tur farkı hedefi ≤ %7.
- **Yarış sonu çizgisi:** Tur çizgisi ve `FinishArch` **aynı yerdedir**; kemer çizginin tam üstüne, kanopi arkasına gelir. `StartGate` grid'in başında durur.
- **Lift uzunluğu:** Hedef ≤ 110 m (~6 s). Pistin iniş yüksekliği bunu aşmayı gerektirirse en çok 150 m (~8 s) olabilir.
- **Lift makarası:** Makara ve kablo hizası şerit merkezinden +0.75 m. Alt istasyon plakası şeritten çıkarılır; collider'lar bundan sonra eklenir.

## Cilalama fazı kararları (Yönetici)
- **Lift girişi:** Kızak geri ışınlanmaz; olduğu yerden yakalanır, hız/yanal konum/yön yumuşak oturur. Lift binicileri liftte ve çıkıştan sonra 0.6 s diğer kızaklarla çarpışmaz (`excludeLayers`).
- **Lift kancası:** `GameContent.LiftHookPrefab`, bindirici başına havuzdan gelir. Yalnız yerelde sürülen kızaklarda görünür.
- **Başlangıç lambaları:** `StartGateLights` geri sayımda 1-2-3 kırmızı, GO'da yalnız yeşil yakar.
- **Bitiş süresi:** `RaceManager.FinishGraceFraction`: `max(20 s, kazanan süresinin %20'si)`.
- **Test otomasyonu:** `-lr-flow` duraklatma, yeniden başlatma, tekrar ve menü dönüşlerini player'da gezer. `AIDriver.SeedOverride` testlerde AI gürültüsünü sabitler.
- **Grafik:** Windows birincil API D3D11, yedek D3D12. Gölge kademesi 2, yumuşak gölge Medium, SSAO yarım çözünürlük. Ekran 60 Hz'e kilitliyken %1-low hedefi henüz doğrulanmadı.

## Sanat varlığı entegrasyon kararları (Yönetici)
- Kızak ek boşlukları: `Grip2.L`/`Grip2.R` (yalnız Tandem, arka yolcu tutamağı), `Wheels_Mid` (Tandem orta destek tekerlekleri, dönmez). Kızak gövdesi `Body` alt nesnesi `LR_Palette_Tint` materyali kullanır; renk `_BaseColor` ile verilir.
- Pist kenar modülleri (bordür/korkuluk/file) dış tarafı modelde **+X**. Sağ kenarda olduğu gibi, **sol kenarda Y ekseninde 180° döndürülerek** yerleştirilir (negatif ölçek KULLANILMAZ — normal/collider bozulur).
- Lift kablosu `SM_Track_Lift_Cable_4m` modülü spline boyunca dizilerek yapılır (LineRenderer değil).
- Geometrik yazılar (BAŞLANGIÇ, YAVAŞ, DUR…) şimdilik TR; EN varyantları yerelleştirme fazında.
- Kızak boyutları kabul edildi: Classic 1.53 m, Sport 1.75 m, Mini 1.43 m, **Tandem 2.70 m** (sürücü bacak boyu nedeniyle; ağır/uzun kızak kimliğine uygun). Collider'lar bu boyutlara göre ayarlanır.
- `SK_Rider.fbx` dokusu `Characters/LR_Palette.png` diye (var olmayan yol) referans veriyor → Unity'de import ayarında materyal paylaşılan `LR_Palette` materyaline yeniden eşlenir (entegrasyon görevi).
- Açık iş (cilalama fazı): `SM_Track_SledStack` dekoru eski kayık formunda, yeni kızak gövdesine güncellenecek.
- Blender 5.2 FBX bilinen hata: `bake_space_transform=True` ile 2+ seviye derin nesnelerin konumu bozulur → kök boşluk yazılmaz (`lr_common.export_fbx(drop_root=True)`), hiyerarşiler sığ tutulur.

## Ağ (Multiplayer)
- Netcode for GameObjects 2.x + Unity Multiplayer Services **Sessions** (Lobby + Relay). Oda kodu ile katılım.
- **Host = sunucu + oyuncu.** Botlar ve `RaceManager` hostta çalışır.
- Kızak hareketi **sahip yetkili** (owner-authoritative `NetworkTransform`, interpolasyonlu) — her oyuncu kendi kızağının fiziğini simüle eder (tepkisellik için). Diğer kızaklar kinematik proxy'dir; çarpışmalar proxy'lere kinematik itme olarak yansır.
- Tur/sıra/bitiş: host doğrular, `NetworkVariable` ile dağıtır.
- Tek oyunculu mod da aynı kod yolunu kullanır (yerel host, Relay olmadan) — iki ayrı mantık yazılmaz.

## Sahne akışı
`Boot` (servis başlatma, ayarlar) → `MainMenu` → `Track_*` (NetworkManager.SceneManager ile yüklenir).

## Unity'yi komut satırından çalıştırma
Unity editörü aynı projede **aynı anda yalnızca bir kez** açılabilir. Batch modda iş yapan ajan:
```
"C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe" -batchmode -quit -projectPath C:\Dev\YarisOyunu\Game -executeMethod <Sınıf.Metot> -logFile C:\Dev\YarisOyunu\Logs\<ad>.log
```
Önce `Get-Process Unity` ile başka bir örnek açık mı kontrol edin; açıksa bitmesini bekleyin. Derleme hatası kontrolü: log'da `error CS` arayın.
