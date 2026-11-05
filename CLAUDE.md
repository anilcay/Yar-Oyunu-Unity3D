# Luge Rush — Ekip Kuralları

Bu repo bir Unity 6 (URP) kızak yarış oyunudur. Ana sohbet **Yöneticidir**; alt ajanlar (tasarımcı, animasyoncu, yazılımcı, seviye tasarımcısı) ona rapor verir.

Okunması zorunlu: `Docs/GDD.md` (oyun tasarımı), `Docs/ARCHITECTURE.md` (kod/animasyon sözleşmesi), `Docs/ART_GUIDE.md` (sanat kuralları).

## Kurallar
- **Git:** Ajanlar `git add/commit/push` YAPMAZ. Yalnızca `git status`/`git diff` okuyabilir. Commit ve push'u Yönetici yapar.
- **Sahiplik:** Her ajan yalnızca kendisine verilen klasörlere yazar. Başka ajanın klasöründe değişiklik gerekiyorsa raporunda belirtir.
- **Unity tekil örnek:** Unity'yi batch modda çalıştırmadan önce `Get-Process Unity` ile kontrol et; başka örnek varsa bekle. Unity'yi yalnızca görevi Unity içeren ajan çalıştırır.
- **Sözleşme:** `ARCHITECTURE.md`'deki isim ve arayüzleri değiştirme; gerekiyorsa raporda öner.
- **Doğrulama:** İş bitti demeden önce: C# → Unity batch derlemesinde `error CS` yok; Blender → betik hatasız çalışıyor ve `Art/Renders/` önizlemesi var.
- **Rapor:** Bitirince: ne yapıldı, hangi dosyalar, nasıl doğrulandı, açık kalanlar, Yöneticinin karar vermesi gereken konular.
- Kod yorumları ve tanımlayıcılar İngilizce; oyun içi metinler TR (EN çevirisi sonra).
- Araçlar: Unity `C:\Program Files\Unity\Hub\Editor\6000.3.19f1\Editor\Unity.exe`, Blender `C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe` (5.2).
- Referans kareler (git dışı): `Reference/sheet_01..05.png` (videodan saniyede 1 kare, 5'li şeritler).

## Token optimization
These rules narrow how you work; they do not replace the rules above (the required docs and the report format still apply).

1. Search before reading: use targeted search first and read whole files only when needed. Start with `Game/Assets/_LugeRush/Scripts/` and files directly related to the task. Do not scan generated folders (`Library/`, `Temp/`, `Logs/`, `Obj/`, `Build/`, `Builds/`) unless the task needs them; to verify a build, grep the specific log (e.g. for `error CS`) instead of reading it.
2. In large searches, use the Explore subagent when needed and bring back only brief findings.
3. After changes, show the relevant diffs and a short summary, not full files.
4. Do not repeat decisions already made in the same session.
5. When context fills up, suggest `/compact` or a new session, and preserve important decisions.
6. Keep default responses short but technically sufficient.
