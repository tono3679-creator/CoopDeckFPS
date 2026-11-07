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

## 6. 未対応（次の作業）

- 移動クリップは前進の歩き・走りだけ。横移動・後退でも前向きに走る（自分には影しか見えないが、オンラインでは他のプレイヤーに見える）。8 方向・ジャンプ・射撃・リロード・被弾・ダウン・蘇生のクリップが要る（`meshy_animate` で 1 本 3 クレジット、または既製モーション）。ダウン中も立ったまま。
- カメラの高さ（1.62 m）と CharacterController（1.8 m）は仮リグのまま。キツネは身長 1.6 m なので、頭の高さに合わせるかはゲームプレイの判断（変えていない）。
- スプリント（9.5 m/s）では走りが約 2.4 倍速になる。見た目を良くするなら専用のスプリントクリップ。
- 尻尾・耳・マフラーにボーンがない（腰・頭と一緒に動くだけ）。揺れが欲しければ Blender でボーンを足すか、揺れ物の仕組みを入れる。
- 武器（炎属性ライフル）は未作成。
