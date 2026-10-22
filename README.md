# Layer Slide

Katmanlı blokları tek eksende kaydırıp aynı renkteki kapıdan üst katmanı sıyırdığın, çevrimdışı bir Android bulmaca oyunu.
Tasarımın tek kaynağı Notion'daki **Layer Slide — Kesin Oyun Tasarım Dokümanı (GDD v1.1)** belgesidir.

## Durum

| Alan | Durum |
| --- | --- |
| Unity projesi (6000.3.24f1, URP 17.3.0, Input System 1.17.0, uGUI/TMP) | Kuruldu |
| Bölümler | L001–L010 (GDD 03'ten birebir alınan JSON ve çözüm kayıtları, 58 eylem) |
| Kurallar, girdi, Undo, restart, kazanma, ilerleme kilidi, öğretici | Tamam |
| Kayıt (zarf + SHA-256, tmp/bak, alan/tip denetimi, saveVersion 1→2 migrasyonu, run replay) | Tamam |
| İpucu (Weighted A*, kare başı 2 ms, 250 ms CPU, 1,5 sn duvar saati, 128'lik LRU) | Tamam |
| 3D sunum, kamera, HUD, menüler, ayarlar, ses/mixer/limiter, ikon, splash | Tamam |
| Bölüm editörü, geliştirme günlüğü, release/development APK, BuildManifest | Tamam |
| L011–L030 | Bu sürümün kapsamı dışında bırakıldı |
| Gerçek cihaz ölçümleri ve oyuncu testleri | Yapılmadı (bkz. `KnownIssues.md`) |

## Gereksinimler

- Unity Hub + **Unity 6000.3.24f1**, Android Build Support + Android SDK & NDK Tools + OpenJDK modülleri.
- Release imzası için depo dışında bir keystore (aşağıda).

## Açma ve oynama

1. Unity Hub → *Add project from disk* → bu klasör.
2. `Assets/LayerSlide/Scenes/Bootstrap.unity` sahnesini açıp Play'e bas (doğrudan `Gameplay.unity` da çalışır).
3. Editörde sürükleme sol fare tuşuyla yapılır; Esc, Android geri tuşu gibi davranır.

## Araçlar (Tools → Layer Slide)

| Menü | Ne yapar |
| --- | --- |
| Level Editor | Bölüm listesi, ızgara, alan düzenleme; Validate / Play Recorded Solution / Search Solution / Reset Preview / Export JSON |
| Validate Levels | Statik doğrulama + bütün kayıtlı çözümlerin replay'i (build kapısı) |
| Setup Project | Sahneleri, materyalleri, fontları, mesh bankasını, WAV'ları, mixer'ı, ikonu, URP ve player ayarlarını yeniden üretir |
| Build → Release APK / Development APK | `Builds/LayerSlide-1.0.0-arm64.apk` (+ `.BuildManifest.json`) |
| Export Test Log → From Editor / From Android Device (adb) | Development build günlüklerini `TestLogs/` altına toplar |
| Delete Local Save | Editördeki yerel kaydı siler |

Komut satırından release build:

```sh
Unity -batchmode -buildTarget Android -projectPath . -executeMethod LayerSlide.Editor.AndroidBuild.BuildReleaseBatch -logFile build.log
```

### Release imzası

Build betiği `%USERPROFILE%\.layerslide\keystore.properties` dosyasını okur (yolu `LAYERSLIDE_KEYSTORE_PROPERTIES` ile değiştirilebilir):

```
storePath=C:\Users\<kullanıcı>\.layerslide\layerslide-release.keystore
alias=layerslide-release
storePassword=...
keyPassword=...
```

Keystore ve parola Git'e girmez; ProjectSettings'e de yazılmaz (yalnızca build süresince uygulanır).
**Bu iki dosyanın güvenli bir yedeğini al:** kaybolursa aynı imzayla güncelleme kurulamaz.

## Klasör yapısı (`Assets/LayerSlide`)

| Klasör | İçerik |
| --- | --- |
| `Core` | Unity'den bağımsız saf C#: `Level`, `Board`, `RuleEngine`, `DragResolver`, `CommandHistory`, `LevelValidator`, `SolutionReplayer`, `HintSolver` |
| `Gameplay` | `GameController` (durum makinesi, ekran akışı), `InputController`, `PointerReader`, `TutorialTracker` |
| `Presentation` | `BoardPresenter`, `BlockView`, `GateView`, `CameraFramer`, `FxPool`, `MeshLibrary` + `MeshBank`, `Palette`, `VisualTheme` |
| `UI` | Koddan kurulan uGUI ekranları: HUD, ana menü, bölüm listesi, ayarlar, hakkında, modal, bildirim |
| `Services` | `SaveService` + `SaveFormat` + `JsonTree`, `HintService`, `AudioHapticsService` + `SfxLibrary` + `OutputLimiter`, `LocalTestLogger`, `LevelCatalog`, `AppServices`, `Bootstrap` |
| `Resources` | `Levels/`, `Solutions/` JSON'ları, `Audio/SfxLibrary`, font lisansı |
| `Art`, `Audio`, `Fonts`, `Data` | Materyaller, mesh bankası, ikon; 7 WAV + mixer; Noto Sans + TMP font assetleri; tema ve URP assetleri |
| `Editor` | `ProjectSetup`, `AssetBaker`, `LevelEditorWindow`, `LevelTools`, `AndroidBuild`, `AndroidManifestPostProcessor` |
| `Tests/EditMode`, `Tests/PlayMode` | Kural, girdi, öğretici, kayıt, ipucu, performans testleri; gerçek dokunma olaylarıyla sahne testleri |

Kural sonucu her zaman `RuleEngine` tarafından belirlenir; girdi, ipucu, editör ve çözüm replay'i aynı kodu kullanır.
Sürükleme sırasında model değişmez ve kare başına bellek ayırma yapılmaz; mesh ve materyaller paylaşımlıdır.

## Testler

```sh
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -assemblyNames LayerSlide.Tests.EditMode -testResults editmode.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -assemblyNames LayerSlide.Tests.PlayMode -testResults playmode.xml
```

`-assemblyNames` filtresi, `testables` ile derlenen Input System paketinin kendi testlerini dışarıda tutar.
`LAYERSLIDE_CAPTURE_DIR` verilirse PlayMode testi ekran görüntüleri de kaydeder.

## Lisanslar

Noto Sans yazı tipi SIL Open Font License 1.1 ile dağıtılır (`Assets/LayerSlide/Fonts/OFL.txt`).
Ses efektleri, mesh'ler ve ikon proje içinde üretilir; dış ses/model/görsel varlığı kullanılmaz.
