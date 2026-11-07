# CoopDeckFPS（仮称）

協力型ローグライク FPS × デッキ構築。Unity 6 / URP / Input System。

- 企画・仕様: [Docs/PROJECT_OVERVIEW.md](Docs/PROJECT_OVERVIEW.md)
- 実装構成: [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md)
- 開発計画と進捗: [Docs/DEVELOPMENT_PLAN.md](Docs/DEVELOPMENT_PLAN.md)
- カードシステム（最重要）: [Docs/CARD_SYSTEM.md](Docs/CARD_SYSTEM.md)
- モーション指針: [Docs/ANIMATION_GUIDELINES.md](Docs/ANIMATION_GUIDELINES.md)
- オンライン計画: [Docs/MULTIPLAYER_PLAN.md](Docs/MULTIPLAYER_PLAN.md)
- 変更履歴: [Docs/CHANGELOG.md](Docs/CHANGELOG.md)

## はじめかた

1. Git LFS を有効にしてクローンする（`git lfs install` → `git clone ...`）。
2. Unity **6000.5.4f1**（6000.6.8f1 へ更新予定）でプロジェクトを開く。
3. `Assets/_Game/Scenes/Sandbox_Phase1.unity` を開いて Play。
4. シーンや Prefab が無い・壊れた場合は `CoopDeck > Setup > Run Phase 1 Bootstrap` で再生成できる。

## 操作

| 操作 | キーボード＆マウス | ゲームパッド |
|---|---|---|
| 移動 / 視点 | WASD / マウス | 左 / 右スティック |
| ジャンプ | Space | A |
| スプリント | 左 Shift（押し続け） | 左スティック押し込み（トグル） |
| 回避ダッシュ | 左 Ctrl | B |
| 射撃 / リロード | 左クリック / R | RT / X |
| スキルカード 1〜3 | Q / E / F | LB / RB / Y |
| カード選択（アイテムの対象） | 1 / 2 / 3 | 十字キー ← / → |
| アイテム使用 / 切替 | G / T | 十字キー ↑ / ↓ |
| 一時停止 | Esc | Start |
| 操作ヒント / 性能表示 | F1 / F3 | — / View |

テストシーン：右手のオレンジの床に立つと HP が減る（回復・シールドの確認用）。射撃場右側のピンクの床に的が 5 体密集（範囲・連鎖・吸引の確認用）。

## ビルド

`CoopDeck > Build > Windows (Release)` → `Builds/Windows/CoopDeckFPS.exe`（Git 管理外）。
ROG Ally X での無人計測: `CoopDeckFPS.exe -perfrun 30 -perfout C:\temp\perf.json`（詳細は ARCHITECTURE.md 6 章）。

## 秘密情報

`.mcp.json` などの API キーを含むファイルはコミットしない（`.gitignore` 済み）。
