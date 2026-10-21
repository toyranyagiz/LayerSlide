# Layer Slide

Katmanlı blokları tek eksende kaydırıp aynı renkteki kapıdan üst katmanı sıyırdığın, çevrimdışı bir Android bulmaca oyunu.
Tasarımın tek kaynağı Notion'daki **Layer Slide — Kesin Oyun Tasarım Dokümanı (GDD v1.1)** belgesidir.

## Durum

| Alan | Durum |
| --- | --- |
| Unity projesi (6000.3.24f1, URP 17.3.0, Input System 1.17.0, uGUI/TMP) | Kuruldu |
| Bölümler | L001–L010 (GDD 03'ten birebir alınan JSON ve çözüm kayıtları) |
| Kurallar, Undo, restart, kazanma, ilerleme kilidi | Tamam |
| Kayıt (zarf + SHA-256, tmp/bak, run replay doğrulaması) | Tamam |
| İpucu (Weighted A*, kare başı 2 ms dilim, 128'lik LRU önbellek) | Tamam |
| 3D sunum, kamera, HUD, menüler, ayarlar, öğretici metinleri | Tamam |
| L011–L030, IMGUI bölüm editörü, geliştirme günlüğü, release imzası | Henüz yok |

## Gereksinimler

- Unity Hub + **Unity 6000.3.24f1**
- APK almak için Hub'dan bu editöre **Android Build Support** (OpenJDK + SDK/NDK) modülünü ekle.

## Açma

1. Unity Hub → *Add project from disk* → bu klasör.
2. `Assets/LayerSlide/Scenes/Bootstrap.unity` sahnesini açıp Play'e bas (doğrudan `Gameplay.unity` da çalışır).
3. Editörde sürükleme sol fare tuşuyla yapılır; Esc, Android geri tuşu gibi davranır.

Faydalı menüler: **Tools → Layer Slide → Validate Levels** (statik doğrulama + çözüm replay'i),
**Delete Local Save**, **Setup Project** (sahneleri, materyalleri, fontları, URP ve player ayarlarını yeniden üretir).

## Klasör yapısı (`Assets/LayerSlide`)

| Klasör | İçerik |
| --- | --- |
| `Core` | Unity'den bağımsız saf C# kurallar: `Level`, `Board`, `RuleEngine`, `CommandHistory`, `LevelValidator`, `SolutionReplayer`, `HintSolver` |
| `Gameplay` | `GameController` (durum makinesi ve ekran akışı), `InputController` (eksen kilidi, kapı niyeti, snap), `PointerReader`, `TutorialTracker` |
| `Presentation` | `BoardPresenter`, `BlockView`, `GateView`, `CameraFramer`, `FxPool`, `MeshLibrary` (prosedürel mesh'ler), `Palette`, `VisualTheme` |
| `UI` | Koddan kurulan uGUI ekranları: HUD, ana menü, bölüm listesi, ayarlar, hakkında, modal ve bildirim |
| `Services` | `SaveService`, `HintService`, `AudioHapticsService` (sentez sesler), `LevelCatalog`, `AppServices`, `Bootstrap` |
| `Resources/Levels`, `Resources/Solutions` | Bölüm ve çözüm JSON'ları (`L001.json` …) |
| `Editor` | `ProjectSetup`, `LevelTools` |
| `Tests/EditMode`, `Tests/PlayMode` | Kural/içerik/çözücü/kayıt testleri ve uçtan uca sahne testi |

Kural sonucu her zaman `RuleEngine` tarafından belirlenir; girdi, ipucu ve çözüm replay'i aynı kodu kullanır.
Sürükleme sırasında model değişmez ve kare başına bellek ayırma yapılmaz; mesh ve materyaller paylaşımlıdır.

## Testler

Editörde **Window → General → Test Runner**, veya komut satırından:

```sh
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

PlayMode testi gerçek `Gameplay` sahnesinde L001–L010'u sırayla bitirir ve ilerlemenin diske yazıldığını doğrular.
`LAYERSLIDE_CAPTURE_DIR` ortam değişkeni verilirse ekran görüntüleri de kaydeder.

## Lisanslar

Noto Sans yazı tipi SIL Open Font License 1.1 ile dağıtılır (`Assets/LayerSlide/Fonts/OFL.txt`).
Ses efektleri çalışma anında sentezlenir; dış ses/model varlığı kullanılmaz.
