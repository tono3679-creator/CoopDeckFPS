# LIGHTNING_RABBIT — 雷属性ウサギ（2 体目のプレイアブルキャラクター）

> 2026-11-08 開始。キツネ（Fox Gunner）と同じ Meshy → Blender → Unity の流れ（`CHARACTER_PIPELINE.md`、`FOX_ANIMATION.md`）で作る。
> Meshy は有料。**生成の前に内容と必要クレジットを提示し、ユーザーの承認を得てから実行する。**
> 既存のキツネ（モデル・Prefab・Animator・テスト）は変更・削除しない。

## 0. 進捗

| Phase | 内容 | 状態 |
|---|---|---|
| 1 | 既存キツネと Unity 構成の調査 | 済（§5） |
| 2 | 外見仕様と生成計画の確定 | 済（ユーザー承認：Ultra あり 40 クレジット、耳先 1.70 m、SMG は後、マフラーはボーンで揺らす） |
| 3 | Meshy で高精細 3D モデル生成 | 済（2026-11-08、§3.1。生成・リギング・Blender での検査まで） |
| 4 | Blender で手・指・関節の検証と修正 | 済（Blender 上）：16 万三角形の体を作り直し（§3.2）、指・肘・膝・肩・耳・マフラー・尻尾を曲げて検査（§3.3） |
| 5 | Humanoid リギングとウェイト調整 | 済（Blender 上、§3.3）。Unity の Humanoid 設定は Phase 8 |
| 6 | 既存モーションのリターゲティング | 済（§3.5）：キツネの 47 クリップを Lightning SMG を持つウサギで作り直し、両手が SMG から外れない・SMG が体に入らないことを数値と画像で確認。Unity でも再生して確認（§3.6） |
| 7 | ウサギ専用の耳・ダッシュ動作 | 済（§3.5・§3.6）：ダッシュ（低く前傾し、SMG を前下へ突き出す）・着地・歩幅はクリップに、3 関節の耳・マフラーの 2 本の帯・尻尾は Unity のばね（`ChainSecondaryMotion`） |
| 8 | Unity Prefab・IK・一人称の腕 | 済（2026-11-08、§3.6）：体・SMG・一人称の腕を取り込み、起動時にキツネと差し替える仕組み（`-character LightningRabbit` / CoopDeck > Play As）。EditMode 273/273・PlayMode 74/74 成功、Editor の Play モードで一人称と三人称を撮影して確認 |
| 9 | マテリアルと雷属性 VFX | 済（2026-11-08、§3.7）：部位マスク（毛・顔・目・布・革・金属・ガラス）と発光マスクを Blender で作成、光る部分の複製メッシュを加算で重ね、呼吸・ダッシュ・詠唱で強弱。グローブの火花・ダッシュの残像・耳の放電。VFX Graph ではなく Particle System と手書きシェーダー（火球と同じ判断）。EditMode 283/283・PlayMode 76/76、Editor の Play モードで撮影・負荷計測 |
| 10 | 実ゲーム内の動作確認・撮影（正面・側面・背面・戦闘姿勢・一人称） | 済（2026-11-08、§3.8）：Play モードで正面・左右・背面・斜め前・戦闘（帯電連射の雷撃・詠唱・ダッシュの残像）・一人称を撮影。Release ビルドで `-character LightningRabbit` の自動プレイ（移動・射撃・スキル・敵の波 4）を 3 回、キツネと比べて GPU 約 +0.1 ms |

新しいスキル（連鎖雷撃の強化版・雷球・雷撃ダッシュなど）は別フェーズ。この作業では外見・リグ・アニメーション・Unity 統合までを扱う。

---

## 1. キャラクター仕様

| 項目 | 内容 |
|---|---|
| 名前（仮） | ライトニングラビット（資産名 `LightningRabbit`） |
| 属性 | 雷（`Element.Lightning`） |
| 戦闘タイプ | 高速移動・感電・連鎖攻撃 |
| 性格 | 自信家で少しいたずら好き。素早く敵を翻弄する |
| 武器 | コンパクトな未来的 SMG（三面図には含まれない。§7 で扱いを確認） |

### 1.1 外見（ユーザー提供の三面図より）

| 部位 | 内容 | 位置（キャラクターから見た左右） |
|---|---|---|
| 毛並み | 白〜薄い灰色。頬に黄色い稲妻の模様 | — |
| 耳 | 長く直立。内側はピンク、黄色のギザギザ模様 | 左右 |
| 目 | 大きな琥珀色 | — |
| 髪 | ふわっとした前髪と後ろ髪、頭頂に毛束 | — |
| 通信装置 | 耳の根元のヘッドセット（稲妻マークの円盤、ぶら下がる小型バッテリー）。後頭部にベルト | **左**耳 |
| 上着 | クリーム色の短いベスト（黄色の縁、肩に黄色のバンド）＋黒のクロップトップ（胸に青白く光る稲妻） | — |
| マフラー | 黄色。先端に光る稲妻。体の**右**側へなびく | 右 |
| グローブ | 黒の指なしグローブ、甲に光る稲妻、厚い手首バンド | 左右 |
| 腰 | 茶色の革ベルト。黄色のポーチ（右）、光るバッテリー缶（左） | 右・左 |
| ズボン | 黒のバギーパンツ、黄色の稲妻の柄、裾を絞る | — |
| ブーツ | 黒×灰×黄の装甲ブーツ、すねに光る稲妻 | — |
| 尻尾 | 白い丸い尻尾 | — |

三面図の左右の配置は 3 枚で一致している（ヘッドセットは左耳、ポーチは右・バッテリーは左、マフラーは右へ）。側面図ではポーチとバッテリーが両方とも手前（左）に見える小さな食い違いがある。

### 1.2 キツネとの見分け（シルエット）

| | キツネ（Fox Gunner） | ウサギ（Lightning Rabbit） |
|---|---|---|
| 頭 | 短い三角の耳（全身の約 10%） | 長く直立した耳（全身の約 19%） |
| 尻尾 | 大きな筆形、地面近くまで垂れる | 小さな丸い尻尾 |
| マフラー | 赤、背中に垂れる | 黄、横へなびく |
| 色 | 橙・赤・黒、炎の橙の発光 | 白・黄・黒、青白い発光 |
| 体格 | 耳を除く身長 約 1.44 m | 耳を除く身長 約 1.38 m（やや小柄） |

### 1.3 大きさ（三面図の正面から測定）

- 耳の先から足の裏まで 1,361 px。耳は上から約 19%、目の高さは足から約 69%。
- 耳を除く身長を 1.38 m（キツネより約 6 cm 小柄）とすると、**耳の先まで約 1.70 m**、目の高さ約 1.17 m（キツネは約 1.23 m）。
- Meshy の自動リギングには耳の先までの高さを渡す（`height_meters: 1.70`）。Blender で縮尺を変えられるので、後から調整できる。
- カメラの高さ（1.62 m）・CharacterController（1.8 m）は仮リグのままで、キツネでも変えていない。ウサギでも変えない（ゲームプレイの判断）。

---

## 2. 参照画像

| ファイル | 内容 |
|---|---|
| `ArtSource/LightningRabbit/Reference/Original/rabbit_sheet_{front,side,back}.webp` | ユーザー提供の原画（1086×1448） |
| `ArtSource/LightningRabbit/Reference/rabbit_{front,side,back}.png` | Meshy 用の正方形（1441〜1448 px）。縮小せず、背景色で左右を埋めて体を中央に置いた |

```powershell
$b = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
$r = 'C:\dev\CoopDeckFPS\ArtSource\LightningRabbit\Reference'
foreach ($v in 'front','side','back') {
  & $b -b --factory-startup -P ArtSource\Tools\prepare_reference.py -- --in "$r\Original\rabbit_sheet_$v.webp" --out "$r\rabbit_$v.png"
}
```
`prepare_reference.py` は縁の色の中央値を背景とし、体の外接枠を出力する（大きさの測定に使った）。パスは絶対パスで渡す（Blender は相対パスを .blend 基準で読む）。

---

## 3. Meshy 生成（2026-11-08 実行済み）

キツネで実績のある「複数画像から 3D」を使い、ゲーム用の体は高精細モデルから Blender で作り直す（`refine_character.py rebuild`、キツネと同じ 40,000 三角形）。

| 処理 | 設定 | クレジット |
|---|---|---|
| Multi-Image to 3D | 正面・側面・背面の 3 枚（正面が先頭）、`ai_model: meshy-7.1`、`texture_model: meshy-7`、`texture_resolution: 4k`（2K と同額）、`geometry_resolution: 2k`（Ultra、+5）、`pose_mode: t-pose`、`should_remesh: true`・`target_polycount: 30000`、`save_pre_remeshed_model: true`（高精細マスター）、`remove_lighting: true`、`image_enhancement: false`、`enable_pbr: false`、テクスチャ指示文（下） | **35** |
| Rig | `height_meters: 1.70`（歩き・走り込み） | **5** |
| 合計 | 残高 1,576（2026-11-08 確認）→ 約 1,536 | **40** |

- Ultra をやめると 30＋5＝35。Ultra は細部（指・耳の縁・ヘッドセット）の形を保つために提案している。
- 4K テクスチャは 2K と同額。キツネでは 2K を Real-ESRGAN で 4 倍にしていたが、元が 4K なら拡大の補完に頼る部分が減る。
- 高精細マスター（キツネは 234 万三角形）は `ArtSource/LightningRabbit/Meshy/01_generate/` に保存し、ポリゴン削減はゲーム用の体を作るときだけ行う。
- 追加モーション（`meshy_animate`、1 本 3）は使わない。モーションはキツネと同じ Blender の手続き生成で作る。
- 生成に失敗した、または形が使えない（耳がくっつく、マフラーが体に埋まる等）ときは、再生成の前に報告して再度承認を得る。

テクスチャ指示文（案、800 文字以内）：

> Stylized anime toon game character, hand-painted cel-shaded look with clean flat colour areas and crisp dark linework, no photorealistic fur. Bipedal rabbit with soft white to light grey fur, long upright ears with pink inner ear and yellow zigzag lightning stripes, big amber eyes, small yellow lightning marks on the cheeks. Cream-white cropped vest with yellow trim over a black crop top with a glowing cyan-white lightning emblem, yellow scarf with a glowing lightning tip, black baggy pants with yellow lightning stripes, brown leather belt with a yellow pouch and a glowing cyan battery canister, black fingerless gloves with a glowing lightning emblem on the back, black and yellow armoured boots. Even lighting, no baked shadows or highlights.

### 3.1 結果（Phase 3）

| 項目 | 結果 |
|---|---|
| タスク | 生成 `01a1b765-3df4-71bb-a822-8fdebbcb0abd`（35 クレジット）、リギング `01a1b769-8cf8-7304-8150-5dca00981843`（5）。残高 1,576 → **1,536** |
| 高精細マスター | `Meshy/01_generate/LightningRabbit_highres.glb`：939,370 三角形・469,587 頂点、**穴のない 1 つの閉じたメッシュ**（開いた辺 0）。色なし（形のみ） |
| ゲーム用の下地 | `LightningRabbit_mesh.glb` / `.fbx`：30,862 三角形、4K の基本色 `LightningRabbit_mesh_base_color.png`（UV は細かい島のつぎはぎ。キツネと同じ） |
| リグ | `Meshy/02_rig/LightningRabbit_rigged.glb` / `.fbx` と歩き・走り：**24 ボーン（キツネと同じ名前）**、身長 1.70 m、ウェイトのない頂点 0、1 頂点最大 4 ボーン。指・耳・尻尾のボーンはない |
| 手 | **左右とも 5 本の指（親指を含む）が分かれて生成された**（キツネは指がくっついた平たい手だった）。指なし手袋が手のひらと指の付け根を覆い、指先は素肌。指先がやや尖っている（三面図は丸い）。30k の下地は指が角張り、指先のテクスチャが一部にじむ → 高精細から作り直す体で解消する見込み |
| 耳 | 左右が独立し、内側がくぼんだ形。ヘッドセットは左耳の根元、後頭部にベルト |
| マフラー | 横へ張り出した板ではなく、**右側に膝まで垂れる 2 本の帯**として生成。Meshy の走りでは腕・脚に引っぱられて揺れる（専用ボーンが要る） |
| 尻尾 | 丸い尻尾あり |
| 色 | テクスチャの毛色は三面図どおりの暖かい白。検査画像が灰色っぽいのは Blender の簡易照明のため |

検査画像：`ArtSource/LightningRabbit/Inspection/`（`views.png` 四面＋斜め、`anim_*.png` 歩き・走り、`01_closeups/closeup_{hand_R,hand_L,head}.png`：上から 30k の色・30k の形・高精細の形）。三面図との比較：`Screenshots/Rabbit_Meshy_vs_Reference.png`。

```powershell
# 拡大検査（手・頭。複数のモデルを同じ枠で並べる）
& $b -b --factory-startup -P ArtSource\Tools\render_closeups.py -- --model <30k.glb> --model <highres.glb> --out <dir> --region hands --region head
```

---

## 3.2 ゲーム用の体の滑らかさ（Phase 4、2026-11-08）

ユーザーの指示「極力滑らかに」により、キツネの 4 万三角形ではなく、近距離用に 16 万三角形の体を作り、距離で切り替える（LOD）。

高精細マスター（939,370 三角形）を各三角形数に削減して比較した（`ArtSource/Tools/decimation_study.py`、`Inspection/02_decimation/`）。

| 三角形 | 頂点（Blender） | 高精細からのずれ 平均 / 99%（頭・耳） | 同（手） | 隣り合う面の角度 中央値 / 90% |
|---|---|---|---|---|
| 40,000 | 19,902 | 0.19 / 0.71 mm | 0.27 / 0.83 mm | 17.5° / 69.9° |
| 80,000 | 39,902 | 0.11 / 0.40 mm | 0.15 / 0.42 mm | 15.0° / 53.6° |
| 120,000 | 59,902 | 0.085 / 0.30 mm | 0.11 / 0.33 mm | 13.6° / 46.3° |
| 160,000 | 79,902 | 0.068 / 0.24 mm | 0.084 / 0.24 mm | 12.6° / 42.2° |
| 高精細 | 469,587 | — | — | 7.5° / 29.1°（服のしわ・毛束の折り目を含む） |

- 形のずれはどれも 1 mm 未満だが、陰影の滑らかさは三角形数で目に見えて変わる。顔を 30 cm 幅で写すと（`closeup_face.png`、上から 高精細・16 万・12 万・8 万・4 万）、4 万は目の縁が消えて表面がまだら、8 万はヘッドセットの円盤がでこぼこ、16 万で高精細とほぼ同じ。
- 構成：**LOD0 16 万**（近距離）・LOD1 4 万・LOD2 約 1.5 万。同じ UV・テクスチャ・マテリアルを共有する。LOD はボーン追加とウェイトの後に LOD0 から削減して作る（Phase 5）。
- 自分の体は影だけ描画するので LOD1 に固定する予定（Phase 8）。他のキャラクターとして見えるときだけ LOD0 を使う。
- 負荷の見込み：キツネの実測（4 万で 1 体約 0.2 ms、三角形数にほぼ比例）から、LOD0 が画面いっぱいに 1 体で約 0.8 ms。Phase 8 でこの PC（ROG Ally X）で実測する。

作り直しの結果（`ArtSource/Tools/rebuild_body.py` → `refine_character.py rebuild` をそのまま実行し、体の名前だけ `LightningRabbit_Body` に）：

| 項目 | 結果 |
|---|---|
| 体 | 160,000 三角形・79,902 頂点（Blender）。Unity では UV の継ぎ目で分かれて約 142,700 頂点（65,535 を超えるので 32 ビットのインデックスに自動で切り替わる） |
| ウェイト | Meshy の 24 ボーンから転写、ウェイトなし 0、1 頂点最大 4 |
| UV | 島の大きさをそろえて 4096 に詰めた。テクセル密度 1,306 / 1,449 / 1,541（5% / 50% / 95%、テクセル/m。キツネは 915 / 1,007 / 1,076）。アトラスの使用率は 35.5%（島が細かく、島の間 6 テクセルの余白が多い。詰め方を改善すれば密度を上げられる） |
| 色 | 30k の体と Meshy の 4K テクスチャから焼き直し。光線が届かなかったテクセル 0.06%（同じ島の隣の色で補完） |
| 法線マップ | 高精細から 4096 で焼き込み。低ポリとの傾きの中央値 4.3°、75° を超えて平らに戻したテクセル 0.8% |
| LOD の試算 | 16 万の体を削減すると、4 万で約 46,000 頂点・1.5 万で約 19,000 頂点（Unity 換算）。本番の LOD はボーン追加後に継ぎ目を溶接した状態から作る |

出力：`ArtSource/LightningRabbit/Export/LightningRabbit_HQ.glb`、`T_LightningRabbit_BaseColor.png`、`T_LightningRabbit_Normal.png`。比較画像：`Inspection/03_rebuild/closeup_*.png`（上から 16 万の色・16 万の形・30k の色・30k の形）。頭頂や指先に、元の Meshy テクスチャ由来の小さな暗い点がいくつかある（30k 版にも同じ点がある）。

---

## 3.3 リグとウェイト（Phase 4〜5、2026-11-08）

`ArtSource/Tools/rabbit/`（キツネの `fox_anim` は変更せず、FBX の書き出し設定だけ `fox_common` から読む）：

```powershell
$b = 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe'
& $b -b --factory-startup -P ArtSource\Tools\rabbit\rabbit_segment.py -- --debug <dir>   # 部位の切り分けの確認画像
& $b -b --factory-startup -P ArtSource\Tools\rabbit\rabbit_rig.py -- --check <dir>      # ボーン・ウェイト・曲げ検査
& $b -b --factory-startup -P ArtSource\Tools\rabbit\rabbit_rig.py -- --export           # .blend と LOD 入り FBX
```

| 追加ボーン | 親 | 内容 |
|---|---|---|
| `<Side>HandThumb1-3`・`Index1-3`・`Middle1-3`・`Ring1-3`・`Pinky1-3` | `<Side>Hand` | 指 1 本ずつ 3 関節（片手 15 本、Humanoid の指に対応。Thumb1 は中手骨） |
| `Ear_L1-3` / `Ear_R1-3` | Head | 耳の根元から先まで 3 本（Unity で手続き制御、クリップはキーを持たない） |
| `Tail01-02` | Hips | 丸い尻尾 |
| `Scarf_F1-4` / `Scarf_B1-4` | Spine（UpperChest） | 右側に垂れるマフラーの前後 2 本の帯 |
| `Weapon`・`IK_Hand_L/R`・`IK_Elbow_L/R` | Spine / Weapon | キツネと同じ銃の座標系と手の IK 目標（クリップがキーを打つ） |

合計 75 ボーン（変形用 70）。元の 24 ボーンと T ポーズは変えていない。

部位の切り分け（`rabbit_segment.py`、16 万の体は 1 つにつながった閉じたメッシュ）：
- 耳：頭のウェイトで高さ 1.37 m より上を、左右それぞれ先端から広げる。左耳の根元のヘッドセット（暗い色、1.42 m 以下）は頭に残す。左 1,012・右 778 頂点。
- 尻尾：腰の後ろの毛色の部分を一番後ろの点から広げる（1,363 頂点）。
- マフラーの帯：右側・首の結び目より下のマフラーの黄色で、縦 25 cm 以上の塊（ポーチ・靴底の黄色を除く）。2 本は上でつながっているので分かれる高さで切る。先端の稲妻模様や縁取り（黄色以外）は帯に囲まれた部分として取り込む（取り込む前は Meshy の腕・太ももウェイトのまま残り、曲げると長い筋に伸びていた）。前 2,189・後 1,758 頂点。
- 指：手首からの距離で手を輪切りにし、遠い位置から順に 1 本ずつ分かれる塊を指として取る（指の間は水かきでつながっている）。分かれた部分に軸を合わせ、付け根の関節まで延長する（関節は指の長さの 45%・75%、親指は 42%・70%）。水かきは近い 2 本の指に距離の逆 2 乗で配分。

ウェイト：各鎖の範囲で関節ごとに滑らかに切り替え、根元は親に混ぜる（耳→頭、尻尾→骨盤、帯→上胸。尻尾と帯は Meshy の太もも・腕のウェイトを捨てる。キツネで尻尾が太ももに引っぱられた教訓）。指の付け根の帯（片手 700〜900 頂点）だけウェイトを 4 回ならす。1 頂点最大 4 ボーン、ウェイトのない頂点 0。

曲げ検査（`Inspection/04_rig/deform_check.png`・`deform_closeups.png`、灰色の形のみ）：
- 腕を下ろす（65〜70°）・肘 125°・股関節 75°＋膝 110°・背骨のひねり 25°・頭の回転 35°・耳を後ろへ・帯を後ろへ・尻尾を上へ・握りこぶし・銃のグリップ程度の握り。
- 修正したもの：①帯の模様・縁取りが腕と脚に引っぱられて筋状に伸びた → 帯に取り込んで解消。②握ると指の間に細い裂け目 → 水かきの配分と付け根のならしで解消。
- 残っているもの：指を 185° まで曲げた極端なこぶしでは指先の爪が手のひらにめり込む（握りの程度では起きない）。親指の握りは「下へ曲げる」だけの検査で、銃のグリップを包む向き（対向）は Phase 6 の構えで確認する。

出力：
- `ArtSource/LightningRabbit/Animation/LightningRabbit_Rig.blend`（作業用リグ、ウサギは -Y 向き）
- `ArtSource/LightningRabbit/Export/Unity/LightningRabbit.fbx`（Unity 用に 180° 回した +Z 向き。`LightningRabbit_Body_LOD0/1/2` = 160,000 / 40,000 / 15,000 三角形、79,902 / 19,902 / 7,402 頂点、同じ UV・ウェイト）。LOD は名前の `_LOD0..2` で Unity が LOD グループを作る。`Assets/` にはまだ入れていない（Phase 8）
- 確認画像 `Inspection/04_rig/segments.png`（部位の色分け）・`lods.png`（LOD ごとの形）

---

## 3.4 武器：Lightning SMG（2026-11-08）

ユーザー提供の設定画「雷のウサギアタッカー」（`ArtSource/LightningSMG/Reference/Original/thunder_rabbit_sheet.png`）の「メインウェポン（サブマシンガン）」から作った。設定画のキャラクターの衣装は今のウサギと違うが、使ったのは武器だけ。要望：一人称で見てもジャギーやテクスチャの荒さが見えない高品質なモデルとテクスチャ。

| 手順 | 内容 |
|---|---|
| 参照画像 | 設定画から側面・正面・上面・背面を切り出し（元は側面 382×184 px、正面・背面 約 66×186 px）、Real-ESRGAN（`realesrgan-x4plus-anime`）で 4 倍にして正方形に（`ArtSource/Tools/crop_reference.py`、`Reference/{side,front,top,back}.png`） |
| Meshy | Multi-Image to 3D（側面を先頭に 4 枚）、meshy-7.1 + meshy-7、**4K テクスチャ・Ultra 形状**、リメッシュ 3 万三角形、高精細マスター保存。タスク `01a1b8ef-549f-73df-a1ed-549e6f2ea04b`、**35 クレジット**（残高 1,536 → 1,501、ユーザー承認済み） |
| 高精細マスター | `Meshy/LightningSMG_highres.glb`：427,470 三角形、穴のない 1 つの閉じたメッシュ |
| 向き・大きさ | `prepare_prop_for_unity.py`：銃口 -X を前へ、**全長 0.52 m**（ウサギの腕の短さに合わせた。後で変えられる）、原点はピストルグリップの上寄り。ソケット `Muzzle`（グリップから前 37 cm・上 9.7 cm）、`LeftHandGrip`（縦のフォアグリップ、前 23.3 cm・上 1.4 cm） |
| 作り直し | `ArtSource/Tools/rebuild_prop_hq.py`：共有の `refine_prop.py` を変更せずに呼び、元テクスチャの拡大だけ差し替え（4K を Real-ESRGAN で 4 倍にしてから 2×2 で平均し 8K の焼き込み元にする。`refine_prop.py` の 4 倍拡大だと 16K・約 3 GB になるため）。高精細を **10 万三角形**へ削減、40° 以上の角は硬いまま、均一 UV、色と法線マップを 4096 で焼く。遠景用に **LOD1（3 万三角形）** |
| 三角形数の決め方 | 高精細・10 万・6 万・3 万を一人称と同じ縮尺で拡大比較（`Inspection/01_decimation/`）。3 万はエネルギー窓の内側や稲妻の彫りがぼやけ、照準レールが角ばる。10 万は高精細とほぼ同じ |
| テクスチャ | テクセル密度 4,999 / 5,609 / 5,793（5% / 50% / 95%、テクセル/m。Fire Rifle は約 4,000）、アトラス使用率 40%。光線の外れ 2.2% は同じ島の隣の色で補完。法線マップは低ポリとの傾きの中央値 2.0°、平らに戻したテクセル 0.6% |

一人称の確認（`ArtSource/Tools/fp_view_check.py`、`Inspection/03_fp/`）：キツネの一人称の構図（グリップが目から右 15 cm・下 20 cm・前 39 cm、縦の画角 70°、1920×1080、アンチエイリアス 8 倍）で描き、等倍で切り出して Meshy の元と比較。
- テクスチャの画素の粗さは見えない。画面上の細かさ（目から 39 cm で約 2,000 px/m、ストックの 24 cm で約 3,200 px/m）に対してテクスチャは 5,600 テクセル/m で、どこでも 1 画素に 1.7 テクセル以上ある。
- 形は作り直した方が照準・レール・銃口の穴の角がくっきりしている。
- この構図だとストックが目の前 24 cm で画面の大きな部分を占める。一人称の配置（銃の位置・向き）は Unity に入れるときにウサギ用に調整する。
- ゲーム内のジャギーは Unity 側の設定にも左右される（High 画質は MSAA 4 倍、テクスチャは BC7・ミップマップ・異方性 8、一人称用の細い輪郭線）。Unity での確認は Phase 8。

出力（Unity への取り込み前）：`ArtSource/LightningSMG/Export/Unity/LightningSMG.fbx`（`LightningSMG_Body_LOD0/1` とソケット 2 つ、Unity 用に +Z 向き）、`T_LightningSMG_BaseColor.png`・`T_LightningSMG_Normal.png`（4096）、`Export/LightningSMG_HQ.glb`、8K の焼き込み元 `Export/LightningSMG_atlas_x2.png`。

**2026-11-09 作り直し**：Fire Rifle と同じ `rebuild_weapon_hq.py`（55k＋1.0 mm ベベル＋面積重み付き法線、LOD0 91,290）と武器シェーダー `CoopDeck/Weapon/HardSurfaceToon`（材質マスク：黄橙の塗装・紺の樹脂・水色のガラス、エッジハイライト）に置き換えた。ソケットは同じ。`WEAPON_VISUAL_QUALITY.md` §7。

## 3.5 モーション：Lightning SMG を持つ全クリップ（Phase 6〜7、2026-11-08）

キツネの 47 クリップ（`fox_anim/fox_clips.py`、読むだけで変更しない）を、ウサギのリグと Lightning SMG で解き直した。キツネの数値で合わない所だけをウサギ側で差し替える。

| 項目 | 内容 |
|---|---|
| 体格 | キャラクター空間の距離を肩の高さの比 0.93 で縮める（`rabbit_motion.install_scale`）。銃の上の点は銃の座標のまま |
| 握り（`rabbit_weapon.py`） | SMG を 1 cm ごとに実測：ピストルグリップは 17° 傾き・幅 3.4 cm、縦のフォアグリップは中心が前 23.3 cm・幅 3 cm、マガジンは前 14〜16 cm、照準線はグリップの 15 cm 上。右手はピストルグリップ、左手はフォアグリップを、どちらも拳銃の握り方で持つ。ウサギの手袋の手のひらは厚い（骨から 3〜5 cm、キツネは 1.4 cm）ので、手のひらの位置を握りの側面から 2〜2.7 cm 離し、指の曲げを 0.8 にした（握りが手に入るのは 1 cm 程度、指は握りを貫通しない） |
| 構え | 肩付けはしない。ウサギは頭・頬の毛・マフラーが肩の前を埋めていて、肩に当てるとストックが胸と頬に入る。SMG はお腹の高さで右寄り（グリップ：右 0.18・前 0.22・上 0.88 m、縮尺前）、銃口を 12° 内向き、右肘を外へ張り、ストックは右腕の下に通す。位置は格子探索で決めた（両手が届く・ダッシュや着地で頭が前に落ちてもストックに当たらない・体に入らない） |
| 腕の短さ | 左腕が伸び切らないよう、銃口を内向きにし、左へのストレイフ・後退では胸を開かず SMG を 2 cm 左肩へ寄せる。残りは解き方の補正：手が握りに届かない分だけ SMG（両手の握りごと）を手の方へ寄せて解き直す。寄せた量はほとんどのクリップで 2.5 cm 以下 |
| 体への食い込み | 解き方の補正をもう一つ：SMG が頭・首・胸・肩・上腕・腰・脚に入ったら（SMG の点が体の中、体の点が SMG の中の両方を検査）、その深さ＋6 mm だけ押し出して解き直す。キツネが銃を顔へ引き寄せる動き（インスペクト・スライド・ダッシュ）は、ウサギ用に銃の動きを前・下へ変えた（`GUN_SCALE`） |
| ADS | ウサギは照準を目に合わせられない（照準を目の前に置くと、頭が大きく首が短いのでストックが頬と頭を貫通する）。ウサギの ADS は「レイズドレディ」：SMG を右頬の横まで上げ、まっすぐ前へ向ける（照準線は目の約 5 cm 下）。食い込みなしで両手が届く一番高い位置を探索で決めた。ゲームに ADS の入力はまだ無い |
| 倒れ・死亡 | キツネは銃を膝の上に横たえ、長い左腕で床を突く。ウサギでは SMG が太ももに入り、左手が床から 6 cm 浮くので、座った姿勢では SMG を胸の前で前へ向けて持ち、左手は腰の近くの床に置く。死亡は、仰向けに倒れる途中で右手が離れ、SMG は右腰の横の床に残る（寝た姿勢では腕がキツネの位置まで届かないため） |
| 歩き・走り | ゲームの速度（1.5 / 6.5 / 9.5 m/s）のまま、周期を短く・足を高く上げて軽快に（歩き 0.52 秒、走り 0.42 秒、全力 0.38 秒） |
| ウサギ専用（Phase 7） | ダッシュは体を 6 cm 低く 10° 前へ倒し、SMG を前下へ突き出す。着地はキツネの 70% の時間。Skill_QuickCast は、スキルエフェクト制作セッションが作り直したキツネの版（0.12 秒で突き出す）をそのまま使う（腕が短いので、突き出した手のひらは目標の数 cm 手前で止まる）。耳・マフラー・尻尾はクリップに入れず、Unity でばねで動かす（Phase 8） |

確認（`ArtSource/Tools/rabbit/rabbit_check.py`、`Inspection/05_motion/`）：
- 全 47 クリップ・全フレームで、SMG を持っている間の手の誤差 0.0 cm（`rabbit_build_clips.py` の報告）。
- SMG と体の食い込み（手と前腕を除く）を全クリップ 10 コマずつ走査：47 クリップ中 44 は 5 mm 未満（2026-11-08 に Sprint を修正：キツネの全力疾走の持ち方は銃口を 22° 下げるが、ウサギは右腕の下にストックを挟むので、それだとストックの端が上腕に最大 21 mm 入っていた。ウサギだけ 13° にして解消。10〜16° ならどれも食い込まない）。残りは Revived・Rifle_Inspect（毛先 6〜8 mm）。Death は 2026-11-08 に修正：膝の上から床へ SMG をまっすぐ移していたので倒れ始めにグリップが右腿へ 8〜11 mm（3 コマ）入っていた。倒れる途中で SMG を外・前・上へ弧を描いて逃がし（`DEATH_ARC`）、残りは着地で尻尾の毛が床の SMG に触れる 6〜7 mm（`rabbit_check.py timeline` で書き出すコマごとに確認）。
- 構えを 4 方向と手の拡大で、主なクリップをコマ送りで描いて目視（`stance*.png`、`clip_*.png`、全クリップは `clips/`）。

出力：`ArtSource/LightningRabbit/Export/Unity/A_LightningRabbit_Set.fbx`（アーマチュアと 47 テイク）、`A_LightningRabbit_Clips.json`（キツネと同じ形式のクリップ表）、`Animation/LightningRabbit_Clips.blend`。Unity には §3.6 で取り込んだ。

## 3.6 Unity への取り込みとキャラクターの差し替え（Phase 7〜8、2026-11-08）

**キャラクターの選び方**：選択画面はまだ無い。
- ビルドでは起動引数 `-character LightningRabbit` で選ぶ。
- Editor ではメニュー CoopDeck > Play As > Lightning Rabbit で選ぶ（EditorPrefs に保存）。
- 何も選ばなければキツネ。PF_Player はキツネのまま作り、ウサギを選んだときだけ、開始時（他の部品が起動する前）に体・持つ武器・一人称の腕と武器をウサギのキットに差し替える。
- 移動・体力・武器の性能・デッキなどゲームプレイの部品は差し替えない。SMG の射撃性能はキツネのライフルと同じ。
- PlayMode テストはアセンブリ全体でキツネに固定する（`CharacterSelectionPin`）。Play As の選択で既存テストの結果は変わらない。

| 部分 | 内容 |
|---|---|
| 新しいアセンブリ `CoopDeck.Characters` | `CharacterDefinition`（ScriptableObject：id・表示名・属性・キット。`ScriptableObjects/Characters/CD_FoxGunner`・`CD_LightningRabbit`）、`CharacterKit`（体と一人称部分のプレハブ）、`CharacterSelection`（起動引数と Editor の選択）、`PlayerCharacterSwap`（PF_Player に付く差し替え役）。参照は Core・Animation・Weapons・Player・Skills（`ARCHITECTURE.md` §2） |
| 体 `PF_LightningRabbit_Body` | `LightningRabbit.fbx`（LOD0 160,000・LOD1 40,000・LOD2 15,000 三角形、75 ボーン、Humanoid はキツネと同じ Meshy のボーン対応）、4K の色と法線マップ。マテリアルはキツネの調整済み SoftToon と輪郭線を複製し、ウサギのテクスチャに替えたもの。部位マスク（毛・布・革・金属・発光の質感）は Phase 9 で作るまで、全体を「毛」の値にしている |
| クリップ | `A_LightningRabbit_Set.fbx` の 47 クリップを Humanoid で取り込み、`AC_LightningRabbit_TP`（キツネと同じ 4 レイヤーとパラメータ）を作る。耳・マフラー・尻尾はクリップから外す（ばねが動かす）。ウサギの 5 本指の関節は上半身レイヤーに入る |
| 手の IK・視線・ばね | キツネと同じ手の IK（`IK_Hand_*`）と視線（目の高さ 1.26 m）。尻尾 2 ボーンはキツネの `FoxSecondaryMotion`。3 関節の耳 2 本とマフラーの帯 2 本は新しい `ChainSecondaryMotion`：耳はぴくっと動き、走ると後ろへ倒れ、被弾で伏せ、詠唱で立ち、旋回に遅れてついてくる。マフラーは彫刻の形より 45% 下へ垂れ、走ると後ろへなびき、落下で浮き、旋回で振れる |
| SMG `PF_Weapon_LightningSMG` | LOD0 100,000・LOD1 30,000 三角形、4K の色と法線マップ。水色のエネルギー窓だけを発光させる（オレンジは塗装なので光らせない）。三人称と一人称（細い輪郭線）の 2 つのマテリアル |
| 一人称の腕 | `rabbit_fp_arms.py`：ウサギの彫刻（93 万三角形）から腕・手袋・5 本の指を切り出して 4 万三角形、4K の色（Meshy の 4K を 8K に拡大して焼く）と法線マップ。指の骨とウェイトは体のものをそのまま使い、骨の長さも体と同じ。動かし方はキツネと同じ `FirstPersonArms` |
| 一人称の配置 | ウサギの実際の目は頭の骨の 18 cm 前にあり（大きな頭・短い首）、そのままだと肩がカメラの後ろになって短い腕が SMG に届かない。一人称用の目はキツネと同じく頭の骨の真上に置いた。SMG のグリップは目から右 13 cm・下 25 cm・前 33 cm。銃口の向きは最初 10° 左（照準側）に向けていたが、銃身が照準の左を向き、照準へ飛ぶ弾道と約 30° ずれて見えたため（2026-11-08、ユーザー指摘）、キツネと同じ −1°・下向き 0° にした（`CharacterPrefabBuilder.ViewModelRifleYaw/Pitch` を共有）。銃身の線が照準に向かい、弾道が銃身から出て見える。この向きでも両手は目標に届き（ずれ 0.0 cm）、左手首は画面内、左肘は画面の下端より下（`FirstPersonHands_HoldTheSmg_InsideTheView`）。最初の配置は Play モードで 4 案を撮影し、左肘が画面の下端より下にある案を選んだ。SMG の後ろ側が画面右下を占めるのは、キツネのライフルと同じ見え方 |
| 共有ツールの変更 | `FoxGunnerAnimationFactory` のパスと一人称の設定を引数にした。キツネの値をそのまま既定にしたので、キツネの生成物（マスク・体のプレハブ・アート）は差分なしを確認。`CharacterPrefabBuilder` に `BuildCharacterKit` と、PF_Player への差し替え役の追加 |

確認：
- EditMode 273/273、PlayMode 74/74 成功（ウサギ用 8 + 5 件を含む）。
- 差し替え後に残る体はウサギだけで、ウサギのコントローラーで動く。銃口は一人称 SMG、魔法の手はウサギの左手のひら。
- 移動・ストレイフ中の両手と SMG の握りのずれ：体 0.0 cm、一人称 0.0 cm。
- 上を向くと SMG が 34.8° ついてくる。走るとマフラーが 7°、尻尾が 16° 振れ、被弾で耳が伏せる。
- 撮影（Editor の Play モード）：`Screenshots/Rabbit_Unity_FirstPerson_Check.png`（一人称）、`Rabbit_Unity_ThirdPerson_Check.png`（三人称の正面と横）。

残り：§3.7 の末尾（Phase 9 で部位マスク・発光・雷の演出を追加した）。

---

## 3.7 質感・発光・ウサギ自身の雷（Phase 9、2026-11-08）

**VFX Graph は使っていない。** §6 の計画では VFX Graph を導入する予定だったが、火球のときのユーザー確認（`FIREBALL_VFX.md`：公開 API ではグラフのノード追加・接続ができず、コードで確実に作れない）に合わせ、Particle System・LineRenderer・手書きシェーダーで作り、他のエフェクトと同じ `FxPool`・品質設定で再生する。

```powershell
& $b -b ArtSource\LightningRabbit\Animation\LightningRabbit_Rig.blend -P ArtSource\Tools\rabbit\rabbit_look.py -- --check <dir>
# 出力（Export/Unity、Assets/.../LightningRabbit/Textures へ手でコピー）：T_LightningRabbit_{SurfaceMask,GlowMask}.png と FPArms 版
```

| 部分 | 内容 |
|---|---|
| 部位マスク `rabbit_look.py` | 2048、RGBA＝柔らかさ・つや・金属・暖かさ（キツネの `FoxGunnerLookFactory.Presets` と同じ 7 種）。テクセルごとに、色（9 種のトーン）・その上の三角形の部位（主なボーン）・体の上の位置で決める（キツネと同じ考え方）。位置の境目は実測：頭の骨 1.065 m、目 1.13〜1.18 m、首元の黄色はスカーフの襟、お腹 0.77〜0.84 m・ベルト 0.70〜0.775 m（腰回りは腿のボーンの重みが主）、ヘッドセットは左耳の上。単独の点は周りに合わせ、UV の隙間へ 4 テクセル広げる。Unity での割合：布 33%・革 28%・毛 26%・金属 8%・顔 3.3%・ガラス 1.1%・目 0.17% |
| 発光マスク | シアン〜青白のテクセル（色相 0.45〜0.62）と、それに囲まれた白い芯（クロージング）。ヘッドセットとグローブ・ブーツ・胸の稲妻、バッテリー管、スカーフの稲妻だけが光り、頬・耳・ズボンの黄色の稲妻（塗装）は光らない。使用テクセルの 1.0%（一人称の腕 1.6%） |
| 発光の描き方 `CH_GlowLines.shader` | SoftToon に発光が無いので、光る三角形だけを複製した薄いメッシュ（同じ頂点・ボーン・バインドポーズ）を加算で重ねる。LOD ごと：LOD0 2,356／160,000・LOD1 858／40,000・LOD2 470／15,000 三角形。一人称の腕にも同じ複製。色＝元の色 60%＋シアン 40%、細かな電気のちらつき。強さは `_GlowBoost`（スクリプトが毎フレーム設定） |
| 目印 | 両グローブの甲のレンズ（手の光る頂点の中心、体と一人称の腕の両方）と両耳の先 |
| `ElectricBodyFx`（キットに付く） | 発光：ゆっくり呼吸、全力疾走で明るく、武器 VOLT-9 の帯電連射がたまるほど明るく（満タンで +0.7）、12 連続命中の雷撃で最大の光（2.8）と両グローブの火花、詠唱・ダッシュ・被弾で一瞬強く光って 1 秒で戻る（規則は純 C# の `ElectricPulse`）。火花：グローブのレンズから、待機で約 1 秒に 1 回、走ると 1 秒に数回、詠唱とダッシュで即座に。一人称用（数 mm・数 cm で消える）と体用（離れて見える大きさ）の 2 種。体が他人に見えているときだけ：ダッシュの残像（LOD2 を焼いた青い縁取りの像、0.32 秒で消える）、耳の先の短い放電 |
| 品質 | Low：発光と詠唱・ダッシュの火花だけ。Medium：待機中の火花、残像 2 体。High：耳の放電、残像 3 体 |
| 自分の体 | ローカルプレイヤーの体は影だけなので、体の発光メッシュは無効にする（スキニングもしない）。残像・耳の放電も出ない |
| 色 | 体の発光・火花・残像はデザインの青白（#59D7F1→白）。アートバイブルの雷属性の黄色（#FFE040）はスキルのヒット等の属性エフェクト用で、今回は触っていない |
| 共有部分の変更 | `CharacterAnimatorDriver` に `Dashed` イベントを追加（追記のみ）。`PlayerCharacterSwap` は差し替え後にキットの `ICharacterKitListener` へ通知。`CoopDeck.Characters` が `CoopDeck.Effects` を参照 |

確認：
- EditMode 283/283（ウサギの見た目 9 件を追加）、PlayMode 76/76（発光・火花、残像の 2 件を追加）。
- Play モード（この PC＝ROG Ally X、Medium）：ダッシュで発光の強さ 0.05 → 2.07、1.5 秒後 0.18。一人称のグローブから火花。体が見えているときだけ残像 2 体（0.7 秒後には 0）。
- 負荷（一人称、エフェクトあり/なしを 4 秒ずつ 3 回交互）：GPU 3.53 / 3.62 ms、メインスレッド 8.69 / 8.74 ms で差は計測のぶれ以下。残像の焼き込みは 1 体 0.19 ms（体が画面に映っているダッシュのときだけ）。
- 画像（Editor の Play モード）：`Screenshots/Rabbit_Phase9_*.png`。

- 帯電連射との連動（後から追加）：PlayMode で VOLT-9 を的に撃ち、発光が 0.17（待機）→ 0.61（帯電中）→ 3.09（雷撃）→ 1.5 秒後 0.18、雷撃で火花 7 個。EditMode 291/291・PlayMode 82/82。

残り（Phase 10 で対応、§3.8）：
- 実ゲームでの撮影は §3.8 で実施（撮影のあいだだけ体を表示して撮った）。
- 全力疾走でストックが右肩に入る点は 2026-11-08 に修正済み（§3.5）。
- HUD の武器名：ゲームシステム制作セッションがウサギ専用の武器データ「VOLT-9 サンダーホップ」（`WD_Volt9Thunderhop`、固有能力「帯電連射」）を作り、ウサギの初期武器になった（`CharacterDefinition.StartingWeapon`）。

---

## 3.8 実ゲームでの確認と撮影（Phase 10、2026-11-08）

**撮影**（Editor の Play モード、`Sandbox_Phase1`、PF_Player にウサギを選択）：
- 自分の体はふだん影だけなので、撮影のあいだだけ体を表示し、HUD を隠して別のカメラで撮った。
- 戦闘はスクリプトで操作（`ICharacterCommandSource`）：射撃・スキル・ダッシュ。的は体力を増やした `PF_TargetDummy`（120 だと 12 発目の前に倒れて帯電連射が出ない）。
- 射撃は短い連射ごとに狙い直す（連射し続けると反動で照準が的の頭上に抜ける）。

| 画像（`Screenshots/`） | 内容 |
|---|---|
| `Rabbit_Phase10_Sheet_Views.png` | 正面・右側面（マフラー側）・左側面（ヘッドセット側）・背面・斜め前。待機の構え |
| `Rabbit_Phase10_Sheet_Combat.png` | VOLT-9 の帯電連射（12 連続命中の雷撃が的へ）、肩越しの雷撃の瞬間、スキル（回復）の詠唱、ダッシュの残像 3 体 |
| `Rabbit_Phase10_Sheet_FirstPerson.png` | 一人称：雷撃の瞬間、HUD に VOLT-9・帯電ゲージ・カード |
| `Rabbit_Phase10_*.png` | 元のコマ（連写） |

**ビルドでの確認**（Release、72 秒、エラー・警告 0）：`CoopDeckFPS.exe -character LightningRabbit -perfrun 30 -perfarena`。
- 自動操縦で移動・射撃・スキル使用、敵の波 4 まで、NPC ヴォルトと協力コンボ。
- この PC＝ROG Ally X、Handheld、VFX Medium、1920×1080、vSync 2（60 fps 上限）。

| 実行 | 平均 fps | 1% low | 最悪フレーム | メインスレッド | GPU |
|---|---|---|---|---|---|
| ウサギ 1 回目（ビルド直後） | 59.1 | 20.2 | 358 ms（t=18.4 s、1 回だけ） | 4.70 ms | 3.02 ms |
| ウサギ 2 回目 | 59.9 | 43.2 | 33 ms | 4.28 ms | 3.00 ms |
| ウサギ 3 回目 | 60.0 | 52.6 | 33 ms | 4.23 ms | 2.99 ms |
| キツネ | 59.8 | 32.4 | 50 ms | 4.25 ms | 2.88 ms |

- ウサギとキツネの差は GPU で約 0.1 ms。メインスレッドの差は 2・3 回目では計測のぶれの範囲。
- 358 ms の引っかかりは 1 回目だけで、2・3 回目には出なかった。ビルド直後の初回に、初めて使うエフェクトのシェーダー準備が重なったものとみられる（原因の特定はしていない）。
- 終了時の異常終了（Segmentation fault）が 1 回あった（計 9 回中。ウサギ 2 回目か 3 回目）。どちらのログも正常な終了処理（`CodeReloadManager destroyed`）まで出ており、ゲームのコードが止まった後のエンジン側の後片付けで起きている。その後キツネ 3 回・ウサギ 3 回を続けて実行し、すべて正常終了（exit 0）で再現しなかった。
- キツネはそのまま：Fire Rifle・「プロトAR」、キツネの腕（`PerfCaptures/phase10_fox_arena.png`）。
- 計測結果：`PerfCaptures/phase10_{rabbit,fox}_arena*.json` と同名の .png（ビルドが撮った一人称画面）。

残り：
- 全力疾走でストックが右肩に入る点は 2026-11-08 に修正済み（§3.5）。
- 三人称では銃口の光・弾道が出ない。射撃のエフェクトは一人称の銃から出る作りで、他人に見える体の SMG からは出ない（今は他プレイヤーがいないので、キツネも同じ）。
- 帯電連射の雷撃はゲームシステム制作セッションが作り直し中。今回の撮影とビルドは作り直し前の細い弧。

---

## 4. 予想される問題と対策

| 部位 | 予想 | 対策 |
|---|---|---|
| 手・指 | キツネの Meshy 出力は、T ポーズで指がくっついた平たい手（`ArtSource/FoxGunner/Inspection/views.png`）。三面図の手は小さい（1 本の指が約 15 px）ので、ウサギでも 5 本に分かれた指は期待しにくい | **結果：Ultra で 5 本に分かれて出た（§3.1）。** Phase 4 で指ごとに 3 関節のボーンを入れて曲げ、握りの形で潰れ・貫通がないかを検証した（§3.3）。手は作り直さず、スカルプトの指をそのまま使う |
| 耳 | 細長く薄い板。2 本が近い（背面図）ので、根元や先がくっつく恐れ | 左右の耳を別々に選択できるか検査。1 本あたり 3 ボーン（根元・中・先）で、遅れてなびく動きを付ける（キツネは 1 本） |
| マフラーの先 | 横へなびいた形のまま固まった板になる | **結果：右側に垂れる 2 本の帯になった。** 帯ごとに 3 ボーン＋ばねで、走ると後ろへ流れるようにする（ユーザー決定：残してボーンで揺らす） |
| 尻尾 | 小さな球 | 1〜2 ボーンで弾む動き |
| ヘッドセット | 左耳の根元。耳の付け根の変形に巻き込まれる恐れ | ヘッドセットの頂点は頭のボーンに固定、耳のウェイトから外す |
| 服 | ベストの肩、バギーパンツの股 | 腕・脚を曲げたポーズで貫通と潰れを検査（キツネと同じ `inspect_character.py`） |
| 銃（SMG） | 三面図にない | §7 |

---

## 5. 調査結果（Phase 1、2026-11-08）

### 5.1 キツネの構成（再利用の対象）

| 項目 | 現状 | ウサギでの扱い |
|---|---|---|
| モデル | `FoxGunner.fbx`：Meshy 24 ボーン＋尻尾 4・耳 2・指 6・`Weapon`・`IK_Hand_L/R`・`IK_Elbow_L/R`・`FP_Grip_L`＝42 ボーン、40,000 三角形、4K テクスチャ＋法線マップ | 同じ流れ。耳 3×2・尻尾 1〜2・マフラー 3・指 15×2 を追加 |
| シェーダー | `SG_Character_SoftToon`（部位マスクで毛・顔・布・革・金属・ガラスの質感）＋`CH_Outline` | そのまま使う。部位マスク（`FoxGunnerLookFactory` の色・位置の判定）はキツネの衣装用なのでウサギ用を作る |
| Humanoid の骨の対応 | `FoxGunnerAssetFactory.HumanBoneMap`（Meshy 共通） | そのまま使える |
| Animator | `AC_FoxGunner_TP`（4 レイヤー、47 クリップ、パラメータは `AnimatorParams`）。`FoxGunnerAnimationFactory.BuildController` が生成 | レイヤー・パラメータ構成は共通。クリップはウサギのリグで作り直す（銃と手の IK 目標の位置がキツネの腕の長さで作られているため） |
| モーション制作 | `ArtSource/Tools/fox_anim`（Blender の手続き生成。歩行・構え・ソルバー） | キャラクター設定を外から渡せるよう一般化して流用。ウサギは速く・低く・着地を短く |
| 一人称の腕 | `FirstPersonArms`（実行時は汎用。左右とも体の `IK_Hand_L/R` を写す）＋ `fox_fp_arms.py`（2026-11-08 にゲームシステム制作セッションが作り直し：キツネのスカルプトから腕・手・5 本の指を切り出し 4 万三角形、4K の色・法線・質感マスク、指 3 関節、骨の長さは体と同じ） | 同じ方針（ユーザー指示：一人称の腕もそのキャラクター自身の高精細メッシュ・テクスチャから作り、仮の腕や造形し直した手に置き換えない）。ウサギのスカルプトから腕と手を切り出し、肩の位置・胸のひねりはウサギの体から求める。新しい `fox_fp_arms.py` の手順（ゲームシステム制作セッションより）：スカルプトを準備済みの体に合わせ、体の Arm/ForeArm/Hand/指の骨から 1.2 cm 以内の頂点だけ残す（上腕は付け根から 1/4 より先）→ 500 頂点未満の破片を捨てて 4 万三角形 → 4096 で UV を開き直し色・法線・質感マスクを焼く → 指はつながりで 5 本に分け、関節は 0/45/75%（親指 0/40/70%）→ 手のひらの向きの目印 `Palm`・`PalmOut` を置く（`FirstPersonArms` が曲げ軸に使う）。`FP_Grip_L` は今は読まれない |
| 武器・IK | `HeldRifle` を `Weapon` ボーンに付け、Animation Rigging の `TwoBoneIKConstraint` ×2 | 同じ。SMG にするなら握りの位置を作り直す |
| 体の制御 | `FoxBodyAnimator`（視線・銃口補正）・`FoxSecondaryMotion`（尻尾・耳のばね） | 名前以外は汎用。ただし耳は 1 本 1 ボーン、`SetupBody` が尻尾ちょうど 4 本を要求 → 多関節の耳・マフラーに対応させる |
| ゲームとの接続 | `CharacterAnimationEvents` / `CharacterAnimatorDriver` | 汎用。そのまま |

### 5.2 まだ無いもの

- **キャラクターの選択・切替の仕組みは無い。** `PF_Player` は 1 種類で、体は生成時にキツネに固定（`ProjectBootstrap` → `CharacterPrefabBuilder.BuildPlayer`）。キャラクター定義（`PROJECT_OVERVIEW.md` §6 の ScriptableObject）も未実装。
- **VFX Graph は未導入。** エフェクトはすべて Particle System・LineRenderer をコードで生成し、`FxPool`・`VfxQualityGate` で再生している。雷の弧は `LightningArcFx`（コンボの連鎖で使用）。
- 生成ツール（`FoxGunner*Factory`）の定数はキツネ専用で、`CharacterPrefabBuilder` も内部でキツネの工場を直接呼ぶ。キャラクターごとの設定にまとめ直す必要がある（中程度の作業量）。
- テストはすべて `PF_Player` の体がキツネである前提（身長 1.6 m、尻尾 4 本、`AC_FoxGunner_TP` など）。キツネのテストは残し、ウサギ用を並べて追加する。

### 5.3 雷属性の既存要素（重複実装しない）

- カード：連鎖雷撃（`card.chain_lightning`）、サンダーチャージ（`card.thunder_charge`）、雷撃（`card.thunderbolt`、NPC 用）。
- エフェクト：`FX_Skill_{Beam,Burst,Area,Aura}_Lightning`（共通の色違い）、`FX_Aura_Lightning`（敵の帯電）、`LightningArcFx`。
- コンボ：超伝導バースト（氷＋雷）、オーバーロード（火＋雷）。
- NPC「ヴォルト」：雷が得意、専用デッキ `DD_Npc_Volt`（8 枚・手札 2）、体は仮リグ。`PROJECT_OVERVIEW.md` §6 では「ヴォルト（雷・カード循環・高速戦闘）」がプレイアブルキャラクター案として挙がっている。

### 5.4 ユーザーの仕様と現在の実装の違い（今回は変更しない）

| 項目 | 依頼の仕様 | 現在 |
|---|---|---|
| スキルデッキ | 8 枚固定 | プレイヤー 12 枚（`CARD_SYSTEM.md` R1、テストあり）。NPC は 8 枚 |
| 手札 | 3 枚 | 一致 |
| 特化スキル（1 ランに 1 種、手札に固定） | あり | 未実装。R7（ゲージ 0 で自動捨て札）・R8 と衝突するので、手札 3 枠の外に 4 枠目を作るか規則の例外が要る |
| パッシブ | ビルドを変える | 種類だけあり、効果は未実装 |
| キャラクターの適性 | ウサギは雷に適性 | `PROJECT_OVERVIEW.md` §6：全キャラが全属性を使え、専用カードは避け、固有パッシブ・コンボ補正・移動で差を付ける |

---

## 6. Phase 4 以降の作業計画

1. **Blender（Phase 4〜5）**：`inspect_character.py` で検査 → `prepare_for_unity.py --name LightningRabbit` → `refine_character.py rebuild`（40,000 三角形・均一 4K UV・法線マップ）→ ボーン追加とウェイト → 腕・脚・指・耳を曲げたポーズで変形を検査。（実施結果は §3.2・§3.3：体は 16 万三角形、手はスカルプトの指をそのまま使い、リグはウサギ専用の `ArtSource/Tools/rabbit/` で作った）
2. **モーション（Phase 6〜7）**：`fox_anim` の歩行・構え・ソルバーにキャラクター設定（腕の長さ・速度・姿勢の低さ）を渡して全クリップを生成。ウサギ専用：ダッシュで体を低く、着地を短く、雷の詠唱で左手を鋭く突き出す。耳・マフラー・尻尾はクリップに入れず、キツネと同じくばねで手続き制御（待機で小さく動く、走ると後ろへ、ジャンプで遅れる、詠唱で立つ）。
3. **Unity（Phase 8）**：工場をキャラクター設定ごとに動くよう分ける（キツネの出力は変えない）。`PF_Player_LightningRabbit`（または体を差し替える仕組み）と、キャラクター定義の ScriptableObject・選択の口（起動引数やメニュー。選択画面は無いので最小限）を作る。キツネのテストはそのまま通し、ウサギ用のテストを追加。
4. **見た目と VFX（Phase 9）**：部位マスクと発光マップ（青白い稲妻の部分）、`SG_Character_SoftToon` を共用。雷の演出（手元の火花、ダッシュの残像、発光ラインの強弱、耳の帯電）は VFX Graph を導入して作る（Unity 公式パッケージ `com.unity.visualeffectgraph`、無料）。`FxPool` と品質設定（`VfxQualityGate`）に VFX Graph の再生を足す。Ally X で負荷を計測。（実施結果は §3.7：VFX Graph は使わず Particle System・LineRenderer・手書きシェーダーで作った）
5. **確認（Phase 10）**：正面・側面・背面・戦闘姿勢・一人称の撮影、実ゲームでの移動・射撃・スキル、キツネに影響がないこと、コンソールに新しいエラーがないこと。

---

## 7. 決定事項と未決事項

決定（2026-11-08、ユーザー回答）：
1. Meshy：Ultra あり（生成 35＋リギング 5＝40 クレジット）で実行 → 済。
2. 大きさ：耳の先まで 1.70 m（耳を除き 1.38 m）。
3. SMG：体の完成を優先し、銃は後で別に作る。それまでは既存の Fire Rifle で構え・IK・一人称を検証する。→ **変更（2026-11-08、Phase 6 着手時）：ユーザーが先にウサギ用の武器を作るので、モーション（構え・両手の位置）はその武器に合わせる。** → 武器は Lightning SMG（§3.4、ユーザー承認の 35 クレジット）。モーションはこの SMG に合わせて作った（§3.5）。
4. マフラー：残してボーンで揺らす。

未決：
5. **ヴォルトとの関係**（急がない）：`PROJECT_OVERVIEW.md` の雷キャラクター「ヴォルト」をこのウサギにするか、別キャラクターとして残すか。NPC ヴォルトの仮の体を将来ウサギに差し替えることもできる。
