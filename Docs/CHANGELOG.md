# CHANGELOG

形式：日付ごとに「追加 / 変更 / 修正 / 検証」。検証していないものは「検証」に書かない。

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

### 未確認・残課題
- 人の手による操作感（キーボード＆マウス、Ally X 内蔵ゲームパッド）の確認。
- 6000.6.8f1 へのアップグレード。
- 上限なし計測で最悪フレーム 17〜25 ms のスパイクが散発（Profiler で原因調査予定）。
