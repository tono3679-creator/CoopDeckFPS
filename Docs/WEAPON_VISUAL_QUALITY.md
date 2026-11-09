# WEAPON_VISUAL_QUALITY — キツネの一人称の武器（Fire Rifle）の画質改善（2026-11-09）

ユーザー依頼：「一人称の銃の輪郭にジャギーがあり、装甲の面が平坦で、全体的に安っぽい。スタイライズドな造形のまま高品質な FPS 武器の見た目に。単純にポリゴンを増やさず、先に原因を特定すること」。
元のモデル・Prefab は `ArtSource/FireRifle/Backup/20261109_before_quality/`（FBX・テクスチャ 3 枚・マテリアル 2 つ・Prefab・GLB・取り込みスクリプト）に保存した。

---

## 1. 調査結果（原因）

| 項目 | 結果 | 影響 |
|---|---|---|
| 描画解像度・Render Scale | この PC（ROG Ally X）は自動で **Handheld** 段階（`PerformanceBootstrap`）。1920×1080 の画面に **Render Scale 0.833（1600×900）＋ FSR 1** で拡大。High 段階（デスクトップ）は 1.0 | 内部 900p を FSR の鮮鋭化（RCAS）付きで拡大するため、銃の縁が階段状になり、縁取りの光が出る |
| アンチエイリアス | Handheld：**MSAA なし＋FXAA**。High：MSAA 4×＋FXAA。カメラの AA は `CharacterPrefabBuilder` でベースカメラに FXAA | FXAA は細い線を消せない |
| 一人称の輪郭線 | `M_FireRifle_Toon_FP` の輪郭 0.7 mm ＝ 1080p で約 1.2 px、900p で約 1 px | 1 px の裏面法の線は AA が効かず**点線のように途切れる**（ジャギーの主因の一つ） |
| 一人称の別カメラ | 銃・腕は **ViewModel レイヤーをオーバーレイカメラ**（FOV 70、near 0.01）で描画し、PlayerCamera のスタックに積む | ポストプロセスは PlayerCamera だけ。URP は「ポストプロセスが有効なカメラ」の中でトーンマップ・ブルームを掛けるので、**銃には掛かっていなかった**（白い装甲が 1 を超えて白飛び、金のスコープと発光部がのっぺり）。さらに **SMAA もその中で掛かる**ため、今の構成で SMAA にしても銃には効かない（URP 17 の `PostProcess.RenderPostProcessing`／`UniversalRendererRenderGraph` で確認。FXAA だけは最後のカメラの最終パスなので銃にも掛かる） |
| 頂点・三角形 | Meshy リメッシュ 7,678 三角形（位置 3,840）→ 前回の作り直し 30,000 三角形（Unity 30,776 頂点）。高精細 437,374 三角形（UV なし） | 30k の形は高精細から平均 0.05 mm しか離れていない（シルエットは十分） |
| 法線・スムージング | 30k は 40° 以上を硬い角。隣り合う面の角度が 40° を超える辺が **16.4%**（高精細は 3.1%）、10° 未満の細い三角形が 25.6%（高精細 4.8%） | 高精細の**丸い角（ベベル）が削減で硬い折れ目に潰れた**。滑らかにした範囲は細い三角形が法線を曲げ、平らな装甲に**暗いムラ**（Blender の灰色レンダーで確認） |
| テクスチャ | Meshy の基本色 2048 → Real-ESRGAN で 8K → 4096 に焼き直し（BC7・トライリニア・異方性 8、法線マップ 4096 BC5） | 解像度は足りている（Blender の 8× AA 描画では滑らかで、テクスチャのにじみは無い）。白い装甲への赤のはみ出しなどの筆跡は **Meshy の元テクスチャ由来** |
| シェーダー・マテリアル | `CoopDeck/Character/Toon`：2 階調の陰影＋リム＋輪郭線。**金属度・滑らかさ・反射なし**。法線マップは陰影の境目にしか効かない | 白塗装・赤塗装・樹脂・金属がすべて同じ質感で、明るい面は一色の平面（「平坦・安っぽい」の主因） |

同じ構図・8× AA の Blender 描画（`fp_view_check.py`）では 30k のメッシュでも輪郭は滑らかだったので、**ジャギーは主に Unity の描画設定と 1 px の輪郭線、平坦さは主にシェーダー**、形状は丸い角とムラが従の原因と判断した。

## 2. 改善内容

### 2.1 描画（カメラ・URP）

| 変更 | 内容 |
|---|---|
| ポストプロセスを最後のカメラへ | `PlayerCamera.renderPostProcessing = false`、`ViewModelCamera.renderPostProcessing = true`。スタック全体に 1 回だけ掛かる（ワールドも銃も同じトーンマップ・ブルーム・色調整） |
| AA | ベースカメラ（スタック全体の設定）を **SMAA High**。High 段階は MSAA 4× と併用 |
| Handheld の解像度 | Render Scale 0.833＋FSR → **1.0**（`RenderingSetup`）。MSAA 2× は約 +3 ms で見送り |
| 一人称の輪郭線 | 武器シェーダーで画面のピクセル幅に換算し、**最小 1.4 px・最大 2.5 px**（三人称 1〜4 px）。距離による太さの変化は従来どおり |

### 2.2 形状（`ArtSource/Tools/rebuild_weapon_hq.py`）

| 手順 | 内容 |
|---|---|
| 削減 | 高精細を 60,000 三角形へ（Quadric）。40° 以上を硬い角 |
| ベベル | 50° を超える折れ目に **1.2 mm・2 分割のベベル**（目立つ装甲の角が光を拾う） → 109,436 三角形 |
| 法線 | **面積重み付き法線**（Weighted Normal、硬い角は保持）。平らな装甲は平らに陰影が付き、細い三角形のムラが消える |
| LOD | `FireRifle_Body_LOD0`（109k、Unity 100,827 頂点）と `_LOD1`（30k、32,795 頂点）。画面の高さの 12% 以上で LOD0（一人称は常に LOD0）、0.4% 未満で非表示 |
| 寸法 | 全長 0.85 m・原点（ピストルグリップ）・`Muzzle`・`LeftHandGrip` は同じ値（手の持ち位置・射撃・リロードの構図は不変） |

比較（同じ高精細から、灰色の形状）：30k は丸い角が折れ目になり平面にムラ、60k＋重み付き法線で高精細とほぼ同じ見え方、ベベルで装甲の角に光の筋が出る。

### 2.3 テクスチャとマップ（同じツール）

| マップ | 内容 |
|---|---|
| 基本色 `T_FireRifle_BaseColor`（4096） | 元の 8K 拡大アトラス（`FireRifle_atlas_x4.png`）から新しい UV へ焼き直し。**元の品質・配色のまま**（筆跡も残る） |
| 法線 `T_FireRifle_Normal`（4096） | 高精細から（低ポリとの傾きの中央値 2.1°） |
| 表面 `T_FireRifle_Surface`（4096、リニア） | R＝凸の角（高精細の法線の曲率をテクセルごとに測る）、G＝AO（ゲーム用メッシュ自身、1.2 cm）、B＝凹の溝。凸 3.3%・凹 4.0% のテクセル |
| 材質 `T_FireRifle_MaterialMask`（2048、リニア） | R 白塗装・G 赤塗装・B 黒い樹脂・A 金属（残り＝金・橙のアクセント）。基本色の色相・彩度・明度から。面積比 白 11%・赤 25%・樹脂 50%・金属 3.4%・アクセント 10% |

最初は高精細の頂点で曲率と AO を計算したが、平らな面の三角形が大きく（最大 8 mm 超）、角の値が扇状に面へにじんだため、テクセル単位の計算に変えた。

### 2.4 シェーダー `CoopDeck/Weapon/HardSurfaceToon`（`Art/Weapons/Shaders/WPN_HardSurfaceToon.shader`）

キャラクターの `CH_Toon` と同じ 2 階調の陰影・リム・輪郭線を土台に、次を追加した（キャラクター側は変更なし）。

- 材質ごとの値（滑らかさ・金属度・ハイライト・エッジ）：白塗装 0.5/0/0.5/0.8、赤塗装 0.55/0/0.55/0.85、黒い樹脂 0.42/0/0.35/0.8、金属 0.7/0.9/1.0/1.0、金 0.62/0.55/0.7/0.7
- 明るい側にもランバートの階調を少し残す（`_DiffuseWrap` 0.55）
- 帯状に整えたハイライト（トゥーン調）、空・反射プローブの環境反射（金属は色付き、塗装は薄いクリア塗装のつや）
- **エッジハイライト**（表面マップ R）：角を明るい色で縁取る（ベベルの光の拾い）。**AO と溝**（G・B）でパネルの継ぎ目に奥行き
- 一人称だけ：カメラ基準の補助ハイライト（`_ViewLightSpecular` 0.6）。太陽の向きに関係なくつやが読める
- 輪郭線：ピクセル幅の最小・最大

マテリアル：`M_FireRifle_Toon`（三人称）・`M_FireRifle_Toon_FP`（一人称）を新シェーダーへ移した。`FireRifleAssetFactory` はシェーダーを移すときだけ初期値を書き、以降の調整は保持する。

## 3. 比較結果

撮影：PlayMode テスト `WeaponLookCaptureTests`（Explicit）。サンドボックスで同じ立ち位置・同じフレーム（時間停止）を、実際のプレイヤーのカメラスタックで 1920×1080 に描画。画像は `Screenshots/WeaponLook/<before|after...>_<設定>_{fp,muzzle,tp_near,tp_mid}.png`。

| 設定（ラベル） | 内容 | 見え方 |
|---|---|---|
| `HH_now` | 今のこの PC：0.833＋FSR、MSAA なし、FXAA、ポストプロセスはワールドだけ | 階段状の縁、点線の輪郭、銃の白飛び |
| `HH_FSR_SMAA_vmPost` | 0.833＋FSR＋SMAA（最後のカメラ） | 輪郭は改善するが FSR の鮮鋭化で縁がざらつく |
| `HH_RS1_SMAA_vmPost` | **採用（Handheld）**：1.0＋SMAA | 縁が滑らか、輪郭が切れない、トーンマップで白飛びと金の飛びが収まる |
| `HH_RS1_MSAA2_SMAA_vmPost` | 1.0＋MSAA 2×＋SMAA | 少し良いが +3 ms |
| `High_now` | 今のデスクトップ：MSAA 4×＋FXAA | 縁は滑らかだが銃は白飛び |
| `High_SMAA_basePost` | MSAA 4×＋SMAA（ポストプロセスはワールドだけ） | `High_now` と同じ（SMAA が銃に効かない実証） |
| `High_SMAA_vmPost` | **採用（High）**：MSAA 4×＋SMAA（最後のカメラ） | 最も滑らか、白飛びなし |
| `High_RS125_SMAA_vmPost` | Render Scale 1.25 | 1.0 よりわずかに滑らか。MSAA 4×＋SMAA の後では差は小さい（画素数 1.56 倍） |
| `High_noMSAA_SMAA_vmPost` | SMAA だけ | Handheld と同等 |

モデル・シェーダーの効果（`after2_*` と `before_*`）：白い装甲に上面から側面への階調、赤い装甲とスコープの面の切り替わりにエッジの光、継ぎ目の AO、暗い銃身に金属らしい反射と角の摩耗ハイライト。三人称も同じ。マズルフラッシュはトーンマップが掛かり、ピンクがかった白飛びから黄〜橙寄りに（`Compare_MuzzleFlash_PostCamera.png`）。

## 4. 性能（FPS への影響）

ROG Ally X（Ryzen Z1 Extreme、Radeon 780M）。改修前＝`Builds/VfxPerf`（2026-11-09 01:28、旧モデル・旧カメラ・Handheld 0.833＋FSR）、改修後＝`Builds/WeaponQuality`（Release）。どちらも `-screen-fullscreen 1 -screen-width 1920 -screen-height 1080 -perfrun 25`、サンドボックス。結果は `PerfCaptures/weapon_quality_1080p/`。

| 計測 | 改修前 | 改修後 | 差 |
|---|---|---|---|
| Handheld・自動操縦（射撃）・60fps 上限 | 平均 59.9 fps / 1% Low 59.9 / GPU 2.87 ms / CPU メイン 3.75 ms | 59.9 / 59.9 / **4.71 ms** / 3.68 ms | FPS は同じ。GPU +1.8 ms（16.7 ms の枠の 28%） |
| Handheld・静止・上限なし | 90.8 fps / 1% Low 69.2 / GPU 3.40 ms | 89.9 / 86.0 / **4.12 ms** | GPU +0.7 ms |
| High（`-tier high`）・静止・上限なし | 89.9 fps / GPU 7.33 ms | 107.2 / **7.27 ms** | GPU 差なし（MSAA 4× が大半） |

- 上限なしでも約 90 fps で頭打ちになる（フレーム時間が 11.1 ms で一定。外部のフレーム制限と思われる）ため、比較は GPU 時間で行った。
- Editor での描画時間（`Logs/weapon_look_*.json`、1920×1080 のカメラスタック 1 枚を GPU 同期で計測、相対値）：解像度 0.833→1.0＋SMAA で約 +0.7〜1.6 ms、MSAA 2× は約 +3 ms、MSAA 4× は約 +4.8 ms（Ally X では重い）。同じ描画設定どうしではモデル・シェーダーの差は誤差の範囲（前後とも 6.6〜6.9 ms）。
- テクスチャのメモリ：表面マップ 4096 BC7（ミップ込み約 21 MB）と材質マスク 2048 BC7（約 5 MB）が増えた。
- 保存されていた起動解像度（`HKCU\Software\CoopDeck\CoopDeckFPS`）が 960×540 のウィンドウ表示だったため、最初の計測は 960×540 になっていた（GPU 1.0〜1.3 ms → 2.3〜2.4 ms）。上の表は 1080p 全画面を指定し直した値。960×540 は通信テスト（`-screen-width 960`）の名残で、その後の計測で保存値は全画面 1920×1080 に戻っている。計測では今後も解像度を指定し、結果の `resolution` を確認する。

比較画像（`Screenshots/WeaponLook/`）：

| ファイル | 内容 |
|---|---|
| `Compare_FP_BeforeAfter.png` | 一人称、この PC の改修前（0.833＋FSR・FXAA・30k・トゥーン）と改修後 |
| `Compare_FP_Zoom3x.png` | スコープと銃床の 3 倍拡大（前後） |
| `Compare_AA_Settings_OldModel.png` | 旧モデルのまま描画設定 9 通り（3 倍拡大）。`*` はポストプロセスを最後のカメラへ移したもの |
| `Compare_RenderScale_1.0_vs_1.25.png` | High の Render Scale 1.0 と 1.25 |
| `Compare_ThirdPerson_Near.png`・`_Mid.png` | 三人称（1.1 m・3.6 m） |
| `Compare_MuzzleFlash_PostCamera.png` | マズルフラッシュ（ポストプロセスの位置の前後） |

形状の比較（Blender、灰色・16× AA）は `ArtSource/FireRifle/Inspection/quality_20261109/`（`shape_*`、`mesh_report.json`、`shape_study.json`）。

## 5. 手順・コマンド

```powershell
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
& $blender -b --factory-startup -P ArtSource\Tools\rebuild_weapon_hq.py -- --name FireRifle `
  --prepared ArtSource\FireRifle\Export\FireRifle.glb --highres ArtSource\FireRifle\Meshy\FireRifle_highres.glb `
  --atlas ArtSource\FireRifle\Export\FireRifle_atlas_x4.png --forward=-X `
  --socket Muzzle=0,-0.605,0.137 --socket LeftHandGrip=0,-0.401,0.071 `
  --faces 60000 --bevel 0.0012 --bevel-angle 50 --lod 30000 --size 4096 `
  --model-out Assets\_Game\Art\Weapons\FireRifle\Models --texture-out Assets\_Game\Art\Weapons\FireRifle\Textures `
  --glb-out ArtSource\FireRifle\Export [--check <dir>]
```
その後 `CoopDeck > Art > Import Fire Rifle`（または ブートストラップ）。比較撮影：`-runTests -testPlatform PlayMode -testFilter WeaponLookCaptureTests`（`WEAPON_LOOK_TAG`／`Logs/weapon_look_tag.txt` で出力名、`WEAPON_LOOK_SETUPS`／`Logs/weapon_look_setups.txt` で設定を絞る）。

## 6. 残り・次の候補

- 白い装甲への赤のはみ出しなど、Meshy の元テクスチャの筆跡はそのまま（デザインを変えないため）。消すなら塗り分けの境目を面の境界に合わせる処理が要る。
- 小さな UV 島（三角形 1〜2 枚）では曲率・AO にノイズが残る。画面上は数ピクセルで目立たない。
