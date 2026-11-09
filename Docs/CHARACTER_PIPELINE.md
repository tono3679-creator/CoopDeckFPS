# CHARACTER_PIPELINE — Meshy → Blender → Unity のキャラクター制作手順

> 最初の例：**Fox Gunner（炎のキツネガンナー）**、2026-11-07。三面図（正面・側面・背面）から作成。
> Meshy は有料サービス。**生成の前に用途と必要クレジットをユーザーに提示し、承認を得てから実行する**（CLAUDE.md）。

---

## 1. 流れ

| 段階 | 道具 | 出力 |
|---|---|---|
| 1. 参照画像の切り出し | 三面図から正面・側面・背面を正方形 PNG に | `ArtSource/<Name>/Reference/` |
| 2. 3D 生成 | Meshy **Multi-Image to 3D**（meshy-7.1 + meshy-7 テクスチャ 2K、T ポーズ、生成時に約 15,000 三角形へリメッシュ） | `ArtSource/<Name>/Meshy/01_generate/` |
| 3. 自動リギング | Meshy **Rig**（身長を指定。歩き・走りのモーション付き） | `ArtSource/<Name>/Meshy/02_rig/` |
| 4. 検査 | Blender `ArtSource/Tools/inspect_character.py` | `ArtSource/<Name>/Inspection/`（`report.json`、四面と動きの画像） |
| 5. Unity 用に整える | Blender `ArtSource/Tools/prepare_for_unity.py` | FBX・PNG を `Assets/`、GLB を `ArtSource/<Name>/Export/` |
| 5b. テクスチャの高品質化 | Blender `ArtSource/Tools/refine_character.py` ＋ Real-ESRGAN（§8） | `ArtSource/<Name>/Export/<Name>_HQ.glb`、4K の `T_<Name>_BaseColor.png` |
| 6. 取り込み設定と生成 | Unity `FoxGunnerAssetFactory`（キャラごとに用意） | マテリアル・待機クリップ・Animator・Prefab・プレビュー画像 |

`ArtSource/` は Unity に読み込まれない元素材置き場（FBX/PNG/GLB は Git LFS）。

---

## 2. Meshy の設定とクレジット（Fox Gunner の実績）

| 処理 | 設定 | クレジット |
|---|---|---|
| Multi-Image to 3D | 3 枚、`meshy-7.1` + `meshy-7`、2K、PBR なし、`remove_lighting`、`image_enhancement: false`、`pose_mode: t-pose`、`should_remesh` 三角形 15,000、トゥーン調のテクスチャ指示文 | 30 |
| Rig | `height_meters: 1.6`（歩き・走り込み） | 5 |
| 合計 | | **35**（残高 1,601 → 1,566） |

- ゲームの軽量化だけなら単一画像の Smart Topology（`meshy-t2.5`）の方が安い（合計 20）が、背面（尻尾・マフラー）の再現を優先して複数画像を使った。
- 追加モーションは `meshy_animate`（1 本 3 クレジット）。ID は `meshy_list_animations`（無料）で探す。

---

## 3. Blender での検査結果と修正（Fox Gunner）

| 項目 | Meshy の出力 | 対応 |
|---|---|---|
| 頂点 | UV の継ぎ目で分割され 28,746（実位置は 7,311） | 結合して 7,311（Unity では UV 分割後 15,291） |
| 法線 | 面ごとに角張り、同じ位置で法線が大きくばらつく（中央値 58°） | 分割法線を消して全体を平滑化（トゥーンの陰影と輪郭線のため） |
| 原点 | 体が原点から 0.2 m 前（尻尾込みの外接箱の中心が原点） | 腰の真下を原点へ移動 |
| 縮尺 | アーマチュアに 0.01 のスケール | 適用して 1 単位 = 1 m（モーションの位置キーも同率で縮小） |
| 向き | 正面が Blender の -Y。Unity は FBX の軸情報を読むため、書き出し軸を変えても -Z 向きのまま | FBX 書き出し前にリグごと 180° 回転（GLB は glTF の規約どおり +Z 正面のまま） |
| テクスチャ | リグ付き GLB には JPEG 版が入っている | 生成時の無圧縮 PNG を使用（同じ画像。差は JPEG 劣化分のみ） |
| ボーン | 24 本（指・尻尾・耳なし）、1 頂点最大 4 本、未ウェイト頂点 0 | そのまま |
| モーション | その場で動く 30 fps。歩き 1.03 秒 / 走り 0.63 秒 | 接地中の足の速さを測定：歩き 1.2 m/s、走り 4.0 m/s |

注意：Blender の glTF 読み込みはボーン表示用の `Icosphere` を自動で作る。ファイル内のメッシュではないので検査で数えない。

---

## 4. コマンド

```powershell
$blender = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$rig = 'ArtSource\FoxGunner\Meshy\02_rig'

# 検査（GLB/FBX とモーションを読み、report.json と画像を出力）
& $blender -b --factory-startup -P ArtSource\Tools\inspect_character.py -- `
  --model "$rig\FoxGunner_rigged.glb" --fbx "$rig\FoxGunner_rigged.fbx" `
  --anim "$rig\FoxGunner_Walking.glb" --anim "$rig\FoxGunner_Running.glb" --out ArtSource\FoxGunner\Inspection

# Unity 用に整えて書き出し
& $blender -b --factory-startup -P ArtSource\Tools\prepare_for_unity.py -- --name FoxGunner `
  --model "$rig\FoxGunner_rigged.glb" --anim "Walk=$rig\FoxGunner_Walking.glb" --anim "Run=$rig\FoxGunner_Running.glb" `
  --model-out Assets\_Game\Art\Characters\FoxGunner\Models --anim-out Assets\_Game\Animations\FoxGunner `
  --texture-out Assets\_Game\Art\Characters\FoxGunner\Textures --glb-out ArtSource\FoxGunner\Export `
  --texture ArtSource\FoxGunner\Meshy\01_generate\FoxGunner_mesh_base_color.png
```

Unity（エディターを閉じた状態でバッチ実行。開いている場合はメニュー `CoopDeck > Art > Import Fox Gunner`）：

```
"C:\Program Files\Unity\Hub\Editor\6000.5.4f1\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.FoxGunnerAssetFactory.RunBatch -coopdeckOverwriteAnimations -logFile Logs/foxgunner_import.log
```

`-coopdeckOverwriteAnimations` は待機クリップと Animator を作り直す（FBX を出し直したときに付ける）。プレビュー画像は `Screenshots/FoxGunner_Preview.png`。

---

## 5. Unity 側の資産（Fox Gunner）

| 資産 | 内容 |
|---|---|
| `Art/Characters/FoxGunner/Models/FoxGunner.fbx` | Humanoid。骨の対応は明示（Meshy の背骨は上から番号：`Spine02`→Spine、`Spine01`→Chest、`Spine`→UpperChest）。法線は読み込み、接線なし、マテリアルは読み込まない |
| `Textures/T_FoxGunner_BaseColor.png` | 2048、sRGB、アルファなし、圧縮 |
| `Materials/M_FoxGunner_Toon.mat` | `CoopDeck/Character/Toon`（`Art/Characters/Shaders/CH_Toon.shader`）。調整値は再生成でも保持 |
| `Animations/FoxGunner/A_FoxGunner_Walk.fbx` / `Run.fbx` | Humanoid、ループ、ルートは姿勢に焼き込み（ルートモーションなし） |
| `A_FoxGunner_Idle.anim` | 仮の待機：歩行サイクルの平均姿勢＋ゆっくりした呼吸 |
| `AC_FoxGunner_TP.controller` | `AnimatorParams` と同じパラメータ一式。`Locomotion` = Speed の 1D ブレンド（0 待機 / 1.2 歩き / 4.0 走り、さらに 5〜12 m/s に 1 m/s ごとの走りを速度÷4 倍速で並べる） |
| `Prefabs/Characters/PF_FoxGunner_Body.prefab` | FBX の Prefab Variant。Animator（ルートモーションなし）、トゥーンマテリアル |
| `PF_Player` の `BodyVisual` | 上の Prefab の入れ子インスタンス（`CharacterPrefabBuilder.BuildPlayer`）。影のみ描画、Player レイヤー、Animator は常に更新。`CharacterAnimatorDriver` が `FpsMotor` の速度を渡す |

速度合わせ：プレイヤーは歩き 6.5 m/s・スプリント 9.5 m/s で、走りクリップ（4.0 m/s）より速い。1D ブレンドはクリップの「速度」ではなく「長さ」を混ぜるため、4 m/s（1 倍）と 12 m/s（3 倍）の 2 つだけでは 6.5 m/s で 1.27 倍にしかならず、接地した足が約 1.5 m/s 滑った。1 m/s ごとに並べると誤差は約 1%。PlayMode テストで、6.5 m/s 走行時の接地中の足の滑りが 0.25 m/s（速度合わせなしでは約 2.5 m/s）であることを確認している。

トゥーンシェーダー：メインライトを 2 階調（影色を乗算）＋落ち影も同じ段に、追加ライトも段階化、ライトプローブの環境光、リムライト、法線方向に押し出す輪郭線パス（`SRPDefaultUnlit`）。ShadowCaster / DepthOnly / DepthNormals あり、SRP Batcher 対応、Forward+（`_CLUSTER_LIGHT_LOOP`）対応。

テスト：
- `Tests/EditMode/Characters/FoxGunnerAssetTests.cs`：アバターと骨の対応、身長 1.6 m・原点・+Z 向き、予算、シェーダーのパス、クリップ、Animator、走りで足が動き体が根元から離れないこと、`PF_Player` の体がこのモデルで影のみ描画であること。
- `Tests/PlayMode/PlayerBodyPlayModeTests.cs`：生成された `PF_Player` を `CharacterCommands` で前進させ、Animator に速度が届くこと、体がコントローラーの上にあること、接地した足が滑らないこと。

`ProjectBootstrap.Run` が `FoxGunnerAssetFactory.EnsureAll` を呼ぶので、ブートストラップには FBX・PNG（Git LFS）が必要。

---

## 6. 武器（Fire Rifle、2026-11-07）

三面図の下段（側面・正面・上面・背面）から作成。手順はキャラクターと同じで、リギングの代わりに `ArtSource/Tools/prepare_prop_for_unity.py` を使う。

| 段階 | 内容 |
|---|---|
| 参照画像 | 4 枚を正方形 PNG に（`ArtSource/FireRifle/Reference/`）。正面・背面は元の絵が小さく約 6 倍に拡大 |
| Meshy | Multi-Image to 3D（側面を先頭に 4 枚、meshy-7.1 + meshy-7 2K、生成時に約 8,000 三角形へリメッシュ）**30 クレジット**（残高 1,566 → 1,536） |
| Blender | 頂点を結合（12,656 → 3,840）。**40° 以上の角は硬いまま**（全体を平滑化すると平らなパネルの陰影がにじむ）。銃口を前、全長 0.85 m、原点をピストルグリップ（右手）に。銃口と左手の位置を空のオブジェクト（`Muzzle`、`LeftHandGrip`）として FBX に入れる |
| Unity | `FireRifleAssetFactory`（メニュー `CoopDeck > Art > Import Fire Rifle`）：取り込み設定、発光マップ（基本色の明るいオレンジ〜黄色の部分）、トゥーンマテリアル 2 種、`PF_Weapon_FireRifle` |

```powershell
& $blender -b --factory-startup -P ArtSource\Tools\prepare_prop_for_unity.py -- --name FireRifle `
  --model ArtSource\FireRifle\Meshy\FireRifle_mesh.glb --texture ArtSource\FireRifle\Meshy\FireRifle_mesh_base_color.png `
  --forward=-X --length 0.85 --pivot=0.40,0,-0.22 --socket Muzzle=-0.957,0,0.088 --socket LeftHandGrip=-0.50,0,-0.06 `
  --model-out Assets\_Game\Art\Weapons\FireRifle\Models --texture-out Assets\_Game\Art\Weapons\FireRifle\Textures `
  --glb-out ArtSource\FireRifle\Export
```

- 輪郭線：硬い角では法線が分かれ、押し出した輪郭が割れる。`ToonOutlineNormalsPostprocessor` が `Art/Weapons/` 以下のモデルの取り込み時に「位置ごとに平均した法線」を UV3 に書き、シェーダーの `_OUTLINE_UV3_NORMALS` で輪郭線だけそれを使う（UV3 はスキニングされないので、スキンメッシュのキャラクターには使わない）。
- 発光：シェーダーに `_EMISSION`（発光マップ × HDR 色 2.0）を追加。ブルームのしきい値 1 を超えて光る。
- マテリアル：`M_FireRifle_Toon`（三人称・輪郭 3 mm）と `M_FireRifle_Toon_FP`（一人称・輪郭 0.7 mm。カメラから 0.3〜0.6 m なので同じ太さだと太すぎる）。
- 一人称：`ViewModel/Rifle` の仮の箱の銃を置き換え。グリップを右手の手袋に、`LeftHandGrip` が左手の手袋に重なる。銃床がレンズに近すぎないよう 11 cm 前・3.5 cm 下、銃口を 6° 内向き（側面の紋章とエネルギー帯が見える）。`HitscanWeapon.muzzle` と `SkillCaster.castOrigin` はライフルの `Muzzle`。
- 三人称：体の右手のボーンの子。待機姿勢でグリップを手のひらに、銃口を前下がり 35°（ローレディ）にしたときの手からの相対位置を、ポーズを付けた複製で測って設定（`FireRifleAssetFactory.RightHandHold`）。自分には体と同じく影だけ見える。
- テスト：`Tests/EditMode/Weapons/FireRifleAssetTests.cs`（長さ・向き・ソケット・予算・UV3 の輪郭用法線と硬い角・マテリアル・発光の範囲・プレイヤーの一人称と右手）。

### 6.1 高精細モデルからの作り直し（2026-11-08）

Meshy のリメッシュ（7,678 三角形・2K）は、一人称の近距離でスコープや炎のひれが角ばり、塗りがぼやけていた。キツネの体（§8.2）と同じく、Meshy の高精細モデル（`FireRifle_highres.glb`、437,374 三角形、UV なし）から作り直した（`ArtSource/Tools/refine_prop.py`）。

| 手順 | 内容 |
|---|---|
| 位置合わせ | 高精細を `prepare_prop_for_unity.py` と同じ向き（`--forward=-X` を Blender の -Y へ）に回し、今の銃の外接箱に合わせる。距離は平均 0.20 mm・95% 0.48 mm。全長 0.85 m・原点（グリップ）・ソケット（`Muzzle`・`LeftHandGrip`）は同じ |
| 削減 | Quadric collapse で 30,000 三角形。40° 以上の角は硬いまま（パネルの陰影をにじませない） |
| UV | Smart UV Project → 島の大きさをそろえて 4096 に詰める。テクセル密度 3,555/3,997/4,228（5%/50%/95%） |
| 色 | 今の銃の 2K アトラスを島の外へ広げてから Real-ESRGAN で 4 倍（`ArtSource/FireRifle/Export/FireRifle_atlas_x4.png`）、今の銃の UV を通して焼き直し（薄いひれのためケージ 2 mm・探索 4 mm、外れ 2.2% は同じ島の隣から） |
| 法線マップ | 高精細から 4096（`T_FireRifle_Normal.png`）。`CH_Toon` に任意の法線マップ（`_NORMALMAP`・`_BumpMap`・`_BumpScale`、接線は MikkTSpace）を追加 |
| Unity | `FireRifleAssetFactory`：基本色 4096・BC7・トライリニア・異方性 8、法線マップ（BC5）、発光マップは新しい基本色から作り直し（2048） |

```
blender -b --factory-startup -P ArtSource/Tools/refine_prop.py -- --name FireRifle ^
    --prepared ArtSource/FireRifle/Export/FireRifle.glb --highres ArtSource/FireRifle/Meshy/FireRifle_highres.glb ^
    --forward=-X --socket Muzzle=0,-0.605,0.137 --socket LeftHandGrip=0,-0.401,0.071 --esrgan <realesrgan フォルダ> ^
    --faces 30000 --size 4096 --model-out Assets/_Game/Art/Weapons/FireRifle/Models ^
    --texture-out Assets/_Game/Art/Weapons/FireRifle/Textures --glb-out ArtSource/FireRifle/Export [--check <dir>]
```

### 6.2 一人称向けの作り直しと武器シェーダー（2026-11-09）

30k は高精細の丸い角を折れ目に潰し、平らな面にムラが出ていた。`ArtSource/Tools/rebuild_weapon_hq.py` で 60k＋折れ目に 1.2 mm のベベル＋面積重み付き法線（LOD1 30k）に作り直し、表面マップ（凸の角・AO・溝）と材質マスクも焼いて、シェーダー `CoopDeck/Weapon/HardSurfaceToon` で塗装・樹脂・金属の質感とエッジハイライトを付けた。描画側（ポストプロセスの位置・SMAA・Handheld の解像度）も含めて `WEAPON_VISUAL_QUALITY.md`。

---

## 7. 質感の改善（フィギュアっぽさを減らす、2026-11-07）

モデル・リグ・アニメーション・UV・テクスチャの色は変えず、マテリアルとシェーダーだけで素材ごとの質感を付けた。

| 手順 | 内容 |
|---|---|
| バックアップ | 変更前の `M_FoxGunner_Toon`（CH_Toon の 2 階調）は残し、`Materials/Backup/M_FoxGunner_Toon_Original.mat` にも複製 |
| 部位マスク | `FoxGunnerLookFactory` がテクスチャの色（橙・白系・赤・暗色・冷たい灰色・琥珀など）×スキンのボーン（頭・首・胴・鎖骨・腕・手・腰・脚・足・尻尾）×位置（顔の範囲・尻尾・ベルト・ブーツ）で、UV 上のテクセルごとに 毛 / 顔 / 目 / 布 / 革 / 金属 / ガラス を判定。結果を質感パラメータとして `T_FoxGunner_SurfaceMask.png`（R 柔らかさ・G 滑らかさ・B 金属度・A 暖かみ、2048、リニア）と URP Lit 用 `T_FoxGunner_MetallicSmoothness.png` に書く。ボーン名で判定するので尻尾・耳・指のボーンが増えても動く |
| 1. URP Lit | 部位マスク由来の金属度・滑らかさマップで `M_FoxGunner_Lit` を作って比較。陰影は自然になるが、影側が灰色に沈み（つや消しのソフビ人形風）、輪郭線・リム・毛や顔の暖かみは出せない |
| 2. Shader Graph | 足りない部分だけ拡張：`SG_Character_SoftToon`（Unlit、Keep Lighting Variants で影・追加ライト・Forward+ 対応）が `FoxSoftToonLighting.hlsl` を Custom Function で呼ぶ。部位ごとに巻き込みのある柔らかい陰影（段差は弱め）、顔と毛の境目の暖色、毛・布はつや消し、革は柔らかいつや、金属は色付きの反射、控えめなリム |
| 輪郭線 | `CH_Outline`（2 枚目のマテリアル、1.8 mm、線色は表面色を暗くしたもの）。変更前の 4 mm の黒線は薄い毛先から透けて黒い塊になっていた |

部位ごとの値（`FoxGunnerLookFactory.Presets`、柔らかさ / 滑らかさ / 金属度 / 暖かみ）：毛 0.85/0.12/0/0.55、顔 1/0.16/0/0.8、目 0.5/0.75/0/0、布 0.6/0.06/0/0.15、革 0.3/0.42/0/0、金属 0.15/0.62/0.85/0、ガラス 0.2/0.85/0/0。マテリアル側の全体調整は `M_FoxGunner_SoftToon`（Toon Blend 0.45、Toon Width 0.32、Rim 0.18 など）。

比較撮影（同じ照明：太陽・空の環境光・サンドボックスのポストエフェクト）：メニュー `CoopDeck > Art > Fox Gunner Look: Capture Comparison`、またはバッチの `FoxGunnerLookFactory.RunBatch`（`-coopdeckRebuildMask` でマスクも作り直す）。出力は `Screenshots/FoxLook_Compare.png`（上から変更前・URP Lit・Shader Graph 版）、`FoxLook_BeforeAfter.png`、`FoxLook_Sandbox_BeforeAfter.png`（ゲームの照明）、`FoxLook_Regions.png`（部位の判定）。

負荷（ROG Ally X、Handheld 画質、1280×720 を埋めるキツネ 9 体、`FoxGunnerLookFactory.BenchmarkBatch`）：キツネなし 3.16 ms、変更前 3.98 ms、URP Lit 4.17 ms、Shader Graph 版＋輪郭線 4.50 ms（1 体あたり変更前より約 0.06 ms 増）。自分の体は影だけ描画なので、通常のプレイでは影のパスしか動かない。

---

## 8. テクスチャの高品質化（均一な 4K、2026-11-08）

Meshy のテクスチャ（2048）は UV の島ごとに解像度がばらつき（面積加重で 5%点 333・中央 622・95%点 1,017 テクセル/m、約 3 倍の差）、ぼやけた部分と細かい部分が混在し、線画がギザギザ・ノイズが多かった。モデル・リグ・スキン・色は変えずに作り直した。

| 手順 | 内容 |
|---|---|
| 1. dilate | 島の外の空白に島の色を広げる（画像ピラミッドで埋める）。拡大 AI が島の縁を硬い境目として扱わないようにする |
| 2. upscale | Real-ESRGAN（`realesrgan-ncnn-vulkan`、BSD-3、モデル `realesrgan-x4plus-anime`）で 4 倍の 8192 に。内蔵 GPU のメモリに収めるため 512 px＋重なり 32 px のタイルに分けて実行し、中央部分だけをつなぐ（約 2 分）。ツール本体はリポジトリに入れない（公式リリース v0.2.5.0 の Windows 版を各自ダウンロード） |
| 3. bake | 同じ島のまま面積に比例して大きさをそろえ、4096 に詰め直す（新しい継ぎ目なし、島の間 8 テクセル）。旧 UV → 新 UV へ Cycles の EMIT ベイク（16 サンプル、余白 8 px）で色を移す。体を通した平均色は旧 0.594/0.413/0.355 → 新 0.599/0.413/0.355 |

結果：テクセル密度 859/1,074/1,264（5%/50%/95%、差 1.5 倍）。拡大 AI がぼやけた部分に線と面を補うので、全身が同じ細かさになり、線画は滑らか・面はノイズのない均一な塗りになった。
取り込みは 4096・BC7（`CompressedHQ`）・トライリニア・異方性 8（`FoxGunnerAssetFactory.ConfigureTexture`）、部位マスクは 2048 に上げた。

試して採用しなかったもの：
- メッシュの細分化（1 回で 92,100 三角形）＋高解像度メッシュへの吸着：Meshy のメッシュは 12,330 本の開いた辺（毛束・耳・服の重なった板）を持ち、細分化で隙間と割れが出た。今のメッシュは高解像度の形から平均 0.9 mm しか離れていないので、形の改善は小さい。
- 法線のなめらか化：隣の頂点との法線の差が平均 50°（薄い板の表裏が縁でつながる）で、平均化すると陰影が折れて面が目立った（向きのそろった隣だけでも同じ）。`--normal-smooth` は 0（変更なし）。

```
blender -b --factory-startup -P ArtSource/Tools/refine_character.py -- dilate --model ArtSource/FoxGunner/Export/FoxGunner.glb --out atlas_dilated.png
blender -b --factory-startup -P ArtSource/Tools/refine_character.py -- upscale --model x --esrgan <realesrgan フォルダ> --in atlas_dilated.png --out atlas_x4.png
blender -b --factory-startup -P ArtSource/Tools/refine_character.py -- bake --model ArtSource/FoxGunner/Export/FoxGunner.glb --upscaled atlas_x4.png ^
    --size 4096 --texture-out Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_BaseColor.png --glb-out ArtSource/FoxGunner/Export/FoxGunner_HQ.glb
```
`--check <dir>` を付けると旧・新の比較画像（顔・胴・背中・尻尾、テクスチャと灰色）だけを出し、書き出しはしない。
その後は `fox_anim` の順（`fox_common.EXPORT_GLB` が `FoxGunner_HQ.glb` を読む）：`debug_segment.py` → `fox_rig.py` → `build_clips.py` → `fox_fp_arms.py`（一人称の腕の UV と手袋の色も新しいテクスチャから取り直す）→ ブートストラップ → `FoxGunnerLookFactory.RunBatch -coopdeckRebuildMask`。

負荷（Handheld、キツネ 9 体）：キツネなし 2.45 ms、Shader Graph 版＋輪郭線 3.16 ms。テクスチャのメモリは約 2.7 MB（2K・DXT1）→ 約 21 MB（4K・BC7、ミップ込み）。

### 8.1 法線マップ（高精細モデルの陰影を移す、2026-11-08）

ゲーム用メッシュ（15,350 三角形）は Meshy の自動削減で硬い角が溶け、表面がでこぼこ（隣り合う頂点の法線の差が平均 50°）。230 万三角形の高精細モデルは形がくっきりしている（ゴーグルの面取り、ベルト・ポケット、ブーツのベルト、マフラーのしわ）。高精細モデルを単純に 4 万三角形へ削減しても同じくでこぼこだったので、頂点は増やさず、高精細の陰影を法線マップで移した。

- `refine_character.py normalmap`：高精細 GLB を外接箱で体に合わせ、Cycles で selected-to-active の接空間法線をベイク（4096、ケージ 4 mm・探索 8 mm）。別の層（毛束の下・板の裏）に当たった光線は 75° 以上傾くので、その画素（5.8%）は低ポリの法線に戻す（黒い点の除去）。出力 `T_FoxGunner_Normal.png`。
- Unity：法線マップとして取り込み（BC5、4096、トライリニア・異方性 8）。体の FBX は接線を MikkTSpace で計算（Blender のベイクと同じ）。`SG_Character_SoftToon` に `_NormalMap`（既定は bump）と `_NormalStrength`（既定 1）を追加し、Tangent→World の Transform ノードで世界空間にして `FoxSoftToonLighting.hlsl` に渡す。URP Lit の比較用マテリアルにも `_BumpMap`。
- 一人称の腕は体の UV を色にだけ借りている別のメッシュなので、法線マップのない `M_FoxGunner_SoftToon_FirstPerson`（`_NormalStrength` 0）を使う（`FoxGunnerAnimationFactory.AddFirstPersonArms`）。シェーダーは強さ 0・接線なし・NaN のとき元の法線を使う。
- トゥーンの陰影は明るい側がほぼ一定なので、効果は主に影の境目に出る。確認画像：`Screenshots/FoxLook_NormalMap_SideLight.png`（横からの低い太陽、上が法線マップなし・下があり）。輪郭（シルエット）は変わらない。
- 負荷（Handheld、キツネ 9 体）：キツネなし 2.95 ms、Shader Graph 版＋輪郭線 3.50 ms（法線マップなしのときと誤差の範囲）。メモリ約 +21 MB。

```
blender -b --factory-startup -P ArtSource/Tools/refine_character.py -- normalmap --model ArtSource/FoxGunner/Export/FoxGunner_HQ.glb ^
    --highres ArtSource/FoxGunner/Meshy/01_generate/FoxGunner_highres.glb --size 4096 ^
    --out Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_Normal.png [--check <dir>]
```
`--check` は低ポリ・低ポリ＋法線マップ・高精細を同じ照明で描いた比較画像を出す。Shader Graph は手で編集せず、生成スクリプト（作業用、テンプレートは Unity のサンプルから）で作り直した。

### 8.2 体を 4 万三角形で作り直す（2026-11-08）

Meshy の 15k リメッシュは形が溶けていたので、体そのものを高精細モデルから作り直した（`refine_character.py rebuild`）。§8 の均一テクスチャと §8.1 の法線マップは、この新しい体に対して焼き直している（`bake` / `normalmap` モードは 15k の体用として残す）。

| 手順 | 内容 |
|---|---|
| 削減 | 高精細（2,344,168 三角形）を外接箱で体に合わせ、Quadric collapse で 40,000 三角形（Blender で 18,814 頂点） |
| UV | Smart UV Project（66°）→ 島の大きさを面積比でそろえて 4096 に詰める（島の間 6 テクセル）。テクセル密度 915/1,007/1,076（5%/50%/95%） |
| スキン | 24 本の Meshy ボーンのウェイトを 15k の体から最寄り面の補間で移す（未割り当て 0）。尻尾・耳・指は `fox_rig.py` が新しいメッシュで作り直す |
| 色 | 15k の体（元 UV）＋ 8K の拡大アトラスから selected-to-active の EMIT ベイク。外れた光線（0.24%）は探索範囲を広げて再ベイク、残り（0.055%）は同じ島の隣から埋める |
| 法線マップ | 高精細から新しい体へ（`bake_normal_image`、75° 超の 4.6% は元の法線） |

```
blender -b --factory-startup -P ArtSource/Tools/refine_character.py -- rebuild --model ArtSource/FoxGunner/Export/FoxGunner.glb ^
    --highres ArtSource/FoxGunner/Meshy/01_generate/FoxGunner_highres.glb --upscaled atlas_x4.png --faces 40000 --size 4096 --margin 6 ^
    --texture-out Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_BaseColor.png ^
    --normal-out Assets/_Game/Art/Characters/FoxGunner/Textures/T_FoxGunner_Normal.png --glb-out ArtSource/FoxGunner/Export/FoxGunner_HQ.glb
```
その後 `fox_anim` の順（`debug_segment` → `fox_rig` → `build_clips` → `fox_fp_arms`）→ ブートストラップ → `FoxGunnerLookFactory.RunBatch -coopdeckRebuildMask`。

Unity では 40,000 三角形・52,554 頂点（毛束や重なった布が多く UV の島が細かいため、継ぎ目で頂点が分かれる）。継ぎ目を減らす展開（滑らかにした複製で島を決めて開く）も試したが、頂点は約 39k に減る一方で一部の島が大きく歪み（5%点で 20〜90 テクセル/m）、採用しなかった。

負荷（ROG Ally X、Handheld）：
- キツネ 9 体（`BenchmarkBatch`）：キツネなし 2.48 ms、Shader Graph 版＋輪郭線 4.32 ms（15k のときの約 3 倍、1 体あたり約 0.2 ms）。
- ゲーム（ビルド、`-perfrun 30`、プレイヤーの体は影だけ）：平均 59.98 FPS（上限 60）、1% low 58.4 FPS、GPU 2.60 ms、メインスレッド 3.58 ms。15k のとき（GPU 2.4 ms）との差は約 0.2 ms。
- 協力プレイで他のキツネが何体も見えるようになったら、遠景用の LOD（同じ UV・テクスチャのまま 15k へ削減）を検討する。

### 8.3 一人称の腕を高精細モデルから切り出す（2026-11-08）

一人称の腕は、三人称の体と同じ高精細スカルプトから切り出す（`fox_anim/fox_fp_arms.py`）。以前は前腕だけを切り出し、手はメタボールで造形し直した別物（`fox_fp_hands.py`、削除済み）で、腕も 1.1〜1.3 倍に伸ばしていた。今は手・指まで元のキツネで、骨の長さも体と同じ。動き・構図は `FOX_ANIMATION.md` §6。

| 手順 | 内容 |
|---|---|
| 切り出し | 高精細を `Export/FoxGunner.glb` に合わせ、体の腕・手・指の骨に 1.2 cm 以内で付く頂点を残す（上腕は付け根から 1/4 より先）。500 頂点未満の破片は捨て、40,000 三角形に削減（39,999 三角形、Blender で 20,082 頂点） |
| UV・色 | 4096 で開き直し、体（元 UV）＋ 8K の拡大アトラス（`FoxGunner_atlas_x4.png`）から色を焼く（ケージ 4 mm、光線 8 mm）。外れは §8 と同じく探索を広げて再ベイクし、残りを同じ島の隣から埋める |
| 法線・質感マスク | 法線は高精細から（§8.1 と同じ `bake_normal_image`）。SoftToon の質感マスクは体のマスク（RGB＋アルファ）から 2048 で焼く |
| スキン・骨 | 体の腕のウェイトを写す。指はメッシュのつながりで 5 本に分け、3 関節ずつのボーン（関節の前後 4 mm で混ぜる）。手のひらの向きの目印 `Palm`/`PalmOut`。計 41 ボーン |
| Unity | `FoxGunnerAnimationFactory` が取り込み（MikkTSpace の接線、メッシュ圧縮なし、ブレンドシェイプなし）。マテリアル `M_FoxGunner_SoftToon_FirstPerson` は体の SoftToon の数値を写し、テクスチャだけ一人称用（`FoxGunnerLookFactory.EnsureFirstPersonMaterial`）。Unity で 31,642 頂点 |

```
blender -b ArtSource/FoxGunner/Animation/FoxGunner_AnimRig.blend -P ArtSource/Tools/fox_anim/fox_fp_arms.py -- --faces 40000 --size 4096
```
出力：`Models/FoxGunner_FPArms.fbx`、`Textures/T_FoxGunner_FPArms_BaseColor.png` / `_Normal.png` / `_SurfaceMask.png`（`--check <フォルダ>` で指を曲げた確認画像）。

---

## 9. 未対応（次の作業）

- ~~三人称のライフルは素手のモーションに付いているだけ~~ → 2026-11-07 に構えのクリップ一式と両手 IK（`FOX_ANIMATION.md`）。
- ~~一人称の手は仮のカプセルのまま~~ → キツネの腕（高解像度メッシュから）に置き換え（`FOX_ANIMATION.md` §6）。2026-11-08 に手・指まで元のスカルプトから切り出し直した（§8.3）。
- ~~移動クリップは前進の歩き・走りだけ~~ → 2026-11-07 に Blender Python で 8 方向・ジャンプ・射撃・リロード・スキル・被弾・ダウン・蘇生など一式を制作（`FOX_ANIMATION.md`）。
- カメラの高さ（1.62 m）と CharacterController（1.8 m）は仮リグのまま。キツネは身長 1.6 m なので、頭の高さに合わせるかはゲームプレイの判断（変えていない）。
- ~~スプリントで走りが約 2.4 倍速~~ → 専用のスプリントクリップ（9.6 m/s）。
- ~~尻尾・耳にボーンがない~~ → 尻尾 4・耳 2・指 6 を追加し、尻尾・耳は手続き制御（`FOX_ANIMATION.md` §2・§6）。マフラーは未対応。
