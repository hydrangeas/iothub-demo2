# MachineLog.Common

MachineLog.Commonは.NET 8.0ベースの共通ライブラリで、ログデータのモデル、バリデーション、ユーティリティ機能を提供します。

## LogEntry クラス

主要なログエントリモデル：

- プロパティ：
  - MachineId (string): 必須、50文字以内、英数字とハイフンのみ
  - TimeGenerated (DateTimeOffset): 必須、ISO 8601形式
  - Severity (Enum): Debug, Info, Warning, Error, Critical
  - EventId (int): 1〜999999の範囲
  - Message (string): 必須、8000文字以内
  - OperationId (Guid): 必須
  - Tags (Dictionary<string, string>): オプション、最大10ペア
- バリデーション：
  - データアノテーション属性を使用
  - カスタムバリデーションロジック（IValidatableObject実装）
- JSON シリアライズ/デシリアライズ：
  - System.Text.Json.Serialization 属性を使用
  - カスタムJsonConverter実装（特殊なフォーマット処理用）

## LogBatch クラス

ログエントリのバッチ処理用モデル：

- プロパティ：
  - BatchId (Guid): 一意のバッチID（UUID v4）
  - CreatedAt (DateTime): バッチ作成時刻（UTC）
  - Entries (IReadOnlyCollection<LogEntry>): ログエントリのコレクション
  - Size (int): バッチの合計サイズ（バイト）
  - Source (string): バッチのソース識別子
  - ProcessingStats (BatchProcessingStats): 処理統計情報

## バリデーション

FluentValidationを使用した検証ルール：

### LogEntryValidator

- TimeGenerated: 24時間以内の日時であること
- MachineId: 50文字以内、a-zA-Z0-9-のみ
- Severity: 定義された列挙値のみ
- Message: 8000文字以内
- EventId: 1〜999999の整数
- OperationId: 有効なUUID形式
- Tags: 最大10ペアまで

### LogBatchValidator

- BatchId: 有効なGuid
- CreatedAt: 24時間以内の日時
- Entries: 1〜10000エントリ
- Size: 1MB（1,048,576バイト）以下

## 文字列ユーティリティ

- TruncateIfNeeded: 指定された長さを超える文字列を切り詰め
- ToSafeJson: 安全なJSON文字列に変換
- IsValidJson: JSON文字列の検証
- SanitizeString: 文字列のサニタイズ
- GetByteSize: UTF-8エンコードでのバイトサイズ計算

## 日時ユーティリティ

- ToIso8601String: DateTimeをISO 8601形式に変換
- FromIso8601String: ISO 8601文字列からDateTimeに変換
- IsValidIso8601: ISO 8601形式の検証
- GetUnixTimestamp: DateTimeからUnixタイムスタンプに変換
- FromUnixTimestamp: UnixタイムスタンプからDateTimeに変換

## コレクションユーティリティ

- Batch<T>: コレクションをバッチに分割
- SafeAny<T>: nullを考慮したAny拡張メソッド
- ChunkBySize<T>: サイズに基づいてチャンク分割
- ToReadOnlyCollection<T>: 読み取り専用コレクションに変換
- Shuffle<T>: コレクションの要素をランダムに並べ替え

## JSONユーティリティ

- Serialize<T>: オブジェクトをJSON文字列に変換
- Deserialize<T>: JSON文字列からオブジェクトに変換
- IsValidJson: JSON文字列の検証
- MergeJsonObjects: 複数のJSONオブジェクトをマージ
- ParseJsonLines: JSON Lines形式の解析

## リトライユーティリティ

- ExecuteWithRetry<T>: リトライロジックを使用して関数を実行
- ExecuteWithRetryAsync<T>: 非同期関数の実行とリトライ
- CreateExponentialBackoff: 指数バックオフポリシーの作成
- CreateConstantBackoff: 一定間隔バックオフポリシーの作成
- IsTransientException: 一時的な例外の判定

## サイズ計算ユーティリティ

- CalculateSize<T>: オブジェクトのメモリサイズ計算
- CalculateBatchSize: バッチのサイズ計算
- EstimateJsonSize: JSONシリアル化後のサイズ推定
- IsWithinSizeLimit<T>: サイズ制限内かどうかの判定

## パフォーマンス最適化

- ObjectPool<T> クラス：
  - オブジェクトプーリング実装
- LazyInitializer<T> クラス：
  - 遅延初期化パターン実装
- AsyncLazy<T> クラス：
  - 非同期遅延初期化パターン実装
- MemoryExtensions：
  - Span<T>とMemory<T>を活用した効率的なメモリ操作

## セキュリティユーティリティ

- Encryptor クラス：
  - AES暗号化/復号化メソッド
  - RSA暗号化/復号化メソッド
- PasswordHasher クラス：
  - パスワードのハッシュ化と検証
- InputSanitizer クラス：
  - XSS対策のための入力サニタイズ

## 国際化（i18n）とローカライゼーション（l10n）

- LocalizationManager クラス：
  - リソースファイルベースの多言語対応
  - 言語切り替え機能
- CultureInfoProvider クラス：
  - 現在のカルチャ情報の管理
- DateTimeFormatProvider クラス：
  - カルチャに応じた日時フォーマット提供

## テスト支援

- TestDataGenerator クラス：
  - ユニットテスト用のモックデータ生成
- InMemoryDatabase<T> クラス：
  - インメモリデータストア（テスト用）
- TestLogger クラス：
  - テスト用のログキャプチャ機能

## テスト戦略

- 単体テスト：
  - すべてのパブリックメソッドに対するテスト（カバレッジ95%以上）
  - パラメータ化テストの活用
  - エッジケースのテスト
- 統合テスト：
  - 複数のユーティリティクラスを組み合わせたシナリオテスト
- パフォーマンステスト：
  - ベンチマークテスト（BenchmarkDotNet使用）
  - メモリ使用量テスト
- 変更検出テスト：
  - パブリックAPIの変更を検出するテスト
- セキュリティテスト：
  - 暗号化/復号化機能の正常性テスト
  - 入力サニタイズの有効性テスト

## ドキュメンテーション

- XML ドキュメントコメント：
  - すべてのパブリックAPI（クラス、メソッド、プロパティ）に対する詳細な説明
- README.md：
  - ライブラリの概要、使用方法、主要機能の説明
- CHANGELOG.md：
  - バージョン履歴と変更内容の記録

## バージョニング

- セマンティックバージョニング（SemVer）の採用
- 下位互換性の保証（メジャーバージョンアップ以外）

## 依存関係

- 外部ライブラリの最小化（必要最小限のみ使用）
- 使用する外部ライブラリのバージョン固定

## コード品質

- StyleCop.Analyzers による静的コード分析
- SonarQube による継続的コード品質チェック
- コードメトリクスの監視（循環的複雑度、コードの重複等）
