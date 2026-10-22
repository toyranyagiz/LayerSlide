# Known Issues — Layer Slide 1.0.0 (L001–L010)

GDD 08 §14: open critical/high issues must be 0 for delivery; low issues are listed here.
This list states what is **not yet proven**, separately from what is implemented.

## Not yet verified (requires hardware or people)

| ID | Item | Status |
| --- | --- | --- |
| F01–F03, F05–F06 | Frame time p95/p99, cold start, level load, memory, APK size on Galaxy A32 4G, Galaxy A54, Pixel 7 | Not measured. No device was available; nothing is marked as passed. |
| F04 | 0 B GC per drag frame | Verified in the editor for the drag math (`Is.Not.AllocatingGCMemory`); device profiler run pending. |
| F07 | 10 000-command history | Verified in the editor (save/load/replay/undo, timings logged by the test); device timing pending. |
| V01–V10 | Real screen sizes, safe areas, font rendering on phones | Layout reviewed in editor captures only. |
| U01–U05 | Player acceptance test (5 early + 10 final participants) | Not run. |
| X15 | Signature/permission/backup inspection of the release APK | **Not yet verified.** No release APK has been built and inspected yet; run `apksigner verify` and `aapt2 dump permissions/xmltree` on the first release build (expect VIBRATE only, `allowBackup=false`), then on every release build. |

## Low

| ID | Description |
| --- | --- |
| LOW-01 | The HUD title shows only "Bölüm N": the long level names do not fit next to the 48 dp icons at 320–360 dp and GDD 04 §6 forbids runtime font shrinking or ellipsis. |
| LOW-02 | The batch-mode PlayMode tests run in a 640×480 editor window; the board area is small there, so captures are only a rough visual check. |

## Scope

L011–L030 are outside this delivery; the catalog, editor and pipeline accept them without code changes.
