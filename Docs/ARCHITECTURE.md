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
