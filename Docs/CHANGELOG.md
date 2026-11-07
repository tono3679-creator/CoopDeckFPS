# CHANGELOG

形式：日付ごとに「追加 / 変更 / 修正 / 検証」。検証していないものは「検証」に書かない。

## 2026-11-08 — 一人称の腕の構図を作り直し（銃を持つ腕が変に見える）

詳細は `FOX_ANIMATION.md` §6「一人称の構図」。

### 修正
- 左手の握り `FP_Grip_L`（`fox_rig.py` の `FP_LEFT_*`）：指先が下を向く握りに前腕が下から来ていたため、手首が 125° 折れて手が籠手の上に折り返し、黒い塊が腕から生えているように見えていた。ハンドガードの左下を下から包み、指先が前腕の延長（前・右上）を向く握りに変更（手首の曲がり 5〜9°）。
- 左前腕が画面下中央から垂直に立ち上がり、肘があごの下にあるように見えていた。

### 変更
- 一人称の腕を長く（`fox_fp_arms.lengthen_arms`：前腕 1.3 倍・上腕 1.1 倍、伸ばすのは素肌の部分だけで籠手・肘・手の形は保つ）。ビューモデルの銃はキツネ自身の目より遠くにあり、体と同じ長さでは肘を画面外に出せないため。
- 肩を左右別々に画面の下へ配置し、肘のヒントを固定値に（`FoxGunnerAnimationFactory.FirstPersonLeftShoulder/RightShoulder/LeftElbow/RightElbow`、`FirstPersonArms.SetElbowHints`）。左前腕は左下から斜めに入り、肘は画面下端のすぐ外。右前腕は右手の向きに沿って右下から（手首の曲がり 24°）。旧 `FirstPersonShoulders` / `FirstPersonYaw` は削除。
- ビューモデルの銃を 3.5 cm 上・2 cm 左へ、5° 前傾、ヨー -6° → -7°（`CharacterPrefabBuilder.ViewModelRootPosition` / `ViewModelRiflePitch`）。右手の指がグリップにかかるのが画面下端に見える。

### 追加
- 手動実行の `FoxAnimationCaptureTests.Tune_FirstPersonLayout`：JSON の候補ごとに一人称の画面と手首の拡大を撮り、画面上の肘の位置から肩と肘のヒントを解き、手首の曲がりを記録する（JSON がなければ現在の配置を撮る）。
- テスト `FirstPersonArms_ReachInFromTheLowerCorners_WithStraightWrists`（左手は画面内・左肘は画面下端の外で手より左・右手は下端に届く・手首の曲がり 左 30° 右 40° 未満）。
- `Capture_FirstPerson` にスプリントのコマを追加。

### 検証
- EditMode 234/234、PlayMode 60/60（手動実行の撮影 4 件を除く）、コンパイル警告 0。Windows ビルド成功。
- 待機の手首の曲がり 左 5°（旧い握りでは 125°）・右 24°、手と目標のずれ 0.0 cm（待機・走り・スプリント・リロード・詠唱）、手首に残るねじれ最大 72°（上限 75°）。
- 撮影 `FoxAnim_FirstPerson.png`（待機・走り・スプリント・リロード・詠唱）と、ビルドの実画面 `PerfCaptures/fp_view_after.png`（比較 `Screenshots/FoxAnim_FPArms_BeforeAfter.png`）：左前腕が左下から斜めに入り、左手の指がハンドガードにかかる。ゲーム画面ではカード欄が左前腕の下側を、弾数欄が右手（画面右下の角）を隠す。
- ビルドの待機計測（`-perfrun 6 -perfidle`、1920×1080）：平均 60.0 FPS、1% Low 60.0。

## 2026-11-08 — Fox Gunner の高品質化（均一な 4K テクスチャ・法線マップ・4 万三角形の体）

詳細は `CHARACTER_PIPELINE.md` §8・§8.1・§8.2。ボーン構成・アニメーション・配色は変えていない。外部ツール：Real-ESRGAN（BSD-3、ローカル実行、リポジトリには入れない）。

### 追加
- `ArtSource/Tools/refine_character.py`：UV 島の外を埋める（dilate）→ Real-ESRGAN でタイル分割の 4 倍拡大（upscale）→ 同じ島を面積比でそろえて 4096 に詰め直し、旧 UV から色を焼き直す（bake）。`--check` で旧・新の比較画像。
- `ArtSource/FoxGunner/Export/FoxGunner_HQ.glb`（同じ 24 ボーン・メッシュ・スキン、新しい UV、4K テクスチャ入り）。
- テスト `BaseColor_Is4K_HighQuality_WithEvenTexelDensity`（4096・BC7・異方性、テクセル密度の 95%/5% 比 1.8 未満）。
- 法線マップ `T_FoxGunner_Normal.png`（4096、Meshy の 230 万三角形の高精細モデルから接空間でベイク、`refine_character.py normalmap`）。頂点は増やさず、溶けていたゴーグルの面取り・ベルト・ブーツ・マフラーのしわの陰影を高精細と同じにする。
- `SG_Character_SoftToon` に `_NormalMap` / `_NormalStrength`。一人称の腕用に法線マップなしの `M_FoxGunner_SoftToon_FirstPerson`。
- テスト `Body_ShadesWithTheHighResNormalMap_AndTheFirstPersonArmsDoNot`、比較画像 `FoxLook_NormalMap_SideLight.png`（横からの光で法線マップなし／あり）。

### 変更
- `T_FoxGunner_BaseColor.png` を 2048 → 4096（テクセル密度の差 約 3 倍 → 1.5 倍、線画のギザギザとノイズを除去）。取り込みは BC7・トライリニア・異方性 8。
- `fox_common.EXPORT_GLB` を `FoxGunner_HQ.glb` に。`fox_rig.py`・`build_clips.py`・`fox_fp_arms.py` を再実行して `FoxGunner.fbx`（42 ボーン）・`A_FoxGunner_Set.fbx`・`FoxGunner_FPArms.fbx` を新しい UV で再出力。
- 部位マスクを 1024 → 2048 で作り直し（毛 38.3%・顔 4.2%・布 35.0%・革 19.0%・金属 3.4%。テクセル数の割合なので、旧アトラスで顔に多く割かれていた分が減った）。
- `FoxGunnerLookFactory.BenchmarkBatch` が変更した画質レベルを元に戻すように。古い API 呼び出し（CS0618）2 件を置き換え。
- 体の FBX の接線を MikkTSpace で計算（法線マップ用）。一人称の腕は `M_FoxGunner_SoftToon_FirstPerson` を使う（`FoxGunnerAnimationFactory` の 1 行）。
- 体を Meshy の高精細モデルから 40,000 三角形で作り直し（`refine_character.py rebuild`：削減・均一 UV・色と法線マップの焼き直し・24 ボーンのウェイト転写）。`FoxGunner_HQ.glb`・`T_FoxGunner_BaseColor.png`・`T_FoxGunner_Normal.png` を置き換え、`fox_anim` 一式（尻尾 785・耳 1,095/1,111・指 618/435 頂点）と一人称の腕を再出力。テストの予算を 30k〜45k 三角形・6 万頂点に（Unity で 52,554 頂点）。

### 修正
- 一人称の左前腕（肘の内側）に黒い三角形が出ていた。原因は前腕の UV 転写：頂点ごとに最寄りの面から UV を取る方式（Data Transfer）だと、UV の継ぎ目をまたぐ面の角が別々の島に乗り、面がアトラスを横切って伸びる（新しい UV では継ぎ目が増え目立った）。さらに体から切り出した腕は肘付近の大きな三角形が欠けていた。`fox_fp_arms.body_uvs` に置き換え：UV は体全体から取り、各面は中心に最寄りの体の三角形から UV が連続する半径 3 cm の範囲にだけ写す。継ぎ目の端をまたぐ面・アトラスの隙間を横切る面（14,000 面中 680 面）は 1 枚の三角形に収める。`FoxGunner_FPArms.fbx` を再出力（頂点・面数は同じ）。

- 4 万三角形への切り替え後、一人称の左前腕の肌に 1 ピクセルの暗い点が出ていた。原因は部位マスク：各テクセルを基本色の 1 テクセルの色で判定するため、4K（Real-ESRGAN）の塗りの細かなムラで孤立したテクセルが別の部位になり、トゥーンの陰影が点状に変わっていた。`FoxGunnerLookFactory.RemoveSpeckles`（3×3 の多数決 2 回：同じ部位の隣が 1 つ以下で、5 つ以上が別の部位なら合わせる）を判定とにじみ出しの間に追加し、マスクを作り直し（9,409 テクセルを修正、部位の割合はほぼ同じ：毛 38.2%・顔 4.2%・布 35.1%・革 18.3%・金属 4.1%）。

### 検証
- 部位マスクの修正：EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3。`FoxAnim_FirstPerson.png` の左前腕に点なし（細い赤線はテクスチャの傷の模様）。
- 一人称の腕の UV 修正：Blender で体と一人称の前腕を同じカメラ 4 方向から平坦テクスチャで描画して一致を確認（黒い部分なし）、伸びた面 0。Unity の一人称の撮影（`FoxAnim_FirstPerson.png`）で左前腕がきれいなことを確認。
- 4 万三角形の体で EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3（`Diagnose_TailShape`：尻尾の届き 0.69 m・スケール 1.0）、コンパイル警告 0。`FoxHQ_ThreeStages.png`（元・15k＋4K テクスチャ・40k＋法線マップ）。
- 4 万三角形の負荷：キツネ 9 体でキツネなし 2.48 ms → 4.32 ms。Windows ビルド（164 MB、エラー 0）の `-perfrun 30`：平均 59.98 FPS、1% low 58.4 FPS、GPU 2.60 ms、メインスレッド 3.58 ms（プレイヤーの体は影だけ）。
- 15k＋法線マップの段階：EditMode 234/234、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3（コンパイル警告 0）。一人称の手の見た目に変化なし（`FoxAnim_FirstPerson_Hands.png`）。
- 15k＋法線マップの負荷（Handheld、キツネ 9 体）：キツネなし 2.95 ms、Shader Graph 版＋輪郭線 3.50 ms。
- テクスチャのみの段階：EditMode 233/233、PlayMode 59/59、`FoxAnimationCaptureTests` 3/3。
- Unity での撮影：`Screenshots/FoxHQ_BeforeAfter.png`（上が変更前）、`FoxLook_*.png`、`FoxAnim_*.png`（全モーション・一人称の腕の色）。
- ROG Ally X（Handheld、キツネ 9 体、1280×720）：キツネなし 2.45 ms、Shader Graph 版＋輪郭線 3.16 ms。

## 2026-11-07 — Fox Gunner のアニメーション一式（Blender Python → Unity）

外部素材・有料サービスなし。詳細は `FOX_ANIMATION.md`。モーションはスクリプトで生成した暫定版（モーキャプ・手付けではない）。

### 追加
- `ArtSource/Tools/fox_anim/`：尻尾 4・耳 2・指 6 のボーンと自動ウェイト、銃ボーンと手の IK 目標（`fox_rig.py`）、姿勢ソルバー（FK＋解析 2 ボーン IK＋足の接地）、接地足が滑らない歩行生成、47 クリップ（依頼の 01〜42＋補助）、確認用レンダー、一人称の腕（前腕＝Meshy 高解像度メッシュ、手＝Blender で新規造形した指 3 関節の手袋の手と詠唱用シェイプキー：`fox_fp_arms.py` / `fox_fp_hands.py`）。
- Unity：`FoxGunnerAnimationFactory`（Humanoid 取り込み・クリップ表・マスク・4 レイヤーの `AC_FoxGunner_TP`・体の IK/視線/尻尾耳・一人称の腕）、`FoxBodyAnimator`（エイム方向への視線と銃口補正）、`FoxSecondaryMotion`（尻尾・耳のばね、耳のリアクション、Ultimate の尻尾、待機の表情）、`FirstPersonArms`（体の手の目標をビューモデルの銃へ写す 2 ボーン IK）、`CharacterAnimationEvents`（ゲームのイベント→アニメーション）、`SkillMotionRules`（カード→詠唱モーション）。
- `AnimatorParams` に移動・武器・スキル・反応・インタラクションのパラメータ、`SkillMotion` / `InteractionMotion`。`Revivable.Active` / `IsReviving`、`FpsMotor.DashDirection`。

### 変更
- `FoxGunner.fbx` を 42 ボーンで再出力（元の 24 ボーン・メッシュ・UV は同じ）。`AC_FoxGunner_TP` を速度 1D ブレンドから 4 レイヤーの構成へ（パラメータ名は従来どおり）。Meshy の歩き・走り・待機クリップは残す（新 Controller では未使用）。
- `HeldRifle` を右手から `Weapon` ボーンへ。一人称の仮の手（カプセル）をキツネの腕に置き換え。
- `CharacterAnimatorDriver`：m/s の速度リングと再生速度補正、Controller にないパラメータは触らない型付き API。`FpsCharacter` のアニメーション配線を `CharacterAnimationEvents` へ。

### 検証
- EditMode 232/232、PlayMode 59/59（撮影・診断の手動テスト 3 件は除く）。一人称の手のずれ 0.0 cm、手首のねじれは前腕へ 6 割分散（残り最大 69°）。手と銃のずれ 0.0 cm、エイム時の銃口誤差 0.0°（上 35° で 0.2°）、6.5 m/s 走行時の接地足の滑り 0.18 m/s。
- Windows ビルド成功（116 MB、エラー・警告 0）。ROG Ally X（Handheld）で平均 59.9 FPS、メインスレッド 3.7 ms、GPU 2.4 ms。
- Unity での撮影（`Screenshots/FoxAnim_*.png`）で全モーションと一人称の腕を目視確認。

## 2026-11-07 — Fox Gunner の質感改善（フィギュアっぽさを減らす）

外部素材・有料サービスなし（Shader Graph の作りは Unity 公式サンプル「Custom Lighting」を参考、Unity Companion License）。手順と数値は `CHARACTER_PIPELINE.md` §7。

### 追加
- 部位マスク（毛・顔・目・布・革・金属・ガラス）をテクスチャの色・スキンのボーン・位置から自動生成する `FoxGunnerLookFactory`。質感パラメータのマスク `T_FoxGunner_SurfaceMask` と URP Lit 用 `T_FoxGunner_MetallicSmoothness`。
- Shader Graph `SG_Character_SoftToon`＋`FoxSoftToonLighting.hlsl`（部位ごとの柔らかい陰影、顔・毛の暖色、素材ごとのつや、控えめなリム）と、表面色から線色を取る細い輪郭線 `CH_Outline`。
- 比較撮影（同じ照明で変更前・URP Lit・Shader Graph 版、サンドボックスの照明でも）と Ally X での負荷計測のバッチ。

### 変更
- `PF_FoxGunner_Body`（とプレイヤーの体）のマテリアルを `M_FoxGunner_SoftToon`＋`M_FoxGunner_Outline` に。変更前の `M_FoxGunner_Toon` は残し、`Materials/Backup/` にも複製。

### 検証
- EditMode 214/214、PlayMode 54/54（Unity 6000.5.4f1）。追加：部位マスクの割合、Shader Graph と輪郭線シェーダーのコンパイル・影と深度のパス、体のマテリアル構成。
- 同じ照明での比較画像（`Screenshots/FoxLook_*.png`）で、2 段の硬い陰影と太い黒線（尻尾の縁の黒い塊を含む）がなくなり、毛・顔の暖かいグラデーション、布・革・金属の違いが出たことを確認。
- ROG Ally X（Handheld 画質）で、画面を埋めるキツネ 9 体の描画が 3.98 → 4.50 ms（キツネなし 3.16 ms）。

## 2026-11-07 — プレイヤーの武器「Fire Rifle」（Meshy → Blender → Unity）

有料サービス Meshy を使用（ユーザー承認済み、30 クレジット：4 枚の図からの 3D 生成）。手順は `CHARACTER_PIPELINE.md` §6。

### 追加
- 三面図下段の 4 つの図から作ったトゥーン調の炎属性ライフル：FBX（7,678 三角形、全長 0.85 m、原点はグリップ、`Muzzle`・`LeftHandGrip` ソケット付き）、2K テクスチャ、発光マップ、`PF_Weapon_FireRifle`。GLB は `ArtSource/FireRifle/Export/`。
- Blender スクリプト `ArtSource/Tools/prepare_prop_for_unity.py`（頂点の結合、角度による硬い角、向き・長さ・原点の調整、ソケット、FBX/GLB の書き出し）。
- `FireRifleAssetFactory`（メニュー `CoopDeck > Art > Import Fire Rifle`）、`ToonOutlineNormalsPostprocessor`（`Art/Weapons/` 以下のモデルに輪郭線用の平均法線を UV3 で追加）。
- トゥーンシェーダーに発光（`_EMISSION`）と、UV3 の平均法線で押し出す輪郭線（`_OUTLINE_UV3_NORMALS`）。どちらも切り替え式で、キツネの見た目は変わらない。

### 変更
- 一人称の仮の箱の銃をこのライフルに置き換え（輪郭の細い専用マテリアル、11 cm 前・3.5 cm 下・銃口 6° 内向き）。武器とスキルの発射位置はライフルの `Muzzle`。
- 体の右手にも同じライフルを持たせる（待機姿勢で前下がり 35° の構え、影のみ描画）。

### 検証
- EditMode 212/212、PlayMode 54/54（Unity 6000.5.4f1）。追加：`FireRifleAssetTests`（5 件）。
- サンドボックスシーンで描画を確認（`Screenshots/FireRifle_FirstPerson.png`、`FireRifle_ThirdPerson.png`）：一人称の位置と輪郭・発光、三人称の構え（真横から前下がり 35°）。

## 2026-11-07 — プレイヤーの本番モデル「Fox Gunner」（Meshy → Blender → Unity）

有料サービス Meshy を使用（ユーザー承認済み、35 クレジット：複数画像からの 3D 生成 30＋自動リギング 5）。Blender 5.2.3（GPL、winget の公式版）を追加。手順は `CHARACTER_PIPELINE.md`。

### 追加
- 三面図から作ったトゥーン調のキツネのキャラクター：FBX（Humanoid、15,350 三角形、24 ボーン、身長 1.6 m）、2K テクスチャ、歩き・走りのクリップ、仮の待機クリップ（歩行の平均姿勢＋呼吸）、`AC_FoxGunner_TP`、`PF_FoxGunner_Body`。GLB は `ArtSource/FoxGunner/Export/`。
- トゥーンシェーダー `CoopDeck/Character/Toon`（2 階調＋落ち影、追加ライト、環境光、リム、輪郭線、Forward+ 対応）。
- Blender スクリプト `ArtSource/Tools/inspect_character.py`（検査）と `prepare_for_unity.py`（頂点の結合・法線の平滑化・原点と縮尺と向きの修正・書き出し）。
- `FoxGunnerAssetFactory`（取り込み設定・明示的な Humanoid の骨対応・マテリアル・Animator・Prefab・プレビュー画像。メニュー `CoopDeck > Art > Import Fox Gunner`）。

### 変更
- `PF_Player` の `BodyVisual` を仮リグからこのモデルに差し替え（入れ子 Prefab、影のみ描画）。`ProjectBootstrap` がモデルの資産も生成する。NPC は仮リグのまま。
- 走りクリップを移動速度に合わせて速く再生（6.5 m/s で約 1.6 倍、9.5 m/s で約 2.4 倍）。

### 検証
- EditMode 207/207、PlayMode 54/54（Unity 6000.5.4f1）。追加：`FoxGunnerAssetTests`（10 件）、`PlayerBodyPlayModeTests`（6.5 m/s 走行で接地中の足の滑り 0.25 m/s）。
- エディターで描画を確認（`Screenshots/FoxGunner_Preview.png`、`PlayerBody_Preview.png`）：トゥーン陰影・輪郭線・影、一人称カメラから前方に体の影が映る。

## 2026-11-07 — Phase 4：NPC 仲間 AI と協力コンボ

Unity は 6000.5.4f1 のまま。追加パッケージ・外部素材・有料サービスなし（効果音はエディターで合成した仮音源）。

### 追加
- アセンブリ `CoopDeck.NPC`：`NpcController`（AI コントローラー）、`NpcDecisionMaker`（Utility AI）、`NpcStateMachine`、`NpcSituation`、`NpcSteering`、`NpcNavigator`、`NpcCardTraits`、`NpcProfile`（SO）。
- NPC 仲間「ヴォルト」（雷）：プレイヤーと同じ `FpsCharacter`／`FpsMotor`／`HitscanWeapon`／`SkillCaster`／`DeckController` を AI が `CharacterCommands` だけで操作。金色の体・紺の手足・シアンのバイザー・頭上の光る輪で、プレイヤー（青）・敵（赤／紫）と区別。頭上に名前と HP。
- NPC の行動：NavMesh 追従（プレイヤーの斜め後ろ・射線の外）、索敵（視線・聴覚・味方への攻撃・チームが撃った敵）、バースト射撃（プレイヤー越しには撃たない）、攻撃予告を見て回避（ダッシュ）、射線を確保できる位置取り（約 1 秒ごとに 13 候補）、近接敵から距離を取る、離れすぎたら合流（26 m）／瞬間移動（48 m・詰まり）。
- 判断：状態機械（追従／交戦／合流／救助／回避／ダウン）＋ Utility AI（回避 → 救援 → コンボ追撃 → 起点づくり → 支援 → 範囲 → 単体／射撃 → 追従）。思考は 0.2 秒ごと。
- 協力コンボ AI：敵の属性（付与者 ID・残り時間・再発動制限）と手札・射程・射線・CD を見て追撃。得意属性の雷カードは味方の相方属性のために時間制限つきで温存（自動 2.5 秒・コンボ優先 6 秒）、火などはプレイヤーの手札に合わせて起点を作る。発動は照準レイが実際に対象に当たったときだけ。
- 独立した NPC デッキ：`DD_Npc_Volt`（8 枚・手札 2）とプリセット `DD_Npc_Blaze`（火・重力）`DD_Npc_Guardian`（支援）。追加カード「雷撃」（`CD_Thunderbolt`）。
- 指示：方針（自動戦闘／集中攻撃／コンボ優先）を V・R3 で切替、集中攻撃の対象を中クリック・X・LT で指定（`CharacterCommands.OrderTacticPressed / OrderMarkPressed`）。
- NPC デッキ編集テスト UI（Tab、パッドはポーズ中に Y）：所持カード、追加・削除、属性の偏り、初期化、プリセット。戦闘中はロック（`TeamEngagementMonitor`）。`CardCollection`／`DeckLoadout`／`DeckLoadoutController`（報酬は `GrantCard`）。
- ダウン・蘇生：`Revivable`／`ReviveModel`。味方が近くに 2.5 秒立つと HP 45% で蘇生。NPC はダウンしたプレイヤーを救助しに来る。NPC は失血後、プレイヤーの横で HP 50% で復帰。プレイヤーは味方がいなければ従来どおり再出現。
- 協力コンボ演出：コンボ名の上に「★ 協力コンボ！ ★」、協力ボーナス +30%、参加者（例：あなた［氷］→ ヴォルト［雷］）、金色の追加 VFX（`FX_Combo_Coop`、追加の層は VFX 中／高のみ）、専用サウンド（`ComboAudio`、`SFX_Combo_*_Coop.wav`）、画面揺れ ×1.4 と短い FOV パンチ（`CameraShaker.Punch`）。
- NPC HUD（`NpcHud`）：名前・HP・方針・行動・使用スキル・手札 2 枚のゲージと温存表示、ダウン／蘇生の進行。敵頭上に「◆氷 → 雷 でコンボ」「⚡ヴォルト 追撃可：超伝導バースト」「◎ 集中」。照準から 120 px 以内では薄く表示。
- 敵の攻撃予告イベント `CombatEvents.ThreatRaised`（`ThreatWarning`）、`ControllerKind`／`IControllerIdentity`、`FpsCharacter.SetCommandSource`／`FindLocalHuman`、`InputGate`、`AllyCollisionFilter`、`ComboPlanner`。
- 無人計測：JSON に NPC の思考回数・スキル使用・コンボ追撃・起点・回避、協力コンボ数、CPU／GPU 時間（FrameTimingManager）、GC Alloc（1 フレームあたり）、GC 回数。`-perfarena` では NPC も一緒にアリーナへ入る。

### 変更
- コンボ VFX（各段階・協力・雷弧）をシーン開始時にプールへ事前生成。無人計測の JSON に重いフレーム上位 8 件と直前の出来事（コンボ・協力・NPC スキル・敵出現・GC）を記録（`worstFrames`）。
- 射撃場の的を `ITrainingTarget` として登録（NPC はプレイヤーが撃った的にだけ参戦。`FindNearestHostile` は既定で的を除外）。
- 各 HUD・計測ツールの「プレイヤーを探す」処理を `FpsCharacter.FindLocalHuman` に変更（NPC も `FpsCharacter` のため）。
- 操作ヒントとポーズ表示に NPC の操作を追加。Player Settings で Frame Timing Stats を有効化。

### 修正（実装中に見つけたもの）
- NPC 自身の射撃で「チームが撃っている的」が更新され続け、射撃場の的を撃ち続ける → 他の味方の命中だけで判定。
- NPC がスキルを 0.2〜0.5 秒間隔で連打する → 通常スキルは 1 秒間隔（コンボ追撃と緊急回復は例外）。
- 温存がペアの両側（火も雷も）にかかり、NPC が火で起点を作れない → 温存は得意属性（追撃役）のカードだけ。
- 同じ攻撃予告に 2 回回避しうる → 処理済みの予告は再評価しない。
- 着弾点指定の範囲カードが横に 2° ずれた瞬間に発動し、敵の横を抜けて遠くの地面に着弾 → 照準レイが対象（または範囲内の地面）に当たるときだけ発動。スキルの照準中は射撃しない（反動で照準がぶれないように）。

### 検証
- コンパイル：エラー・警告 0。Windows Release ビルド：エラー・警告 0（104.4 MB）。ビルドの無人戦闘のスクリーンショットで協力コンボ表示（協力タグ・+30%・参加者）を確認。
- EditMode **197 件**・PlayMode **53 件**、全件成功（Editor とバッチモードの両方）。追加分：EditMode 40（判断 18・状態機械／操舵／カード特性 8・ロードアウト 3・蘇生／交戦 2・コンボ予測 2・生成資産とシーン 7）、PlayMode 16。
- Editor の Play：射撃場でプレイヤーの氷の槍 → 0.22 秒後にヴォルトが雷撃で追撃 → 超伝導バースト（協力 ×1.3、付与者 Player／完成者 ヴォルト）。NPC の手札に雷撃がないときは追撃しない。敵アリーナで索敵・射撃・リロード・回避・スキル循環、NPC のダウン → 失血 → プレイヤーの横で復帰を確認。
- ROG Ally X 実機（Handheld、900p FSR、VFX 中）：NPC 同行のアリーナ戦 60fps 上限 平均 59.9 / 1% Low 59.9（再計測 58.2）、上限なし 91.5 / 81.4。CPU メインスレッド 3.0〜3.8 ms、GPU 2.4〜2.8 ms。NPC の思考は 30 秒で 150 回（5 Hz）。無人の戦闘中に NPC の追撃・起点づくり・回避・協力コンボ（30 秒に 0〜8 回）が発生。
- GC：Release ビルドでは 1 フレームあたりの GC Alloc 量を取得できない（`gcAllocRecorderValid: false`）ため、GC 回数（30 秒に 8〜9 回）で記録。新規コードの毎フレーム／毎思考の確保（`IReadOnlyList` の foreach による列挙子、HUD 文字列の毎回生成）を削減。

### 未確認・残課題
- コンボ多発の計測で 100 ms 級の引っかかりが 6 回中 2 回（1% Low 45.4 / 34.7）。コンボ VFX の事前生成を追加し、重いフレームの記録（`worstFrames`）を入れた後の 3 回では再現しなかったが、原因は未特定。次に出たら JSON の `worstFrames` と Profiler で確認する。
- 人の手でのプレイ確認（NPC の動きの自然さ・強さ・指示とデッキ編集 UI の使い勝手、協力コンボ演出の見え方）。
- NPC の見た目・動きは仮（仮リグと共通の仮アニメーション）。
- 効果音は合成した仮音源（同名ファイルを差し替えれば反映）。
- NPC は 1 体のみ配置（複数 NPC は構造上対応：思考の位相ずらし・経路計算の共有予算・指示は全 NPC が受ける）。
- 作業中に Unity Editor が一度終了した（20:19 頃、ログにクラッシュの痕跡なし）。以降のテストとビルドはバッチモードで実行。

## 2026-11-07 — 属性コンボと VFX（ユーザー指示「Phase 3：属性コンボ・VFX実装」＝計画上の Phase 4 のコンボ部分）

Unity は 6000.5.4f1 のまま。追加パッケージ・外部素材・有料サービスなし。

### 追加
- 属性：`ElementAuraModel`（`CombatStatus.Elements`）で火・氷・雷・重力を 4 秒保持。付与者 ID（`CombatantRegistry.IdOf`）・チーム・カード強化段階を記録し、消費・期限切れ・死亡で解除。氷は移動 55%（体が氷色になる）、重力は 80%。
- スキルが届いた敵ごとに `CombatEvents.ElementHitApplied` を発行（炎上・吸引などダメージのない効果も含む。撃破した一撃でも可）。
- アセンブリ `CoopDeck.Combos`：`ComboDefinition`（SO）、`ComboResolver`・`ComboDamageBudget`（純 C#）、`ComboSystem`、`ComboEvents`、`ElementAuraPresenter`。
- コンボ 3 種：超伝導バースト（氷＋雷：連鎖雷撃）、灼熱特異点（火＋重力：吸引 → 爆発）、オーバーロード（火＋雷：範囲爆発）。4 秒以内・順序不問・対象ごとの再発動制限 3 秒・協力 ×1.3。通常／強化／究極の 3 段階（カードの `UpgradeLevel`）。
- VFX：コンボ 3 種×3 段階、連鎖の雷弧（`LightningArcFx`）、吸引の渦（`SpiralTrailFx` の Trail Renderer）、衝撃波リング（加算シェーダーにリング表示を追加）、属性オーラ 4 種、画面揺れ（`CameraShaker`、照準には影響しない）、コンボ名表示（`ComboHud`）、コンボのダメージ数値（ピンク）。
- VFX 品質：高／中／低（F4、`-vfx`）。Ally X の既定は中。`VfxQualityGate` で追加の層を省略、粒子は画面の 18% まで。`-noshake`。
- コンボ練習デッキ（F7、既存カード 12 枚）、コンボ段階の強制（F6）。
- 無人計測：`-perfpractice`（練習デッキ・スキルのみ 0.8 秒ごと、コンボ回数を記録）、JSON に `vfx`。

### 変更
- 既存スキルの範囲エフェクトは、範囲に合わせて「広がる」ようにした（粒子そのものが 6 倍に拡大して空に飛ぶのをやめた）。
- `HudController` の操作ヒントにコンボとデバッグキーの行を追加。

### 修正（実装中に見つけたもの）
- `FxPool` が同じエフェクトを 2 回プールへ戻すと、同じインスタンスが二重に使われる可能性 → 既に戻っているものは無視。
- 範囲スキルで群れ全体に同じコンボが起きると大きな VFX が重なり敵が見えにくい → 近くの同じ VFX は 1 つにまとめる（ダメージと「×N」表示はそのまま）。
- 氷の付与が見た目で分かりにくい → 体を氷色にし、オーラを強化。
- 灼熱特異点・爆発の粒子が空へ散ってにじむ → 吸い込みを地面と平行にし、火球を短く小さく。

### 検証
- コンパイル：エラー・警告 0。Windows Release ビルド：エラー・警告 0。
- EditMode **157 件**・PlayMode **37 件**、全件成功。追加分：属性 8、コンボ判定 26（3 種×順序、不一致 5 通り、期限、再発動制限、協力、段階、バジェット）、生成資産・VFX 品質 12、PlayMode 9（コンボ 3 種の発動とダメージ、連鎖しない・再発動制限、期限切れ・不一致、協力 ×1.3、撃破時のコンボ、群れでの VFX まとめとヒット上限、デッキ循環中のゲージ・破棄が正確）。
- Editor の Play：的の群れで 3 種と強化・究極段階を発動しスクリーンショットで確認。敵アリーナで氷の槍 → 連鎖雷撃により超伝導バーストが 3 体に連鎖（合計 116 ダメージ）。Console のエラー・警告 0。
- ROG Ally X（Handheld、900p FSR）：練習デッキ・スキル連発の戦闘で VFX 中 60fps 上限 平均 59.9 / 1% Low 59.9、上限なし 91.6。VFX 高でも 60fps を維持。

### 未確認・残課題
- 人の手でのプレイ確認（コンボの手応え・見やすさ・強さ）。
- カード強化の入手手段（強化表・報酬）がないため、強化・究極段階は F6 で強制したときだけ出る。
- NPC・オンラインの味方は未実装（協力コンボは 2 人の詠唱者を使ったテストで確認）。
- Shader Graph は使っていない（コードから安定して生成できないため。トゥーンシェーダーと合わせて Phase 5 で導入予定）。
- 究極コンボを連発する重い場面の Ally X 計測（無人計測ではコンボは 30 秒に 2〜9 回）。

## 2026-11-07 — Phase 2：敵 AI・仮モーション・1 対多の戦闘

Unity は 6000.5.4f1 のまま。追加パッケージは Animation Rigging 1.4.1（Unity 公式、無料）のみ。6000.6.8f1 はインストールが完了したが、プロジェクトは更新していない。

### 追加
- アセンブリ `CoopDeck.Animation`（`AnimatorParams` / `LocomotionMath` / `CharacterAnimatorDriver`）、`CoopDeck.AI`（`EnemyStateMachine` / `EnemyBrain`）。`CoopDeck.Enemies` に `EnemyDefinition` / `EnemyActor` / `EnemyProjectile` / `WaveSchedule` / `WaveDirector`。`Core` に `ILocomotionSource`、`Combat` に `CombatantRegistry`。
- 敵 2 種：スラッシャー（近接）とシューター（遠距離弾、胸に弱点コア ×1.75）。予兆（腕を振り上げる姿勢＋発光エフェクト）のあとに攻撃し、予兆中に避けられる。頭は Animation Rigging で対象を向く。
- テストシーン北西に敵アリーナ（26×18 m、シアンの枠、遮蔽物 4、出現点 4）。入るとウェーブ開始（4 種類の編成をループごとに強化）。NavMesh を生成ツールで焼く。
- 仮モーション：仮リグ、生成クリップ 16 本、`AC_PlaceholderHumanoid`（8 方向 Blend Tree・落下・死亡・上半身レイヤー）、シューター用 Override。プレイヤーの全身は影だけ描画。一人称の待機時の呼吸。
- HUD：被弾の周辺減光と方向矢印、ダウン表示、ウェーブ表示（`ArenaHud`）、弱点ヒットのオレンジ表示。
- 無人計測：`-perfarena`（アリーナで敵と戦いながら計測。自動操縦が最寄りの敵を狙う）。JSON に到達ウェーブと最大同時敵数。

### 変更
- 弱点（Weakpoint）部位へのヒットに `DamageFlags.Critical` を付与。
- 吸引（`ForceReceiver`）は NavMeshAgent を持つ対象を `agent.Move` で動かす（NavMesh の外へ出ない）。
- `PooledFx.ReleaseNow`（敵弾の命中時に即座にプールへ戻す）。

### 修正（実装中に見つけたもの）
- アリーナの出現点から遮蔽物の陰に出た敵が、プレイヤーを視認できず待機したままになる → ウェーブで出現した敵は最初から追跡する（`EnemyActor.AlertOnSpawn`）。

### 検証
- コンパイル：エラー・警告 0（起動中の Editor）。Windows Release ビルド：エラー・警告 0。
- EditMode **111 件**・PlayMode **28 件**、全件成功（Editor 内で実行）。追加分：敵の状態遷移 8、ウェーブ進行 5、移動ブレンド・弱点・対象探索 5、生成資産（Animator・マスク・クリップ・敵 Prefab・プレイヤーの体・シーンの NavMesh／アリーナ）10、近接の追跡と命中・予兆中の回避・敵弾の命中・横移動での回避・ウェーブ進行・NavMesh 上の吸引・入力遅延 7。
- 入力遅延：仮想ゲームパッドの RT が処理されたフレームで発砲・ヒット（追加 0 フレーム）。
- Editor の Play モード：Wave 1 の 3 体がすべて追跡・予兆・攻撃（修正後）、Wave 2 でシューターが約 11 m を保って弾を撃つ、敵の攻撃でプレイヤーが倒れ 2.5 秒後に復活、アリーナを出るとウェーブが一時停止。Console のエラー・警告 0。スクリーンショットで敵の外見・予兆姿勢・HUD を確認。
- ROG Ally X（Handheld、900p FSR）：`-perfarena` で 60fps 上限 平均 59.9 / 1% Low 58.2・59.8、上限なし 平均 91.2 / 1% Low 70.3（最大 6 体同時、ウェーブ 3〜4 まで）。

### 未確認・残課題
- 人の手でのプレイ確認（敵の強さ・予兆の見やすさ・操作感）。移動・視点・射撃の調整値は Phase 1 のまま（調整項目は `DEVELOPMENT_PLAN.md` Phase 2 に記録）。
- マズルフラッシュ・弾道・着弾エフェクトの調整は未着手。
- 本番モデル・Humanoid リグ、左手 IK・足 IK、スキル／ダウン／蘇生のモーション、画面表示までの入力遅延の計測。
- 最初の計測 1 回だけ 1% Low 46 fps が出た（旧自動操縦、以後再現せず、原因未特定）。
- ウェーブの終わり（報酬・ステージ進行）はなく、ループし続ける。

## 2026-11-07 — Phase 3：ゲージ制スキルデッキと FPS 戦闘の統合

ユーザー指示により Phase 2 より先に実装。Unity は 6000.5.4f1 のまま（アップグレードしていない）。

### 追加
- アセンブリ `CoopDeck.Cards`（`CardDefinition` / `SkillEffectSpec` / `DeckDefinition` / `ElementStyle`）、`CoopDeck.Deck`（`CardRuntime` / `DeckState` / `SeededRandom`）、`CoopDeck.Items`（`GaugeItemDefinition` / `GaugeItemInventory`）、`CoopDeck.Skills`（`SkillCaster` / `DeckController`）。
- 戦闘：`CombatStatus` + `StatusEffectsModel`（シールド、攻撃力強化、被ダメージ増加、継続ダメージ、最後の属性）、`ForceReceiver`（吸引）、`DamageResult.Absorbed`、`ICombatActor.Status`。攻撃力強化は通常射撃にも乗る。
- カード 12 種（資料指定の 8 種＋オリジナル 4 種：フロストノヴァ、サンダーチャージ、焼夷グレネード、グラビティスパイク）と 12 枚の初期デッキ、チャージポーション（選択カード +40）と共鳴結晶（手札全体 +25）。数値は `CARD_SYSTEM.md` §12。
- 生成ツール：カードアイコン 12 枚（`CardIconPainter`、外部素材なし）、属性別スキル VFX 21 個（ビーム・着弾・範囲・オーラ・着弾予告）、カード／デッキ／アイテムのアセット。
- 入力：カード選択（1/2/3、十字←→）。アイテム切替のパッド割り当てを十字→から十字↓へ変更。既存の入力アセットに不足アクションがあれば生成ツールが自動で作り直す。
- HUD：`DeckHud`（手札 3 枚・アイコン・属性・ゲージ・消費・残り回数・CD・Q/E/F と LB/RB/Y・山札／捨て札・アイテム欄・選択枠・破棄／ドロー演出・通知）。HP 欄にシールドと攻撃強化の残り時間。F1 で操作ヒント切替。
- テストシーン：的の密集エリア（5 体）、ダメージ床、プレイヤーの仮リスポーン（`PlayerRespawn`）。
- 無人計測の自動操縦がスキルも 2.2 秒ごとに使用。

### 修正（実装中に見つけたもの）
- 長いカード名がキー表示に重なる → 自動縮小。操作ヒントの 1 行目が画面上端で切れる → 位置を調整。

### 検証
- コンパイル：エラー・警告 0（起動中の Editor で `unity recompile`）。
- EditMode **83 件**・PlayMode **21 件**、全件成功（Editor 内で実行）。デッキ規則、アイテム、状態効果、生成データ、入力割り当て、Q/E/F・LB/RB/Y の仮想デバイス入力、射撃との並行使用、デッキ一巡と再シャッフル、各スキル効果を含む。
- Editor の Play モードで自動操縦がスキルと射撃を並行使用し、手札・山札・捨て札が循環（合計 12 枚を維持）することを確認。
- Windows ビルド（102.9 MB、エラー・警告 0）を Ally X で実行：Handheld・60fps 上限で平均 59.9 / 1% Low 59.9 fps。1080p のスクリーンショットで HUD の可読性と照準が隠れないことを確認。

### 未確認・残課題
- 人の手でのプレイ確認（カードの手触り・バランス・HUD の見やすさ）。
- ステージクリア報酬（3 枚から 1 枚）、カード強化表、パッシブカードの効果は未実装。
- 属性コンボの判定は Phase 4。
- 敵が攻撃してこないため、シールドと回復はダメージ床でのみ確認できる。

## 2026-11-07 — Phase 1：環境構築と FPS 最小実装

### 環境
- Unity CLI 1.0.0-beta.17 を導入（`irm https://unity.com/install.ps1 | iex`）。Unity アカウントでサインインし、Unity Personal ライセンスを有効化。
- `unity projects create` で Unity 6 URP テンプレートから `C:\dev\CoopDeckFPS` を作成（Unity 6000.5.4f1、Unity Cloud 連携なし）。
  - 主なパッケージ：URP 17.5.0、Input System 1.19.0、AI Navigation 2.0.13、Test Framework 1.7.0、uGUI 2.5.0、Pipeline 0.10.0-exp.1（Unity CLI から Editor を操作）、Cinemachine 3.1.7（CLI が自動追加。現状未使用）。
  - Animation Rigging・Netcode for GameObjects は使い始めるフェーズ（2 / 2.5）で追加する。
- Unity 6000.6.8f1 はダウンロード中（回線が遅く数時間）。完了後に Git チェックポイントを作ってからアップグレードする。
- 開発機は ROG Ally X（Ryzen Z1 Extreme、Radeon 内蔵、1920×1080 120 Hz）。

### 追加
- アセンブリ：`CoopDeck.Core / Combat / Input / Effects / Weapons / Player / Enemies / UI / Editor` と EditMode・PlayMode テスト（構成は ARCHITECTURE.md）。
- 入力：`GameInputActions`（キーボード＆マウス／ゲームパッドの既定バインド）、`GameControls.inputactions`、`PlayerInputReader`（マウス感度、スティックの応答カーブと加速、一時停止とカーソルロック、再開クリックで誤射しない）。
- 移動：`FpsMotor`（加減速、スプリント、2 チャージの回避ダッシュ、コヨーテタイム、ジャンプ先行入力、空中制御、接地スナップ、壁ずり、天井判定）、`FpsLook`（リコイルの自動復帰、スプリント FOV）。
- 射撃：`WeaponDefinition`（仮アサルトライフル `WD_AssaultRifle`）、`WeaponState`、`HitscanWeapon`（拡散・ブルーム・距離減衰・部位倍率・リコイル・自動リロード）、`WeaponFx`。
- 戦闘：`DamageInfo`、`Health`/`HealthModel`、`HitZone`（頭・胴、ヘッドショット 2 倍）、`CombatEvents`、`ICombatActor`。
- エフェクト：`FxPool`/`PooledFx`（性能ティアで粒子数を調整）、`TracerFx`、`LightFlash`、シェーダー `CoopDeck/FX/Additive Soft`、Prefab 5 種（マズル・弾道・着弾×2・撃破）。
- 的：`TargetDummy`（撃破 → 3 秒後に復活）、`PatrolMover`（移動標的）、`HitFlash`、頭上 HP バー。
- HUD：拡散連動の照準、ヒットマーカー、ダメージ数値、弾数・リロード、HP、ダッシュ残数、スキル枠 3 つ（Phase 3 の受け口）、一時停止表示、操作ヒント、F3 性能表示。
- 一人称の仮ビュー：ブロック形状のライフルと手、`ViewModelAnimator`（ボブ・スウェイ・キック・リロード・スプリント・着地）、ViewModel 専用オーバーレイカメラ。
- 性能ティア：`PerformanceBootstrap`（Ally X を自動判定、`-tier` / `-uncapped`）、URP アセット `URP_High` / `URP_Handheld`（0.833 倍＋FSR、影縮小、SSAO 無効）。
- 無人計測：`AutoPerfRunner`（`-perfrun`）と `AutopilotCommandSource`。
- エディターツール：`CoopDeck > Setup > Run Phase 1 Bootstrap`（レイヤー・画質・Player Settings・アセット・Prefab・シーンを生成）、`CoopDeck > Build > Windows`。
- シーン `Sandbox_Phase1`：射撃場（静止・移動・高所の的、距離マーカー）、移動コース（階段・20°スロープ・高さ違いのジャンプ台・低い天井のトンネル・柱）、坂テスト（15/30/45° と登れない 55°）。
- ドキュメント 7 点、README、CLAUDE.md、`.gitignore`（Library 等と `.mcp.json` を除外）、`.gitattributes`（Unity YAML、Git LFS 対象）。

### 修正（実装中に見つけたもの）
- 連射：トリガーを押したままリロード／待機すると待ち時間が貯まり、次に数発まとめて出る問題を修正。端数の持ち越しは次弾待ちの間だけにした（テスト追加）。
- 画質：テンプレートの「Mobile」レベルが Windows ビルドから除外されていたため、Ally X で Handheld に切り替わらなかった。両レベルを Windows に含めるよう生成ツールを修正。
- 一人称の銃が低すぎて画面外に出ていたため位置を調整。弾数表示の「0」と「/ 30」の重なりを修正。

### 検証
- バッチモードのコンパイル：エラー・警告 0（`CoopDeck.*`）。
- EditMode 38 件・PlayMode 9 件、全件成功（移動速度、走行、停止、ジャンプ高、段差、壁ずり、回避、部位ダメージ、リコイル、自動リロード、コマンド駆動のキャラクター統合、入力定義、ティア判定）。
- Windows ビルド成功（約 103 MB）。Ally X 実機で起動し、自動操縦で射撃・撃破・リロードが動作することをスクリーンショットで確認。
- Ally X 性能：Handheld・60fps 上限で平均 59.9 / 1% Low 58.1 fps。上限なしで平均 96.9 fps（DEVELOPMENT_PLAN.md の記録表）。
- 起動中の Editor（`unity open`）を Unity CLI ＋ Pipeline で操作：`Sandbox_Phase1` を開いて Play、フレーム進行を確認、`script eval` で自動操縦に切り替えて射撃（的 4 体にダメージ）、Game ビューをキャプチャ、コンソールのエラー・警告 0 件。

- ユーザーが Editor で `Sandbox_Phase1` をプレイし、操作感 OK（2026-11-07）。
- GitHub（`tono3679-creator/CoopDeckFPS`）へ push。LFS オブジェクト 2 件を含む。

### 未確認・残課題
- 6000.6.8f1 へのアップグレード（ダウンロード待ち）。
- 操作感の確認をデバイス別（キーボード＆マウス／Ally X 内蔵ゲームパッド）に記録すること（Phase 2 の調整時）。
- 上限なし計測で最悪フレーム 17〜25 ms のスパイクが散発（Profiler で原因調査予定）。
